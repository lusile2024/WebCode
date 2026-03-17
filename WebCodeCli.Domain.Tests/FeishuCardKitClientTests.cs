using System.Net;
using System.Text.Json;
using FeishuNetSdk.Im.Dtos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebCodeCli.Domain.Common.Options;
using WebCodeCli.Domain.Domain.Service.Channels;

namespace WebCodeCli.Domain.Tests;

public class FeishuCardKitClientTests
{
    [Fact]
    public async Task ReplyRawCardAsync_Throws_WhenReplyReturnsBusinessError()
    {
        var handler = new StubHttpMessageHandler(
        [
            CreateJsonResponse("""{"tenant_access_token":"token-123","expire":7200}"""),
            CreateJsonResponse("""{"code":0,"data":{"card_id":"card_123"}}"""),
            CreateJsonResponse("""{"code":10002,"msg":"invalid card payload"}""")
        ]);

        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ReplyRawCardAsync(
                "om_reply",
                """{"schema":"2.0","body":{"elements":[]}}""",
                TestContext.Current.CancellationToken));

        Assert.Contains("Reply interactive card message failed", exception.Message);
        Assert.Equal(
        [
            "/open-apis/auth/v3/tenant_access_token/internal",
            "/open-apis/cardkit/v1/cards",
            "/open-apis/im/v1/messages/om_reply/reply"
        ], handler.RequestPaths);
    }

    [Fact]
    public async Task ReplyElementsCardAsync_CreatesCardThenRepliesWithCardId()
    {
        var handler = new StubHttpMessageHandler(
        [
            CreateJsonResponse("""{"tenant_access_token":"token-123","expire":7200}"""),
            CreateJsonResponse("""{"code":0,"data":{"card_id":"card_123"}}"""),
            CreateJsonResponse("""{"code":0,"data":{"message_id":"om_reply_success"}}""")
        ]);

        var client = CreateClient(handler);
        var card = new ElementsCardV2Dto
        {
            Header = new ElementsCardV2Dto.HeaderSuffix
            {
                Template = "blue",
                Title = new HeaderTitleElement { Content = "Help card" }
            },
            Body = new ElementsCardV2Dto.BodySuffix
            {
                Elements =
                [
                    new
                    {
                        tag = "div",
                        text = new { tag = "plain_text", content = "hello" }
                    }
                ]
            }
        };

        var messageId = await client.ReplyElementsCardAsync("om_reply", card, TestContext.Current.CancellationToken);

        Assert.Equal("om_reply_success", messageId);
        Assert.Equal(
        [
            "/open-apis/auth/v3/tenant_access_token/internal",
            "/open-apis/cardkit/v1/cards",
            "/open-apis/im/v1/messages/om_reply/reply"
        ], handler.RequestPaths);

        using var createDoc = JsonDocument.Parse(handler.RequestBodies[1]);
        Assert.Equal("card_json", createDoc.RootElement.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.String, createDoc.RootElement.GetProperty("data").ValueKind);

        using var cardDoc = JsonDocument.Parse(createDoc.RootElement.GetProperty("data").GetString()!);
        Assert.Equal("2.0", cardDoc.RootElement.GetProperty("schema").GetString());
        Assert.Equal("blue", cardDoc.RootElement.GetProperty("header").GetProperty("template").GetString());
        Assert.Equal("Help card", cardDoc.RootElement.GetProperty("header").GetProperty("title").GetProperty("content").GetString());
        Assert.Equal("div", cardDoc.RootElement.GetProperty("body").GetProperty("elements")[0].GetProperty("tag").GetString());

        using var requestDoc = JsonDocument.Parse(handler.RequestBodies[2]);
        Assert.Equal("interactive", requestDoc.RootElement.GetProperty("msg_type").GetString());
        Assert.Equal(JsonValueKind.String, requestDoc.RootElement.GetProperty("content").ValueKind);

        using var replyDoc = JsonDocument.Parse(requestDoc.RootElement.GetProperty("content").GetString()!);
        Assert.Equal("card", replyDoc.RootElement.GetProperty("type").GetString());
        Assert.Equal("card_123", replyDoc.RootElement.GetProperty("data").GetProperty("card_id").GetString());
    }

    private static FeishuCardKitClient CreateClient(StubHttpMessageHandler handler)
    {
        var options = Options.Create(new FeishuOptions
        {
            AppId = "app-id",
            AppSecret = "app-secret",
            HttpTimeoutSeconds = 30
        });

        return new FeishuCardKitClient(
            options,
            NullLogger<FeishuCardKitClient>.Instance,
            new StubHttpClientFactory(new HttpClient(handler)));
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
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHttpMessageHandler(IEnumerable<HttpResponseMessage> responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<string> RequestPaths { get; } = [];
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestPaths.Add(request.RequestUri!.AbsolutePath);
            RequestBodies.Add(request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));

            if (_responses.Count == 0)
            {
                throw new Xunit.Sdk.XunitException($"Unexpected request sent to {request.RequestUri}.");
            }

            return _responses.Dequeue();
        }
    }
}
