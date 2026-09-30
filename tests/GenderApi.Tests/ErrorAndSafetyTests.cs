using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenderApi.Tests
{
    public class ErrorTests
    {
        private const string Gender = "/api/v2/gender";

        private static string Problem(int status, string code, string action, string billing = "not_charged", string? charged = "0", string mode = "api_key", string? reason = null)
        {
            return "{\"type\":\"urn:genderapi:problem:" + code + "\",\"title\":\"" + code.Replace('_', ' ') + "\",\"status\":" + status
                + ",\"detail\":\"d\",\"instance\":\"urn:uuid:22222222-2222-4222-8222-222222222222\",\"code\":\"" + code
                + "\",\"request_id\":\"22222222-2222-4222-8222-222222222222\",\"documentation\":\"https://api.genderapi.io/api/v2/errors\",\"action\":\"" + action
                + "\",\"meta\":{\"request_id\":\"22222222-2222-4222-8222-222222222222\",\"duration_ms\":3,\"access\":{\"mode\":\"" + mode + "\",\"reason\":" + (reason == null ? "null" : "\"" + reason + "\"")
                + "},\"usage\":{\"charged_credits\":" + (charged ?? "null") + ",\"remaining_credits\":" + (charged == null ? "null" : "5") + ",\"billing_status\":\"" + billing + "\",\"resets_at\":null,\"limit\":null,\"period_seconds\":null}}}";
        }

        private static async Task<GenderApiException> SendExpectingError(StubHandler stub, Func<GenderApiClient, Task> call)
        {
            using var client = ClientFactory.Create(stub);
            var ex = await Assert.ThrowsAnyAsync<GenderApiException>(() => call(client));
            Assert.Equal(1, stub.CallCount); // never retried
            return ex;
        }

        [Fact]
        public async Task Status_401_is_structured()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.Unauthorized, Problem(401, "invalid_api_key", "check_credentials", mode: "unauthenticated", reason: "api_key_invalid"), "application/problem+json");
            var ex = await SendExpectingError(stub, c => c.NameAsync("Onur"));

            Assert.Equal(401, ex.StatusCode);
            Assert.Equal("invalid_api_key", ex.Code);
            Assert.Equal("check_credentials", ex.Action);
            Assert.Equal("invalid api key", ex.Title);
            Assert.Equal("d", ex.Detail);
            Assert.Equal("22222222-2222-4222-8222-222222222222", ex.RequestId);
            Assert.Equal("not_charged", ex.BillingStatus);
            Assert.Equal("unauthenticated", ex.Meta!.Access!.Mode);
            Assert.Contains("invalid_api_key", ex.RawBody);
            Assert.DoesNotContain(ClientFactory.TestKey, ex.Message);
        }

        [Fact]
        public async Task Status_403_insufficient_credits_uses_openapi_example()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.Forbidden, Fixtures.Example(Gender, "post", "403", "insufficient"), "application/problem+json");
            var ex = await SendExpectingError(stub, c => c.NameAsync("Onur"));

            Assert.Equal(403, ex.StatusCode);
            Assert.Equal("insufficient_credits", ex.Code);
            Assert.Equal("add_credits_or_wait_for_reset", ex.Action);
            Assert.Equal("not_charged", ex.BillingStatus);
            Assert.Equal(0, ex.Meta!.Usage!.RemainingCredits);
            Assert.Equal("2026-09-26T12:00:00.000Z", ex.Meta.Usage.ResetsAt);
            Assert.Equal("https://api.genderapi.io/api/v2/errors", ex.Problem!.Documentation);
        }

        [Fact]
        public async Task Status_422_exposes_validation_pointers()
        {
            var stub = new StubHandler().EnqueueJson((HttpStatusCode)422, Fixtures.Example(Gender, "post", "422", "validation"), "application/problem+json");
            var ex = await SendExpectingError(stub, c => c.EmailAsync("not-an-email"));

            Assert.Equal(422, ex.StatusCode);
            Assert.Equal("validation_error", ex.Code);
            ProblemFieldError e = Assert.Single(ex.Errors);
            Assert.Equal("/value", e.Pointer);
            Assert.Equal("Invalid email address.", e.Message);
            Assert.Null(ex.Meta!.Usage!.RemainingCredits);
        }

        [Fact]
        public async Task Status_429_surfaces_retry_after_and_does_not_retry()
        {
            var stub = new StubHandler().EnqueueJson((HttpStatusCode)429, Problem(429, "rate_limit_exceeded", "wait_then_retry"), "application/problem+json",
                r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7)));
            var ex = await SendExpectingError(stub, c => c.NameAsync("Onur"));

            Assert.Equal(429, ex.StatusCode);
            Assert.Equal("rate_limit_exceeded", ex.Code);
            Assert.Equal(TimeSpan.FromSeconds(7), ex.RetryAfter);
            Assert.Equal("7", ex.RetryAfterRaw);
        }

        [Fact]
        public async Task Status_429_with_http_date_retry_after()
        {
            DateTimeOffset when = DateTimeOffset.UtcNow.AddMinutes(2);
            var stub = new StubHandler().EnqueueJson((HttpStatusCode)429, Problem(429, "concurrency_limit", "wait_then_retry"), "application/problem+json",
                r => r.Headers.RetryAfter = new RetryConditionHeaderValue(when));
            var ex = await SendExpectingError(stub, c => c.NameAsync("Onur"));

            Assert.NotNull(ex.RetryAfter);
            Assert.InRange(ex.RetryAfter!.Value.TotalSeconds, 60, 121);
        }

        [Fact]
        public async Task Status_502_prediction_failure_exposes_billing_status()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.BadGateway, Problem(502, "ai_upstream_error", "inspect_billing_before_retry", billing: "confirmed", charged: "0"), "application/problem+json");
            var ex = await SendExpectingError(stub, c => c.NameAsync("Onur", aiMode: AiMode.Always));

            Assert.Equal(502, ex.StatusCode);
            Assert.Equal("ai_upstream_error", ex.Code);
            Assert.Equal("confirmed", ex.BillingStatus);
            Assert.Equal(0, ex.Meta!.Usage!.ChargedCredits);
        }

        [Fact]
        public async Task Status_503_unconfirmed_billing_uses_openapi_example()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.ServiceUnavailable, Fixtures.Example(Gender, "post", "503", "unconfirmed"), "application/problem+json");
            var ex = await SendExpectingError(stub, c => c.NameAsync("Onur"));

            Assert.Equal(503, ex.StatusCode);
            Assert.Equal("billing_reconciliation_required", ex.Code);
            Assert.Equal("contact_support", ex.Action);
            Assert.Equal("unconfirmed", ex.BillingStatus);
            Assert.Null(ex.Meta!.Usage!.ChargedCredits);
            Assert.Null(ex.Meta.Usage.RemainingCredits);
            Assert.Equal("11111111-1111-4111-8111-111111111111", ex.RequestId);
        }

        [Fact]
        public async Task All_failed_batch_keeps_data()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.BadGateway, Fixtures.Example("/api/v2/gender/batch", "post", "502", "batchFailed"), "application/problem+json");
            var ex = await SendExpectingError(stub, c => c.GenderBatchAsync(new[] { GenderRequest.ForName("Alex", aiMode: AiMode.Always) }));

            Assert.Equal(502, ex.StatusCode);
            Assert.Equal("ai_upstream_error", ex.Code);
            BatchItemResult item = Assert.Single(ex.BatchData!);
            Assert.Equal(0, item.Index);
            Assert.Equal(0, item.ChargedCredits);
            Assert.False(item.IsSuccess);
            Assert.Equal("ai_upstream_error", item.Error!.Code);
            Assert.Equal(1, ex.Meta!.Summary!.Failed);
            Assert.Equal("confirmed", ex.BillingStatus);
            Assert.True(ex.Problem!.ExtensionData!.ContainsKey("results")); // legacy alias preserved
        }

        [Fact]
        public async Task Request_id_falls_back_to_header_for_non_json_proxy_errors()
        {
            var stub = new StubHandler().Enqueue((req, ct) =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.BadGateway)
                {
                    Content = new StringContent("<html>502 Bad Gateway</html>", Encoding.UTF8, "text/html"),
                };
                r.Headers.Add("X-Request-ID", "hdr-123");
                return Task.FromResult(r);
            });
            var ex = await SendExpectingError(stub, c => c.NameAsync("Onur"));

            Assert.Equal(502, ex.StatusCode);
            Assert.Null(ex.Code);
            Assert.Null(ex.Problem);
            Assert.Equal("hdr-123", ex.RequestId);
            Assert.Contains("502 Bad Gateway", ex.RawBody);
        }

        [Fact]
        public async Task Success_status_with_non_json_body_is_an_error()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, "<html>ok</html>", "text/html");
            var ex = await SendExpectingError(stub, c => c.NameAsync("Onur"));
            Assert.IsType<GenderApiTransportException>(ex);
            Assert.Equal(200, ex.StatusCode);
        }

        [Fact]
        public async Task Success_status_with_invalid_json_is_an_error()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, "{\"data\":");
            var ex = await SendExpectingError(stub, c => c.NameAsync("Onur"));
            Assert.IsType<GenderApiTransportException>(ex);
        }
    }

    public class SafetyTests
    {
        [Theory]
        [InlineData(HttpStatusCode.MovedPermanently)]
        [InlineData(HttpStatusCode.Found)]
        [InlineData(HttpStatusCode.TemporaryRedirect)]
        [InlineData(HttpStatusCode.PermanentRedirect)]
        public async Task Redirects_are_rejected_not_followed(HttpStatusCode status)
        {
            var stub = new StubHandler().Enqueue((req, ct) =>
            {
                var r = new HttpResponseMessage(status) { Content = new StringContent(string.Empty) };
                r.Headers.Location = new Uri("https://evil.example/steal");
                return Task.FromResult(r);
            });
            using var client = ClientFactory.Create(stub);

            var ex = await Assert.ThrowsAsync<GenderApiRedirectException>(() => client.NameAsync("Onur"));

            Assert.Equal((int)status, ex.StatusCode);
            Assert.Equal(new Uri("https://evil.example/steal"), ex.Location);
            Assert.Equal(1, stub.CallCount);
        }

        [Fact]
        public async Task A_redirect_followed_by_an_injected_handler_is_rejected()
        {
            var stub = new StubHandler().Enqueue((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Fixtures.KeyedExample("/api/v2/gender", "post", "200", "dataset"), Encoding.UTF8, "application/json"),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://elsewhere.example/api/v2/gender"),
            }));
            using var client = ClientFactory.Create(stub);

            await Assert.ThrowsAsync<GenderApiRedirectException>(() => client.NameAsync("Onur"));
            Assert.Equal(1, stub.CallCount);
        }

        [Fact]
        public void Default_timeout_is_ten_seconds()
        {
            Assert.Equal(TimeSpan.FromSeconds(10), new GenderApiClientOptions().Timeout);
        }

        [Fact]
        public async Task Timeout_raises_timeout_exception_without_retry()
        {
            var stub = new StubHandler().Enqueue(async (req, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            using var client = ClientFactory.Create(stub, timeout: TimeSpan.FromMilliseconds(100));

            var ex = await Assert.ThrowsAsync<GenderApiTimeoutException>(() => client.NameAsync("Onur"));

            Assert.Contains("billing outcome is unknown", ex.Message);
            Assert.Equal(1, stub.CallCount);
        }

        [Fact]
        public async Task Caller_cancellation_is_not_reported_as_timeout()
        {
            var stub = new StubHandler().Enqueue(async (req, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            using var client = ClientFactory.Create(stub);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

            var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.NameAsync("Onur", cancellationToken: cts.Token));
            Assert.IsNotType<GenderApiTimeoutException>(ex);
        }

        [Fact]
        public async Task Network_failure_is_a_transport_error_without_retry()
        {
            var stub = new StubHandler().Enqueue((req, ct) => throw new HttpRequestException("connection reset"));
            using var client = ClientFactory.Create(stub);

            var ex = await Assert.ThrowsAsync<GenderApiTransportException>(() => client.ValidatePhoneAsync("+15550000000"));

            Assert.Contains("Do not automatically retry", ex.Message);
            Assert.Equal(1, stub.CallCount);
        }

        [Fact]
        public void Invalid_timeout_is_rejected()
        {
            Assert.Throws<GenderApiValidationException>(() => new GenderApiClient(new GenderApiClientOptions { Timeout = TimeSpan.Zero }));
        }
    }

    public class ValidationTests
    {
        private static async Task AssertInvalid(Func<GenderApiClient, Task> call)
        {
            var stub = new StubHandler();
            using var client = ClientFactory.Create(stub);
            await Assert.ThrowsAsync<GenderApiValidationException>(() => call(client));
            Assert.Equal(0, stub.CallCount); // no network call
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("bad\u0000value")]
        [InlineData("tab\tvalue")]
        [InlineData("del\u007fvalue")]
        public Task Invalid_values_are_rejected(string value) => AssertInvalid(c => c.NameAsync(value));

        [Fact]
        public Task Null_value_is_rejected() => AssertInvalid(c => c.NameAsync(null!));

        [Fact]
        public Task Too_long_value_is_rejected() => AssertInvalid(c => c.NameAsync(new string('a', 255)));

        [Fact]
        public async Task Value_of_254_characters_is_accepted()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, Fixtures.KeyedExample("/api/v2/gender", "post", "200", "unknown"));
            using var client = ClientFactory.Create(stub);
            await client.NameAsync(new string('a', 254));
            Assert.Equal(1, stub.CallCount);
        }

        [Theory]
        [InlineData("tr")]
        [InlineData("TUR")]
        [InlineData("T")]
        [InlineData("")]
        [InlineData("1A")]
        public Task Invalid_countries_are_rejected(string country) => AssertInvalid(c => c.NameAsync("Onur", country: country));

        [Fact]
        public Task Undefined_type_is_rejected() => AssertInvalid(c => c.GenderAsync((GenderInputType)99, "Onur"));

        [Fact]
        public Task Undefined_ai_mode_is_rejected() => AssertInvalid(c => c.NameAsync("Onur", aiMode: (AiMode)42));

        [Theory]
        [InlineData(AiMode.Off)]
        [InlineData(AiMode.Always)]
        public Task Force_to_genderize_cannot_combine_with_off_or_always(AiMode mode)
            => AssertInvalid(c => c.UsernameAsync("prenses", aiMode: mode, forceToGenderize: true));

        [Fact]
        public Task Empty_id_is_rejected() => AssertInvalid(c => c.NameAsync("Onur", id: ""));

        [Fact]
        public Task Too_long_id_is_rejected() => AssertInvalid(c => c.NameAsync("Onur", id: new string('i', 65)));

        [Fact]
        public Task Null_request_is_rejected() => AssertInvalid(c => c.GenderAsync(null!));

        [Fact]
        public Task Empty_batch_is_rejected() => AssertInvalid(c => c.GenderBatchAsync(Array.Empty<GenderRequest>()));

        [Fact]
        public Task Null_batch_is_rejected() => AssertInvalid(c => c.GenderBatchAsync(null!));

        [Fact]
        public Task Batch_over_50_items_is_rejected()
            => AssertInvalid(c => c.GenderBatchAsync(Enumerable.Range(0, 51).Select(i => GenderRequest.ForName("n" + i))));

        [Fact]
        public Task Batch_with_duplicate_ids_is_rejected()
            => AssertInvalid(c => c.GenderBatchAsync(new[] { GenderRequest.ForName("A", id: "x"), GenderRequest.ForName("B", id: "x") }));

        [Fact]
        public Task Batch_with_invalid_item_is_rejected()
            => AssertInvalid(c => c.GenderBatchAsync(new[] { GenderRequest.ForName("A"), GenderRequest.ForName("") }));

        [Fact]
        public async Task Batch_of_50_items_is_accepted()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, Fixtures.KeyedExample("/api/v2/gender/batch", "post", "200", "batch"));
            using var client = ClientFactory.Create(stub);
            await client.GenderBatchAsync(Enumerable.Range(0, 50).Select(i => GenderRequest.ForName("n" + i, id: "id" + i)));
            Assert.Equal(1, stub.CallCount);
        }

        [Theory]
        [InlineData("12")]
        [InlineData("+1 555 abc")]
        [InlineData("123456789012345678901234567890123")]
        public Task Invalid_phone_numbers_are_rejected(string number) => AssertInvalid(c => c.ValidatePhoneAsync(number));

        [Fact]
        public Task Invalid_phone_country_is_rejected() => AssertInvalid(c => c.ValidatePhoneAsync("+15550000000", "usa"));
    }
}
