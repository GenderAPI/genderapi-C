using System;
using System.Net.Http;

namespace GenderApi
{
    /// <summary>Options for <see cref="GenderApiClient"/>.</summary>
    public sealed class GenderApiClientOptions
    {
        /// <summary>The production V2 base URL.</summary>
        public const string DefaultBaseUrl = "https://api.genderapi.io/api/v2";

        /// <summary>The name of the environment variable read when <see cref="ApiKey"/> is null.</summary>
        public const string ApiKeyEnvironmentVariable = "GENDERAPI_API_KEY";

        /// <summary>The default per-request timeout (10 seconds).</summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// API key sent as <c>Authorization: Bearer &lt;key&gt;</c>. When null, the value of the
        /// <c>GENDERAPI_API_KEY</c> environment variable is used. When both are empty, requests are
        /// sent without a key and the server decides access (it may apply its shared IP trial).
        /// Keep the key on the server; never embed it in browser or mobile code.
        /// </summary>
        public string? ApiKey { get; set; }

        /// <summary>
        /// Base URL. Defaults to <see cref="DefaultBaseUrl"/>. Must be HTTPS; plain HTTP is accepted only
        /// for <c>localhost</c>, <c>127.0.0.1</c> or <c>[::1]</c> (local test servers).
        /// </summary>
        public string BaseUrl { get; set; } = DefaultBaseUrl;

        /// <summary>Per-request timeout (default 10 seconds). Use <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> to disable.</summary>
        public TimeSpan Timeout { get; set; } = DefaultTimeout;

        /// <summary>
        /// Optional <see cref="System.Net.Http.HttpClient"/> to use instead of the client-owned one.
        /// It is not disposed by <see cref="GenderApiClient"/>. Configure its handler with
        /// <c>AllowAutoRedirect = false</c>; the client rejects 3xx responses and redirected responses.
        /// Default request headers on this instance are not modified.
        /// </summary>
        public HttpClient? HttpClient { get; set; }

        /// <summary>
        /// When true (the default) and an API key is configured, a successful response whose
        /// <c>meta.access.mode</c> is present and is not <c>api_key</c> (for example <c>ip_trial</c> because the
        /// key was not recognized) raises <see cref="GenderApiAccessModeException"/> instead of returning.
        /// The request has already been processed at that point. Has no effect when no key is configured,
        /// and never applies to <c>CapabilitiesAsync</c> or <c>ErrorCatalogAsync</c>.
        /// </summary>
        public bool RequireApiKeyAccess { get; set; } = true;

        /// <summary>Optional suffix appended to the <c>User-Agent</c> (for example <c>"my-app/1.2"</c>).</summary>
        public string? UserAgentSuffix { get; set; }
    }
}
