using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenderApi.Tests
{
    public class SuccessTests
    {
        private const string Gender = "/api/v2/gender";

        [Fact]
        public async Task Single_dataset_result_is_parsed()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, Fixtures.Example(Gender, "post", "200", "dataset"));
            using var client = ClientFactory.Create(stub);

            GenderResponse res = await client.NameAsync("Onur", country: "TR");

            Prediction p = res.Data!;
            Assert.Equal("male", p.Gender);
            Assert.True(p.IsIdentified);
            Assert.Equal(0.9, p.Confidence);
            Assert.Equal("observed_frequency", p.ConfidenceKind);
            Assert.Equal(100, p.SampleCount);
            Assert.Equal("dataset", p.Source);
            Assert.Null(p.Reason);
            Assert.Equal("dataset", p.CountrySource);
            Assert.Equal("onur", p.Match!.Name);
            Assert.Equal("normalized", p.Match.Method);
            Assert.Equal("country", p.Match.Scope);
            Assert.Equal("TR", p.Match.Country);
            Assert.Equal("Onur", p.Input!.Value);
            Assert.Equal("ip_trial", res.Meta!.Access!.Mode);
            Assert.Equal("api_key_missing", res.Meta.Access.Reason);
            Assert.Equal("confirmed", res.Meta.Usage!.BillingStatus);
            Assert.Equal(1, res.Meta.Usage.ChargedCredits);
            Assert.Equal(9, res.Meta.Usage.RemainingCredits);
            Assert.Equal("2026-09-26T12:00:00.000Z", res.Meta.Usage.ResetsAt);
            Assert.Equal(10, res.Meta.Usage.Limit);
            Assert.Equal(86400, res.Meta.Usage.PeriodSeconds);
            Assert.Equal("11111111-1111-4111-8111-111111111111", res.RequestId);
            Assert.Equal(200, res.StatusCode);
            Assert.Contains("\"onur\"", res.RawJson);
            Assert.Equal(1, stub.CallCount);
        }

        [Fact]
        public async Task Single_ai_alias_result_keeps_model_reported_confidence_and_negative_balance()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, Fixtures.Example(Gender, "post", "200", "alias"));
            using var client = ClientFactory.Create(stub);

            GenderResponse res = await client.UsernameAsync("prenses", country: "TR", forceToGenderize: true);

            Prediction p = res.Data!;
            Assert.Equal("female", p.Gender);
            Assert.Null(p.Name);
            Assert.Equal("ai", p.Source);
            Assert.Equal("model_reported", p.ConfidenceKind);
            Assert.Equal(0.7, p.Confidence); // never converted to a percentage
            Assert.Null(p.SampleCount);
            Assert.Equal("model_inference", p.Match!.Method);
            Assert.Equal("ai_association", p.CountrySource);
            Assert.True(p.Input!.ForceToGenderize);
            Assert.Equal(2, res.Meta!.Usage!.ChargedCredits);
            Assert.Equal(-1, res.Meta.Usage.RemainingCredits);
        }

        [Fact]
        public async Task Single_unknown_result_is_success_not_error()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, Fixtures.Example(Gender, "post", "200", "unknown"));
            using var client = ClientFactory.Create(stub);

            GenderResponse res = await client.NameAsync("zzzxxyy", aiMode: AiMode.Off);

            Prediction p = res.Data!;
            Assert.Null(p.Gender);
            Assert.True(p.IsUnknown);
            Assert.Equal("unknown", p.ResultStatus);
            Assert.Equal("not_found", p.Reason);
            Assert.Null(p.Confidence);
            Assert.Null(p.ConfidenceKind);
            Assert.Equal("none", p.Source);
            Assert.Null(p.Match!.Method);
            Assert.Equal(1, res.Meta!.Usage!.ChargedCredits);
        }

        [Fact]
        public async Task Unknown_fields_are_tolerated_and_preserved()
        {
            string json = Fixtures.WithExtraFields(Fixtures.Example(Gender, "post", "200", "dataset"));
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, json);
            using var client = ClientFactory.Create(stub);

            GenderResponse res = await client.NameAsync("Onur");

            Assert.Equal("x", res.ExtensionData!["future_top_level"].GetString());
            Assert.Equal(1, res.Meta!.ExtensionData!["future_meta"].GetInt32());
            Assert.True(res.Meta.Usage!.ExtensionData!["future_usage"].GetBoolean());
            Assert.Equal(1, res.Data!.ExtensionData!["future_data"].GetProperty("nested").GetInt32());
            Assert.Equal("m", res.Data.Match!.ExtensionData!["future_match"].GetString());
            Assert.Equal("i", res.Data.Input!.ExtensionData!["future_input"].GetString());
        }

        [Fact]
        public async Task Unknown_enum_values_are_tolerated()
        {
            string json = Fixtures.Example(Gender, "post", "200", "dataset")
                .Replace("\"observed_frequency\"", "\"future_kind\"")
                .Replace("\"ip_trial\"", "\"future_mode\"");
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, json);
            using var client = ClientFactory.Create(stub);

            GenderResponse res = await client.NameAsync("Onur");

            Assert.Equal("future_kind", res.Data!.ConfidenceKind);
            Assert.Equal("future_mode", res.Meta!.Access!.Mode);
        }

        [Fact]
        public async Task Batch_partial_success_returns_result_and_exposes_failed_items()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, Fixtures.Example("/api/v2/gender/batch", "post", "200", "batch"));
            using var client = ClientFactory.Create(stub);

            BatchResponse res = await client.GenderBatchAsync(new[]
            {
                GenderRequest.ForName("Onur", "TR", id: "known"),
                GenderRequest.ForName("zzzxxyy", id: "missing"),
                GenderRequest.ForName("Alex", aiMode: AiMode.Always, id: "failed"),
            });

            Assert.Equal(3, res.Data!.Count);
            Assert.True(res.HasFailures);
            Assert.Equal(2, res.SucceededItems.Count);
            BatchItemResult failed = Assert.Single(res.FailedItems);
            Assert.Equal(2, failed.Index);
            Assert.Equal("failed", failed.Id);
            Assert.Equal(0, failed.ChargedCredits);
            Assert.Null(failed.Data);
            Assert.Equal("ai_upstream_error", failed.Error!.Code);
            Assert.Equal("inspect_billing_before_retry", failed.Error.Action);
            Assert.Equal(502, failed.Error.Status);

            Assert.Equal("known", res.Data[0].Id);
            Assert.Equal(1, res.Data[0].ChargedCredits);
            Assert.Equal("male", res.Data[0].Data!.Gender);
            Assert.True(res.Data[1].Data!.IsUnknown);

            BatchSummary s = res.Meta!.Summary!;
            Assert.Equal((3, 2, 1, 1, 1), (s.Total, s.Succeeded, s.Identified, s.Unknown, s.Failed));
            Assert.Equal(2, res.Meta.Usage!.ChargedCredits);
        }

        [Fact]
        public async Task Usage_is_parsed()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, Fixtures.Example("/api/v2/usage", "get", "200", "usage"));
            using var client = ClientFactory.Create(stub);

            UsageResponse res = await client.UsageAsync();

            Assert.Equal(HttpMethod.Get, stub.Requests[0].Method);
            Assert.Equal("https://api.genderapi.io/api/v2/usage", stub.Requests[0].Uri.ToString());
            Assert.Null(stub.Requests[0].Body);
            Assert.Equal(9, res.Data!.RemainingCredits);
            Assert.Equal("2026-09-26T12:00:00.000Z", res.Data.ExpiresAt);
            Assert.Equal("not_charged", res.Meta!.Usage!.BillingStatus);
            Assert.Equal(0, res.Meta.Usage.ChargedCredits);
        }

        [Fact]
        public async Task Phone_validation_is_parsed_and_body_is_exact()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, Fixtures.Example("/api/v2/phone/validate", "post", "200", "phone"));
            using var client = ClientFactory.Create(stub);

            PhoneValidationResponse res = await client.ValidatePhoneAsync("+90 (555) 000-0000", "TR");

            Assert.Equal("https://api.genderapi.io/api/v2/phone/validate", stub.Requests[0].Uri.ToString());
            Assert.Equal("{\"number\":\"+90 (555) 000-0000\",\"country\":\"TR\"}", stub.Requests[0].Body);
            Assert.False(res.Data!.Valid);
            Assert.False(res.Data.Possible);
            Assert.Null(res.Data.E164);
            Assert.Null(res.Data.CountryCallingCode);
            Assert.Equal(1, res.Meta!.Usage!.ChargedCredits);
        }

        [Fact]
        public async Task Capabilities_and_error_catalog_send_no_key()
        {
            var stub = new StubHandler()
                .EnqueueJson(HttpStatusCode.OK, "{\"version\":\"2.0.0\",\"capabilities\":{\"max_batch_items\":50}}")
                .EnqueueJson(HttpStatusCode.OK, "{\"errors\":{\"rate_limit_exceeded\":{\"code\":\"rate_limit_exceeded\",\"http_statuses\":[429],\"action\":\"wait_then_retry\",\"description\":\"d\"}}}");
            using var client = ClientFactory.Create(stub);

            JsonElement caps = await client.CapabilitiesAsync();
            ErrorCatalog catalog = await client.ErrorCatalogAsync();

            Assert.Equal("2.0.0", caps.GetProperty("version").GetString());
            Assert.Equal(50, caps.GetProperty("capabilities").GetProperty("max_batch_items").GetInt32());
            Assert.Equal(new[] { 429 }, catalog.Errors!["rate_limit_exceeded"].HttpStatuses);
            Assert.Equal("wait_then_retry", catalog.Errors["rate_limit_exceeded"].Action);
            Assert.Equal("https://api.genderapi.io/api/v2", stub.Requests[0].Uri.ToString());
            Assert.Equal("https://api.genderapi.io/api/v2/errors", stub.Requests[1].Uri.ToString());
            Assert.All(stub.Requests, r => Assert.Null(r.Authorization));
        }
    }

    public class RequestTests
    {
        [Fact]
        public async Task Headers_use_bearer_and_key_never_appears_in_url()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, Fixtures.Example("/api/v2/gender", "post", "200", "dataset"));
            using var client = ClientFactory.Create(stub);

            await client.NameAsync("Onur");

            RecordedRequest r = stub.Requests[0];
            Assert.Equal(HttpMethod.Post, r.Method);
            Assert.Equal("https://api.genderapi.io/api/v2/gender", r.Uri.ToString());
            Assert.Equal("Bearer " + ClientFactory.TestKey, r.Authorization);
            Assert.DoesNotContain(ClientFactory.TestKey, r.Uri.ToString());
            Assert.Empty(r.Uri.Query);
            Assert.DoesNotContain(ClientFactory.TestKey, r.Body);
            Assert.Equal("application/json", r.ContentType);
            Assert.Contains("application/json", r.Accept);
            Assert.StartsWith("genderapi-dotnet/2.0.0", r.UserAgent);
        }

        [Fact]
        public async Task Wire_fields_are_exact()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, Fixtures.Example("/api/v2/gender", "post", "200", "alias"));
            using var client = ClientFactory.Create(stub);

            await client.GenderAsync(GenderInputType.Username, "prenses", country: "TR", aiMode: AiMode.Fallback, forceToGenderize: true, id: "u-1");

            Assert.Equal(
                "{\"type\":\"username\",\"value\":\"prenses\",\"country\":\"TR\",\"id\":\"u-1\",\"forceToGenderize\":true,\"options\":{\"ai_mode\":\"fallback\"}}",
                stub.Requests[0].Body);
        }

        [Fact]
        public async Task Optional_fields_are_omitted_and_non_ascii_stays_utf8()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, Fixtures.Example("/api/v2/gender", "post", "200", "dataset"));
            using var client = ClientFactory.Create(stub);

            await client.EmailAsync("müge@example.com");

            Assert.Equal("{\"type\":\"email\",\"value\":\"müge@example.com\"}", stub.Requests[0].Body);
        }

        [Fact]
        public async Task Batch_body_wraps_items()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, Fixtures.Example("/api/v2/gender/batch", "post", "200", "batch"));
            using var client = ClientFactory.Create(stub);

            await client.GenderBatchAsync(new[]
            {
                GenderRequest.ForName("Onur", "TR", AiMode.Off, id: "a"),
                GenderRequest.ForEmail("x@example.com", id: "b"),
            });

            Assert.Equal("https://api.genderapi.io/api/v2/gender/batch", stub.Requests[0].Uri.ToString());
            Assert.Equal(
                "{\"items\":[{\"type\":\"name\",\"value\":\"Onur\",\"country\":\"TR\",\"id\":\"a\",\"options\":{\"ai_mode\":\"off\"}},{\"type\":\"email\",\"value\":\"x@example.com\",\"id\":\"b\"}]}",
                stub.Requests[0].Body);
        }

        [Fact]
        public async Task Without_a_key_no_authorization_header_is_sent()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, Fixtures.Example("/api/v2/gender", "post", "200", "dataset"));
            using var client = ClientFactory.Create(stub, apiKey: null);

            Assert.False(client.HasApiKey);
            GenderResponse res = await client.NameAsync("Onur");

            Assert.Null(stub.Requests[0].Authorization);
            Assert.Equal("ip_trial", res.Meta!.Access!.Mode);
        }

        [Fact]
        public void Constructing_the_client_makes_no_request()
        {
            var stub = new StubHandler();
            using (ClientFactory.Create(stub))
            {
            }

            using (new GenderApiClient("0123456789abcdef01234567"))
            {
            }

            Assert.Equal(0, stub.CallCount);
        }

        [Fact]
        public void Injected_http_client_is_not_disposed()
        {
            var stub = new StubHandler();
            var http = new HttpClient(stub);
            var client = new GenderApiClient(new GenderApiClientOptions { HttpClient = http });
            client.Dispose();

            Assert.Equal(TimeSpan.FromSeconds(100), http.Timeout); // still usable, untouched
            http.Dispose();
        }

        [Fact]
        public async Task Injected_http_client_default_headers_are_not_modified()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, Fixtures.Example("/api/v2/gender", "post", "200", "dataset"));
            var http = new HttpClient(stub);
            using var client = new GenderApiClient(new GenderApiClientOptions { ApiKey = ClientFactory.TestKey, HttpClient = http });

            await client.NameAsync("Onur");

            Assert.Null(http.DefaultRequestHeaders.Authorization);
            Assert.Empty(http.DefaultRequestHeaders.UserAgent);
        }
    }

    public class BaseUrlTests
    {
        [Theory]
        [InlineData("http://api.genderapi.io/api/v2")]
        [InlineData("http://example.com")]
        [InlineData("ftp://127.0.0.1/api")]
        [InlineData("not a url")]
        [InlineData("https://user:pass@api.genderapi.io/api/v2")]
        [InlineData("https://api.genderapi.io/api/v2?key=abc")]
        public void Insecure_or_invalid_base_urls_are_rejected(string baseUrl)
        {
            Assert.Throws<GenderApiValidationException>(() => new GenderApiClient(new GenderApiClientOptions { BaseUrl = baseUrl }));
        }

        [Theory]
        [InlineData("http://127.0.0.1:8080/api/v2", "http://127.0.0.1:8080/api/v2/gender")]
        [InlineData("http://localhost:3000/api/v2/", "http://localhost:3000/api/v2/gender")]
        [InlineData("http://[::1]:3000/api/v2", "http://[::1]:3000/api/v2/gender")]
        [InlineData("https://staging.example.test/api/v2/", "https://staging.example.test/api/v2/gender")]
        [InlineData("http://localhost:3000", "http://localhost:3000/gender")]
        public async Task Https_and_local_http_base_urls_are_accepted(string baseUrl, string expected)
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, Fixtures.Example("/api/v2/gender", "post", "200", "dataset"));
            using var client = ClientFactory.Create(stub, baseUrl: baseUrl);

            await client.NameAsync("Onur");

            Assert.Equal(expected, stub.Requests[0].Uri.ToString());
        }

        [Fact]
        public void Default_base_url_is_production_https()
        {
            using var client = new GenderApiClient(new GenderApiClientOptions { ApiKey = "" });
            Assert.Equal("https://api.genderapi.io/api/v2", client.BaseUrl.ToString());
        }
    }

    [Collection("Environment")]
    public class EnvironmentKeyTests
    {
        [Fact]
        public async Task Key_is_read_from_environment_when_not_given()
        {
            string? previous = Environment.GetEnvironmentVariable("GENDERAPI_API_KEY");
            Environment.SetEnvironmentVariable("GENDERAPI_API_KEY", "fedcba9876543210fedcba98");
            try
            {
                var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, Fixtures.Example("/api/v2/gender", "post", "200", "dataset"));
                using var client = new GenderApiClient(new GenderApiClientOptions { HttpClient = new HttpClient(stub) });

                await client.NameAsync("Onur");

                Assert.Equal("Bearer fedcba9876543210fedcba98", stub.Requests[0].Authorization);
            }
            finally
            {
                Environment.SetEnvironmentVariable("GENDERAPI_API_KEY", previous);
            }
        }

        [Fact]
        public void Key_with_control_characters_is_rejected_without_echoing_it()
        {
            var ex = Assert.Throws<GenderApiValidationException>(() => new GenderApiClient("abc\r\nX-Injected: 1"));
            Assert.DoesNotContain("abc", ex.Message);
        }
    }
}
