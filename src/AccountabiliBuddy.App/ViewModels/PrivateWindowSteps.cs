using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>How to allow the extension in a browser's private windows, in that browser's words.</summary>
public static class PrivateWindowSteps
{
    public static string For(BrowserKind browser)
    {
        return browser switch
        {
            BrowserKind.Chrome => "chrome://extensions → AccountabiliBuddy → Details → turn on \"Allow in Incognito\"",
            BrowserKind.Brave => "brave://extensions → AccountabiliBuddy → Details → turn on \"Allow in Private\"",
            BrowserKind.Edge => "edge://extensions → AccountabiliBuddy → Details → turn on \"Allow in InPrivate\"",
            BrowserKind.Firefox => "about:addons → AccountabiliBuddy → Details → \"Run in Private Windows\" → Allow",
            _ => "open the browser's extensions page → AccountabiliBuddy → Details → allow it in private windows",
        };
    }
}
