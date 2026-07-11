using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ObsidianDocsMcp.Services;

public class OllamaEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaEmbeddingService> _logger;
    private readonly string _modelName;
    private readonly string _ollamaUrl;

    public OllamaEmbeddingService(HttpClient httpClient, IConfiguration configuration, ILogger<OllamaEmbeddingService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _ollamaUrl = configuration["Ollama:Url"] ?? "http://localhost:11434";
        _modelName = configuration["Ollama:Model"] ?? "nomic-embed-text";
    }

    public async Task<float[]> GetEmbeddingAsync(string text)
    {
        try
        {
            var requestUrl = $"{_ollamaUrl.TrimEnd('/')}/api/embeddings";
            var requestBody = new OllamaEmbeddingRequest
            {
                Model = _modelName,
                Prompt = text
            };

            var response = await _httpClient.PostAsJsonAsync(requestUrl, requestBody);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>();
            if (result?.Embedding == null || result.Embedding.Length == 0)
            {
                _logger.LogWarning("Ollama returned an empty embedding.");
                return Array.Empty<float>();
            }

            return result.Embedding;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating embedding from Ollama for model {Model} at {Url}", _modelName, _ollamaUrl);
            throw;
        }
    }

    private class OllamaEmbeddingRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;
    }

    private class OllamaEmbeddingResponse
    {
        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = Array.Empty<float>();
    }
}
