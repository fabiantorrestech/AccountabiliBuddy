using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Adds the URL, tab id and channel of a browser window's active tab to its snapshot.
/// The extension cannot see native window handles, so tabs are paired with windows by title:
/// browsers title their window "&lt;active tab title&gt; - &lt;browser name&gt;", so the window title starts with
/// the tab title. The longest matching tab title wins; a tab from the same browser wins a tie.
/// Two windows of one browser showing tabs with identical titles cannot be told apart.
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

    public static bool IsBrowserProcess(string processName)
    {
        return BrowserByProcessName.ContainsKey(processName);
    }

    public static ActivitySnapshot Enrich(ActivitySnapshot snapshot, IReadOnlyList<BrowserConnection> connections)
    {
        if (snapshot.IsEmptyDesktop
            || connections.Count == 0
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

        return best is null
            ? snapshot
            : snapshot with { Url = best.Url, TabId = best.TabId, Channel = best.Channel };
    }

    private static bool IsSameBrowser(BrowserKind reported, BrowserKind fromProcess)
    {
        // The extension cannot always tell Chromium browsers apart, so "Chromium" matches any of them.
        return reported == fromProcess
            || (reported == BrowserKind.Chromium && fromProcess != BrowserKind.Firefox);
    }
}
