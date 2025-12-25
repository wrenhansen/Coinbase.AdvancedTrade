using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coinbase.AdvancedTrade.Interfaces;
using Coinbase.AdvancedTrade.Models;
using Coinbase.AdvancedTrade.Utilities;

namespace Coinbase.AdvancedTrade.ExchangeManagers
{
    /// <summary>
    /// Manages account-related activities for the Coinbase Advanced Trade API.
    /// </summary>
    public class AccountsManager : BaseManager, IAccountsManager
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AccountsManager"/> class.
        /// </summary>
        /// <param name="authenticator">The Coinbase authenticator.</param>
        public AccountsManager(CoinbaseAuthenticator authenticator) : base(authenticator)
        {
            if (authenticator == null)
            {
                throw new ArgumentNullException(nameof(authenticator), "Authenticator cannot be null.");
            }
        }

        /// <inheritdoc/>
        public async Task<List<Account>> ListAccountsAsync(int limit = 49, string cursor = null)
        {
            if (limit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");
            }

            try
            {
                var parameters = new { limit, cursor };

                var response = await _authenticator
                    .SendAuthenticatedRequestAsync("GET", "/api/v3/brokerage/accounts", UtilityHelper.ConvertToDictionary(parameters))
                    .ConfigureAwait(false) ?? new Dictionary<string, object>();

                return UtilityHelper.DeserializeJsonElement<List<Account>>(response, "accounts");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to list accounts.", ex);
            }
        }

        /// <inheritdoc/>
        public async Task<Account> GetAccountAsync(string accountUuid)
        {
            if (string.IsNullOrWhiteSpace(accountUuid))
            {
                throw new ArgumentException("Account UUID cannot be null or empty.", nameof(accountUuid));
            }

            try
            {
                var response = await _authenticator
                    .SendAuthenticatedRequestAsync("GET", $"/api/v3/brokerage/accounts/{accountUuid}")
                    .ConfigureAwait(false) ?? new Dictionary<string, object>();

                return UtilityHelper.DeserializeJsonElement<Account>(response, "account");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to get account with UUID '{accountUuid}'.", ex);
            }
        }
    }
}
