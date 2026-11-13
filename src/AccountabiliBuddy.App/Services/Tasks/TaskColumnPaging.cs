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

    private static bool Fits(double room, int count, double width, double spacing) => count * width + (count - 1) * spacing <= room + 0.01;
}
