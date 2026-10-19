using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Adds the URL, tab id and channel of a browser window's active tab to its snapshot.
/// The extension cannot see native window handles, so tabs are paired with windows by title:
/// browsers title their window "&lt;active tab title&gt; - &lt;browser name&gt;", so the window title starts with
/// the tab title. The longest matching tab title wins; a tab from the same browser wins a tie.
/// Two windows of one browser showing tabs with identical titles cannot be told apart.
/// A main window of a watched browser that matches no reported tab is marked <see cref="ActivitySnapshot.IsUnseenBrowserWindow"/>.
/// Main windows of a browser in <c>withoutPrivateAccess</c> are marked <see cref="ActivitySnapshot.LacksPrivateWindowAccess"/>,
/// except the browser's own pages (extensions page, settings), so the user can still turn the setting on.
/// </summary>
public static class BrowserTabMatcher
{
    private static readonly Dictionary<string, BrowserKind> BrowserByProcessName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = BrowserKind.Chrome,
        ["brave"] = BrowserKind.Brave,
        ["msedge"] = BrowserKind.Edge,
        ["firefox"] = BrowserKind.Firefox,
        ["chromium"] = BrowserKind.Chromium,
        ["opera"] = BrowserKind.Chromium,
        ["vivaldi"] = BrowserKind.Chromium,
    };

    // How browsers end the title of a normal or private main window ("<tab title> - Google Chrome").
    // Chrome's incognito title is the same as a normal one; Firefox ends private windows with "Private Browsing".
    // Other windows (DevTools, picture-in-picture, installed web apps) don't end like this and are left alone.
    // Browser-internal pages, by address or by the title the browser gives them.
    private static readonly string[] InternalPageSchemes = { "chrome://", "edge://", "brave://", "about:", "opera://", "vivaldi://" };
    private static readonly string[] InternalPageTitles = { "Extensions", "Add-ons Manager", "Settings", "Manage Extensions" };

    private static readonly string[] MainWindowTitleEndings =
    {
        "Google Chrome", "Microsoft Edge", "Brave", "Mozilla Firefox", "Private Browsing", "Chromium", "Opera", "Vivaldi",
    };

    public static bool IsBrowserProcess(string processName)
    {
        return BrowserByProcessName.ContainsKey(processName);
    }

    /// <param name="watchedBrowsers">Browsers whose windows must be visible to the extension
    /// (their extension has connected since they were granted in Setup).</param>
    /// <param name="withoutPrivateAccess">Watched browsers whose extension isn't allowed in private windows.</param>
    public static ActivitySnapshot Enrich(
        ActivitySnapshot snapshot,
        IReadOnlyList<BrowserConnection> connections,
        IReadOnlySet<BrowserKind> watchedBrowsers,
        IReadOnlySet<BrowserKind>? withoutPrivateAccess = null)
    {
        ActivitySnapshot enriched = MatchTab(snapshot, connections, watchedBrowsers);
        if (withoutPrivateAccess is null
            || !BrowserByProcessName.TryGetValue(enriched.ProcessName, out BrowserKind browser)
            || !withoutPrivateAccess.Contains(browser)
            || !IsMainWindowTitle(enriched.WindowTitle.Trim())
            || IsInternalPage(enriched))
        {
            return enriched;
        }

        return enriched with { LacksPrivateWindowAccess = true };
    }

    private static bool IsInternalPage(ActivitySnapshot snapshot)
    {
        string title = snapshot.WindowTitle.Trim();
        return (snapshot.Url is { } url && InternalPageSchemes.Any(scheme => url.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)))
               || InternalPageTitles.Any(page => title.StartsWith(page + " - ", StringComparison.Ordinal)
                                                 || title.StartsWith(page + " – ", StringComparison.Ordinal));
    }

    private static ActivitySnapshot MatchTab(
        ActivitySnapshot snapshot,
        IReadOnlyList<BrowserConnection> connections,
        IReadOnlySet<BrowserKind> watchedBrowsers)
    {
        if (snapshot.IsEmptyDesktop
            || !BrowserByProcessName.TryGetValue(snapshot.ProcessName, out BrowserKind windowBrowser))
        {
            return snapshot;
        }

        string windowTitle = snapshot.WindowTitle.Trim();
        BrowserActivity? best = null;
        int bestScore = 0;

        foreach (BrowserConnection connection in connections)
        {
            foreach (BrowserActivity tab in connection.ActiveTabs)
            {
                string tabTitle = tab.Title.Trim();
                if (tabTitle.Length == 0 || !windowTitle.StartsWith(tabTitle, StringComparison.Ordinal))
                {
                    continue;
                }

                int score = (tabTitle.Length * 2) + (IsSameBrowser(connection.Browser, windowBrowser) ? 1 : 0);
                if (score > bestScore)
                {
                    best = tab;
                    bestScore = score;
                }
            }
        }

        if (best is not null)
        {
            return snapshot with { Url = best.Url, TabId = best.TabId, Channel = best.Channel };
        }

        return watchedBrowsers.Contains(windowBrowser) && IsMainWindowTitle(windowTitle)
            ? snapshot with { IsUnseenBrowserWindow = true }
            : snapshot;
    }

    private static bool IsMainWindowTitle(string windowTitle)
    {
        // Edge writes "Microsoft\u200B Edge" with a zero-width space.
        string title = windowTitle.Replace("\u200B", string.Empty, StringComparison.Ordinal);
        return MainWindowTitleEndings.Any(ending => title.EndsWith(ending, StringComparison.Ordinal));
    }

    private static bool IsSameBrowser(BrowserKind reported, BrowserKind fromProcess)
    {
        // The extension cannot always tell Chromium browsers apart, so "Chromium" matches any of them.
        return reported == fromProcess
            || (reported == BrowserKind.Chromium && fromProcess != BrowserKind.Firefox);
    }
}
