namespace AccountabiliBuddy.Core.Browser;

/// <summary>
/// Names shared by the app, the native messaging host and the extension.
/// This file is also compiled into the native host, which deliberately does not reference Core.
/// </summary>
public static class BrowserBridge
{
    /// <summary>Native messaging host name the extension connects to (must match the extension's code).</summary>
    public const string HostName = "com.accountabilibuddy.host";

    /// <summary>
    /// Chromium extension id. Fixed by the public "key" in extensions/chromium/manifest.json, so it is the same
    /// on every machine and in every Chromium browser.
    /// </summary>
    public const string ChromiumExtensionId = "ecemkilpalellpppcmfcgcgggcmlghhe";

    /// <summary>Firefox add-on id, set in extensions/firefox/manifest.json.</summary>
    public const string FirefoxExtensionId = "accountabilibuddy@fabiantorrestech.github.io";

    /// <summary>
    /// Per-user pipe name. Including the user's SID keeps two signed-in users from colliding;
    /// the pipe's access list additionally restricts it to that user.
    /// </summary>
    public static string PipeName(string userSid)
    {
        return $"AccountabiliBuddy.Browser.{userSid}";
    }
}
