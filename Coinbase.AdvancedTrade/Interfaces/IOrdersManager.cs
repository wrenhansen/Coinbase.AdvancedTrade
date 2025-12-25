using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coinbase.AdvancedTrade.Enums;
using Coinbase.AdvancedTrade.Models;

namespace Coinbase.AdvancedTrade.Interfaces
{
    /// <summary>
    /// Provides asynchronous methods for managing and interacting with orders.
    /// </summary>
    public interface IOrdersManager
    {
        /// <summary>
        /// Asynchronously lists orders based on the provided criteria.
        /// </summary>
        Task<List<Order>> ListOrdersAsync(
            string productId = null,
            OrderStatus[] orderStatus = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            OrderType? orderType = null,
            OrderSide? orderSide = null);

        /// <summary>
        /// Asynchronously lists fills based on the provided criteria.
        /// </summary>
        Task<List<Fill>> ListFillsAsync(
            string orderId = null,
            string productId = null,
            DateTime? startSequenceTimestamp = null,
            DateTime? endSequenceTimestamp = null);

        /// <summary>
        /// Asynchronously retrieves a specific order by its ID.
        /// </summary>
        Task<Order> GetOrderAsync(string orderId);

        /// <summary>
        /// Asynchronously cancels a set of orders based on their IDs.
        /// </summary>
        Task<List<CancelOrderResult>> CancelOrdersAsync(string[] orderIds);

        /// <summary>
        /// Asynchronously creates a market order.
        /// </summary>
        /// <param name="clientOrderId">Optional client order ID to use instead of an auto-generated GUID.</param>
        Task<string> CreateMarketOrderAsync(string productId, OrderSide side, string size, string clientOrderId = null);

        /// <summary>
        /// Asynchronously creates a market order and returns the full order details (via a follow-up GET).
        /// </summary>
        /// <param name="returnOrder">Must be true to return the full order details. If false, an exception is thrown.</param>
        /// <param name="clientOrderId">Optional client order ID to use instead of an auto-generated GUID.</param>
        Task<Order> CreateMarketOrderAsync(string productId, OrderSide side, string amount, bool returnOrder = true, string clientOrderId = null);

        /// <summary>
        /// Asynchronously creates a limit order with good-till-canceled (GTC) duration.
        /// </summary>
        /// <param name="clientOrderId">Optional client order ID to use instead of an auto-generated GUID.</param>
        Task<string> CreateLimitOrderGTCAsync(string productId, OrderSide side, string baseSize, string limitPrice, bool postOnly = true, string clientOrderId = null);

        /// <summary>
        /// Asynchronously creates a limit order with good-till-canceled (GTC) duration and returns the full order details (via a follow-up GET).
        /// </summary>
        /// <param name="clientOrderId">Optional client order ID to use instead of an auto-generated GUID.</param>
        Task<Order> CreateLimitOrderGTCAsync(string productId, OrderSide side, string baseSize, string limitPrice, bool postOnly, bool returnOrder = true, string clientOrderId = null);

        /// <summary>
        /// Asynchronously creates a limit order with good-till-date (GTD) duration.
        /// </summary>
        /// <param name="clientOrderId">Optional client order ID to use instead of an auto-generated GUID.</param>
        Task<string> CreateLimitOrderGTDAsync(string productId, OrderSide side, string baseSize, string limitPrice, DateTime endTime, bool postOnly = true, string clientOrderId = null);

        /// <summary>
        /// Asynchronously creates a limit order with good-till-date (GTD) duration and returns the full order details (via a follow-up GET).
        /// </summary>
        /// <param name="clientOrderId">Optional client order ID to use instead of an auto-generated GUID.</param>
        Task<Order> CreateLimitOrderGTDAsync(string productId, OrderSide side, string baseSize, string limitPrice, DateTime endTime, bool postOnly = true, bool returnOrder = true, string clientOrderId = null);

        /// <summary>
        /// Asynchronously creates a stop limit order with good-till-canceled (GTC) duration.
        /// </summary>
        /// <param name="clientOrderId">Optional client order ID to use instead of an auto-generated GUID.</param>
        Task<string> CreateStopLimitOrderGTCAsync(string productId, OrderSide side, string baseSize, string limitPrice, string stopPrice, string clientOrderId = null);

        /// <summary>
        /// Asynchronously creates a stop limit order with good-till-canceled (GTC) duration and returns the full order details (via a follow-up GET).
        /// </summary>
        /// <param name="clientOrderId">Optional client order ID to use instead of an auto-generated GUID.</param>
        Task<Order> CreateStopLimitOrderGTCAsync(string productId, OrderSide side, string baseSize, string limitPrice, string stopPrice, bool returnOrder = true, string clientOrderId = null);

        /// <summary>
        /// Asynchronously creates a stop limit order with good-till-date (GTD) duration.
        /// </summary>
        /// <param name="clientOrderId">Optional client order ID to use instead of an auto-generated GUID.</param>
        Task<string> CreateStopLimitOrderGTDAsync(string productId, OrderSide side, string baseSize, string limitPrice, string stopPrice, DateTime endTime, string clientOrderId = null);

        /// <summary>
        /// Asynchronously creates a stop limit order with good-till-date (GTD) duration and returns the full order details (via a follow-up GET).
        /// </summary>
        /// <param name="clientOrderId">Optional client order ID to use instead of an auto-generated GUID.</param>
        Task<Order> CreateStopLimitOrderGTDAsync(string productId, OrderSide side, string baseSize, string limitPrice, string stopPrice, DateTime endTime, bool returnOrder = true, string clientOrderId = null);

        /// <summary>
        /// Asynchronously creates a limit IOC order with Smart Order Routing (SOR).
        /// </summary>
        /// <param name="clientOrderId">Optional client order ID to use instead of an auto-generated GUID.</param>
        Task<string> CreateSORLimitIOCOrderAsync(string productId, OrderSide side, string baseSize, string limitPrice, string clientOrderId = null);

        /// <summary>
        /// Asynchronously creates a limit IOC order with Smart Order Routing (SOR) and returns the full order details (via a follow-up GET).
        /// </summary>
        /// <param name="returnOrder">Must be true to return the full order details. If false, an exception is thrown.</param>
        /// <param name="clientOrderId">Optional client order ID to use instead of an auto-generated GUID.</param>
        Task<Order> CreateSORLimitIOCOrderAsync(string productId, OrderSide side, string baseSize, string limitPrice, bool returnOrder = true, string clientOrderId = null);

        /// <summary>
        /// Asynchronously edits an existing order with a specified new size or new price.
        /// Only limit orders with a time in force type of good-till-cancelled can be edited.
        /// </summary>
        Task<bool> EditOrderAsync(string orderId, string price, string size);

        /// <summary>
        /// Asynchronously simulates an edit of an existing order with a specified new size or new price to preview the result.
        /// Only limit orders with a time in force type of good-till-cancelled can be previewed.
        /// </summary>
        Task<EditOrderPreviewResult> EditOrderPreviewAsync(string orderId, string price, string size);
    }
}
