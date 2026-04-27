namespace towerdefensegame.scripts.world;

/// <summary>
/// Disjoint-set with path-halving + union-by-rank. O(α(n)) per op.
/// Allocate once per rebuild; not designed for incremental edge removal.
/// </summary>
public sealed class UnionFind
{
    private readonly int[] _parent;
    private readonly byte[] _rank;

    public UnionFind(int n)
    {
        _parent = new int[n];
        _rank   = new byte[n];
        for (int i = 0; i < n; i++) _parent[i] = i;
    }

    public int Find(int x)
    {
        while (_parent[x] != x)
        {
            _parent[x] = _parent[_parent[x]];
            x = _parent[x];
        }
        return x;
    }

    public bool Union(int a, int b)
    {
        int ra = Find(a), rb = Find(b);
        if (ra == rb) return false;
        if (_rank[ra] < _rank[rb]) (ra, rb) = (rb, ra);
        _parent[rb] = ra;
        if (_rank[ra] == _rank[rb]) _rank[ra]++;
        return true;
    }
}
