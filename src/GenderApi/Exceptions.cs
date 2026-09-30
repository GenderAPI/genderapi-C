using System;
using System.Collections.Generic;

namespace GenderApi
{
    /// <summary>
    /// Raised for an HTTP error response (status &gt;= 400), an unexpected response, or a transport failure.
    /// The client never retries. A lost or failed response can still have been billed: inspect
    /// <see cref="BillingStatus"/> and <see cref="Action"/> before sending a new request.
    /// </summary>
    public class GenderApiException : Exception
    {
        /// <summary>Creates an exception.</summary>
        public GenderApiException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }

        /// <summary>HTTP status code, when a response was received.</summary>
        public int? StatusCode { get; internal set; }

        /// <summary>Stable machine-readable error code (for example <c>rate_limit_exceeded</c>).</summary>
        public string? Code { get; internal set; }

        /// <summary>Problem title.</summary>
        public string? Title { get; internal set; }

        /// <summary>Human-readable detail. Match on <see cref="Code"/>, not on this text.</summary>
        public string? Detail { get; internal set; }

        /// <summary>Recommended action from the server (for example <c>wait_then_retry</c>, <c>contact_support</c>).</summary>
        public string? Action { get; internal set; }

        /// <summary>Validation pointers (<c>errors</c>), when present.</summary>
        public IReadOnlyList<ProblemFieldError> Errors { get; internal set; } = Array.Empty<ProblemFieldError>();

        /// <summary>Request id from the body, <c>meta.request_id</c> or the <c>X-Request-ID</c> header.</summary>
        public string? RequestId { get; internal set; }

        /// <summary>Parsed <c>Retry-After</c> header (seconds or HTTP date), when present.</summary>
        public TimeSpan? RetryAfter { get; internal set; }

        /// <summary>Raw <c>Retry-After</c> header value, when present.</summary>
        public string? RetryAfterRaw { get; internal set; }

        /// <summary><c>meta.usage.billing_status</c> when present: <c>not_charged</c>, <c>confirmed</c> or <c>unconfirmed</c>.</summary>
        public string? BillingStatus => Meta?.Usage?.BillingStatus;

        /// <summary>The parsed Problem Details body, when the body was a JSON object.</summary>
        public Problem? Problem { get; internal set; }

        /// <summary><c>meta</c> of the error body, when present.</summary>
        public Meta? Meta { get; internal set; }

        /// <summary>
        /// Batch item outcomes, kept when all executed batch items failed (<c>data</c> on the error body).
        /// </summary>
        public IReadOnlyList<BatchItemResult>? BatchData { get; internal set; }

        /// <summary>Raw response body, when one was received. It can contain personal input values; do not log it wholesale.</summary>
        public string? RawBody { get; internal set; }
    }

    /// <summary>
    /// No usable HTTP response was received (network failure, invalid or non-JSON body). The billing
    /// outcome is unknown; do not automatically retry. Check <c>UsageAsync</c> before resubmitting.
    /// </summary>
    public class GenderApiTransportException : GenderApiException
    {
        /// <summary>Creates an exception.</summary>
        public GenderApiTransportException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }

    /// <summary>The request exceeded the configured timeout. The billing outcome is unknown; do not automatically retry.</summary>
    public sealed class GenderApiTimeoutException : GenderApiTransportException
    {
        /// <summary>Creates an exception.</summary>
        public GenderApiTimeoutException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }

    /// <summary>The server answered with a redirect (3xx). Redirects are never followed so the key is not forwarded.</summary>
    public sealed class GenderApiRedirectException : GenderApiException
    {
        /// <summary>Creates an exception.</summary>
        public GenderApiRedirectException(string message)
            : base(message)
        {
        }

        /// <summary>The <c>Location</c> header, when present.</summary>
        public Uri? Location { get; internal set; }
    }

    /// <summary>
    /// An API key was configured but the successful (2xx) response reports another access mode in
    /// <c>meta.access.mode</c> (usually <c>ip_trial</c> because the key was not recognized). The request
    /// has already been processed and trial credits may have been used; <see cref="Result"/> holds the
    /// complete typed response the method would have returned, including billing metadata. Disable with
    /// <see cref="GenderApiClientOptions.RequireApiKeyAccess"/>. <see cref="GenderApiException.Code"/> is
    /// <c>unexpected_access_mode</c>.
    /// </summary>
    public sealed class GenderApiAccessModeException : GenderApiException
    {
        /// <summary>The error code carried by this exception.</summary>
        public const string ErrorCode = "unexpected_access_mode";

        /// <summary>Creates an exception.</summary>
        public GenderApiAccessModeException(string message, object result, string? accessMode, string? accessReason)
            : base(message)
        {
            Result = result ?? throw new ArgumentNullException(nameof(result));
            AccessMode = accessMode;
            AccessReason = accessReason;
            Code = ErrorCode;
        }

        /// <summary><c>meta.access.mode</c> reported by the server (for example <c>ip_trial</c>).</summary>
        public string? AccessMode { get; }

        /// <summary><c>meta.access.reason</c> reported by the server (for example <c>api_key_invalid</c>), when present.</summary>
        public string? AccessReason { get; }

        /// <summary>
        /// The complete typed response the method would have returned (<see cref="GenderResponse"/>,
        /// <see cref="BatchResponse"/>, <see cref="UsageResponse"/> or <see cref="PhoneValidationResponse"/>).
        /// The raw JSON is also available as <see cref="GenderApiException.RawBody"/>.
        /// </summary>
        public object Result { get; }
    }

    /// <summary>Invalid input detected on the client. No network request was made.</summary>
    public sealed class GenderApiValidationException : ArgumentException
    {
        /// <summary>Creates an exception.</summary>
        public GenderApiValidationException(string message, string? paramName = null)
            : base(message, paramName)
        {
        }
    }
}
