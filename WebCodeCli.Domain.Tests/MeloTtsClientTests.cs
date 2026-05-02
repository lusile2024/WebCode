using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Model.Channels;
using WebCodeCli.Domain.Domain.Service.Channels;

namespace WebCodeCli.Domain.Tests;

public sealed class MeloTtsClientTests
{
    [Fact]
    public async Task GetHealthAsync_ParsesLocalServiceResponse()
    {
        var handler = new StubHttpMessageHandler(
        [
            CreateJsonResponse("""{"status":"ok","device":"cuda","defaultVoiceId":"zh_female_default"}""")
        ]);

        var client = CreateClient(handler);

        var result = await client.GetHealthAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsAvailable);
        Assert.Equal("ok", result.ServiceStatus);
        Assert.Equal("cuda", result.Device);
        Assert.Equal("zh_female_default", result.DefaultVoiceId);
        Assert.Equal("/health", Assert.Single(handler.RequestPaths));
    }

    [Fact]
    public async Task GetVoicesAsync_ParsesVoiceList()
    {
        var handler = new StubHttpMessageHandler(
        [
            CreateJsonResponse(
                """
                {
                  "voices": [
                    {
                      "voiceId": "zh_female_default",
                      "displayName": "Chinese Female Default",
                      "language": "zh",
                      "gender": "female"
                    },
                    {
                      "voiceId": "en_male_default",
                      "displayName": "English Male Default",
                      "language": "en",
                      "gender": "male"
                    }
                  ]
                }
                """)
        ]);

        var client = CreateClient(handler);

        var result = await client.GetVoicesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Collection(
            result,
            voice =>
            {
                Assert.Equal("zh_female_default", voice.VoiceId);
                Assert.Equal("Chinese Female Default", voice.DisplayName);
                Assert.Equal("zh", voice.Language);
                Assert.Equal("female", voice.Gender);
            },
            voice =>
            {
                Assert.Equal("en_male_default", voice.VoiceId);
                Assert.Equal("English Male Default", voice.DisplayName);
                Assert.Equal("en", voice.Language);
                Assert.Equal("male", voice.Gender);
            });
        Assert.Equal("/voices", Assert.Single(handler.RequestPaths));
    }

    [Fact]
    public async Task GetHealthAsync_UsesDedicatedHttpClientName()
    {
        var handler = new StubHttpMessageHandler(
        [
            CreateJsonResponse("""{"status":"ok"}""")
        ]);
        var factory = new StubHttpClientFactory(new HttpClient(handler));
        var client = CreateClient(handler, factory);

        await client.GetHealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal("MeloTtsClient", factory.CreatedClientName);
    }

    private static MeloTtsClient CreateClient(StubHttpMessageHandler handler, StubHttpClientFactory? factory = null)
    {
        return new MeloTtsClient(
            Options.Create(new FeishuReplyTtsOptions
            {
                TtsServiceBaseUrl = "http://127.0.0.1:5057",
                TtsServiceTimeoutSeconds = 15
            }),
            NullLogger<MeloTtsClient>.Instance,
            factory ?? new StubHttpClientFactory(new HttpClient(handler)));
    }

    private static HttpResponseMessage CreateJsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json)
        };
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public string? CreatedClientName { get; private set; }

        public HttpClient CreateClient(string name)
        {
            CreatedClientName = name;
            return client;
        }
    }

    private sealed class StubHttpMessageHandler(IEnumerable<HttpResponseMessage> responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<string> RequestPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestPaths.Add(request.RequestUri!.AbsolutePath);

            if (_responses.Count == 0)
            {
                throw new Xunit.Sdk.XunitException($"Unexpected request sent to {request.RequestUri}.");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }
}
