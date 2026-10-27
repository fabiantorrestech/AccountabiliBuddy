namespace AccountabiliBuddy.Relay;

/// <summary>
/// The command line: <c>--urls</c> (default https://0.0.0.0:8743), <c>--proxy</c> (plain HTTP on 127.0.0.1 for a reverse
/// proxy that does the TLS), <c>--data &lt;dir&gt;</c> (default ./relay-data).
/// </summary>
internal sealed class RelayOptions
{
    private const string DefaultHttpsUrl = "https://0.0.0.0:8743";
    private const string DefaultProxyUrl = "http://127.0.0.1:8743";

    private RelayOptions(string urls, bool behindProxy, string dataDirectory)
    {
        Urls = urls;
        BehindProxy = behindProxy;
        DataDirectory = dataDirectory;
    }

    public string Urls { get; }

    /// <summary>Plain HTTP on the loopback address only; a reverse proxy in front gives the certificate and the client address.</summary>
    public bool BehindProxy { get; }

    public string DataDirectory { get; }

    /// <exception cref="ArgumentException">An unknown or incomplete option, or an address that doesn't fit the mode.</exception>
    public static RelayOptions Parse(string[] args)
    {
        string? urls = null;
        string? data = null;
        bool proxy = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--proxy":
                    proxy = true;
                    break;
                case "--urls":
                    urls = ReadValue(args, ref i);
                    break;
                case "--data":
                    data = ReadValue(args, ref i);
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{args[i]}'. Options: --urls <url>, --proxy, --data <dir>.");
            }
        }

        urls ??= proxy ? DefaultProxyUrl : DefaultHttpsUrl;
        RequireModeFits(urls, proxy);
        return new RelayOptions(urls, proxy, Path.GetFullPath(data ?? "relay-data"));
    }

    private static string ReadValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{args[index]} needs a value.");
        }

        index++;
        return args[index];
    }

    private static void RequireModeFits(string urls, bool proxy)
    {
        foreach (string url in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            bool https = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            bool http = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
            if (!https && !http)
            {
                throw new ArgumentException($"'{url}' must start with https:// or http://.");
            }

            if (proxy && https)
            {
                throw new ArgumentException("--proxy serves plain HTTP; the reverse proxy does the TLS. Use an http:// address.");
            }

            if (!proxy && http)
            {
                throw new ArgumentException("The relay is HTTPS only. Use --proxy for plain HTTP behind a reverse proxy.");
            }

            if (proxy && !IsLoopback(url))
            {
                throw new ArgumentException("--proxy only listens on 127.0.0.1 or localhost, so only the proxy can reach it.");
            }
        }
    }

    private static bool IsLoopback(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && (uri.IsLoopback || uri.Host == "localhost");
    }
}
