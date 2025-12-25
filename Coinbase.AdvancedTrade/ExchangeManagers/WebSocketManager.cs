using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Coinbase.AdvancedTrade.Enums;
using Coinbase.AdvancedTrade.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Coinbase.AdvancedTrade
{
    /// <summary>
    /// Manages WebSocket communication for real-time data feeds from Coinbase Advanced Trade.
    /// </summary>
    public class WebSocketManager : IDisposable
    {
        private const string DefaultChannelNameHeartbeat = "heartbeats";
        private const string DefaultChannelNameCandles = "candles";
        private const string DefaultChannelNameMarketTrades = "market_trades";
        private const string DefaultChannelNameStatus = "status";
        private const string DefaultChannelNameTicker = "ticker";
        private const string DefaultChannelNameTickerBatch = "ticker_batch";
        private const string DefaultChannelNameLevel2 = "level2";
        private const string DefaultChannelNameUser = "user";

        private readonly Uri _webSocketUri;
        private readonly string _apiKey;
        private readonly string _apiSecret;
        private readonly ApiKeyType _apiKeyType;
        private readonly int _bufferSize;

        private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _subscriptionLock = new SemaphoreSlim(1, 1);

        private readonly HashSet<string> _subscriptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private ClientWebSocket _webSocket;
        private CancellationTokenSource _receiveCts;
        private Task _receiveLoopTask;
        private bool _disposed;

        private readonly Dictionary<string, Action<string>> _messageMap;

        /// <summary>
        /// Event raised whenever a raw message is received.
        /// </summary>
        public event EventHandler<MessageEventArgs> MessageReceived;

        /// <summary>
        /// Event raised whenever a candle message is received.
        /// </summary>
        public event EventHandler<WebSocketMessageEventArgs<CandleMessage>> CandleMessageReceived;

        /// <summary>
        /// Event raised whenever a heartbeat message is received.
        /// </summary>
        public event EventHandler<WebSocketMessageEventArgs<HeartbeatMessage>> HeartbeatMessageReceived;

        /// <summary>
        /// Event raised whenever a market trade message is received.
        /// </summary>
        public event EventHandler<WebSocketMessageEventArgs<MarketTradeMessage>> MarketTradeMessageReceived;

        /// <summary>
        /// Event raised whenever a status message is received.
        /// </summary>
        public event EventHandler<WebSocketMessageEventArgs<StatusMessage>> StatusMessageReceived;

        /// <summary>
        /// Event raised whenever a ticker message is received.
        /// </summary>
        public event EventHandler<WebSocketMessageEventArgs<TickerMessage>> TickerMessageReceived;

        /// <summary>
        /// Event raised whenever a ticker batch message is received.
        /// </summary>
        public event EventHandler<WebSocketMessageEventArgs<TickerMessage>> TickerBatchMessageReceived;

        /// <summary>
        /// Event raised whenever a level2 message is received.
        /// </summary>
        public event EventHandler<WebSocketMessageEventArgs<Level2Message>> Level2MessageReceived;

        /// <summary>
        /// Event raised whenever a user message is received.
        /// </summary>
        public event EventHandler<WebSocketMessageEventArgs<UserMessage>> UserMessageReceived;

        /// <summary>
        /// Current WebSocket state.
        /// </summary>
        public Enums.WebSocketState WebSocketState
        {
            get
            {
                if (_webSocket == null)
                {
                    return Enums.WebSocketState.CLOSED;
                }

                switch (_webSocket.State)
                {
                    case System.Net.WebSockets.WebSocketState.Open:
                        return Enums.WebSocketState.OPEN;

                    case System.Net.WebSockets.WebSocketState.Connecting:
                        return Enums.WebSocketState.CONNECTING;

                    default:
                        return Enums.WebSocketState.CLOSED;
                }
            }
        }

        /// <summary>
        /// Returns current subscriptions. Values are channel strings (e.g. "ticker").
        /// </summary>
        public IEnumerable<string> Subscriptions
        {
            get { return _subscriptions; }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WebSocketManager"/> class.
        /// </summary>
        public WebSocketManager(
            string webSocketUri,
            string apiKey,
            string apiSecret,
            int bufferSize = 5 * 1024 * 1024,
            ApiKeyType apiKeyType = ApiKeyType.CoinbaseDeveloperPlatform)
        {
            if (string.IsNullOrWhiteSpace(webSocketUri))
            {
                throw new ArgumentException("WebSocket URI cannot be null or empty.", nameof(webSocketUri));
            }

            Uri uri;
            if (!Uri.TryCreate(webSocketUri, UriKind.Absolute, out uri))
            {
                throw new ArgumentException("Invalid WebSocket URI.", nameof(webSocketUri));
            }
            _webSocketUri = uri;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("API Key cannot be null or empty.", nameof(apiKey));
            }

            if (string.IsNullOrWhiteSpace(apiSecret))
            {
                throw new ArgumentException("API Secret cannot be null or empty.", nameof(apiSecret));
            }

            if (bufferSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bufferSize), "Buffer size must be greater than zero.");
            }

            _apiKey = apiKey;
            _apiSecret = apiSecret;
            _bufferSize = bufferSize;
            _apiKeyType = apiKeyType;

            _messageMap = new Dictionary<string, Action<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { DefaultChannelNameCandles, HandleCandleMessage },
                { DefaultChannelNameHeartbeat, HandleHeartbeatMessage },
                { DefaultChannelNameMarketTrades, HandleMarketTradeMessage },
                { DefaultChannelNameStatus, HandleStatusMessage },
                { DefaultChannelNameTicker, HandleTickerMessage },
                { DefaultChannelNameTickerBatch, HandleTickerBatchMessage },
                { DefaultChannelNameLevel2, HandleLevel2Message },
                { DefaultChannelNameUser, HandleUserMessage }
            };
        }

        /// <summary>
        /// Maps a <see cref="ChannelType"/> enum to the channel string used in the WebSocket protocol.
        /// </summary>
        public static string GetChannelString(ChannelType channelType)
        {
            switch (channelType)
            {
                case ChannelType.Candles:
                    return DefaultChannelNameCandles;

                case ChannelType.Heartbeats:
                    return DefaultChannelNameHeartbeat;

                case ChannelType.MarketTrades:
                    return DefaultChannelNameMarketTrades;

                case ChannelType.Status:
                    return DefaultChannelNameStatus;

                case ChannelType.Ticker:
                    return DefaultChannelNameTicker;

                case ChannelType.TickerBatch:
                    return DefaultChannelNameTickerBatch;

                case ChannelType.Level2:
                    return DefaultChannelNameLevel2;

                case ChannelType.User:
                    return DefaultChannelNameUser;

                default:
                    throw new ArgumentException("Invalid channel type.", nameof(channelType));
            }
        }

        /// <summary>
        /// Connect to the WebSocket endpoint.
        /// </summary>
        public async ValueTask ConnectAsync()
        {
            ThrowIfDisposed();

            await _connectionLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_webSocket != null && _webSocket.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    return;
                }

                // Ensure any previous socket is torn down before reconnecting.
                await DisconnectInternalAsync().ConfigureAwait(false);

                _receiveCts = new CancellationTokenSource();

                _webSocket = new ClientWebSocket();
                _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

                await _webSocket.ConnectAsync(_webSocketUri, _receiveCts.Token).ConfigureAwait(false);

                _receiveLoopTask = Task.Run(() => ReceiveMessagesLoopAsync(_receiveCts.Token));
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        /// <summary>
        /// Disconnect from the WebSocket endpoint.
        /// </summary>
        public async ValueTask DisconnectAsync()
        {
            ThrowIfDisposed();

            await _connectionLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await DisconnectInternalAsync().ConfigureAwait(false);
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private async Task DisconnectInternalAsync()
        {
            try
            {
                if (_receiveCts != null)
                {
                    _receiveCts.Cancel();
                }
            }
            catch
            {
                // Ignore.
            }

            var socket = _webSocket;
            _webSocket = null;

            if (socket != null)
            {
                try
                {
                    if (socket.State == System.Net.WebSockets.WebSocketState.Open ||
                        socket.State == System.Net.WebSockets.WebSocketState.CloseReceived)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // Ignore close errors.
                }
                finally
                {
                    socket.Dispose();
                }
            }

            var loop = _receiveLoopTask;
            _receiveLoopTask = null;

            if (loop != null)
            {
                try
                {
                    await Task.WhenAny(loop, Task.Delay(2000)).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore.
                }
            }

            if (_receiveCts != null)
            {
                _receiveCts.Dispose();
                _receiveCts = null;
            }
        }

        /// <summary>
        /// Subscribe to a channel and set of products.
        /// </summary>
        public async ValueTask SubscribeAsync(string[] products, ChannelType channelType)
        {
            ThrowIfDisposed();

            var channelString = GetChannelString(channelType);

            await _subscriptionLock.WaitAsync().ConfigureAwait(false);
            try
            {
                EnsureConnected();

                if (_subscriptions.Contains(channelString))
                {
                    return;
                }

                var subscriptionMessage = CreateSubscriptionMessage(products, channelString, "subscribe");
                await SendMessageAsync(subscriptionMessage).ConfigureAwait(false);

                _subscriptions.Add(channelString);
            }
            finally
            {
                _subscriptionLock.Release();
            }
        }

        /// <summary>
        /// Unsubscribe from a channel and set of products.
        /// </summary>
        public async ValueTask UnsubscribeAsync(string[] products, ChannelType channelType)
        {
            ThrowIfDisposed();

            var channelString = GetChannelString(channelType);

            await _subscriptionLock.WaitAsync().ConfigureAwait(false);
            try
            {
                EnsureConnected();

                if (!_subscriptions.Contains(channelString))
                {
                    return;
                }

                var unsubscribeMessage = CreateSubscriptionMessage(products, channelString, "unsubscribe");
                await SendMessageAsync(unsubscribeMessage).ConfigureAwait(false);

                _subscriptions.Remove(channelString);
            }
            finally
            {
                _subscriptionLock.Release();
            }
        }

        private void EnsureConnected()
        {
            if (_webSocket == null || _webSocket.State != System.Net.WebSockets.WebSocketState.Open)
            {
                throw new InvalidOperationException("WebSocket is not connected. Call ConnectAsync first.");
            }
        }

        private async Task ReceiveMessagesLoopAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[_bufferSize];

            using (var messageStream = new MemoryStream())
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var socket = _webSocket;
                    if (socket == null || socket.State != System.Net.WebSockets.WebSocketState.Open)
                    {
                        break;
                    }

                    WebSocketReceiveResult result;
                    try
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    if (result.Count > 0)
                    {
                        messageStream.Write(buffer, 0, result.Count);
                    }

                    if (!result.EndOfMessage)
                    {
                        continue;
                    }

                    var messageBytes = messageStream.ToArray();
                    messageStream.SetLength(0);

                    MessageReceived?.Invoke(this, new MessageEventArgs(new ArraySegment<byte>(messageBytes, 0, messageBytes.Length)));

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var messageText = Encoding.UTF8.GetString(messageBytes);
                        if (!string.IsNullOrWhiteSpace(messageText))
                        {
                            try
                            {
                                ProcessMessage(messageText);
                            }
                            catch
                            {
                                // Ignore malformed messages.
                            }
                        }
                    }
                }
            }
        }

        private void ProcessMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var json = JsonConvert.DeserializeObject<JObject>(message);
            if (json == null)
            {
                return;
            }

            JToken channelToken;
            if (json.TryGetValue("channel", StringComparison.OrdinalIgnoreCase, out channelToken))
            {
                var channel = channelToken == null ? null : channelToken.ToString();
                if (!string.IsNullOrWhiteSpace(channel))
                {
                    Action<string> handler;
                    if (_messageMap.TryGetValue(channel, out handler))
                    {
                        handler(message);
                    }
                }
            }
        }

        private async Task SendMessageAsync(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException("Message cannot be null or empty.", nameof(message));
            }

            var socket = _webSocket;
            if (socket == null || socket.State != System.Net.WebSockets.WebSocketState.Open)
            {
                throw new InvalidOperationException("WebSocket is not connected.");
            }

            var bytes = Encoding.UTF8.GetBytes(message);
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
        }

        private string CreateSubscriptionMessage(string[] products, string channel, string type)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            if (_apiKeyType == ApiKeyType.CoinbaseDeveloperPlatform)
            {
                var jwt = JwtTokenGenerator.GenerateJwt(_apiKey, _apiSecret, "public_websocket_api", type, null);

                var subscriptionMessage = new
                {
                    type,
                    product_ids = products,
                    channel,
                    api_key = _apiKey,
                    timestamp,
                    jwt
                };

                return JsonConvert.SerializeObject(subscriptionMessage);
            }

            var signature = GenerateSignature(channel, products, timestamp);

            var legacyMessage = new
            {
                type,
                product_ids = products,
                channel,
                api_key = _apiKey,
                timestamp,
                signature
            };

            return JsonConvert.SerializeObject(legacyMessage);
        }

        private string GenerateSignature(string channel, string[] products, string timestamp)
        {
            if (string.IsNullOrWhiteSpace(channel))
            {
                throw new ArgumentException("Channel cannot be null or empty.", nameof(channel));
            }

            if (string.IsNullOrWhiteSpace(timestamp))
            {
                throw new ArgumentException("Timestamp cannot be null or empty.", nameof(timestamp));
            }

            var productIds = products == null ? string.Empty : string.Join(",", products);
            var message = timestamp + channel + productIds;

            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_apiSecret)))
            {
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
                return Convert.ToBase64String(hash);
            }
        }

        private void HandleCandleMessage(string message) { HandleTypedMessage(message, CandleMessageReceived); }
        private void HandleHeartbeatMessage(string message) { HandleTypedMessage(message, HeartbeatMessageReceived); }
        private void HandleMarketTradeMessage(string message) { HandleTypedMessage(message, MarketTradeMessageReceived); }
        private void HandleStatusMessage(string message) { HandleTypedMessage(message, StatusMessageReceived); }
        private void HandleTickerMessage(string message) { HandleTypedMessage(message, TickerMessageReceived); }
        private void HandleTickerBatchMessage(string message) { HandleTypedMessage(message, TickerBatchMessageReceived); }
        private void HandleLevel2Message(string message) { HandleTypedMessage(message, Level2MessageReceived); }
        private void HandleUserMessage(string message) { HandleTypedMessage(message, UserMessageReceived); }

        private void HandleTypedMessage<T>(string message, EventHandler<WebSocketMessageEventArgs<T>> handler)
        {
            if (handler == null)
            {
                return;
            }

            var typedMessage = JsonConvert.DeserializeObject<WebSocketMessage<T>>(message);
            if (typedMessage == null || typedMessage.Events == null || typedMessage.Events.Count == 0)
            {
                return;
            }

            foreach (var ev in typedMessage.Events)
            {
                handler.Invoke(this, new WebSocketMessageEventArgs<T>(ev));
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WebSocketManager));
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                if (_receiveCts != null)
                {
                    _receiveCts.Cancel();
                }
            }
            catch
            {
                // Ignore.
            }

            try
            {
                if (_webSocket != null)
                {
                    _webSocket.Dispose();
                }
            }
            catch
            {
                // Ignore.
            }

            if (_receiveCts != null)
            {
                _receiveCts.Dispose();
            }

            _connectionLock.Dispose();
            _subscriptionLock.Dispose();
        }
    }
}
