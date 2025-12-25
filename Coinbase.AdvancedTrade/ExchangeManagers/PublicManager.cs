using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coinbase.AdvancedTrade.Enums;
using Coinbase.AdvancedTrade.Interfaces;
using Coinbase.AdvancedTrade.Models.Public;
using Coinbase.AdvancedTrade.Utilities;
using Newtonsoft.Json;
using RestSharp;

namespace Coinbase.AdvancedTrade.ExchangeManagers
{
    /// <summary>
    /// Manages public activities for the Coinbase Advanced Trade API.
    /// </summary>
    public class PublicManager : BaseManager, IPublicManager
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PublicManager"/> class with an authenticator (optional).
        /// </summary>
        public PublicManager(CoinbaseAuthenticator authenticator) : base(authenticator)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PublicManager"/> class without authentication.
        /// </summary>
        public PublicManager() : base(null)
        {
        }

        /// <inheritdoc/>
        public async Task<ServerTime> GetCoinbaseServerTimeAsync()
        {
            try
            {
                var request = new RestRequest("/api/v3/brokerage/time", Method.Get);
                var response = await _client.ExecuteAsync<ServerTime>(request).ConfigureAwait(false);

                if (!response.IsSuccessful)
                {
                    throw CreateRequestFailedException("get server time", response);
                }

                return response.Data;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to get server time.", ex);
            }
        }

        /// <inheritdoc/>
        public async Task<List<PublicProduct>> ListPublicProductsAsync(int? limit = null, int? offset = null, string productType = null, List<string> productIds = null)
        {
            if (limit.HasValue && limit.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");
            }

            if (offset.HasValue && offset.Value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be greater than or equal to zero.");
            }

            try
            {
                var request = new RestRequest("/api/v3/brokerage/market/products", Method.Get);

                if (limit.HasValue)
                {
                    request.AddQueryParameter("limit", limit.Value.ToString());
                }

                if (offset.HasValue)
                {
                    request.AddQueryParameter("offset", offset.Value.ToString());
                }

                if (!string.IsNullOrWhiteSpace(productType))
                {
                    request.AddQueryParameter("product_type", productType);
                }

                if (productIds != null && productIds.Any())
                {
                    foreach (var productId in productIds.Where(p => !string.IsNullOrWhiteSpace(p)))
                    {
                        request.AddQueryParameter("product_ids", productId);
                    }
                }

                var response = await _client.ExecuteAsync(request).ConfigureAwait(false);

                if (!response.IsSuccessful)
                {
                    throw CreateRequestFailedException("list public products", response);
                }

                var responseDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(response.Content);
                return UtilityHelper.DeserializeJsonElement<List<PublicProduct>>(responseDict, "products");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to list public products.", ex);
            }
        }

        /// <inheritdoc/>
        public async Task<PublicProduct> GetPublicProductAsync(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId))
            {
                throw new ArgumentException("Product ID cannot be null or empty.", nameof(productId));
            }

            try
            {
                var request = new RestRequest($"/api/v3/brokerage/market/products/{productId}", Method.Get);
                var response = await _client.ExecuteAsync(request).ConfigureAwait(false);

                if (!response.IsSuccessful)
                {
                    throw CreateRequestFailedException("get public product", response);
                }

                return JsonConvert.DeserializeObject<PublicProduct>(response.Content);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to get public product.", ex);
            }
        }

        /// <inheritdoc/>
        public async Task<PublicProductBook> GetPublicProductBookAsync(string productId, int? limit = null)
        {
            if (string.IsNullOrWhiteSpace(productId))
            {
                throw new ArgumentException("Product ID cannot be null or empty.", nameof(productId));
            }

            if (limit.HasValue && limit.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");
            }

            try
            {
                var request = new RestRequest("/api/v3/brokerage/market/product_book", Method.Get);
                request.AddQueryParameter("product_id", productId);

                if (limit.HasValue)
                {
                    request.AddQueryParameter("limit", limit.Value.ToString());
                }

                var response = await _client.ExecuteAsync(request).ConfigureAwait(false);

                if (!response.IsSuccessful)
                {
                    throw CreateRequestFailedException("get public product book", response);
                }

                var responseDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(response.Content);
                return UtilityHelper.DeserializeJsonElement<PublicProductBook>(responseDict, "pricebook");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to get public product book.", ex);
            }
        }

        /// <inheritdoc/>
        public async Task<PublicMarketTrades> GetPublicMarketTradesAsync(string productId, int limit, long? start = null, long? end = null)
        {
            if (string.IsNullOrWhiteSpace(productId))
            {
                throw new ArgumentException("Product ID cannot be null or empty.", nameof(productId));
            }

            if (limit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");
            }

            try
            {
                var request = new RestRequest($"/api/v3/brokerage/market/products/{productId}/ticker", Method.Get);
                request.AddQueryParameter("limit", limit.ToString());

                if (start.HasValue)
                {
                    request.AddQueryParameter("start", start.Value.ToString());
                }

                if (end.HasValue)
                {
                    request.AddQueryParameter("end", end.Value.ToString());
                }

                var response = await _client.ExecuteAsync(request).ConfigureAwait(false);

                if (!response.IsSuccessful)
                {
                    throw CreateRequestFailedException("get public market trades", response);
                }

                return JsonConvert.DeserializeObject<PublicMarketTrades>(response.Content);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to get public market trades.", ex);
            }
        }

        /// <inheritdoc/>
        public async Task<List<PublicCandle>> GetPublicProductCandlesAsync(string productId, long start, long end, Granularity granularity)
        {
            if (string.IsNullOrWhiteSpace(productId))
            {
                throw new ArgumentException("Product ID cannot be null or empty.", nameof(productId));
            }

            if (end < start)
            {
                throw new ArgumentException("End must be greater than or equal to start.", nameof(end));
            }

            try
            {
                var request = new RestRequest($"/api/v3/brokerage/market/products/{productId}/candles", Method.Get);

                request.AddQueryParameter("start", start.ToString());
                request.AddQueryParameter("end", end.ToString());
                request.AddQueryParameter("granularity", granularity.ToString());

                var response = await _client.ExecuteAsync(request).ConfigureAwait(false);

                if (!response.IsSuccessful)
                {
                    throw CreateRequestFailedException("get public product candles", response);
                }

                var responseDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(response.Content);
                return UtilityHelper.DeserializeJsonElement<List<PublicCandle>>(responseDict, "candles");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to get public product candles.", ex);
            }
        }

        private static InvalidOperationException CreateRequestFailedException(string operation, RestResponse response)
        {
            var status = response?.StatusCode;
            var content = response?.Content;
            return new InvalidOperationException($"Failed to {operation}. Status: {status}, Content: {content}");
        }
    }
}
