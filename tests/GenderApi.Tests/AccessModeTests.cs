using System.Net;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace GenderApi.Tests
{
    public class AccessModeTests
    {
        private const string Gender = "/api/v2/gender";

        [Fact]
        public async Task Key_with_ip_trial_response_raises_access_mode_exception_with_full_result()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, Fixtures.Example(Gender, "post", "200", "dataset"));
            using var client = ClientFactory.Create(stub);

            var ex = await Assert.ThrowsAsync<GenderApiAccessModeException>(() => client.NameAsync("Onur", country: "TR"));

            Assert.IsAssignableFrom<GenderApiException>(ex);
            Assert.Equal("unexpected_access_mode", ex.Code);
            Assert.Equal("ip_trial", ex.AccessMode);
            Assert.Equal("api_key_missing", ex.AccessReason);
            Assert.Equal(200, ex.StatusCode);
            Assert.Equal("11111111-1111-4111-8111-111111111111", ex.RequestId);
            Assert.Equal(
                "Expected API-key access but the response reports access mode \"ip_trial\". Check your API key; this request may have consumed IP-trial credits.",
                ex.Message);
            Assert.DoesNotContain(ClientFactory.TestKey, ex.Message);

            GenderResponse result = Assert.IsType<GenderResponse>(ex.Result);
            Assert.Equal("male", result.Data!.Gender);
            Assert.Equal("ip_trial", result.Meta!.Access!.Mode);
            Assert.Equal(1, result.Meta.Usage!.ChargedCredits);
            Assert.Equal(200, result.StatusCode);
            Assert.Equal("11111111-1111-4111-8111-111111111111", result.RequestId);
            Assert.Equal("confirmed", ex.BillingStatus);
            Assert.Equal(result.RawJson, ex.RawBody);
            Assert.Equal(1, stub.CallCount);
        }

        [Fact]
        public async Task Without_a_key_ip_trial_response_is_returned()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, Fixtures.Example(Gender, "post", "200", "dataset"));
            using var client = ClientFactory.Create(stub, apiKey: null);

            GenderResponse res = await client.NameAsync("Onur");

            Assert.Equal("ip_trial", res.Meta!.Access!.Mode);
            Assert.Equal(1, stub.CallCount);
        }

        [Fact]
        public async Task Opt_out_returns_ip_trial_response()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, Fixtures.Example(Gender, "post", "200", "dataset"));
            using var client = ClientFactory.Create(stub, requireApiKeyAccess: false);

            GenderResponse res = await client.NameAsync("Onur");

            Assert.Equal("ip_trial", res.Meta!.Access!.Mode);
            Assert.NotNull(stub.Requests[0].Authorization);
            Assert.Equal(1, stub.CallCount);
        }

        [Fact]
        public async Task Missing_access_mode_is_not_an_error()
        {
            JsonNode node = JsonNode.Parse(Fixtures.Example(Gender, "post", "200", "dataset"))!;
            node["meta"]!.AsObject().Remove("access");
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, node.ToJsonString());
            using var client = ClientFactory.Create(stub);

            GenderResponse res = await client.NameAsync("Onur");

            Assert.Null(res.Meta!.Access);
        }

        [Fact]
        public async Task Batch_with_top_level_ip_trial_raises()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, Fixtures.Example("/api/v2/gender/batch", "post", "200", "batch"));
            using var client = ClientFactory.Create(stub);

            var ex = await Assert.ThrowsAsync<GenderApiAccessModeException>(() => client.GenderBatchAsync(new[]
            {
                GenderRequest.ForName("Onur", id: "a"),
                GenderRequest.ForEmail("x@example.com", id: "b"),
            }));

            Assert.Equal("ip_trial", ex.AccessMode);
            BatchResponse result = Assert.IsType<BatchResponse>(ex.Result);
            Assert.NotEmpty(result.Data!);
            Assert.Equal(1, stub.CallCount);
        }

        [Fact]
        public async Task Usage_and_phone_with_ip_trial_raise()
        {
            var stub = new StubHandler()
                .EnqueueJson(HttpStatusCode.OK, Fixtures.Example("/api/v2/usage", "get", "200", "usage"))
                .EnqueueJson(HttpStatusCode.OK, Fixtures.Example("/api/v2/phone/validate", "post", "200", "phone"));
            using var client = ClientFactory.Create(stub);

            var usage = await Assert.ThrowsAsync<GenderApiAccessModeException>(() => client.UsageAsync());
            Assert.IsType<UsageResponse>(usage.Result);
            var phone = await Assert.ThrowsAsync<GenderApiAccessModeException>(() => client.ValidatePhoneAsync("+90 (555) 000-0000", "TR"));
            Assert.IsType<PhoneValidationResponse>(phone.Result);
            Assert.Equal(2, stub.CallCount);
        }

        [Fact]
        public async Task Capabilities_with_a_key_is_never_checked()
        {
            var stub = new StubHandler().EnqueueJson(HttpStatusCode.OK, "{\"data\":{},\"meta\":{\"access\":{\"mode\":\"ip_trial\"}}}");
            using var client = ClientFactory.Create(stub);

            await client.CapabilitiesAsync();

            Assert.Equal(1, stub.CallCount);
        }
    }
}
