using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace GenderApi.Tests
{
    /// <summary>Records every request and answers from a queue. No network access.</summary>
    internal sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();

        public List<RecordedRequest> Requests { get; } = new();

        public int CallCount => Requests.Count;

        public StubHandler Enqueue(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responses.Enqueue(responder);
            return this;
        }

        public StubHandler EnqueueJson(HttpStatusCode status, string json, string contentType = "application/json", Action<HttpResponseMessage>? configure = null)
        {
            return Enqueue((req, ct) =>
            {
                var response = new HttpResponseMessage(status)
                {
                    Content = new StringContent(json, Encoding.UTF8, contentType),
                    RequestMessage = req,
                };
                configure?.Invoke(response);
                return Task.FromResult(response);
            });
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, request.Headers, request.Content?.Headers, body));
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("Unexpected extra HTTP request (the client must not retry).");
            }

            return await _responses.Dequeue()(request, cancellationToken);
        }
    }

    internal sealed class RecordedRequest
    {
        public RecordedRequest(HttpMethod method, Uri uri, System.Net.Http.Headers.HttpRequestHeaders headers, System.Net.Http.Headers.HttpContentHeaders? contentHeaders, string? body)
        {
            Method = method;
            Uri = uri;
            Authorization = headers.Authorization?.ToString();
            UserAgent = string.Join(" ", headers.TryGetValues("User-Agent", out var ua) ? ua : Array.Empty<string>());
            Accept = headers.Accept.ToString();
            ContentType = contentHeaders?.ContentType?.ToString();
            Body = body;
        }

        public HttpMethod Method { get; }

        public Uri Uri { get; }

        public string? Authorization { get; }

        public string UserAgent { get; }

        public string Accept { get; }

        public string? ContentType { get; }

        public string? Body { get; }
    }

    /// <summary>Example payloads from the published OpenAPI document (tests/…/Fixtures/openapi-v2.json).</summary>
    internal static class Fixtures
    {
        private static readonly Lazy<JsonNode> OpenApi = new(() =>
            JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openapi-v2.json")))!);

        public static string Example(string path, string method, string status, string name)
        {
            JsonNode? content = OpenApi.Value["paths"]![path]![method]!["responses"]![status]!["content"]!;
            foreach (var entry in content.AsObject())
            {
                JsonNode? value = entry.Value?["examples"]?[name]?["value"];
                if (value != null)
                {
                    return value.ToJsonString();
                }
            }

            throw new InvalidOperationException($"No example {name} for {method} {path} {status}");
        }

        public static string WithExtraFields(string json)
        {
            JsonNode node = JsonNode.Parse(json)!;
            node["future_top_level"] = "x";
            node["meta"]!["future_meta"] = 1;
            node["meta"]!["usage"]!["future_usage"] = true;
            JsonNode data = node["data"]!;
            if (data is JsonObject obj)
            {
                obj["future_data"] = new JsonObject { ["nested"] = 1 };
                obj["match"]!["future_match"] = "m";
                obj["input"]!["future_input"] = "i";
            }

            return node.ToJsonString();
        }
    }

    internal static class ClientFactory
    {
        public const string TestKey = "0123456789abcdef01234567";

        public static GenderApiClient Create(StubHandler handler, string? apiKey = TestKey, TimeSpan? timeout = null, string baseUrl = "https://api.genderapi.io/api/v2")
        {
            return new GenderApiClient(new GenderApiClientOptions
            {
                ApiKey = apiKey ?? string.Empty,
                BaseUrl = baseUrl,
                HttpClient = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan },
                Timeout = timeout ?? GenderApiClientOptions.DefaultTimeout,
            });
        }
    }
}
