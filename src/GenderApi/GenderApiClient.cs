using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenderApi.Internal;

namespace GenderApi
{
    /// <summary>
    /// Client for the GenderAPI.io V2 API (<c>https://api.genderapi.io/api/v2</c>).
    /// <para>
    /// Server-side only: never embed the API key in browser or mobile code. Constructing the client
    /// makes no request. Every prediction or phone request is a single attempt: the client never retries
    /// (a lost response may still be billed), never follows redirects, and applies a 10-second default timeout.
    /// </para>
    /// <para>Instances are thread-safe and intended to be reused.</para>
    /// </summary>
    public sealed class GenderApiClient : IGenderApiClient
    {
        /// <summary>SDK version.</summary>
        public const string Version = "2.0.0";

        private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions();
        private static readonly JsonWriterOptions WriteOptions = new JsonWriterOptions
        {
            // Keep non-ASCII input as UTF-8 so bodies stay well under the 64 KiB limit.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private readonly HttpClient _http;
        private readonly bool _ownsHttpClient;
        private readonly string? _apiKey;
        private readonly Uri _baseUrl;
        private readonly TimeSpan _timeout;
        private readonly string _userAgent;
        private readonly bool _requireApiKeyAccess;
        private int _disposed;

        /// <summary>Creates a client that reads the key from <c>GENDERAPI_API_KEY</c> (or sends none).</summary>
        public GenderApiClient()
            : this(new GenderApiClientOptions())
        {
        }

        /// <summary>Creates a client with the given API key (null reads <c>GENDERAPI_API_KEY</c>).</summary>
        public GenderApiClient(string? apiKey)
            : this(new GenderApiClientOptions { ApiKey = apiKey })
        {
        }

        /// <summary>Creates a client with the given options. No request is made.</summary>
        public GenderApiClient(GenderApiClientOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _apiKey = Validation.NormalizeApiKey(options.ApiKey ?? Environment.GetEnvironmentVariable(GenderApiClientOptions.ApiKeyEnvironmentVariable));
            _baseUrl = Validation.NormalizeBaseUrl(options.BaseUrl);

            if (options.Timeout != Timeout.InfiniteTimeSpan && (options.Timeout <= TimeSpan.Zero || options.Timeout.TotalMilliseconds > int.MaxValue))
            {
                throw new GenderApiValidationException("Timeout must be positive (or Timeout.InfiniteTimeSpan).", nameof(options.Timeout));
            }

            _timeout = options.Timeout;

            string ua = "genderapi-dotnet/" + Version;
            if (!string.IsNullOrWhiteSpace(options.UserAgentSuffix))
            {
                if (Validation.HasControlCharacter(options.UserAgentSuffix!))
                {
                    throw new GenderApiValidationException("UserAgentSuffix must not contain control characters.", nameof(options.UserAgentSuffix));
                }

                ua += " " + options.UserAgentSuffix!.Trim();
            }

            _userAgent = ua;
            _requireApiKeyAccess = options.RequireApiKeyAccess;

            if (options.HttpClient != null)
            {
                _http = options.HttpClient;
                _ownsHttpClient = false;
            }
            else
            {
                var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false,
                };
                _http = new HttpClient(handler, disposeHandler: true)
                {
                    // The per-request timeout below governs; HttpClient.Timeout would hide the cause.
                    Timeout = Timeout.InfiniteTimeSpan,
                };
                _ownsHttpClient = true;
            }
        }

        /// <summary>True when an API key was configured (the key itself is never exposed).</summary>
        public bool HasApiKey => _apiKey != null;

        /// <summary>The normalized base URL.</summary>
        public Uri BaseUrl => _baseUrl;

        /// <inheritdoc />
        public Task<GenderResponse> GenderAsync(GenderRequest request, CancellationToken cancellationToken = default)
        {
            Validation.ValidateItem(request, nameof(request));
            byte[] body = WriteJson(w => WriteItem(w, request));
            return SendAsync<GenderResponse>(HttpMethod.Post, "/gender", body, authenticate: true, cancellationToken);
        }

        /// <inheritdoc />
        public Task<GenderResponse> GenderAsync(GenderInputType type, string value, string? country = null, AiMode? aiMode = null, bool? forceToGenderize = null, string? id = null, CancellationToken cancellationToken = default)
            => GenderAsync(new GenderRequest(type, value, country, aiMode, forceToGenderize, id), cancellationToken);

        /// <inheritdoc />
        public Task<GenderResponse> NameAsync(string value, string? country = null, AiMode? aiMode = null, bool? forceToGenderize = null, string? id = null, CancellationToken cancellationToken = default)
            => GenderAsync(GenderRequest.ForName(value, country, aiMode, forceToGenderize, id), cancellationToken);

        /// <inheritdoc />
        public Task<GenderResponse> EmailAsync(string value, string? country = null, AiMode? aiMode = null, bool? forceToGenderize = null, string? id = null, CancellationToken cancellationToken = default)
            => GenderAsync(GenderRequest.ForEmail(value, country, aiMode, forceToGenderize, id), cancellationToken);

        /// <inheritdoc />
        public Task<GenderResponse> UsernameAsync(string value, string? country = null, AiMode? aiMode = null, bool? forceToGenderize = null, string? id = null, CancellationToken cancellationToken = default)
            => GenderAsync(GenderRequest.ForUsername(value, country, aiMode, forceToGenderize, id), cancellationToken);

        /// <inheritdoc />
        public Task<BatchResponse> GenderBatchAsync(IEnumerable<GenderRequest> items, CancellationToken cancellationToken = default)
        {
            List<GenderRequest> list = Validation.ValidateBatch(items, nameof(items));
            byte[] body = WriteJson(w =>
            {
                w.WriteStartObject();
                w.WritePropertyName("items");
                w.WriteStartArray();
                foreach (GenderRequest item in list)
                {
                    WriteItem(w, item);
                }

                w.WriteEndArray();
                w.WriteEndObject();
            });
            return SendAsync<BatchResponse>(HttpMethod.Post, "/gender/batch", body, authenticate: true, cancellationToken);
        }

        /// <inheritdoc />
        public Task<UsageResponse> UsageAsync(CancellationToken cancellationToken = default)
            => SendAsync<UsageResponse>(HttpMethod.Get, "/usage", null, authenticate: true, cancellationToken);

        /// <inheritdoc />
        public Task<PhoneValidationResponse> ValidatePhoneAsync(string number, string? country = null, CancellationToken cancellationToken = default)
        {
            Validation.ValidatePhone(number, country);
            byte[] body = WriteJson(w =>
            {
                w.WriteStartObject();
                w.WriteString("number", number);
                if (country != null)
                {
                    w.WriteString("country", country);
                }

                w.WriteEndObject();
            });
            return SendAsync<PhoneValidationResponse>(HttpMethod.Post, "/phone/validate", body, authenticate: true, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<JsonElement> CapabilitiesAsync(CancellationToken cancellationToken = default)
        {
            RawResponse raw = await SendRawAsync(HttpMethod.Get, string.Empty, null, authenticate: false, cancellationToken).ConfigureAwait(false);
            using JsonDocument doc = ParseSuccessDocument(raw);
            return doc.RootElement.Clone();
        }

        /// <inheritdoc />
        public async Task<ErrorCatalog> ErrorCatalogAsync(CancellationToken cancellationToken = default)
        {
            RawResponse raw = await SendRawAsync(HttpMethod.Get, "/errors", null, authenticate: false, cancellationToken).ConfigureAwait(false);
            using JsonDocument doc = ParseSuccessDocument(raw);
            return Deserialize<ErrorCatalog>(doc, raw);
        }

        /// <summary>Disposes the client-owned <see cref="HttpClient"/>. An injected HttpClient is not disposed.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0 && _ownsHttpClient)
            {
                _http.Dispose();
            }
        }

        private static void WriteItem(Utf8JsonWriter w, GenderRequest item)
        {
            w.WriteStartObject();
            w.WriteString("type", TypeToWire(item.Type));
            w.WriteString("value", item.Value);
            if (item.Country != null)
            {
                w.WriteString("country", item.Country);
            }

            if (item.Id != null)
            {
                w.WriteString("id", item.Id);
            }

            if (item.ForceToGenderize.HasValue)
            {
                w.WriteBoolean("forceToGenderize", item.ForceToGenderize.Value);
            }

            if (item.AiMode.HasValue)
            {
                w.WritePropertyName("options");
                w.WriteStartObject();
                w.WriteString("ai_mode", AiModeToWire(item.AiMode.Value));
                w.WriteEndObject();
            }

            w.WriteEndObject();
        }

        internal static string TypeToWire(GenderInputType type)
        {
            switch (type)
            {
                case GenderInputType.Name: return "name";
                case GenderInputType.Email: return "email";
                case GenderInputType.Username: return "username";
                default: throw new GenderApiValidationException("type must be name, email or username.", nameof(type));
            }
        }

        internal static string AiModeToWire(AiMode mode)
        {
            switch (mode)
            {
                case AiMode.Off: return "off";
                case AiMode.Fallback: return "fallback";
                case AiMode.Always: return "always";
                default: throw new GenderApiValidationException("ai_mode must be off, fallback or always.", nameof(mode));
            }
        }

        private static byte[] WriteJson(Action<Utf8JsonWriter> write)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, WriteOptions))
            {
                write(writer);
            }

            return stream.ToArray();
        }

        private async Task<T> SendAsync<T>(HttpMethod method, string path, byte[]? body, bool authenticate, CancellationToken cancellationToken)
            where T : class
        {
            RawResponse raw = await SendRawAsync(method, path, body, authenticate, cancellationToken).ConfigureAwait(false);
            using JsonDocument doc = ParseSuccessDocument(raw);
            T result = Deserialize<T>(doc, raw);
            if (result is IApiResponse target)
            {
                target.SetTransport(raw.StatusCode, raw.Body, raw.RequestIdHeader);
                if (authenticate)
                {
                    CheckAccessMode(target, result, raw);
                }
            }

            return result;
        }

        private void CheckAccessMode(IApiResponse response, object result, RawResponse raw)
        {
            if (_apiKey == null || !_requireApiKeyAccess)
            {
                return;
            }

            Access? access = response.Meta?.Access;
            string? mode = access?.Mode;
            if (mode == null || mode == "api_key")
            {
                return;
            }

            throw new GenderApiAccessModeException(
                "Expected API-key access but the response reports access mode " + JsonSerializer.Serialize(mode)
                    + ". Check your API key; this request may have consumed IP-trial credits.",
                result,
                mode,
                access!.Reason)
            {
                StatusCode = raw.StatusCode,
                RequestId = FirstNonEmpty(response.Meta?.RequestId, raw.RequestIdHeader),
                Meta = response.Meta,
                RawBody = raw.Body,
            };
        }

        private async Task<RawResponse> SendRawAsync(HttpMethod method, string path, byte[]? body, bool authenticate, CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(GenderApiClient));
            }

            cancellationToken.ThrowIfCancellationRequested();

            var uri = new Uri(_baseUrl.AbsoluteUri.TrimEnd('/') + path, UriKind.Absolute);
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/problem+json"));
            request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
            if (authenticate && _apiKey != null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            }

            if (body != null)
            {
                var content = new ByteArrayContent(body);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                request.Content = content;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (_timeout != Timeout.InfiniteTimeSpan)
            {
                timeoutCts.CancelAfter(_timeout);
            }

            HttpResponseMessage response;
            string text;
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                throw new GenderApiTimeoutException("The request timed out; the billing outcome is unknown. Do not automatically retry; check usage first.", ex);
            }
            catch (HttpRequestException ex)
            {
                throw new GenderApiTransportException("Transport failed; the billing outcome is unknown. Do not automatically retry; check usage first.", ex);
            }

            using (response)
            {
                try
                {
#if NET5_0_OR_GREATER
                    text = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
#else
                    text = response.Content == null ? string.Empty : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException ex)
                {
                    throw new GenderApiTimeoutException("The request timed out while reading the response; the billing outcome is unknown. Do not automatically retry.", ex);
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is DecoderFallbackException)
                {
                    throw new GenderApiTransportException("Reading the response failed; the billing outcome is unknown. Do not automatically retry.", ex);
                }

                var raw = new RawResponse
                {
                    StatusCode = (int)response.StatusCode,
                    Body = text,
                    ContentType = response.Content?.Headers.ContentType?.MediaType,
                    RequestIdHeader = FirstHeader(response, "X-Request-ID"),
                    RetryAfterRaw = FirstHeader(response, "Retry-After"),
                    RetryAfter = ParseRetryAfter(response),
                };

                if (raw.StatusCode >= 300 && raw.StatusCode < 400)
                {
                    throw new GenderApiRedirectException("The server answered with HTTP " + raw.StatusCode + "; redirects are not followed so the API key is not forwarded. Check BaseUrl.")
                    {
                        StatusCode = raw.StatusCode,
                        Location = response.Headers.Location,
                        RequestId = raw.RequestIdHeader,
                        RawBody = text,
                    };
                }

                Uri? finalUri = response.RequestMessage?.RequestUri;
                if (finalUri != null && finalUri != uri)
                {
                    throw new GenderApiRedirectException("The HTTP handler followed a redirect. Configure the injected HttpClient handler with AllowAutoRedirect = false.")
                    {
                        StatusCode = raw.StatusCode,
                        Location = finalUri,
                        RequestId = raw.RequestIdHeader,
                    };
                }

                if (raw.StatusCode >= 400)
                {
                    throw BuildHttpError(raw);
                }

                if (raw.StatusCode < 200 || raw.StatusCode >= 300)
                {
                    throw new GenderApiException("Unexpected HTTP " + raw.StatusCode + " response.")
                    {
                        StatusCode = raw.StatusCode,
                        RequestId = raw.RequestIdHeader,
                        RawBody = text,
                    };
                }

                return raw;
            }
        }

        private static JsonDocument ParseSuccessDocument(RawResponse raw)
        {
            if (!IsJson(raw.ContentType))
            {
                throw new GenderApiTransportException("Expected a JSON response; the billing outcome is unknown. Inspect the request before another submission.")
                {
                    StatusCode = raw.StatusCode,
                    RequestId = raw.RequestIdHeader,
                    RawBody = raw.Body,
                };
            }

            JsonDocument? doc = TryParseObject(raw.Body);
            if (doc == null)
            {
                throw new GenderApiTransportException("The response is not a JSON object; the billing outcome is unknown.")
                {
                    StatusCode = raw.StatusCode,
                    RequestId = raw.RequestIdHeader,
                    RawBody = raw.Body,
                };
            }

            return doc;
        }

        private static T Deserialize<T>(JsonDocument doc, RawResponse raw)
            where T : class
        {
            try
            {
                T? value = doc.RootElement.Deserialize<T>(ReadOptions);
                if (value != null)
                {
                    return value;
                }
            }
            catch (JsonException ex)
            {
                throw new GenderApiTransportException("The response does not match the V2 contract; the billing outcome is unknown.", ex)
                {
                    StatusCode = raw.StatusCode,
                    RequestId = raw.RequestIdHeader,
                    RawBody = raw.Body,
                };
            }

            throw new GenderApiTransportException("The response body is empty.")
            {
                StatusCode = raw.StatusCode,
                RequestId = raw.RequestIdHeader,
                RawBody = raw.Body,
            };
        }

        private static GenderApiException BuildHttpError(RawResponse raw)
        {
            Problem? problem = null;
            List<BatchItemResult>? batchData = null;
            if (IsJson(raw.ContentType))
            {
                using JsonDocument? doc = TryParseObject(raw.Body);
                if (doc != null)
                {
                    try
                    {
                        problem = doc.RootElement.Deserialize<Problem>(ReadOptions);
                    }
                    catch (JsonException)
                    {
                        problem = null;
                    }

                    if (doc.RootElement.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array)
                    {
                        try
                        {
                            batchData = data.Deserialize<List<BatchItemResult>>(ReadOptions);
                        }
                        catch (JsonException)
                        {
                            batchData = null;
                        }
                    }
                }
            }

            string? requestId = FirstNonEmpty(problem?.RequestId, problem?.Meta?.RequestId, raw.RequestIdHeader);
            string message;
            if (problem?.Code != null)
            {
                message = "GenderAPI request failed with HTTP " + raw.StatusCode + " (" + problem.Code + ")"
                    + (problem.Action != null ? "; action: " + problem.Action : string.Empty)
                    + (problem.Meta?.Usage?.BillingStatus != null ? "; billing_status: " + problem.Meta.Usage.BillingStatus : string.Empty)
                    + ".";
            }
            else
            {
                message = "GenderAPI request failed with HTTP " + raw.StatusCode + " without a Problem Details body (possibly a proxy error).";
            }

            return new GenderApiException(message)
            {
                StatusCode = raw.StatusCode,
                Code = problem?.Code,
                Title = problem?.Title,
                Detail = problem?.Detail,
                Action = problem?.Action,
                Errors = (IReadOnlyList<ProblemFieldError>?)problem?.Errors ?? Array.Empty<ProblemFieldError>(),
                RequestId = requestId,
                RetryAfter = raw.RetryAfter,
                RetryAfterRaw = raw.RetryAfterRaw,
                Problem = problem,
                Meta = problem?.Meta,
                BatchData = batchData,
                RawBody = raw.Body,
            };
        }

        private static JsonDocument? TryParseObject(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                return null;
            }

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                doc.Dispose();
                return null;
            }

            return doc;
        }

        private static bool IsJson(string? mediaType)
        {
            return mediaType != null && mediaType.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string? FirstHeader(HttpResponseMessage response, string name)
        {
            if (response.Headers.TryGetValues(name, out IEnumerable<string>? values))
            {
                return values.FirstOrDefault();
            }

            return null;
        }

        private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
        {
            RetryConditionHeaderValue? retry = response.Headers.RetryAfter;
            if (retry == null)
            {
                return null;
            }

            if (retry.Delta.HasValue)
            {
                return retry.Delta.Value;
            }

            if (retry.Date.HasValue)
            {
                TimeSpan wait = retry.Date.Value - DateTimeOffset.UtcNow;
                return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
            }

            return null;
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (string? v in values)
            {
                if (!string.IsNullOrEmpty(v))
                {
                    return v;
                }
            }

            return null;
        }

        private sealed class RawResponse
        {
            public int StatusCode { get; set; }

            public string Body { get; set; } = string.Empty;

            public string? ContentType { get; set; }

            public string? RequestIdHeader { get; set; }

            public string? RetryAfterRaw { get; set; }

            public TimeSpan? RetryAfter { get; set; }
        }
    }
}
