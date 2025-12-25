using System;
using Coinbase.AdvancedTrade.Enums;
using Coinbase.AdvancedTrade.ExchangeManagers;
using Coinbase.AdvancedTrade.Interfaces;

namespace Coinbase.AdvancedTrade
{
    /// <summary>
    /// Provides access to the Coinbase Advanced Trade API.
    /// </summary>
    public sealed class CoinbaseClient : IDisposable
    {
        /// <summary>
        /// Default WebSocket endpoint for market data.
        /// </summary>
        public const string DefaultMarketWebSocketUri = "wss://advanced-trade-ws.coinbase.com";

        /// <summary>
        /// Default WebSocket buffer size (5 MiB).
        /// </summary>
        public const int DefaultWebSocketBufferSize = 5 * 1024 * 1024;

        /// <summary>
        /// Account endpoints.
        /// </summary>
        public IAccountsManager Accounts { get; }

        /// <summary>
        /// Product endpoints.
        /// </summary>
        public IProductsManager Products { get; }

        /// <summary>
        /// Order endpoints.
        /// </summary>
        public IOrdersManager Orders { get; }

        /// <summary>
        /// Fee endpoints.
        /// </summary>
        public IFeesManager Fees { get; }

        /// <summary>
        /// Public endpoints.
        /// </summary>
        public IPublicManager Public { get; }

        /// <summary>
        /// WebSocket manager for real-time feeds.
        /// </summary>
        public WebSocketManager WebSocket { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CoinbaseClient"/> class.
        /// </summary>
        /// <param name="apiKey">The API key for authentication.</param>
        /// <param name="apiSecret">The API secret for authentication.</param>
        /// <param name="websocketBufferSize">The buffer size for WebSocket messages in bytes.</param>
        /// <param name="apiKeyType">API key type (CDP or legacy).</param>
        public CoinbaseClient(
            string apiKey,
            string apiSecret,
            int websocketBufferSize = DefaultWebSocketBufferSize,
            ApiKeyType apiKeyType = ApiKeyType.CoinbaseDeveloperPlatform)
            : this(apiKey, apiSecret, DefaultMarketWebSocketUri, websocketBufferSize, apiKeyType)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CoinbaseClient"/> class with a specific WebSocket endpoint.
        /// </summary>
        /// <param name="apiKey">The API key for authentication.</param>
        /// <param name="apiSecret">The API secret for authentication.</param>
        /// <param name="webSocketUri">The WebSocket endpoint URI.</param>
        /// <param name="websocketBufferSize">The buffer size for WebSocket messages in bytes.</param>
        /// <param name="apiKeyType">API key type (CDP or legacy).</param>
        public CoinbaseClient(
            string apiKey,
            string apiSecret,
            string webSocketUri,
            int websocketBufferSize = DefaultWebSocketBufferSize,
            ApiKeyType apiKeyType = ApiKeyType.CoinbaseDeveloperPlatform)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("API key cannot be null or empty.", nameof(apiKey));
            }

            if (string.IsNullOrWhiteSpace(apiSecret))
            {
                throw new ArgumentException("API secret cannot be null or empty.", nameof(apiSecret));
            }

            if (string.IsNullOrWhiteSpace(webSocketUri))
            {
                throw new ArgumentException("WebSocket URI cannot be null or empty.", nameof(webSocketUri));
            }

            if (websocketBufferSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(websocketBufferSize), "WebSocket buffer size must be greater than zero.");
            }

            var authenticator = new CoinbaseAuthenticator(apiKey, apiSecret, apiKeyType);

            Accounts = new AccountsManager(authenticator);
            Products = new ProductsManager(authenticator);
            Orders = new OrdersManager(authenticator);
            Fees = new FeesManager(authenticator);
            Public = new PublicManager(authenticator);

            WebSocket = new WebSocketManager(webSocketUri, apiKey, apiSecret, websocketBufferSize, apiKeyType);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            WebSocket?.Dispose();
        }
    }
}
