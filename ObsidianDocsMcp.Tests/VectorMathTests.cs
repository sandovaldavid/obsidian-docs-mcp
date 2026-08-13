using ObsidianDocsMcp.Services;
using Xunit;

namespace ObsidianDocsMcp.Tests;

public class VectorMathTests
{
    [Fact]
    public void IdenticalVectorsHaveSimilarityOne()
    {
        var v = new float[] { 0.5f, -1.2f, 3.3f };

        Assert.Equal(1.0, VectorMath.CosineSimilarity(v, v), 6);
    }

    [Fact]
    public void OrthogonalVectorsHaveSimilarityZero()
    {
        Assert.Equal(0.0, VectorMath.CosineSimilarity([1f, 0f], [0f, 1f]), 6);
    }

    [Fact]
    public void OppositeVectorsHaveSimilarityMinusOne()
    {
        Assert.Equal(-1.0, VectorMath.CosineSimilarity([1f, 2f], [-1f, -2f]), 6);
    }

    [Fact]
    public void MismatchedLengthsReturnZero()
    {
        Assert.Equal(0.0, VectorMath.CosineSimilarity([1f, 2f], [1f, 2f, 3f]));
    }

    [Fact]
    public void ZeroVectorReturnsZero()
    {
        Assert.Equal(0.0, VectorMath.CosineSimilarity([0f, 0f], [1f, 2f]));
    }
}
