namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// How the Shelf's columns page (round 2 PG1, owner 2026-11-14): the columns share the Tasks card's width evenly and are never narrower
/// than the minimum (300); when they do not fit, as many whole columns as fit are shown and the rest are paged, never scrolled sideways.
/// Up to <see cref="ShrinkUpTo"/> columns (the three bins) may shrink to the smallest width first, so they fit the smallest window
/// (901 × 567) as before; only when even those do not fit are they paged too. Pure, so the checks can call it.
/// </summary>
public static class TaskColumnPaging
{
    /// <summary>The most columns that may shrink below the minimum before paging starts (the three bins).</summary>
    public const int ShrinkUpTo = 3;

    /// <summary>More than this many pages show "2 / 9" in place of one dot each.</summary>
    public const int MostDots = 7;

    /// <summary>How many columns one page shows (at least 1); all of them when they fit.</summary>
    public static int ColumnsPerPage(double room, int count, double minWidth, double smallestWidth, double spacing, int shrinkUpTo = ShrinkUpTo)
    {
        if (count <= 1 || double.IsNaN(room) || double.IsInfinity(room))
        {
            return Math.Max(1, count);
        }

        if (Fits(room, count, minWidth, spacing) || (count <= shrinkUpTo && Fits(room, count, Math.Min(minWidth, smallestWidth), spacing)))
        {
            return count;
        }

        return Math.Clamp((int)Math.Floor((room + spacing) / (minWidth + spacing)), 1, count);
    }

    /// <summary>The width of each column when <paramref name="perPage"/> of them share <paramref name="room"/> (the last page keeps it too).</summary>
    public static double ColumnWidth(double room, int perPage, double spacing)
    {
        int slots = Math.Max(1, perPage);
        return Math.Max(0, Math.Floor((room - (slots - 1) * spacing) / slots));
    }

    public static int PageCount(int count, int perPage)
    {
        int size = Math.Max(1, perPage);
        return count <= 0 ? 1 : (count + size - 1) / size;
    }

    /// <summary>The page that holds column <paramref name="first"/> (the anchor kept across a resize).</summary>
    public static int PageOf(int first, int count, int perPage) => count <= 0 ? 0 : Math.Clamp(first, 0, count - 1) / Math.Max(1, perPage);

    /// <summary>The index of the first column on <paramref name="page"/>.</summary>
    public static int FirstOfPage(int page, int count, int perPage) => Math.Clamp(page, 0, PageCount(count, perPage) - 1) * Math.Max(1, perPage);

    public static bool UsesCount(int pageCount) => pageCount > MostDots;

    // ---- 0.1.154: Group by Labels with folded columns (rails). Pages differ in size: each takes up to the open columns that fit (at most
    // mostOpen) plus the rails that still fit beside them; a page's rails are drawn at its right end. ----

    /// <summary>
    /// How many columns each page holds, walking the columns in order: a column joins the page while the page's open columns stay within
    /// what fits (at most <paramref name="mostOpen"/>) and all of them, rails at <paramref name="railWidth"/>, fit the room; otherwise it
    /// starts the next page. A page always takes at least one column.
    /// </summary>
    public static IReadOnlyList<int> RailPages(IReadOnlyList<bool> isRail, double room, double minWidth, double railWidth, double spacing, int mostOpen)
    {
        var sizes = new List<int>();
        int opensFit = OpensFit(room, minWidth, spacing, mostOpen);
        int open = 0;
        int rails = 0;
        foreach (bool rail in isRail)
        {
            int nextOpen = open + (rail ? 0 : 1);
            int nextRails = rails + (rail ? 1 : 0);
            bool empty = open + rails == 0;
            bool fits = nextOpen <= opensFit
                && (!double.IsFinite(room) || nextOpen * minWidth + nextRails * railWidth + (nextOpen + nextRails - 1) * spacing <= room + 0.01);
            if (!empty && !fits)
            {
                sizes.Add(open + rails);
                open = rail ? 0 : 1;
                rails = rail ? 1 : 0;
                continue;
            }

            open = nextOpen;
            rails = nextRails;
        }

        if (open + rails > 0 || sizes.Count == 0)
        {
            sizes.Add(open + rails);
        }

        return sizes;
    }

    /// <summary>
    /// An open label column's width with no rail beside it: the room shared by as many open columns as a full page holds (fewer when there
    /// are fewer open columns in all), so a part-filled page keeps the full pages' width.
    /// </summary>
    public static double OpenSlotWidth(double room, int openCount, double minWidth, double spacing, int mostOpen)
    {
        if (!double.IsFinite(room))
        {
            return minWidth;
        }

        int perPage = Math.Clamp(Math.Min(OpensFit(room, minWidth, spacing, mostOpen), Math.Max(1, openCount)), 1, Math.Max(1, mostOpen));
        return ColumnWidth(room, perPage, spacing);
    }

    /// <summary>The page (0-based) holding column <paramref name="index"/> when the pages are <paramref name="sizes"/> long.</summary>
    public static int PageOfIndex(IReadOnlyList<int> sizes, int index)
    {
        int start = 0;
        for (int page = 0; page < sizes.Count; page++)
        {
            start += sizes[page];
            if (index < start)
            {
                return page;
            }
        }

        return Math.Max(0, sizes.Count - 1);
    }

    /// <summary>The index of the first column of <paramref name="page"/> when the pages are <paramref name="sizes"/> long.</summary>
    public static int StartOfPage(IReadOnlyList<int> sizes, int page)
    {
        int last = Math.Clamp(page, 0, Math.Max(0, sizes.Count - 1));
        int start = 0;
        for (int index = 0; index < last; index++)
        {
            start += sizes[index];
        }

        return start;
    }

    private static int OpensFit(double room, double minWidth, double spacing, int mostOpen)
    {
        int most = Math.Max(1, mostOpen);
        return double.IsFinite(room) ? Math.Clamp((int)Math.Floor((room + spacing) / (minWidth + spacing)), 1, most) : most;
    }

    private static bool Fits(double room, int count, double width, double spacing) => count * width + (count - 1) * spacing <= room + 0.01;
}
