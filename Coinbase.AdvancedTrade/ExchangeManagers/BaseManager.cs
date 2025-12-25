using RestSharp;

namespace Coinbase.AdvancedTrade.ExchangeManagers
{
    /// <summary>
    /// Provides base functionality for Coinbase API managers.
    /// </summary>
    public abstract class BaseManager
    {
        /// <summary>
        /// Authenticator instance for Coinbase API authentication (null for unauthenticated public calls).
        /// </summary>
        protected readonly CoinbaseAuthenticator _authenticator;

        /// <summary>
        /// REST client for direct (typically public) API requests.
        /// </summary>
        protected readonly RestClient _client;

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseManager"/> class.
        /// </summary>
        /// <param name="authenticator">The Coinbase authenticator instance (optional for public endpoints).</param>
        /// <param name="baseUrl">The base URL for the REST client.</param>
        protected BaseManager(CoinbaseAuthenticator authenticator, string baseUrl = "https://api.coinbase.com")
        {
            _authenticator = authenticator;

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = "https://api.coinbase.com";
            }

            var options = new RestClientOptions(baseUrl)
            {
                ThrowOnAnyError = false
            };

            _client = new RestClient(options);
        }
    }
}
