using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Coinbase.AdvancedTrade.Enums;
using Newtonsoft.Json;
using RestSharp;

namespace Coinbase.AdvancedTrade
{
    /// <summary>
    /// Represents an authenticator for Coinbase API requests.
    /// Responsible for generating appropriate headers and sending authenticated requests to the Coinbase Advanced Trade API.
    /// </summary>
    public sealed class CoinbaseAuthenticator
    {
        private const string ApiUrl = "https://api.coinbase.com";

        // Keep this reasonably conservative since this is a library and callers might not control their SynchronizationContext.
        private const int DefaultTimeoutMilliseconds = 100_000;

        private static readonly string UserAgent = "Coinbase.AdvancedTrade.Sdk/1.1.0";

        private readonly RestClient _client;

        private readonly string _apiKey;
        private readonly string _apiSecret;
        private readonly string _oAuth2AccessToken;
        private readonly bool _useOAuth;
        private readonly ApiKeyType _apiKeyType;

        /// <summary>
        /// Initializes a new instance of the <see cref="CoinbaseAuthenticator"/> class using an API key and secret.
        /// </summary>
        /// <param name="apiKey">The API key for Coinbase authentication.</param>
        /// <param name="apiSecret">The API secret for Coinbase authentication.</param>
        /// <param name="apiKeyType">The type of API key, CoinbaseDeveloperPlatform or Legacy (deprecated).</param>
        public CoinbaseAuthenticator(string apiKey, string apiSecret, ApiKeyType apiKeyType = ApiKeyType.CoinbaseDeveloperPlatform)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("API key cannot be null or empty.", nameof(apiKey));
            }

            if (string.IsNullOrWhiteSpace(apiSecret))
            {
                throw new ArgumentException("API secret cannot be null or empty.", nameof(apiSecret));
            }

            _apiKey = apiKey;
            _apiSecret = apiSecret;
            _apiKeyType = apiKeyType;
            _useOAuth = false;

            _client = CreateRestClient(ApiUrl);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CoinbaseAuthenticator"/> class using an OAuth2 access token.
        /// </summary>
        /// <param name="oAuth2AccessToken">The OAuth2 access token for Coinbase authentication.</param>
        public CoinbaseAuthenticator(string oAuth2AccessToken)
        {
            if (string.IsNullOrWhiteSpace(oAuth2AccessToken))
            {
                throw new ArgumentException("OAuth2 access token cannot be null or empty.", nameof(oAuth2AccessToken));
            }

            _oAuth2AccessToken = oAuth2AccessToken;
            _useOAuth = true;

            // Not used for OAuth requests but kept initialized for safety.
            _apiKeyType = ApiKeyType.CoinbaseDeveloperPlatform;

            _client = CreateRestClient(ApiUrl);
        }

        private static RestClient CreateRestClient(string baseUrl)
        {
            var options = new RestClientOptions(baseUrl)
            {
                ThrowOnAnyError = false,
                Timeout = TimeSpan.FromMilliseconds(DefaultTimeoutMilliseconds)
            };

            return new RestClient(options);
        }

        /// <summary>
        /// Sends an authenticated asynchronous request to a specified path.
        /// </summary>
        /// <param name="method">The HTTP method for the request (GET, POST, etc.).</param>
        /// <param name="path">The API path for the request.</param>
        /// <param name="queryParams">Optional query parameters.</param>
        /// <param name="bodyObj">Optional body object.</param>
        /// <returns>A dictionary containing the JSON response.</returns>
        public async Task<Dictionary<string, object>> SendAuthenticatedRequestAsync(
            string method,
            string path,
            Dictionary<string, string> queryParams = null,
            object bodyObj = null)
        {
            if (string.IsNullOrWhiteSpace(method))
            {
                throw new ArgumentException("Method cannot be null or empty.", nameof(method));
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path cannot be null or empty.", nameof(path));
            }

            if (!Enum.GetNames(typeof(Method)).Any(e => e.Equals(method, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"Invalid method type '{method}'.", nameof(method));
            }

            // Normalize the path to start with a slash.
            if (!path.StartsWith("/", StringComparison.Ordinal))
            {
                path = "/" + path;
            }

            var headers = _useOAuth
                ? CreateOAuth2Headers()
                : CreateHeaders(method, path, bodyObj);

            return await ExecuteRequestAsync(method, path, bodyObj, headers, queryParams).ConfigureAwait(false);
        }

        private Dictionary<string, string> CreateOAuth2Headers()
        {
            return new Dictionary<string, string>
            {
                { "Authorization", $"Bearer {_oAuth2AccessToken}" }
            };
        }

        private Dictionary<string, string> CreateHeaders(string method, string path, object bodyObj)
        {
            return _apiKeyType == ApiKeyType.CoinbaseDeveloperPlatform
                ? CreateJwtHeaders(method, path)
                : CreateLegacyHeaders(method, path, bodyObj);
        }

        private Dictionary<string, string> CreateJwtHeaders(string method, string path)
        {
            // JWT is required for CDP API keys.
            var jwtToken = JwtTokenGenerator.GenerateJwt(_apiKey, _apiSecret, "retail_rest_api_proxy", method, path);

            return new Dictionary<string, string>
            {
                { "Authorization", $"Bearer {jwtToken}" }
            };
        }

        private async Task<Dictionary<string, object>> ExecuteRequestAsync(
            string method,
            string path,
            object bodyObj,
            Dictionary<string, string> headers,
            Dictionary<string, string> queryParams)
        {
            if (!Enum.TryParse(method, ignoreCase: true, out Method httpMethod))
            {
                throw new ArgumentException($"Invalid method '{method}'.", nameof(method));
            }

            var request = new RestRequest(path, httpMethod);

            // Default headers
            request.AddHeader("Accept", "application/json");
            request.AddHeader("User-Agent", UserAgent);

            // Auth headers
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.AddHeader(header.Key, header.Value);
                }
            }

            // Query parameters
            if (queryParams != null)
            {
                foreach (var param in queryParams)
                {
                    if (string.IsNullOrWhiteSpace(param.Key) || param.Value is null)
                    {
                        continue;
                    }

                    request.AddQueryParameter(param.Key, param.Value);
                }
            }

            // Body
            if (bodyObj != null)
            {
                // RestSharp's AddJsonBody can serialize with System.Text.Json depending on configuration.
                // We serialize explicitly with Newtonsoft.Json to keep payloads consistent across versions.
                var jsonBody = JsonConvert.SerializeObject(bodyObj);
                request.AddStringBody(jsonBody, DataFormat.Json);
            }

            RestResponse response;
            try
            {
                response = await _client.ExecuteAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"An error occurred while calling Coinbase API '{httpMethod} {path}'.", ex);
            }

            return HandleResponse(response, httpMethod, path);
        }

        private static Dictionary<string, object> HandleResponse(RestResponse response, Method method, string path)
        {
            if (response == null)
            {
                throw new InvalidOperationException($"Coinbase API '{method} {path}' returned no response.");
            }

            if (response.ErrorException != null)
            {
                throw new InvalidOperationException(
                    $"Coinbase API '{method} {path}' failed. HTTP {(int)response.StatusCode} {response.StatusCode}.",
                    response.ErrorException);
            }

            if (string.IsNullOrWhiteSpace(response.Content))
            {
                if (response.IsSuccessful)
                {
                    return null;
                }

                throw new InvalidOperationException(
                    $"Coinbase API '{method} {path}' failed. HTTP {(int)response.StatusCode} {response.StatusCode}. Empty response body.");
            }

            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, object>>(response.Content);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Failed to parse Coinbase API response as JSON for '{method} {path}'. HTTP {(int)response.StatusCode} {response.StatusCode}. Body: {response.Content}",
                    ex);
            }
        }

        private string Key => _apiKey;
        private string Secret => _apiSecret;

        /// <summary>
        /// Generates headers for legacy API key authentication.
        /// </summary>
        [Obsolete("Legacy API key authentication is deprecated and will be removed in future versions.")]
        private Dictionary<string, string> CreateLegacyHeaders(string method, string path, object bodyObj)
        {
            var body = bodyObj != null ? JsonConvert.SerializeObject(bodyObj) : null;
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            // Coinbase legacy signature: timestamp + method + path + body
            var message = $"{timestamp}{method.ToUpperInvariant()}{path}{body}";
            var signature = GenerateSignature(message);

            return new Dictionary<string, string>
            {
                { "CB-ACCESS-KEY", Key },
                { "CB-ACCESS-SIGN", signature },
                { "CB-ACCESS-TIMESTAMP", timestamp }
            };
        }

        /// <summary>
        /// Generates a signature using HMACSHA256 for the provided message.
        /// </summary>
        [Obsolete("Legacy API key authentication is deprecated and will be removed in future versions.")]
        private string GenerateSignature(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                throw new ArgumentException("Message cannot be null or empty.", nameof(message));
            }

            // Remove query string from the message, if present.
            var queryStringIndex = message.IndexOf('?');
            if (queryStringIndex != -1)
            {
                message = message.Substring(0, queryStringIndex);
            }

            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret)))
            {
                var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
                return BitConverter.ToString(signatureBytes).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
