using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Coinbase.AdvancedTrade.Interfaces;
using Coinbase.AdvancedTrade.Models;
using Coinbase.AdvancedTrade.Utilities;

namespace Coinbase.AdvancedTrade.ExchangeManagers
{
    /// <summary>
    /// Manages fee-related activities for the Coinbase Advanced Trade API.
    /// </summary>
    public class FeesManager : BaseManager, IFeesManager
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FeesManager"/> class.
        /// </summary>
        /// <param name="authenticator">The Coinbase authenticator.</param>
        public FeesManager(CoinbaseAuthenticator authenticator) : base(authenticator)
        {
            if (authenticator == null)
            {
                throw new ArgumentNullException(nameof(authenticator), "Authenticator cannot be null.");
            }
        }

        /// <inheritdoc/>
        public async Task<TransactionsSummary> GetTransactionsSummaryAsync(
            DateTime startDate,
            DateTime endDate,
            string userNativeCurrency = "USD",
            string productType = "SPOT")
        {
            if (string.IsNullOrWhiteSpace(userNativeCurrency))
            {
                throw new ArgumentException("User native currency cannot be null or empty.", nameof(userNativeCurrency));
            }

            if (string.IsNullOrWhiteSpace(productType))
            {
                throw new ArgumentException("Product type cannot be null or empty.", nameof(productType));
            }

            var startUtc = startDate.ToUniversalTime();
            var endUtc = endDate.ToUniversalTime();

            if (endUtc < startUtc)
            {
                throw new ArgumentException("End date must be greater than or equal to start date.", nameof(endDate));
            }

            var paramsDict = new Dictionary<string, string>
            {
                { "start_date", startUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) },
                { "end_date", endUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) },
                { "user_native_currency", userNativeCurrency },
                { "product_type", productType }
            };

            try
            {
                var response = await _authenticator
                    .SendAuthenticatedRequestAsync("GET", "/api/v3/brokerage/transaction_summary", paramsDict)
                    .ConfigureAwait(false);

                if (response == null)
                {
                    return null;
                }

                return new TransactionsSummary
                {
                    TotalVolume = UtilityHelper.ExtractDoubleValue(response, "total_volume") ?? 0.0,
                    TotalFees = UtilityHelper.ExtractDoubleValue(response, "total_fees") ?? 0.0,
                    AdvancedTradeOnlyVolume = UtilityHelper.ExtractDoubleValue(response, "advanced_trade_only_volume") ?? 0.0,
                    AdvancedTradeOnlyFees = UtilityHelper.ExtractDoubleValue(response, "advanced_trade_only_fees") ?? 0.0,
                    CoinbaseProVolume = UtilityHelper.ExtractDoubleValue(response, "coinbase_pro_volume") ?? 0.0,
                    CoinbaseProFees = UtilityHelper.ExtractDoubleValue(response, "coinbase_pro_fees") ?? 0.0,
                    Low = UtilityHelper.ExtractDoubleValue(response, "low") ?? 0.0,
                    FeeTier = UtilityHelper.DeserializeJsonElement<FeeTier>(response, "fee_tier"),
                    MarginRate = UtilityHelper.DeserializeJsonElement<MarginRate>(response, "margin_rate"),
                    GoodsAndServicesTax = UtilityHelper.DeserializeJsonElement<GoodsAndServicesTax>(response, "goods_and_services_tax")
                };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to get transactions summary.", ex);
            }
        }
    }
}
