using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenderApi.Tests
{
    /// <summary>
    /// Exercises the client-owned HttpClient (default handler) against a local HTTP server on 127.0.0.1.
    /// </summary>
    public class LocalServerTests
    {
        private sealed class LocalServer : IDisposable
        {
            private readonly HttpListener _listener = new();
            private readonly Func<HttpListenerContext, Task> _handle;
            private int _hits;

            public LocalServer(Func<HttpListenerContext, Task> handle)
            {
                _handle = handle;
                int port = FreePort();
                BaseUrl = "http://127.0.0.1:" + port + "/api/v2";
                _listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
                _listener.Start();
                _ = Loop();
            }

            public string BaseUrl { get; }

            public int Hits => Volatile.Read(ref _hits);

            public string? LastAuthorization { get; private set; }

            public string? LastPath { get; private set; }

            public void Dispose()
            {
                try
                {
                    _listener.Stop();
                    _listener.Close();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            private async Task Loop()
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try
                    {
                        ctx = await _listener.GetContextAsync();
                    }
                    catch (Exception)
                    {
                        return;
                    }

                    Interlocked.Increment(ref _hits);
                    LastAuthorization = ctx.Request.Headers["Authorization"];
                    LastPath = ctx.Request.Url!.AbsolutePath;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _handle(ctx);
                        }
                        catch (Exception)
                        {
                        }
                    });
                }
            }

            private static int FreePort()
            {
                var l = new TcpListener(IPAddress.Loopback, 0);
                l.Start();
                int port = ((IPEndPoint)l.LocalEndpoint).Port;
                l.Stop();
                return port;
            }
        }

        private static async Task WriteJson(HttpListenerContext ctx, int status, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }

        [Fact]
        public async Task Default_handler_returns_parsed_response()
        {
            using var server = new LocalServer(ctx => WriteJson(ctx, 200, Fixtures.Example("/api/v2/gender", "post", "200", "dataset")));
            using var client = new GenderApiClient(new GenderApiClientOptions { ApiKey = ClientFactory.TestKey, BaseUrl = server.BaseUrl });

            GenderResponse res = await client.NameAsync("Onur");

            Assert.Equal("male", res.Data!.Gender);
            Assert.Equal("/api/v2/gender", server.LastPath);
            Assert.Equal("Bearer " + ClientFactory.TestKey, server.LastAuthorization);
        }

        [Fact]
        public async Task Default_handler_does_not_follow_redirects()
        {
            using var server = new LocalServer(ctx =>
            {
                if (ctx.Request.Url!.AbsolutePath == "/api/v2/gender")
                {
                    ctx.Response.StatusCode = 307;
                    ctx.Response.RedirectLocation = "/elsewhere";
                    ctx.Response.Close();
                    return Task.CompletedTask;
                }

                return WriteJson(ctx, 200, Fixtures.Example("/api/v2/gender", "post", "200", "dataset"));
            });
            using var client = new GenderApiClient(new GenderApiClientOptions { ApiKey = ClientFactory.TestKey, BaseUrl = server.BaseUrl });

            var ex = await Assert.ThrowsAsync<GenderApiRedirectException>(() => client.NameAsync("Onur"));

            Assert.Equal(307, ex.StatusCode);
            Assert.Equal(1, server.Hits);
            Assert.Equal("/api/v2/gender", server.LastPath);
        }

        [Fact]
        public async Task Default_handler_times_out_once()
        {
            using var server = new LocalServer(async ctx =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                await WriteJson(ctx, 200, "{}");
            });
            using var client = new GenderApiClient(new GenderApiClientOptions
            {
                ApiKey = ClientFactory.TestKey,
                BaseUrl = server.BaseUrl,
                Timeout = TimeSpan.FromMilliseconds(200),
            });

            await Assert.ThrowsAsync<GenderApiTimeoutException>(() => client.NameAsync("Onur"));
            await Task.Delay(300);
            Assert.Equal(1, server.Hits);
        }

        [Fact]
        public async Task Default_handler_surfaces_429_without_retry()
        {
            using var server = new LocalServer(ctx =>
            {
                ctx.Response.Headers["Retry-After"] = "3";
                return WriteJson(ctx, 429, "{\"type\":\"urn:genderapi:problem:rate_limit_exceeded\",\"title\":\"rate limit exceeded\",\"status\":429,\"detail\":\"d\",\"instance\":\"i\",\"code\":\"rate_limit_exceeded\",\"request_id\":\"r-1\",\"action\":\"wait_then_retry\"}");
            });
            using var client = new GenderApiClient(new GenderApiClientOptions { ApiKey = ClientFactory.TestKey, BaseUrl = server.BaseUrl });

            var ex = await Assert.ThrowsAsync<GenderApiException>(() => client.NameAsync("Onur"));

            Assert.Equal(429, ex.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(3), ex.RetryAfter);
            Assert.Equal("r-1", ex.RequestId);
            Assert.Equal(1, server.Hits);
        }
    }
}
