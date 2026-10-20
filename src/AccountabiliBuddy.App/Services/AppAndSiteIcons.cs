using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Icons for the apps and sites in History, kept as small image files in the data folder's "icons" folder. An
/// app's icon is read from its program file the first time it's seen in use (or when History asks and it's
/// running); a site's icon comes from the browser extension, which takes it from the browser's own cache. No icon
/// is ever downloaded. Use from the UI thread only (watcher and browser events are marshalled here).
/// </summary>
public sealed class AppAndSiteIcons
{
    private readonly string _folder;
    private readonly IAppIconReader _reader;
    private readonly Dictionary<string, Bitmap?> _loaded = new(StringComparer.OrdinalIgnoreCase);

    // Apps already looked at this run, so a missing icon isn't read again on every window switch.
    private readonly HashSet<string> _triedApps = new(StringComparer.OrdinalIgnoreCase);

    public AppAndSiteIcons(string folder, IAppIconReader reader)
    {
        _folder = folder;
        _reader = reader;
    }

    /// <summary>Raised when a new icon was saved.</summary>
    public event EventHandler? Changed;

    public void Start(IActivityWatcher watcher, IBrowserActivitySource browsers)
    {
        watcher.ActivityChanged += (_, snapshot) => Dispatcher.UIThread.Post(() => RequestAppIcon(snapshot.ProcessName));
        browsers.SiteIconReceived += (_, icon) => Dispatcher.UIThread.Post(() => SaveSiteIcon(icon));
    }

    /// <summary>The icon for a History name (a site's host or an app's process name), or null when there is none yet.</summary>
    public Bitmap? Find(string name)
    {
        if (_loaded.TryGetValue(name, out Bitmap? known))
        {
            return known;
        }

        Bitmap? bitmap = Load(PathFor("site", name)) ?? Load(PathFor("app", name));
        _loaded[name] = bitmap;
        return bitmap;
    }

    /// <summary>Reads the app's icon in the background if it isn't saved yet (only works while the app runs).</summary>
    public void RequestAppIcon(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName) || !_triedApps.Add(processName) || File.Exists(PathFor("app", processName)))
        {
            return;
        }

        _ = Task.Run(() => _reader.ReadIcon(processName)).ContinueWith(
            read =>
            {
                if (read.IsCompletedSuccessfully && read.Result is IconPixels pixels)
                {
                    Dispatcher.UIThread.Post(() => SaveAppIcon(processName, pixels));
                }
            },
            TaskScheduler.Default);
    }

    private void SaveAppIcon(string processName, IconPixels pixels)
    {
        try
        {
            using var bitmap = new WriteableBitmap(
                new PixelSize(pixels.Width, pixels.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using (ILockedFramebuffer frame = bitmap.Lock())
            {
                for (int row = 0; row < pixels.Height; row++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(
                        pixels.Bgra, row * pixels.Width * 4, frame.Address + (row * frame.RowBytes), pixels.Width * 4);
                }
            }

            Directory.CreateDirectory(_folder);
            bitmap.Save(PathFor("app", processName));
            Forget(processName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // No icon then; the name still shows with a letter badge.
        }
    }

    private void SaveSiteIcon(SiteIcon icon)
    {
        string? host = UrlNormalizer.GetHost(icon.Url);
        if (host is null || File.Exists(PathFor("site", host)))
        {
            return;
        }

        try
        {
            // Only keep what decodes as an image.
            using (var check = new Bitmap(new MemoryStream(icon.Image)))
            {
            }

            Directory.CreateDirectory(_folder);
            File.WriteAllBytes(PathFor("site", host), icon.Image);
            Forget(host);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
                                       or NotSupportedException)
        {
            // Not an image Avalonia can show, or the folder can't be written.
        }
    }

    private void Forget(string name)
    {
        _loaded.Remove(name);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static Bitmap? Load(string path)
    {
        try
        {
            return File.Exists(path) ? new Bitmap(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
                                       or NotSupportedException)
        {
            return null;
        }
    }

    private string PathFor(string kind, string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string safe = new string(name.ToLowerInvariant().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return Path.Combine(_folder, $"{kind}-{safe}.png");
    }
}
