// Changes:
// - Added support for caller-supplied clientOrderId (customer order ID) for all create order methods.
// - Preserved existing public method signatures by forwarding to new overloads.
// - Fixed CreateLimitOrderGTDAsync(Order-returning overload) to call GTD instead of GTC.

using Coinbase.AdvancedTrade.Enums;
using Coinbase.AdvancedTrade.Interfaces;
using Coinbase.AdvancedTrade.Models;
using Coinbase.AdvancedTrade.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Coinbase.AdvancedTrade.ExchangeManagers
{
    /// <summary>
    /// Manages order-related activities for the Coinbase Advanced Trade API.
    /// </summary>
    public class OrdersManager : BaseManager, IOrdersManager
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OrdersManager"/> class.
        /// </summary>
        /// <param name="authenticator">The authenticator for Coinbase API requests.</param>
        /// <exception cref="ArgumentNullException">Thrown if the provided authenticator is null.</exception>
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
                var response = await _authenticator.SendAuthenticatedRequestAsync(
                        "GET",
                        "/api/v3/brokerage/orders/historical/batch",
                        UtilityHelper.ConvertToDictionary(paramsObj))
                    ?? new Dictionary<string, object>();

                return UtilityHelper.DeserializeJsonElement<List<Order>>(response, "orders");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to list orders", ex);
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
                var response = await _authenticator.SendAuthenticatedRequestAsync(
                        "GET",
                        "/api/v3/brokerage/orders/historical/fills",
                        UtilityHelper.ConvertToDictionary(paramsObj))
                    ?? new Dictionary<string, object>();

                return UtilityHelper.DeserializeJsonElement<List<Fill>>(response, "fills");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to list fills", ex);
            }
        }

        /// <inheritdoc/>
        public async Task<Order> GetOrderAsync(string orderId)
        {
            if (string.IsNullOrWhiteSpace(orderId))
            {
                throw new ArgumentException("Order ID cannot be null, empty, or consist only of white-space characters.", nameof(orderId));
            }

            try
            {
                string endpoint = $"/api/v3/brokerage/orders/historical/{orderId}";

                var response = await _authenticator.SendAuthenticatedRequestAsync("GET", endpoint)
                    ?? new Dictionary<string, object>();

                return UtilityHelper.DeserializeJsonElement<Order>(response, "order");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to get the order", ex);
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

                var response = await _authenticator.SendAuthenticatedRequestAsync(
                        "POST",
                        "/api/v3/brokerage/orders/batch_cancel",
                        null,
                        requestBody)
                    ?? new Dictionary<string, object>();

                return UtilityHelper.DeserializeJsonElement<List<CancelOrderResult>>(response, "results");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to cancel orders", ex);
            }
        }

        private static string ResolveClientOrderId(string clientOrderId)
        {
            return string.IsNullOrWhiteSpace(clientOrderId)
                ? Guid.NewGuid().ToString()
                : clientOrderId;
        }

        /// <summary>
        /// Creates an order based on the provided configurations.
        /// </summary>
        /// <param name="productId">The ID of the product for the order.</param>
        /// <param name="side">Specifies whether to buy or sell.</param>
        /// <param name="orderConfiguration">Configuration details for the order.</param>
        /// <param name="clientOrderId">
        /// Optional caller-provided ID for Coinbase "client_order_id". If null or whitespace, a GUID is generated.
        /// </param>
        /// <returns>Order ID upon successful order creation; otherwise, null.</returns>
        private async Task<string> CreateOrderAsync(string productId, OrderSide side, OrderConfiguration orderConfiguration, string clientOrderId = null)
        {
            if (string.IsNullOrWhiteSpace(productId))
            {
                throw new ArgumentException("Product ID cannot be null, empty, or consist only of white-space characters.", nameof(productId));
            }

            if (side != OrderSide.BUY && side != OrderSide.SELL)
            {
                throw new ArgumentException("Invalid side value provided.", nameof(side));
            }

            if (orderConfiguration is null)
            {
                throw new ArgumentNullException(nameof(orderConfiguration), "Order configuration cannot be null.");
            }

            try
            {
                var resolvedClientOrderId = ResolveClientOrderId(clientOrderId);

                var orderRequest = new
                {
                    client_order_id = resolvedClientOrderId,
                    product_id = productId,
                    side = side.ToString(),
                    order_configuration = orderConfiguration
                };

                var response = await _authenticator.SendAuthenticatedRequestAsync(
                        "POST",
                        "/api/v3/brokerage/orders",
                        null,
                        orderRequest)
                    ?? new Dictionary<string, object>();

                if (response.TryGetValue("success_response", out var successResponse))
                {
                    var successResponseStr = successResponse?.ToString();
                    if (!string.IsNullOrEmpty(successResponseStr))
                    {
                        var successResponseDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(successResponseStr);
                        if (successResponseDict?.TryGetValue("order_id", out var orderId) == true)
                        {
                            return orderId;
                        }
                    }
                }
                else if (response.ContainsKey("error_response"))
                {
                    var errorResponseObj = response["error_response"];

                    if (errorResponseObj is JObject errorResponseObject)
                    {
                        var errorResponseValue = errorResponseObject.ToString();
                        if (!string.IsNullOrEmpty(errorResponseValue))
                        {
                            var errorResponse = JsonConvert.DeserializeObject<Dictionary<string, string>>(errorResponseValue);
                            if (errorResponse != null)
                            {
                                var error = errorResponse.ContainsKey("error") ? errorResponse["error"] : "Unknown Error";
                                var message = errorResponse.ContainsKey("message") ? errorResponse["message"] : "No Message";
                                var errorDetails = errorResponse.ContainsKey("error_details") ? errorResponse["error_details"] : "No Details";

                                throw new Exception($"Order creation failed. Error: {error}. Message: {message}. Details: {errorDetails}");
                            }
                        }
                    }

                    return null;
                }

                return null;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to create order: {ex.Message}", ex);
            }
        }

        private async Task<Order> GetOrderWithRetryAsync(string orderId, int maxRetries = 20, int delay = 500)
        {
            if (string.IsNullOrWhiteSpace(orderId))
            {
                throw new ArgumentException("Order ID cannot be null", nameof(orderId));
            }

            Order order = null;
            int retryCount = 0;

            while (retryCount < maxRetries)
            {
                order = await GetOrderAsync(orderId);
                if (order != null)
                {
                    break;
                }

                retryCount++;
                await Task.Delay(delay);
            }

            return order;
        }

        // -------------------------
        // Market Orders
        // -------------------------

        /// <inheritdoc/>
        public Task<string> CreateMarketOrderAsync(string productId, OrderSide side, string amount)
        {
            return CreateMarketOrderAsync(productId, side, amount, clientOrderId: null);
        }

        /// <summary>
        /// Creates a market IOC order with an optional caller-provided clientOrderId.
        /// </summary>
        public async Task<string> CreateMarketOrderAsync(string productId, OrderSide side, string amount, string clientOrderId)
        {
            if (string.IsNullOrEmpty(productId))
            {
                throw new ArgumentException("Product ID cannot be null or empty.", nameof(productId));
            }

            MarketIoc marketDetails;
            switch (side)
            {
                case OrderSide.BUY:
                    marketDetails = new MarketIoc { QuoteSize = amount };
                    break;
                case OrderSide.SELL:
                    marketDetails = new MarketIoc { BaseSize = amount };
                    break;
                default:
                    throw new ArgumentException($"Invalid order side provided: {side}.");
            }

            var orderConfiguration = new OrderConfiguration
            {
                MarketIoc = marketDetails
            };

            return await CreateOrderAsync(productId, side, orderConfiguration, clientOrderId);
        }

        /// <inheritdoc/>
        public Task<Order> CreateMarketOrderAsync(string productId, OrderSide side, string amount, bool returnOrder = true)
        {
            return CreateMarketOrderAsync(productId, side, amount, clientOrderId: null, returnOrder: returnOrder);
        }

        /// <summary>
        /// Creates a market IOC order and optionally returns the Order object, with an optional caller-provided clientOrderId.
        /// </summary>
        public async Task<Order> CreateMarketOrderAsync(string productId, OrderSide side, string amount, string clientOrderId, bool returnOrder = true)
        {
            if (!returnOrder)
            {
                throw new ArgumentException("returnOrder must be true to return an Order object.", nameof(returnOrder));
            }

            string orderId = await CreateMarketOrderAsync(productId, side, amount, clientOrderId);
            return await GetOrderWithRetryAsync(orderId);
        }

        // -------------------------
        // Limit Orders - GTC
        // -------------------------

        /// <inheritdoc/>
        public Task<string> CreateLimitOrderGTCAsync(string productId, OrderSide side, string baseSize, string limitPrice, bool postOnly)
        {
            return CreateLimitOrderGTCAsync(productId, side, baseSize, limitPrice, postOnly, clientOrderId: null);
        }

        /// <summary>
        /// Creates a GTC limit order with an optional caller-provided clientOrderId.
        /// </summary>
        public async Task<string> CreateLimitOrderGTCAsync(string productId, OrderSide side, string baseSize, string limitPrice, bool postOnly, string clientOrderId)
        {
            if (string.IsNullOrEmpty(productId))
            {
                throw new ArgumentException("Product ID cannot be null or empty.", nameof(productId));
            }

            if (string.IsNullOrEmpty(baseSize))
            {
                throw new ArgumentException("Base size cannot be null or empty.", nameof(baseSize));
            }

            if (string.IsNullOrEmpty(limitPrice))
            {
                throw new ArgumentException("Limit price cannot be null or empty.", nameof(limitPrice));
            }

            var orderConfiguration = new OrderConfiguration
            {
                LimitGtc = new LimitGtc
                {
                    BaseSize = baseSize,
                    LimitPrice = limitPrice,
                    PostOnly = postOnly
                }
            };

            return await CreateOrderAsync(productId, side, orderConfiguration, clientOrderId);
        }

        /// <inheritdoc/>
        public Task<Order> CreateLimitOrderGTCAsync(string productId, OrderSide side, string baseSize, string limitPrice, bool postOnly, bool returnOrder = true)
        {
            return CreateLimitOrderGTCAsync(productId, side, baseSize, limitPrice, postOnly, clientOrderId: null, returnOrder: returnOrder);
        }

        /// <summary>
        /// Creates a GTC limit order and returns the Order object, with an optional caller-provided clientOrderId.
        /// </summary>
        public async Task<Order> CreateLimitOrderGTCAsync(string productId, OrderSide side, string baseSize, string limitPrice, bool postOnly, string clientOrderId, bool returnOrder = true)
        {
            if (!returnOrder)
            {
                throw new ArgumentException("returnOrder must be true to return an Order object.", nameof(returnOrder));
            }

            string orderId = await CreateLimitOrderGTCAsync(productId, side, baseSize, limitPrice, postOnly, clientOrderId);
            return await GetOrderWithRetryAsync(orderId);
        }

        // -------------------------
        // Limit Orders - GTD
        // -------------------------

        /// <inheritdoc/>
        public Task<string> CreateLimitOrderGTDAsync(string productId, OrderSide side, string baseSize, string limitPrice, DateTime endTime, bool postOnly = true)
        {
            return CreateLimitOrderGTDAsync(productId, side, baseSize, limitPrice, endTime, clientOrderId: null, postOnly: postOnly);
        }

        /// <summary>
        /// Creates a GTD limit order with an optional caller-provided clientOrderId.
        /// </summary>
        public async Task<string> CreateLimitOrderGTDAsync(string productId, OrderSide side, string baseSize, string limitPrice, DateTime endTime, string clientOrderId, bool postOnly = true)
        {
            if (string.IsNullOrEmpty(productId))
            {
                throw new ArgumentException("Product ID cannot be null or empty.", nameof(productId));
            }

            if (string.IsNullOrEmpty(baseSize))
            {
                throw new ArgumentException("Base size cannot be null or empty.", nameof(baseSize));
            }

            if (string.IsNullOrEmpty(limitPrice))
            {
                throw new ArgumentException("Limit price cannot be null or empty.", nameof(limitPrice));
            }

            if (endTime <= DateTime.UtcNow)
            {
                throw new ArgumentException("End time should be in the future.", nameof(endTime));
            }

            var orderConfig = new OrderConfiguration
            {
                LimitGtd = new LimitGtd
                {
                    BaseSize = baseSize,
                    LimitPrice = limitPrice,
                    EndTime = endTime,
                    PostOnly = postOnly
                }
            };

            return await CreateOrderAsync(productId, side, orderConfig, clientOrderId);
        }

        /// <inheritdoc/>
        public Task<Order> CreateLimitOrderGTDAsync(string productId, OrderSide side, string baseSize, string limitPrice, DateTime endTime, bool postOnly = true, bool returnOrder = true)
        {
            return CreateLimitOrderGTDAsync(productId, side, baseSize, limitPrice, endTime, clientOrderId: null, postOnly: postOnly, returnOrder: returnOrder);
        }

        /// <summary>
        /// Creates a GTD limit order and returns the Order object, with an optional caller-provided clientOrderId.
        /// </summary>
        public async Task<Order> CreateLimitOrderGTDAsync(string productId, OrderSide side, string baseSize, string limitPrice, DateTime endTime, string clientOrderId, bool postOnly = true, bool returnOrder = true)
        {
            if (!returnOrder)
            {
                throw new ArgumentException("returnOrder must be true to return an Order object.", nameof(returnOrder));
            }

            // Fixed: This must call GTD (not GTC).
            string orderId = await CreateLimitOrderGTDAsync(productId, side, baseSize, limitPrice, endTime, clientOrderId, postOnly);
            return await GetOrderWithRetryAsync(orderId);
        }

        // -------------------------
        // Stop Limit Orders - GTC
        // -------------------------

        /// <inheritdoc/>
        public Task<string> CreateStopLimitOrderGTCAsync(string productId, OrderSide side, string baseSize, string limitPrice, string stopPrice)
        {
            return CreateStopLimitOrderGTCAsync(productId, side, baseSize, limitPrice, stopPrice, clientOrderId: null);
        }

        /// <summary>
        /// Creates a GTC stop limit order with an optional caller-provided clientOrderId.
        /// </summary>
        public async Task<string> CreateStopLimitOrderGTCAsync(string productId, OrderSide side, string baseSize, string limitPrice, string stopPrice, string clientOrderId)
        {
            if (string.IsNullOrEmpty(productId))
            {
                throw new ArgumentException("Product ID cannot be null or empty.", nameof(productId));
            }

            if (string.IsNullOrEmpty(baseSize))
            {
                throw new ArgumentException("Base size cannot be null or empty.", nameof(baseSize));
            }

            if (string.IsNullOrEmpty(limitPrice))
            {
                throw new ArgumentException("Limit price cannot be null or empty.", nameof(limitPrice));
            }

            if (string.IsNullOrEmpty(stopPrice))
            {
                throw new ArgumentException("Stop price cannot be null or empty.", nameof(stopPrice));
            }

            string stopDirection;
            switch (side)
            {
                case OrderSide.BUY:
                    stopDirection = "STOP_DIRECTION_STOP_UP";
                    break;
                case OrderSide.SELL:
                    stopDirection = "STOP_DIRECTION_STOP_DOWN";
                    break;
                default:
                    throw new ArgumentException($"Invalid order side provided: {side}.");
            }

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

            return await CreateOrderAsync(productId, side, orderConfig, clientOrderId);
        }

        /// <inheritdoc/>
        public Task<Order> CreateStopLimitOrderGTCAsync(string productId, OrderSide side, string baseSize, string limitPrice, string stopPrice, bool returnOrder = true)
        {
            return CreateStopLimitOrderGTCAsync(productId, side, baseSize, limitPrice, stopPrice, clientOrderId: null, returnOrder: returnOrder);
        }

        /// <summary>
        /// Creates a GTC stop limit order and returns the Order object, with an optional caller-provided clientOrderId.
        /// </summary>
        public async Task<Order> CreateStopLimitOrderGTCAsync(string productId, OrderSide side, string baseSize, string limitPrice, string stopPrice, string clientOrderId, bool returnOrder = true)
        {
            if (!returnOrder)
            {
                throw new ArgumentException("returnOrder must be true to return an Order object.", nameof(returnOrder));
            }

            string orderId = await CreateStopLimitOrderGTCAsync(productId, side, baseSize, limitPrice, stopPrice, clientOrderId);
            return await GetOrderWithRetryAsync(orderId);
        }

        // -------------------------
        // Stop Limit Orders - GTD
        // -------------------------

        /// <inheritdoc/>
        public Task<string> CreateStopLimitOrderGTDAsync(string productId, OrderSide side, string baseSize, string limitPrice, string stopPrice, DateTime endTime)
        {
            return CreateStopLimitOrderGTDAsync(productId, side, baseSize, limitPrice, stopPrice, endTime, clientOrderId: null);
        }

        /// <summary>
        /// Creates a GTD stop limit order with an optional caller-provided clientOrderId.
        /// </summary>
        public async Task<string> CreateStopLimitOrderGTDAsync(string productId, OrderSide side, string baseSize, string limitPrice, string stopPrice, DateTime endTime, string clientOrderId)
        {
            if (string.IsNullOrEmpty(productId))
            {
                throw new ArgumentException("Product ID cannot be null or empty.", nameof(productId));
            }

            if (string.IsNullOrEmpty(baseSize))
            {
                throw new ArgumentException("Base size cannot be null or empty.", nameof(baseSize));
            }

            if (string.IsNullOrEmpty(limitPrice))
            {
                throw new ArgumentException("Limit price cannot be null or empty.", nameof(limitPrice));
            }

            if (string.IsNullOrEmpty(stopPrice))
            {
                throw new ArgumentException("Stop price cannot be null or empty.", nameof(stopPrice));
            }

            if (endTime <= DateTime.UtcNow)
            {
                throw new ArgumentException("End time should be in the future.", nameof(endTime));
            }

            string stopDirection;
            switch (side)
            {
                case OrderSide.BUY:
                    stopDirection = "STOP_DIRECTION_STOP_UP";
                    break;
                case OrderSide.SELL:
                    stopDirection = "STOP_DIRECTION_STOP_DOWN";
                    break;
                default:
                    throw new ArgumentException($"Invalid order side provided: {side}.");
            }

            var orderConfig = new OrderConfiguration
            {
                StopLimitGtd = new StopLimitGtd
                {
                    BaseSize = baseSize,
                    LimitPrice = limitPrice,
                    StopPrice = stopPrice,
                    StopDirection = stopDirection,
                    EndTime = endTime
                }
            };

            return await CreateOrderAsync(productId, side, orderConfig, clientOrderId);
        }

        /// <inheritdoc/>
        public Task<Order> CreateStopLimitOrderGTDAsync(string productId, OrderSide side, string baseSize, string limitPrice, string stopPrice, DateTime endTime, bool returnOrder = true)
        {
            return CreateStopLimitOrderGTDAsync(productId, side, baseSize, limitPrice, stopPrice, endTime, clientOrderId: null, returnOrder: returnOrder);
        }

        /// <summary>
        /// Creates a GTD stop limit order and returns the Order object, with an optional caller-provided clientOrderId.
        /// </summary>
        public async Task<Order> CreateStopLimitOrderGTDAsync(string productId, OrderSide side, string baseSize, string limitPrice, string stopPrice, DateTime endTime, string clientOrderId, bool returnOrder = true)
        {
            if (!returnOrder)
            {
                throw new ArgumentException("returnOrder must be true to return an Order object.", nameof(returnOrder));
            }

            string orderId = await CreateStopLimitOrderGTDAsync(productId, side, baseSize, limitPrice, stopPrice, endTime, clientOrderId);
            return await GetOrderWithRetryAsync(orderId);
        }

        // -------------------------
        // SOR Limit IOC Orders
        // -------------------------

        /// <inheritdoc/>
        public Task<string> CreateSORLimitIOCOrderAsync(string productId, OrderSide side, string baseSize, string limitPrice)
        {
            return CreateSORLimitIOCOrderAsync(productId, side, baseSize, limitPrice, clientOrderId: null);
        }

        /// <summary>
        /// Creates a SOR limit IOC order with an optional caller-provided clientOrderId.
        /// </summary>
        public async Task<string> CreateSORLimitIOCOrderAsync(string productId, OrderSide side, string baseSize, string limitPrice, string clientOrderId)
        {
            if (string.IsNullOrEmpty(productId))
            {
                throw new ArgumentException("Product ID cannot be null or empty.", nameof(productId));
            }

            if (string.IsNullOrEmpty(baseSize))
            {
                throw new ArgumentException("Base size cannot be null or empty.", nameof(baseSize));
            }

            if (string.IsNullOrEmpty(limitPrice))
            {
                throw new ArgumentException("Limit price cannot be null or empty.", nameof(limitPrice));
            }

            var orderConfiguration = new OrderConfiguration
            {
                SorLimitIoc = new SorLimitIoc
                {
                    BaseSize = baseSize,
                    LimitPrice = limitPrice
                }
            };

            return await CreateOrderAsync(productId, side, orderConfiguration, clientOrderId);
        }

        /// <inheritdoc/>
        public Task<Order> CreateSORLimitIOCOrderAsync(string productId, OrderSide side, string baseSize, string limitPrice, bool returnOrder = true)
        {
            return CreateSORLimitIOCOrderAsync(productId, side, baseSize, limitPrice, clientOrderId: null, returnOrder: returnOrder);
        }

        /// <summary>
        /// Creates a SOR limit IOC order and returns the Order object, with an optional caller-provided clientOrderId.
        /// </summary>
        public async Task<Order> CreateSORLimitIOCOrderAsync(string productId, OrderSide side, string baseSize, string limitPrice, string clientOrderId, bool returnOrder = true)
        {
            if (!returnOrder)
            {
                throw new ArgumentException("returnOrder must be true to return an Order object.", nameof(returnOrder));
            }

            string orderId = await CreateSORLimitIOCOrderAsync(productId, side, baseSize, limitPrice, clientOrderId);
            return await GetOrderWithRetryAsync(orderId);
        }

        /// <inheritdoc/>
        public async Task<bool> EditOrderAsync(string orderId, string price = null, string size = null)
        {
            if (string.IsNullOrEmpty(orderId))
            {
                throw new ArgumentException("Order ID cannot be null or empty.", nameof(orderId));
            }

            if (string.IsNullOrEmpty(price))
            {
                throw new ArgumentException("Price cannot be null or empty.", nameof(price));
            }

            if (string.IsNullOrEmpty(size))
            {
                throw new ArgumentException("Size cannot be null or empty.", nameof(size));
            }

            var requestBody = new
            {
                order_id = orderId,
                price,
                size
            };

            try
            {
                var response = await _authenticator.SendAuthenticatedRequestAsync(
                        "POST",
                        "/api/v3/brokerage/orders/edit",
                        null,
                        requestBody)
                    ?? new Dictionary<string, object>();

                var responseObject = UtilityHelper.DeserializeDictionary<Dictionary<string, JToken>>(response);

                if (responseObject != null && responseObject.TryGetValue("success", out var successValue) && successValue.ToObject<bool>())
                {
                    return true;
                }

                var errorMessage = "Failed to edit order.";

                if (responseObject?.TryGetValue("errors", out var errorsValue) == true && errorsValue is JArray errorsArray && errorsArray.Any())
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
            if (string.IsNullOrEmpty(orderId))
            {
                throw new ArgumentException("Order ID cannot be null or empty.", nameof(orderId));
            }

            if (string.IsNullOrEmpty(price))
            {
                throw new ArgumentException("Price cannot be null or empty.", nameof(price));
            }

            if (string.IsNullOrEmpty(size))
            {
                throw new ArgumentException("Size cannot be null or empty.", nameof(size));
            }

            var requestBody = new
            {
                order_id = orderId,
                price,
                size
            };

            try
            {
                var response = await _authenticator.SendAuthenticatedRequestAsync(
                        "POST",
                        "/api/v3/brokerage/orders/edit_preview",
                        null,
                        requestBody)
                    ?? new Dictionary<string, object>();

                var responseObject = UtilityHelper.DeserializeDictionary<Dictionary<string, JToken>>(response);

                if (responseObject != null && responseObject.TryGetValue("errors", out var errorsValue) && errorsValue is JArray errorsArray && errorsArray.Any())
                {
                    var errorsList = errorsArray.ToList();
                    if (errorsList.Any())
                    {
                        var errorMessage = "Failed to preview order edit.";

                        foreach (var errorObj in errorsList)
                        {
                            if (errorObj is JObject errorObject)
                            {
                                if (errorObject.TryGetValue("edit_failure_reason", out var editFailureReason))
                                {
                                    errorMessage += $" Edit Failure Reason: {editFailureReason.ToString()}.";
                                }

                                if (errorObject.TryGetValue("preview_failure_reason", out var previewFailureReason))
                                {
                                    errorMessage += $" Preview Failure Reason: {previewFailureReason.ToString()}.";
                                }
                            }
                        }

                        throw new InvalidOperationException(errorMessage);
                    }
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
