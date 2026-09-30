using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenderApi
{
    /// <summary>GenderAPI.io V2 operations. Implemented by <see cref="GenderApiClient"/>.</summary>
    public interface IGenderApiClient : IDisposable
    {
        /// <summary>
        /// <c>POST /gender</c>: one billable prediction. Unknown results (<c>gender</c> null) are successful
        /// and billable. Throws <see cref="GenderApiValidationException"/> without a request for invalid input,
        /// and <see cref="GenderApiException"/> for HTTP errors. Never retried.
        /// </summary>
        Task<GenderResponse> GenderAsync(GenderRequest request, CancellationToken cancellationToken = default);

        /// <summary><c>POST /gender</c> with explicit arguments.</summary>
        Task<GenderResponse> GenderAsync(GenderInputType type, string value, string? country = null, AiMode? aiMode = null, bool? forceToGenderize = null, string? id = null, CancellationToken cancellationToken = default);

        /// <summary><c>POST /gender</c> with <c>type: "name"</c>.</summary>
        Task<GenderResponse> NameAsync(string value, string? country = null, AiMode? aiMode = null, bool? forceToGenderize = null, string? id = null, CancellationToken cancellationToken = default);

        /// <summary><c>POST /gender</c> with <c>type: "email"</c>.</summary>
        Task<GenderResponse> EmailAsync(string value, string? country = null, AiMode? aiMode = null, bool? forceToGenderize = null, string? id = null, CancellationToken cancellationToken = default);

        /// <summary><c>POST /gender</c> with <c>type: "username"</c>.</summary>
        Task<GenderResponse> UsernameAsync(string value, string? country = null, AiMode? aiMode = null, bool? forceToGenderize = null, string? id = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// <c>POST /gender/batch</c> with 1–50 items (the server may allow fewer, e.g. 10 on the IP trial).
        /// Partial success returns normally; inspect <see cref="BatchResponse.FailedItems"/>. When all executed
        /// items fail, a <see cref="GenderApiException"/> is thrown with <see cref="GenderApiException.BatchData"/>.
        /// </summary>
        Task<BatchResponse> GenderBatchAsync(IEnumerable<GenderRequest> items, CancellationToken cancellationToken = default);

        /// <summary><c>GET /usage</c>: current balance and quota (free).</summary>
        Task<UsageResponse> UsageAsync(CancellationToken cancellationToken = default);

        /// <summary><c>POST /phone/validate</c>: one billable phone validation. Never retried.</summary>
        Task<PhoneValidationResponse> ValidatePhoneAsync(string number, string? country = null, CancellationToken cancellationToken = default);

        /// <summary><c>GET /</c>: deployment version, limits and AI availability (no key sent).</summary>
        Task<JsonElement> CapabilitiesAsync(CancellationToken cancellationToken = default);

        /// <summary><c>GET /errors</c>: public error catalog (no key sent).</summary>
        Task<ErrorCatalog> ErrorCatalogAsync(CancellationToken cancellationToken = default);
    }
}
