using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

// Response models. Enumerated response values are kept as strings so that new server values
// never break deserialization. Unknown fields are preserved in ExtensionData.
namespace GenderApi
{
    /// <summary>Base type for every V2 response envelope (<c>data</c> + <c>meta</c>).</summary>
    /// <typeparam name="TData">The type of <c>data</c>.</typeparam>
    public class ApiResponse<TData> : IApiResponse
    {
        /// <summary>The <c>data</c> member.</summary>
        [JsonPropertyName("data")]
        public TData? Data { get; set; }

        /// <summary>The <c>meta</c> member: request id, access mode and billing.</summary>
        [JsonPropertyName("meta")]
        public Meta? Meta { get; set; }

        /// <summary>Unknown top-level fields, preserved as returned.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }

        /// <summary>
        /// The raw JSON response body. It can contain personal input values; do not log it wholesale.
        /// </summary>
        [JsonIgnore]
        public string RawJson { get; internal set; } = string.Empty;

        /// <summary>The HTTP status code of the response.</summary>
        [JsonIgnore]
        public int StatusCode { get; internal set; }

        /// <summary><c>meta.request_id</c>, or the <c>X-Request-ID</c> header when meta has none.</summary>
        [JsonIgnore]
        public string? RequestId { get; internal set; }

        void IApiResponse.SetTransport(int statusCode, string rawJson, string? requestIdHeader)
        {
            StatusCode = statusCode;
            RawJson = rawJson;
            RequestId = !string.IsNullOrEmpty(Meta?.RequestId) ? Meta!.RequestId : requestIdHeader;
        }
    }

    internal interface IApiResponse
    {
        Meta? Meta { get; }

        void SetTransport(int statusCode, string rawJson, string? requestIdHeader);
    }

    /// <summary>Response of <c>POST /gender</c>.</summary>
    public sealed class GenderResponse : ApiResponse<Prediction>
    {
    }

    /// <summary>
    /// Response of <c>POST /gender/batch</c>. A 200 response can contain failed items; inspect
    /// <see cref="FailedItems"/>. Resubmitting successful items charges them again.
    /// </summary>
    public sealed class BatchResponse : ApiResponse<List<BatchItemResult>>
    {
        /// <summary>Items that returned <c>data</c>.</summary>
        [JsonIgnore]
        public IReadOnlyList<BatchItemResult> SucceededItems => (Data ?? new List<BatchItemResult>()).Where(i => i.IsSuccess).ToList();

        /// <summary>Items that returned <c>error</c>.</summary>
        [JsonIgnore]
        public IReadOnlyList<BatchItemResult> FailedItems => (Data ?? new List<BatchItemResult>()).Where(i => !i.IsSuccess).ToList();

        /// <summary>True when at least one item failed.</summary>
        [JsonIgnore]
        public bool HasFailures => (Data ?? new List<BatchItemResult>()).Any(i => !i.IsSuccess);
    }

    /// <summary>Response of <c>GET /usage</c> (free).</summary>
    public sealed class UsageResponse : ApiResponse<UsageData>
    {
    }

    /// <summary>Response of <c>POST /phone/validate</c>.</summary>
    public sealed class PhoneValidationResponse : ApiResponse<PhoneValidation>
    {
    }

    /// <summary>A gender inference. It is an inference, never verification of a person's identity.</summary>
    public sealed class Prediction
    {
        /// <summary>The echoed input.</summary>
        [JsonPropertyName("input")]
        public PredictionInput? Input { get; set; }

        /// <summary>The returned dataset name or extracted given name; can be null.</summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary><c>male</c>, <c>female</c> or null (unknown).</summary>
        [JsonPropertyName("gender")]
        public string? Gender { get; set; }

        /// <summary>Country associated with the result; not nationality, residence or ethnicity.</summary>
        [JsonPropertyName("country")]
        public string? Country { get; set; }

        /// <summary>
        /// 0–1 score whose meaning depends on <see cref="ConfidenceKind"/>. It is not a calibrated
        /// probability or a measured accuracy. Null when <see cref="Gender"/> is null.
        /// </summary>
        [JsonPropertyName("confidence")]
        public double? Confidence { get; set; }

        /// <summary><c>observed_frequency</c> (dataset), <c>model_reported</c> (AI) or null.</summary>
        [JsonPropertyName("confidence_kind")]
        public string? ConfidenceKind { get; set; }

        /// <summary>Dataset sample count; null for AI and for no match.</summary>
        [JsonPropertyName("sample_count")]
        public long? SampleCount { get; set; }

        /// <summary><c>dataset</c>, <c>ai</c> or <c>none</c>.</summary>
        [JsonPropertyName("source")]
        public string? Source { get; set; }

        /// <summary><c>identified</c> (gender is male/female) or <c>unknown</c> (gender is null).</summary>
        [JsonPropertyName("result_status")]
        public string? ResultStatus { get; set; }

        /// <summary>Null for identified; <c>not_found</c>, <c>no_name_candidate</c>, <c>ambiguous</c> or <c>insufficient_evidence</c> for unknown.</summary>
        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        /// <summary><c>dataset</c>, <c>ai_association</c> or null.</summary>
        [JsonPropertyName("country_source")]
        public string? CountrySource { get; set; }

        /// <summary>Dataset candidate and lookup scope.</summary>
        [JsonPropertyName("match")]
        public PredictionMatch? Match { get; set; }

        /// <summary>Unknown fields, preserved as returned.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }

        /// <summary>True when <see cref="ResultStatus"/> is <c>identified</c>.</summary>
        [JsonIgnore]
        public bool IsIdentified => ResultStatus == "identified";

        /// <summary>True when <see cref="ResultStatus"/> is <c>unknown</c>. Unknown results are successful and billable.</summary>
        [JsonIgnore]
        public bool IsUnknown => ResultStatus == "unknown";
    }

    /// <summary>The input echoed in a prediction.</summary>
    public sealed class PredictionInput
    {
        /// <summary><c>name</c>, <c>email</c> or <c>username</c>.</summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        /// <summary>The input value.</summary>
        [JsonPropertyName("value")]
        public string? Value { get; set; }

        /// <summary>The requested country, or null.</summary>
        [JsonPropertyName("country")]
        public string? Country { get; set; }

        /// <summary>Present when <c>forceToGenderize</c> was requested.</summary>
        [JsonPropertyName("forceToGenderize")]
        public bool? ForceToGenderize { get; set; }

        /// <summary>Unknown fields, preserved as returned.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>The <c>match</c> object of a prediction.</summary>
    public sealed class PredictionMatch
    {
        /// <summary>Normalized dataset candidate that matched; null for AI or no match.</summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary><c>normalized</c>, <c>token</c>, <c>substring</c>, <c>model_inference</c> or null.</summary>
        [JsonPropertyName("method")]
        public string? Method { get; set; }

        /// <summary><c>country</c>, <c>global</c> or null.</summary>
        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        /// <summary>Requested country when a country-specific dataset row was used; otherwise null.</summary>
        [JsonPropertyName("country")]
        public string? Country { get; set; }

        /// <summary>Unknown fields, preserved as returned.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>One item of a batch response. Exactly one of <see cref="Data"/> or <see cref="Error"/> is set.</summary>
    public sealed class BatchItemResult
    {
        /// <summary>Zero-based position of the submitted item.</summary>
        [JsonPropertyName("index")]
        public int Index { get; set; }

        /// <summary>The submitted item id, when one was sent.</summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        /// <summary>Credits charged for this item (0 for failed items after a confirmed refund).</summary>
        [JsonPropertyName("charged_credits")]
        public int? ChargedCredits { get; set; }

        /// <summary>The prediction, for a successful item.</summary>
        [JsonPropertyName("data")]
        public Prediction? Data { get; set; }

        /// <summary>The item error (smaller Problem shape), for a failed item.</summary>
        [JsonPropertyName("error")]
        public Problem? Error { get; set; }

        /// <summary>Unknown fields, preserved as returned.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }

        /// <summary>True when the item has <c>data</c> and no <c>error</c>.</summary>
        [JsonIgnore]
        public bool IsSuccess => Data != null && Error == null;
    }

    /// <summary>The <c>meta</c> object.</summary>
    public sealed class Meta
    {
        /// <summary>Unique id of this HTTP attempt (also in <c>X-Request-ID</c>).</summary>
        [JsonPropertyName("request_id")]
        public string? RequestId { get; set; }

        /// <summary>Server processing time in milliseconds.</summary>
        [JsonPropertyName("duration_ms")]
        public long? DurationMs { get; set; }

        /// <summary>How the request was authorized.</summary>
        [JsonPropertyName("access")]
        public Access? Access { get; set; }

        /// <summary>Billing outcome of this request.</summary>
        [JsonPropertyName("usage")]
        public Usage? Usage { get; set; }

        /// <summary>Batch counts (batch responses only).</summary>
        [JsonPropertyName("summary")]
        public BatchSummary? Summary { get; set; }

        /// <summary>Unknown fields, preserved as returned.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>The <c>meta.access</c> object.</summary>
    public sealed class Access
    {
        /// <summary><c>api_key</c>, <c>ip_trial</c> or <c>unauthenticated</c>.</summary>
        [JsonPropertyName("mode")]
        public string? Mode { get; set; }

        /// <summary>Null, <c>api_key_missing</c>, <c>api_key_invalid</c> or <c>api_key_not_found</c>.</summary>
        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        /// <summary>Unknown fields, preserved as returned.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>The <c>meta.usage</c> object.</summary>
    public sealed class Usage
    {
        /// <summary>Net charge of the operation; null when billing is unconfirmed.</summary>
        [JsonPropertyName("charged_credits")]
        public int? ChargedCredits { get; set; }

        /// <summary>Balance at completion. Can be negative; null when unknown.</summary>
        [JsonPropertyName("remaining_credits")]
        public long? RemainingCredits { get; set; }

        /// <summary><c>not_charged</c>, <c>confirmed</c> or <c>unconfirmed</c>.</summary>
        [JsonPropertyName("billing_status")]
        public string? BillingStatus { get; set; }

        /// <summary>UTC IP-trial reset timestamp (ISO 8601); null otherwise. Not a subscription expiry.</summary>
        [JsonPropertyName("resets_at")]
        public string? ResetsAt { get; set; }

        /// <summary>Trial credit limit (10) or null.</summary>
        [JsonPropertyName("limit")]
        public int? Limit { get; set; }

        /// <summary>Trial period in seconds (86400) or null.</summary>
        [JsonPropertyName("period_seconds")]
        public int? PeriodSeconds { get; set; }

        /// <summary>Unknown fields, preserved as returned.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>The <c>meta.summary</c> object of a batch response.</summary>
    public sealed class BatchSummary
    {
        /// <summary>Total items (succeeded + failed).</summary>
        [JsonPropertyName("total")]
        public int Total { get; set; }

        /// <summary>Succeeded items (identified + unknown).</summary>
        [JsonPropertyName("succeeded")]
        public int Succeeded { get; set; }

        /// <summary>Items with a male/female result.</summary>
        [JsonPropertyName("identified")]
        public int Identified { get; set; }

        /// <summary>Successful, billable items with a null gender.</summary>
        [JsonPropertyName("unknown")]
        public int Unknown { get; set; }

        /// <summary>Failed items.</summary>
        [JsonPropertyName("failed")]
        public int Failed { get; set; }

        /// <summary>Unknown fields, preserved as returned.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>The <c>data</c> object of <c>GET /usage</c>.</summary>
    public sealed class UsageData
    {
        /// <summary>Current balance; can be negative.</summary>
        [JsonPropertyName("remaining_credits")]
        public long? RemainingCredits { get; set; }

        /// <summary>Account expiry timestamp, or null.</summary>
        [JsonPropertyName("expires_at")]
        public string? ExpiresAt { get; set; }

        /// <summary>UTC IP-trial reset timestamp, or null.</summary>
        [JsonPropertyName("resets_at")]
        public string? ResetsAt { get; set; }

        /// <summary>Trial credit limit, or null.</summary>
        [JsonPropertyName("limit")]
        public int? Limit { get; set; }

        /// <summary>Trial period in seconds, or null.</summary>
        [JsonPropertyName("period_seconds")]
        public int? PeriodSeconds { get; set; }

        /// <summary>Unknown fields, preserved as returned.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>The <c>data</c> object of <c>POST /phone/validate</c>.</summary>
    public sealed class PhoneValidation
    {
        /// <summary>Whether the number is valid.</summary>
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        /// <summary>Whether the number is possible.</summary>
        [JsonPropertyName("possible")]
        public bool Possible { get; set; }

        /// <summary>E.164 form, or null.</summary>
        [JsonPropertyName("e164")]
        public string? E164 { get; set; }

        /// <summary>Detected country, or null.</summary>
        [JsonPropertyName("country")]
        public string? Country { get; set; }

        /// <summary>Country calling code, or null.</summary>
        [JsonPropertyName("country_calling_code")]
        public int? CountryCallingCode { get; set; }

        /// <summary>Unknown fields, preserved as returned.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>RFC 9457 Problem Details returned by the API (and by failed batch items).</summary>
    public sealed class Problem
    {
        /// <summary>Problem type URI.</summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        /// <summary>Short title.</summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>HTTP status.</summary>
        [JsonPropertyName("status")]
        public int? Status { get; set; }

        /// <summary>Human-readable detail. Do not match on this text; use <see cref="Code"/>.</summary>
        [JsonPropertyName("detail")]
        public string? Detail { get; set; }

        /// <summary>Instance URI.</summary>
        [JsonPropertyName("instance")]
        public string? Instance { get; set; }

        /// <summary>Stable machine-readable error code (see <c>GET /errors</c>).</summary>
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        /// <summary>Request id.</summary>
        [JsonPropertyName("request_id")]
        public string? RequestId { get; set; }

        /// <summary>Link to the error catalog.</summary>
        [JsonPropertyName("documentation")]
        public string? Documentation { get; set; }

        /// <summary>Recommended action, e.g. <c>correct_request</c> or <c>wait_then_retry</c>.</summary>
        [JsonPropertyName("action")]
        public string? Action { get; set; }

        /// <summary>Validation pointers.</summary>
        [JsonPropertyName("errors")]
        public List<ProblemFieldError>? Errors { get; set; }

        /// <summary>Metadata (top-level application errors only).</summary>
        [JsonPropertyName("meta")]
        public Meta? Meta { get; set; }

        /// <summary>Unknown fields (for example batch <c>data</c>/<c>results</c>), preserved as returned.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>One validation pointer of a Problem.</summary>
    public sealed class ProblemFieldError
    {
        /// <summary>JSON pointer to the invalid field, e.g. <c>/value</c>.</summary>
        [JsonPropertyName("pointer")]
        public string? Pointer { get; set; }

        /// <summary>Message for the field.</summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        /// <summary>Unknown fields, preserved as returned.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Response of <c>GET /errors</c>.</summary>
    public sealed class ErrorCatalog
    {
        /// <summary>Error entries keyed by code.</summary>
        [JsonPropertyName("errors")]
        public Dictionary<string, ErrorCatalogEntry>? Errors { get; set; }

        /// <summary>Unknown fields, preserved as returned.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>One error catalog entry.</summary>
    public sealed class ErrorCatalogEntry
    {
        /// <summary>Stable code.</summary>
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        /// <summary>HTTP statuses that can carry this code.</summary>
        [JsonPropertyName("http_statuses")]
        public List<int>? HttpStatuses { get; set; }

        /// <summary>Recommended action.</summary>
        [JsonPropertyName("action")]
        public string? Action { get; set; }

        /// <summary>Explanation.</summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>Unknown fields, preserved as returned.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }
}
