using ComplianceCopilot.Shared.Rag;

namespace ComplianceCopilot.Tests;

public class VectorMathTests
{
    [Fact]
    public void CosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        float[] a = [1f, 2f, 3f];
        float[] b = [1f, 2f, 3f];

        Assert.Equal(1.0, VectorMath.CosineSimilarity(a, b), precision: 6);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        float[] a = [1f, 0f];
        float[] b = [0f, 1f];

        Assert.Equal(0.0, VectorMath.CosineSimilarity(a, b), precision: 6);
    }

    [Fact]
    public void CosineSimilarity_OppositeVectors_ReturnsNegativeOne()
    {
        float[] a = [1f, 0f];
        float[] b = [-1f, 0f];

        Assert.Equal(-1.0, VectorMath.CosineSimilarity(a, b), precision: 6);
    }

    [Fact]
    public void CosineSimilarity_MismatchedLength_Throws()
    {
        float[] a = [1f, 2f];
        float[] b = [1f, 2f, 3f];

        Assert.Throws<ArgumentException>(() => VectorMath.CosineSimilarity(a, b));
    }

    [Fact]
    public void CosineSimilarity_ZeroVector_ReturnsZeroRatherThanNaN()
    {
        float[] a = [0f, 0f];
        float[] b = [1f, 2f];

        Assert.Equal(0.0, VectorMath.CosineSimilarity(a, b));
    }
}
