using Hyperion.DataStructures;
using Xunit;

namespace Hyperion.DataStructures.Tests;

public class SkiplistTests
{
    [Fact]
    public void Insert_ShouldAddElementsInOrder()
    {
        var sl = new Skiplist();
        sl.Insert(10.0, "a");
        sl.Insert(5.0, "b");
        sl.Insert(15.0, "c");
        sl.Insert(10.0, "d");
        Assert.Equal(4u, sl.Length);
        var node = sl.Head.Levels[0].Forward;
        Assert.Equal(5.0, node.Score);
        Assert.Equal("b", node.Ele);
        node = node.Levels[0].Forward;
        Assert.Equal(10.0, node.Score);
        Assert.Equal("a", node.Ele);
    }

    [Fact]
    public void GetRank_ShouldReturnCorrectRank()
    {
        var sl = new Skiplist();
        sl.Insert(10.0, "a");
        sl.Insert(5.0, "b");
        sl.Insert(15.0, "c");
        Assert.Equal(1u, sl.GetRank(5.0, "b"));
        Assert.Equal(2u, sl.GetRank(10.0, "a"));
        Assert.Equal(3u, sl.GetRank(15.0, "c"));
    }

    [Fact]
    public void UpdateScore_ShouldUpdateAndMaintainOrder()
    {
        var sl = new Skiplist();
        sl.Insert(10.0, "a");
        sl.Insert(5.0, "b");
        sl.Insert(15.0, "c");
        sl.UpdateScore(10.0, "a", 20.0);
        Assert.Equal(3u, sl.Length);
        Assert.Equal(3u, sl.GetRank(20.0, "a"));
    }

    [Fact]
    public void Delete_ShouldRemoveNode()
    {
        var sl = new Skiplist();
        sl.Insert(10.0, "a");
        sl.Insert(5.0, "b");
        Assert.Equal(1, sl.Delete(10.0, "a"));
        Assert.Equal(1u, sl.Length);
        Assert.Equal(0u, sl.GetRank(10.0, "a"));
    }
}
