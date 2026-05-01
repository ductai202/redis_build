using Hyperion.DataStructures;
using Xunit;

namespace Hyperion.DataStructures.Tests;

public class ZSetTests
{
    [Fact]
    public void Add_ShouldAddAndReturnCorrectCount()
    {
        var zs = new ZSet();
        Assert.Equal(1, zs.Add(10.0, "a"));
        Assert.Equal(1, zs.Add(20.0, "b"));
        Assert.Equal(0, zs.Add(20.0, "b"));
        Assert.Equal(0, zs.Add(25.0, "b"));
        Assert.Equal(2, zs.Len());
    }

    [Fact]
    public void Rem_ShouldRemoveElement()
    {
        var zs = new ZSet();
        zs.Add(10.0, "a");
        zs.Add(20.0, "b");
        Assert.Equal(1, zs.Rem("a"));
        Assert.Equal(0, zs.Rem("a"));
        Assert.Equal(1, zs.Len());
    }

    [Fact]
    public void GetRank_ShouldReturnCorrectRank()
    {
        var zs = new ZSet();
        zs.Add(10.0, "a");
        zs.Add(30.0, "c");
        zs.Add(20.0, "b");

        var (rankA, _) = zs.GetRank("a", false);
        Assert.Equal(0, rankA);

        var (rankC, _) = zs.GetRank("c", false);
        Assert.Equal(2, rankC);

        var (revRankA, _) = zs.GetRank("a", true);
        Assert.Equal(2, revRankA);
    }

    [Fact]
    public void GetScore_ShouldReturnScore()
    {
        var zs = new ZSet();
        zs.Add(15.5, "test");

        var (exist, score) = zs.GetScore("test");
        Assert.True(exist);
        Assert.Equal(15.5, score);

        var (exist2, _) = zs.GetScore("notfound");
        Assert.False(exist2);
    }

    [Fact]
    public void GetRange_ShouldReturnCorrectElements()
    {
        var zs = new ZSet();
        zs.Add(10.0, "a");
        zs.Add(20.0, "b");
        zs.Add(30.0, "c");
        zs.Add(40.0, "d");

        var range = zs.GetRange(1, 2);
        Assert.Equal(2, range.Count);
        Assert.Equal("b", range[0]);
        Assert.Equal("c", range[1]);

        var revRange = zs.GetRange(1, 2, true);
        Assert.Equal("c", revRange[0]);
        Assert.Equal("b", revRange[1]);
    }
}
