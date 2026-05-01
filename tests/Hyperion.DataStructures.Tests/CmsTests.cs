using Hyperion.DataStructures;
using Xunit;

namespace Hyperion.DataStructures.Tests;

public class CmsTests
{
    [Fact]
    public void Cms_ShouldInitializeAndCountProperly()
    {
        var cms = new CMS(200, 5);

        Assert.Equal(200u, cms.Width);
        Assert.Equal(5u, cms.Depth);
        Assert.Equal(0u, cms.Count("apple"));

        cms.IncrBy("apple", 3);
        cms.IncrBy("apple", 2);

        Assert.True(cms.Count("apple") >= 5);
        Assert.True(cms.Count("apple") <= 6);

        cms.IncrBy("banana", 1);
        Assert.True(cms.Count("banana") >= 1);
    }

    [Fact]
    public void Cms_ShouldCalculateDimensions()
    {
        var (w, d) = CMS.CalcCMSDim(0.01, 0.01);

        Assert.Equal(200u, w);
        Assert.Equal(7u, d);
    }
}
