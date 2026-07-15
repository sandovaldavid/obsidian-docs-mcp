using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ObsidianDocsMcp.Services;
using Xunit;

namespace ObsidianDocsMcp.Tests;

public class OllamaEmbeddingServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        public StubHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private static OllamaEmbeddingService CreateService(StubHandler handler, string? url = null, string? model = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ollama:Url"] = url,
                ["Ollama:Model"] = model
            })
            .Build();
        return new OllamaEmbeddingService(new HttpClient(handler), configuration, NullLogger<OllamaEmbeddingService>.Instance);
    }

    [Fact]
    public async Task SendsModelAndPromptToTheEmbeddingsEndpoint()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"embedding":[0.1,0.2,0.3]}""");
        var service = CreateService(handler, url: "http://ollama.test:11434/", model: "test-model");

        var embedding = await service.GetEmbeddingAsync("hello world");

        Assert.Equal("http://ollama.test:11434/api/embeddings", handler.LastRequest!.RequestUri!.ToString());
        Assert.Contains("\"test-model\"", handler.LastRequestBody);
        Assert.Contains("hello world", handler.LastRequestBody);
        Assert.Equal([0.1f, 0.2f, 0.3f], embedding);
    }

    [Fact]
    public async Task EmptyEmbeddingResponseReturnsEmptyArray()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"embedding":[]}""");
        var service = CreateService(handler);

        var embedding = await service.GetEmbeddingAsync("text");

        Assert.Empty(embedding);
    }

    [Fact]
    public async Task HttpErrorThrows()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError, "boom");
        var service = CreateService(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetEmbeddingAsync("text"));
    }
}
