namespace Hyperion.DataStructures;

public class SkiplistLevel
{
    public SkiplistNode? Forward { get; set; }
    public uint Span { get; set; }
}

public class SkiplistNode
{
    public string Ele { get; set; }
    public double Score { get; set; }
    public SkiplistNode? Backward { get; set; }
    public SkiplistLevel[] Levels { get; set; }

    public SkiplistNode(int level, double score, string ele)
    {
        Score = score;
        Ele = ele;
        Levels = new SkiplistLevel[level];
        for (int i = 0; i < level; i++) Levels[i] = new SkiplistLevel();
    }
}

public class Skiplist
{
    public const int MaxLevel = 32;
    private readonly Random _random = new();
    public SkiplistNode Head { get; private set; }
    public SkiplistNode? Tail { get; private set; }
    public uint Length { get; private set; }
    public int Level { get; private set; }

    public Skiplist()
    {
        Level = 1;
        Length = 0;
        Head = new SkiplistNode(MaxLevel, 0, "");
    }

    private int RandomLevel()
    {
        int level = 1;
        while (_random.Next(2) == 1) level++;
        return Math.Min(level, MaxLevel);
    }

    public SkiplistNode Insert(double score, string ele)
    {
        var update = new SkiplistNode[MaxLevel];
        var rank = new uint[MaxLevel];
        var x = Head;
        for (int i = Level - 1; i >= 0; i--)
        {
            rank[i] = i == Level - 1 ? 0 : rank[i + 1];
            while (x.Levels[i].Forward != null && (x.Levels[i].Forward.Score < score || (x.Levels[i].Forward.Score == score && string.CompareOrdinal(x.Levels[i].Forward.Ele, ele) < 0)))
            {
                rank[i] += x.Levels[i].Span;
                x = x.Levels[i].Forward;
            }
            update[i] = x;
        }
        int level = RandomLevel();
        if (level > Level)
        {
            for (int i = Level; i < level; i++)
            {
                rank[i] = 0;
                update[i] = Head;
                update[i].Levels[i].Span = Length;
            }
            Level = level;
        }
        x = new SkiplistNode(level, score, ele);
        for (int i = 0; i < level; i++)
        {
            x.Levels[i].Forward = update[i].Levels[i].Forward;
            update[i].Levels[i].Forward = x;
            x.Levels[i].Span = update[i].Levels[i].Span - (rank[0] - rank[i]);
            update[i].Levels[i].Span = (rank[0] - rank[i]) + 1;
        }
        for (int i = level; i < Level; i++) update[i].Levels[i].Span++;
        x.Backward = update[0] == Head ? null : update[0];
        if (x.Levels[0].Forward != null) x.Levels[0].Forward.Backward = x;
        else Tail = x;
        Length++;
        return x;
    }

    public uint GetRank(double score, string ele)
    {
        var x = Head;
        uint rank = 0;
        for (int i = Level - 1; i >= 0; i--)
        {
            while (x.Levels[i].Forward != null && (x.Levels[i].Forward.Score < score || (x.Levels[i].Forward.Score == score && string.CompareOrdinal(x.Levels[i].Forward.Ele, ele) <= 0)))
            {
                rank += x.Levels[i].Span;
                x = x.Levels[i].Forward;
            }
            if (x.Score == score && x.Ele == ele) return rank;
        }
        return 0;
    }

    public SkiplistNode? UpdateScore(double curScore, string ele, double newScore)
    {
        var update = new SkiplistNode[MaxLevel];
        var x = Head;
        for (int i = Level - 1; i >= 0; i--)
        {
            while (x.Levels[i].Forward != null && (x.Levels[i].Forward.Score < curScore || (x.Levels[i].Forward.Score == curScore && string.CompareOrdinal(x.Levels[i].Forward.Ele, ele) < 0))) x = x.Levels[i].Forward;
            update[i] = x;
        }
        x = x.Levels[0].Forward;
        if (x == null || x.Score != curScore || x.Ele != ele) return null;
        if ((x.Backward == null || x.Backward.Score < newScore) && (x.Levels[0].Forward == null || x.Levels[0].Forward.Score > newScore))
        {
            x.Score = newScore;
            return x;
        }
        DeleteNode(x, update);
        return Insert(newScore, ele);
    }

    private void DeleteNode(SkiplistNode x, SkiplistNode[] update)
    {
        for (int i = 0; i < Level; i++)
        {
            if (update[i].Levels[i].Forward == x)
            {
                update[i].Levels[i].Span += x.Levels[i].Span - 1;
                update[i].Levels[i].Forward = x.Levels[i].Forward;
            }
            else update[i].Levels[i].Span--;
        }
        if (x.Levels[0].Forward != null) x.Levels[0].Forward.Backward = x.Backward;
        else Tail = x.Backward;
        while (Level > 1 && Head.Levels[Level - 1].Forward == null) Level--;
        Length--;
    }

    public int Delete(double score, string ele)
    {
        var update = new SkiplistNode[MaxLevel];
        var x = Head;
        for (int i = Level - 1; i >= 0; i--)
        {
            while (x.Levels[i].Forward != null && (x.Levels[i].Forward.Score < score || (x.Levels[i].Forward.Score == score && string.CompareOrdinal(x.Levels[i].Forward.Ele, ele) < 0))) x = x.Levels[i].Forward;
            update[i] = x;
        }
        x = x.Levels[0].Forward;
        if (x != null && x.Score == score && x.Ele == ele)
        {
            DeleteNode(x, update);
            return 1;
        }
        return 0;
    }
}
