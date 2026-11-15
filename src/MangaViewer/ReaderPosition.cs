namespace MangaViewer;

public sealed class ReaderPosition
{
    private readonly HashSet<int> landscapePages = [];
    public int Count { get; private set; }
    public int Index { get; private set; }
    public bool Facing { get; set; } = true;
    public bool LeftAdvances { get; set; } = true;
    public int VisibleCount => VisibleCountAt(Index);

    public void Open(int count)
    {
        Count = count;
        Index = 0;
        landscapePages.Clear();
    }

    public void SetLandscape(int index, bool landscape)
    {
        if (landscape) landscapePages.Add(index);
        else landscapePages.Remove(index);
    }

    private int VisibleCountAt(int index) => Math.Min(Facing && index > 0
        && !landscapePages.Contains(index) && !landscapePages.Contains(index + 1) ? 2 : 1, Count - index);

    public void Seek(int index) => Index = Math.Clamp(index, 0, Math.Max(0, Count - 1));

    public void Advance(bool next)
    {
        if (next && Index + VisibleCount >= Count)
            return;
        if (next)
        {
            Seek(Index + VisibleCount);
            return;
        }
        if (!Facing)
        {
            Seek(Index - 1);
            return;
        }
        int previous = 0;
        for (int start = 1; start < Index; start += VisibleCountAt(start)) previous = start;
        Seek(previous);
    }

    public void MoveHorizontal(bool left) => Advance(left == LeftAdvances);
}
