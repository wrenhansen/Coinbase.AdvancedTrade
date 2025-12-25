using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coinbase.AdvancedTrade.Enums;
using Coinbase.AdvancedTrade.Interfaces;
using Coinbase.AdvancedTrade.Models;
using Coinbase.AdvancedTrade.Utilities;
using Newtonsoft.Json.Linq;

namespace Coinbase.AdvancedTrade.ExchangeManagers
{
    /// <summary>
    /// Manages order-related activities for the Coinbase Advanced Trade API.
    /// </summary>
    public class OrdersManager : BaseManager, IOrdersManager
    {
        // Coinbase currently allows client_order_id up to 128 characters.
        private const int MaxClientOrderIdLength = 128;

        /// <summary>
        /// Initializes a new instance of the <see cref="OrdersManager"/> class.
        /// </summary>
        /// <param name="authenticator">The authenticator for Coinbase API requests.</param>
        public OrdersManager(CoinbaseAuthenticator authenticator) : base(authenticator)
        {
            if (authenticator == null)
            {
                throw new ArgumentNullException(nameof(authenticator), "Authenticator cannot be null.");
            }
        }

        /// <inheritdoc/>
        public async Task<List<Order>> ListOrdersAsync(
            string productId = null,
            OrderStatus[] orderStatus = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            OrderType? orderType = null,
            OrderSide? orderSide = null)
        {
            ValidateOrderStatus(orderStatus);

            string[] orderStatusStrings = UtilityHelper.EnumToStringArray(orderStatus);
            string startDateString = UtilityHelper.FormatDateToISO8601(startDate);
            string endDateString = UtilityHelper.FormatDateToISO8601(endDate);
            string orderTypeString = orderType?.ToString();
            string orderSideString = orderSide?.ToString();

            var paramsObj = new
            {
                product_id = productId,
                order_status = orderStatusStrings,
                start_date = startDateString,
                end_date = endDateString,
                order_type = orderTypeString,
                order_side = orderSideString
            };

            try
            {
                var response = await _authenticator
                    .SendAuthenticatedRequestAsync("GET", "/api/v3/brokerage/orders/historical/batch", UtilityHelper.ConvertToDictionary(paramsObj))
                    .ConfigureAwait(false) ?? new Dictionary<string, object>();

                return UtilityHelper.DeserializeJsonElement<List<Order>>(response, "orders");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to list orders.", ex);
            }
        }

        private static void ValidateOrderStatus(OrderStatus[] orderStatus)
        {
            if (orderStatus != null && orderStatus.Contains(OrderStatus.OPEN) && orderStatus.Length > 1)
            {
                throw new ArgumentException("Cannot pair OPEN orders with other order types.");
            }
        }

        /// <inheritdoc/>
        public async Task<List<Fill>> ListFillsAsync(
            string orderId = null,
            string productId = null,
            DateTime? startSequenceTimestamp = null,
            DateTime? endSequenceTimestamp = null)
        {
            string startSequenceTimestampString = UtilityHelper.FormatDateToISO8601(startSequenceTimestamp);
            string endSequenceTimestampString = UtilityHelper.FormatDateToISO8601(endSequenceTimestamp);

            var paramsObj = new
            {
                order_id = orderId,
                product_id = productId,
                start_sequence_timestamp = startSequenceTimestampString,
                end_sequence_timestamp = endSequenceTimestampString
            };

            try
            {
                var response = await _authenticator
                    .SendAuthenticatedRequestAsync("GET", "/api/v3/brokerage/orders/historical/fills", UtilityHelper.ConvertToDictionary(paramsObj))
                    .ConfigureAwait(false) ?? new Dictionary<string, object>();

                return UtilityHelper.DeserializeJsonElement<List<Fill>>(response, "fills");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to list fills.", ex);
            }
        }

        /// <inheritdoc/>
        public async Task<Order> GetOrderAsync(string orderId)
        {
            if (string.IsNullOrWhiteSpace(orderId))
            {
                throw new ArgumentException("Order ID cannot be null, empty, or whitespace.", nameof(orderId));
            }

            try
            {
                string endpoint = $"/api/v3/brokerage/orders/historical/{orderId}";

                var response = await _authenticator
                    .SendAuthenticatedRequestAsync("GET", endpoint)
                    .ConfigureAwait(false) ?? new Dictionary<string, object>();

                return UtilityHelper.DeserializeJsonElement<Order>(response, "order");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to get order '{orderId}'.", ex);
            }
        }

        /// <inheritdoc/>
        public async Task<List<CancelOrderResult>> CancelOrdersAsync(string[] orderIds)
        {
            if (orderIds == null || orderIds.Length == 0)
            {
                throw new ArgumentException("Order IDs array cannot be null or empty.", nameof(orderIds));
            }

            try
            {
                var requestBody = new { order_ids = orderIds };

                var response = await _authenticator
                    .SendAuthenticatedRequestAsync("POST", "/api/v3/brokerage/orders/batch_cancel", null, requestBody)
                    .ConfigureAwait(false) ?? new Dictionary<string, object>();

                return UtilityHelper.DeserializeJsonElement<List<CancelOrderResult>>(response, "results");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to cancel orders.", ex);
            }
        }

        /// <summary>
        /// Creates an order based on the provided configuration.
        /// </summary>
        private async Task<string> CreateOrderAsync(string productId, OrderSide side, OrderConfiguration orderConfiguration, string clientOrderId = null)
        {
            EnsureNotNullOrWhitespace(productId, nameof(productId));

            if (side != OrderSide.BUY && side != OrderSide.SELL)
            {
                throw new ArgumentException("Invalid side value provided.", nameof(side));
            }

            if (orderConfiguration is null)
            {
                throw new ArgumentNullException(nameof(orderConfiguration), "Order configuration cannot be null.");
            }

            var resolvedClientOrderId = ResolveClientOrderId(clientOrderId);

            var orderRequest = new Dictionary<string, object>
            {
                { "client_order_id", resolvedClientOrderId },
                { "product_id", productId },
                { "side", side.ToString() },
                { "order_configuration", orderConfiguration }
            };

            try
            {
                var response = await _authenticator
                    .SendAuthenticatedRequestAsync("POST", "/api/v3/brokerage/orders", null, orderRequest)
                    .ConfigureAwait(false) ?? new Dictionary<string, object>();

                // Prefer the documented response shape: success + success_response/error_response.
                if (TryGetBoolean(response, "success", out var success) && success)
                {
                    var orderId = TryExtractOrderId(response);
                    if (!string.IsNullOrWhiteSpace(orderId))
                    {
                        return orderId;
                    }

                    throw new InvalidOperationException("Order creation succeeded but the response did not include 'order_id'.");
                }

                if (response.TryGetValue("error_response", out var errorResponseObj) && errorResponseObj != null)
                {
                    throw BuildOrderCreateException(errorResponseObj);
                }

                // Fallback: some failures might expose a message without an error_response wrapper.
                if (response.TryGetValue("message", out var messageObj) && messageObj != null)
                {
                    throw new InvalidOperationException($"Order creation failed. Message: {messageObj}");
                }

                return null;
            }
            catch (Exception ex) when (!(ex is InvalidOperationException))
            {
                throw new InvalidOperationException($"Failed to create order. ClientOrderId: '{resolvedClientOrderId}'.", ex);
            }
        }

        private static string ResolveClientOrderId(string clientOrderId)
        {
            if (string.IsNullOrWhiteSpace(clientOrderId))
            {
                return Guid.NewGuid().ToString();
            }

            var trimmed = clientOrderId.Trim();

            if (trimmed.Length > MaxClientOrderIdLength)
            {
                throw new ArgumentException($"clientOrderId must be {MaxClientOrderIdLength} characters or fewer.", nameof(clientOrderId));
            }

            return trimmed;
        }

        private static void EnsureNotNullOrWhitespace(string value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"{paramName} cannot be null or empty.", paramName);
            }
        }

        private static bool TryGetBoolean(Dictionary<string, object> dict, string key, out bool value)
        {
            value = false;

            if (dict == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (!dict.TryGetValue(key, out var obj) || obj == null)
            {
                return false;
            }

            if (obj is bool b)
            {
                value = b;
                return true;
            }

            if (obj is JToken token && token.Type == JTokenType.Boolean)
            {
                value = token.Value<bool>();
                return true;
            }

            return bool.TryParse(obj.ToString(), out value);
        }

        private static string TryExtractOrderId(Dictionary<string, object> response)
        {
            if (response == null)
            {
                return null;
            }

            // success_response.order_id is the documented location
            if (response.TryGetValue("success_response", out var successResponseObj) && successResponseObj != null)
            {
                var successObj = CoerceToJObject(successResponseObj);
                var orderId = successObj?.Value<string>("order_id");
                if (!string.IsNullOrWhiteSpace(orderId))
                {
                    return orderId;
                }
            }

            // Fallback: some APIs return order_id top-level
            if (response.TryGetValue("order_id", out var orderIdObj) && orderIdObj != null)
            {
                var orderId = orderIdObj.ToString();
                return string.IsNullOrWhiteSpace(orderId) ? null : orderId;
            }

            return null;
        }

        private static Exception BuildOrderCreateException(object errorResponseObj)
        {
            var errorObj = CoerceToJObject(errorResponseObj);
            if (errorObj == null)
            {
                return new InvalidOperationException($"Order creation failed. Error response: {errorResponseObj}");
            }

            var error = errorObj.Value<string>("error") ?? "Unknown Error";
            var message = errorObj.Value<string>("message") ?? "No Message";
            var details = errorObj.Value<string>("error_details") ?? "No Details";

            return new InvalidOperationException($"Order creation failed. Error: {error}. Message: {message}. Details: {details}");
        }

        private static JObject CoerceToJObject(object obj)
        {
            if (obj == null)
            {
                return null;
            }

            if (obj is JObject jObject)
            {
                return jObject;
            }

            if (obj is JToken token && token.Type == JTokenType.Object)
            {
                return (JObject)token;
            }

            var asString = obj.ToString();
            if (!string.IsNullOrWhiteSpace(asString))
            {
                var trimmed = asString.Trim();
                if (trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal))
                {
                    try
                    {
                        return JObject.Parse(trimmed);
                    }
                    catch
                    {
                        // Ignore and return null below.
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Attempts to retrieve an order by its ID with retry logic.
        /// </summary>
        private async Task<Order> GetOrderWithRetryAsync(string orderId, int maxRetries = 20, int delay = 500)
        {
            if (string.IsNullOrWhiteSpace(orderId))
            {
                throw new ArgumentException("Order ID cannot be null.", nameof(orderId));
            }

            if (maxRetries <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxRetries), "maxRetries must be greater than zero.");
            }

            if (delay < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(delay), "delay must be greater than or equal to zero.");
            }

            Order order = null;
            int retryCount = 0;

            while (retryCount < maxRetries)
            {
                order = await GetOrderAsync(orderId).ConfigureAwait(false);
                if (order != null)
                {
                    break;
                }

                retryCount++;

                if (delay > 0)
                {
                    await Task.Delay(delay).ConfigureAwait(false);
                }
            }

            return order;
        }

        /// <inheritdoc/>
        public async Task<string> CreateMarketOrderAsync(string productId, OrderSide side, string size, string clientOrderId = null)
        {
            EnsureNotNullOrWhitespace(productId, nameof(productId));
            EnsureNotNullOrWhitespace(size, nameof(size));

            MarketIoc marketDetails;
            switch (side)
            {
                case OrderSide.BUY:
                    marketDetails = new MarketIoc { QuoteSize = size };
                    break;
                case OrderSide.SELL:
                    marketDetails = new MarketIoc { BaseSize = size };
                    break;
                default:
                    throw new ArgumentException($"Invalid order side provided: {side}.", nameof(side));
            }

            var orderConfiguration = new OrderConfiguration
            {
                MarketIoc = marketDetails
            };

            return await CreateOrderAsync(productId, side, orderConfiguration, clientOrderId).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<Order> CreateMarketOrderAsync(string productId, OrderSide side, string amount, bool returnOrder = true, string clientOrderId = null)
        {
            if (!returnOrder)
            {
                throw new ArgumentException("returnOrder must be true to return an Order object.", nameof(returnOrder));
            }

            var orderId = await CreateMarketOrderAsync(productId, side, amount, clientOrderId).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(orderId) ? null : await GetOrderWithRetryAsync(orderId).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<string> CreateLimitOrderGTCAsync(string productId, OrderSide side, string baseSize, string limitPrice, bool postOnly = true, string clientOrderId = null)
        {
            EnsureNotNullOrWhitespace(productId, nameof(productId));
            EnsureNotNullOrWhitespace(baseSize, nameof(baseSize));
            EnsureNotNullOrWhitespace(limitPrice, nameof(limitPrice));

            var orderConfiguration = new OrderConfiguration
            {
                LimitGtc = new LimitGtc
                {
                    BaseSize = baseSize,
                    LimitPrice = limitPrice,
                    PostOnly = postOnly
                }
            };

            return await CreateOrderAsync(productId, side, orderConfiguration, clientOrderId).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<Order> CreateLimitOrderGTCAsync(string productId, OrderSide side, string baseSize, string limitPrice, bool postOnly, bool returnOrder = true, string clientOrderId = null)
        {
            if (!returnOrder)
            {
                throw new ArgumentException("returnOrder must be true to return an Order object.", nameof(returnOrder));
            }

            var orderId = await CreateLimitOrderGTCAsync(productId, side, baseSize, limitPrice, postOnly, clientOrderId).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(orderId) ? null : await GetOrderWithRetryAsync(orderId).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<string> CreateLimitOrderGTDAsync(string productId, OrderSide side, string baseSize, string limitPrice, DateTime endTime, bool postOnly = true, string clientOrderId = null)
        {
            EnsureNotNullOrWhitespace(productId, nameof(productId));
            EnsureNotNullOrWhitespace(baseSize, nameof(baseSize));
            EnsureNotNullOrWhitespace(limitPrice, nameof(limitPrice));

            var endUtc = endTime.ToUniversalTime();
            if (endUtc <= DateTime.UtcNow)
            {
                throw new ArgumentException("End time should be in the future.", nameof(endTime));
            }

            var orderConfig = new OrderConfiguration
            {
                LimitGtd = new LimitGtd
                {
                    BaseSize = baseSize,
                    LimitPrice = limitPrice,
                    EndTime = endUtc,
                    PostOnly = postOnly
                }
            };

            return await CreateOrderAsync(productId, side, orderConfig, clientOrderId).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<Order> CreateLimitOrderGTDAsync(string productId, OrderSide side, string baseSize, string limitPrice, DateTime endTime, bool postOnly = true, bool returnOrder = true, string clientOrderId = null)
        {
            if (!returnOrder)
            {
                throw new ArgumentException("returnOrder must be true to return an Order object.", nameof(returnOrder));
            }

            var orderId = await CreateLimitOrderGTDAsync(productId, side, baseSize, limitPrice, endTime, postOnly, clientOrderId).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(orderId) ? null : await GetOrderWithRetryAsync(orderId).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<string> CreateStopLimitOrderGTCAsync(string productId, OrderSide side, string baseSize, string limitPrice, string stopPrice, string clientOrderId = null)
        {
            EnsureNotNullOrWhitespace(productId, nameof(productId));
            EnsureNotNullOrWhitespace(baseSize, nameof(baseSize));
            EnsureNotNullOrWhitespace(limitPrice, nameof(limitPrice));
            EnsureNotNullOrWhitespace(stopPrice, nameof(stopPrice));

            var stopDirection = GetStopDirection(side);

            var orderConfig = new OrderConfiguration
            {
                StopLimitGtc = new StopLimitGtc
                {
                    BaseSize = baseSize,
                    LimitPrice = limitPrice,
                    StopPrice = stopPrice,
                    StopDirection = stopDirection
                }
            };

            return await CreateOrderAsync(productId, side, orderConfig, clientOrderId).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<Order> CreateStopLimitOrderGTCAsync(string productId, OrderSide side, string baseSize, string limitPrice, string stopPrice, bool returnOrder = true, string clientOrderId = null)
        {
            if (!returnOrder)
            {
                throw new ArgumentException("returnOrder must be true to return an Order object.", nameof(returnOrder));
            }

            var orderId = await CreateStopLimitOrderGTCAsync(productId, side, baseSize, limitPrice, stopPrice, clientOrderId).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(orderId) ? null : await GetOrderWithRetryAsync(orderId).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<string> CreateStopLimitOrderGTDAsync(string productId, OrderSide side, string baseSize, string limitPrice, string stopPrice, DateTime endTime, string clientOrderId = null)
        {
            EnsureNotNullOrWhitespace(productId, nameof(productId));
            EnsureNotNullOrWhitespace(baseSize, nameof(baseSize));
            EnsureNotNullOrWhitespace(limitPrice, nameof(limitPrice));
            EnsureNotNullOrWhitespace(stopPrice, nameof(stopPrice));

            var endUtc = endTime.ToUniversalTime();
            if (endUtc <= DateTime.UtcNow)
            {
                throw new ArgumentException("End time should be in the future.", nameof(endTime));
            }

            var stopDirection = GetStopDirection(side);

            var orderConfig = new OrderConfiguration
            {
                StopLimitGtd = new StopLimitGtd
                {
                    BaseSize = baseSize,
                    LimitPrice = limitPrice,
                    StopPrice = stopPrice,
                    StopDirection = stopDirection,
                    EndTime = endUtc
                }
            };

            return await CreateOrderAsync(productId, side, orderConfig, clientOrderId).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<Order> CreateStopLimitOrderGTDAsync(string productId, OrderSide side, string baseSize, string limitPrice, string stopPrice, DateTime endTime, bool returnOrder = true, string clientOrderId = null)
        {
            if (!returnOrder)
            {
                throw new ArgumentException("returnOrder must be true to return an Order object.", nameof(returnOrder));
            }

            var orderId = await CreateStopLimitOrderGTDAsync(productId, side, baseSize, limitPrice, stopPrice, endTime, clientOrderId).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(orderId) ? null : await GetOrderWithRetryAsync(orderId).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<string> CreateSORLimitIOCOrderAsync(string productId, OrderSide side, string baseSize, string limitPrice, string clientOrderId = null)
        {
            EnsureNotNullOrWhitespace(productId, nameof(productId));
            EnsureNotNullOrWhitespace(baseSize, nameof(baseSize));
            EnsureNotNullOrWhitespace(limitPrice, nameof(limitPrice));

            var orderConfiguration = new OrderConfiguration
            {
                SorLimitIoc = new SorLimitIoc
                {
                    BaseSize = baseSize,
                    LimitPrice = limitPrice
                }
            };

            return await CreateOrderAsync(productId, side, orderConfiguration, clientOrderId).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<Order> CreateSORLimitIOCOrderAsync(string productId, OrderSide side, string baseSize, string limitPrice, bool returnOrder = true, string clientOrderId = null)
        {
            if (!returnOrder)
            {
                throw new ArgumentException("returnOrder must be true to return an Order object.", nameof(returnOrder));
            }

            var orderId = await CreateSORLimitIOCOrderAsync(productId, side, baseSize, limitPrice, clientOrderId).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(orderId) ? null : await GetOrderWithRetryAsync(orderId).ConfigureAwait(false);
        }

        private static string GetStopDirection(OrderSide side)
        {
            switch (side)
            {
                case OrderSide.BUY:
                    return "STOP_DIRECTION_STOP_UP";
                case OrderSide.SELL:
                    return "STOP_DIRECTION_STOP_DOWN";
                default:
                    throw new ArgumentException($"Invalid order side provided: {side}.", nameof(side));
            }
        }

        /// <inheritdoc/>
        public async Task<bool> EditOrderAsync(string orderId, string price, string size)
        {
            EnsureNotNullOrWhitespace(orderId, nameof(orderId));
            EnsureNotNullOrWhitespace(price, nameof(price));
            EnsureNotNullOrWhitespace(size, nameof(size));

            var requestBody = new
            {
                order_id = orderId,
                price,
                size
            };

            try
            {
                var response = await _authenticator
                    .SendAuthenticatedRequestAsync("POST", "/api/v3/brokerage/orders/edit", null, requestBody)
                    .ConfigureAwait(false) ?? new Dictionary<string, object>();

                var responseObject = UtilityHelper.DeserializeDictionary<Dictionary<string, JToken>>(response);

                if (responseObject != null &&
                    responseObject.TryGetValue("success", out var successValue) &&
                    successValue != null &&
                    successValue.Type == JTokenType.Boolean &&
                    successValue.ToObject<bool>())
                {
                    return true;
                }

                var errorMessage = "Failed to edit order.";

                if (responseObject?.TryGetValue("errors", out var errorsValue) == true &&
                    errorsValue is JArray errorsArray &&
                    errorsArray.Any())
                {
                    var errorDetails = errorsArray.FirstOrDefault();
                    if (errorDetails != null && errorDetails["edit_failure_reason"] != null)
                    {
                        errorMessage += $" Reason: {errorDetails["edit_failure_reason"]}";
                    }
                }

                throw new InvalidOperationException(errorMessage);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to edit order due to an exception.", ex);
            }
        }

        /// <inheritdoc/>
        public async Task<EditOrderPreviewResult> EditOrderPreviewAsync(string orderId, string price, string size)
        {
            EnsureNotNullOrWhitespace(orderId, nameof(orderId));
            EnsureNotNullOrWhitespace(price, nameof(price));
            EnsureNotNullOrWhitespace(size, nameof(size));

            var requestBody = new
            {
                order_id = orderId,
                price,
                size
            };

            try
            {
                var response = await _authenticator
                    .SendAuthenticatedRequestAsync("POST", "/api/v3/brokerage/orders/edit_preview", null, requestBody)
                    .ConfigureAwait(false) ?? new Dictionary<string, object>();

                var responseObject = UtilityHelper.DeserializeDictionary<Dictionary<string, JToken>>(response);

                if (responseObject != null &&
                    responseObject.TryGetValue("errors", out var errorsValue) &&
                    errorsValue is JArray errorsArray &&
                    errorsArray.Any())
                {
                    var errorMessage = "Failed to preview order edit.";

                    foreach (var errorObj in errorsArray)
                    {
                        if (errorObj is JObject errorObject)
                        {
                            if (errorObject.TryGetValue("edit_failure_reason", out var editFailureReason))
                            {
                                errorMessage += $" Edit Failure Reason: {editFailureReason}.";
                            }

                            if (errorObject.TryGetValue("preview_failure_reason", out var previewFailureReason))
                            {
                                errorMessage += $" Preview Failure Reason: {previewFailureReason}.";
                            }
                        }
                    }

                    throw new InvalidOperationException(errorMessage);
                }

                var result = new EditOrderPreviewResult
                {
                    Slippage = responseObject.TryGetValue("slippage", out var tempValue) ? tempValue.ToString() : string.Empty,
                    OrderTotal = responseObject.TryGetValue("order_total", out tempValue) ? tempValue.ToString() : string.Empty,
                    CommissionTotal = responseObject.TryGetValue("commission_total", out tempValue) ? tempValue.ToString() : string.Empty,
                    QuoteSize = responseObject.TryGetValue("quote_size", out tempValue) ? tempValue.ToString() : string.Empty,
                    BaseSize = responseObject.TryGetValue("base_size", out tempValue) ? tempValue.ToString() : string.Empty,
                    BestBid = responseObject.TryGetValue("best_bid", out tempValue) ? tempValue.ToString() : string.Empty,
                    BestAsk = responseObject.TryGetValue("best_ask", out tempValue) ? tempValue.ToString() : string.Empty,
                    AverageFilledPrice = responseObject.TryGetValue("average_filled_price", out tempValue) ? tempValue.ToString() : string.Empty
                };

                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to preview order edit due to an exception.", ex);
            }
        }
    }
}
