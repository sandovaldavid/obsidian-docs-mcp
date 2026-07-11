using System.Threading.Tasks;

namespace ObsidianDocsMcp.Services;

public interface IEmbeddingService
{
    Task<float[]> GetEmbeddingAsync(string text);
}
