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

        // Should return exactly 5, or at least 5 (since it's a sketch, it might overestimate, but with width 200 it's likely exact for a single item test)
        Assert.True(cms.Count("apple") >= 5);
        Assert.True(cms.Count("apple") <= 6); // Just a sanity check to ensure it doesn't blow up

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
