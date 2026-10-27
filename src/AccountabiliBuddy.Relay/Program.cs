using System.Net;
using System.Security.Cryptography.X509Certificates;
using AccountabiliBuddy.Relay;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The sealed relay (ADR 46). It stores opaque bytes and tells connected devices when a file changed; it never
// decrypts anything and never learns the group code or the key.
if (args.Contains("--version"))
{
    Console.WriteLine(RelayBuildInfo.Describe());
    return 0;
}

RelayOptions options;
try
{
    options = RelayOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

// The build and runtime first, so an old runtime is easy to spot in the log (a self-contained build or a Docker image
// keeps the runtime it was made with; the server's package updates don't change it).
Console.WriteLine($"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ} {RelayBuildInfo.Describe()}");

DataFolder.EnsurePrivate(options.DataDirectory);
AdmissionKey admission = AdmissionKey.LoadOrCreate(options.DataDirectory, out bool admissionCreated);
X509Certificate2? certificate = null;
if (!options.BehindProxy)
{
    certificate = RelayCertificate.LoadOrCreate(options.DataDirectory, out bool created);
    PrintPinLines(options, certificate, admission, created);
}
else
{
    PrintProxyLines(options, admission);
}

if (admissionCreated)
{
    Console.WriteLine("Made a new admission key (relay-admission.key). Only a device that has it can start a new group here.");
}

WebApplicationBuilder builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var limits = new ClientLimits(options.BehindProxy);
builder.WebHost.UseUrls(options.Urls);
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.AddServerHeader = false;
    kestrel.Limits.MaxRequestBodySize = RelayLimits.MaxFileBytes;
    kestrel.Limits.MaxConcurrentConnections = 1024;

    // A slow trickle of bytes must not hold a connection slot for hours: the body needs 4 KiB/s after a 5 s grace and
    // the headers 10 s. (The event stream turns its own response minimum off; it sends no request body.)
    kestrel.Limits.MinRequestBodyDataRate = new MinDataRate(4096, TimeSpan.FromSeconds(5));
    kestrel.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
    kestrel.ConfigureEndpointDefaults(endpoint =>
    {
        // HTTP/1.1 only: the event stream needs nothing more, and the apps' clients speak it.
        endpoint.Protocols = HttpProtocols.Http1;
        if (!options.BehindProxy)
        {
            endpoint.Use(LimitConnectionsPerAddress(limits));
        }
    });
    if (certificate is not null)
    {
        kestrel.ConfigureHttpsDefaults(https => https.ServerCertificate = certificate);
    }
});

var store = new GroupStore(options.DataDirectory, options.MaxDiskBytes, options.MaxGroups);
var broadcaster = new ChangeBroadcaster();
builder.Services.AddSingleton(store);
builder.Services.AddHostedService<ExpiryCleanupService>();

WebApplication app = builder.Build();
app.UseRequestLog();
app.UseRateLimit(new RequestRateLimiter(), limits);
app.UseInFlightLimit(limits);
new RelayEndpoints(store, broadcaster, limits, admission).Map(app);

Console.WriteLine($"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ} relay starting on {options.Urls}, data in {options.DataDirectory}");
app.Run();
return 0;

/// <summary>Closes a new connection at once when its address already has the maximum open (before any TLS work).</summary>
static Func<ConnectionDelegate, ConnectionDelegate> LimitConnectionsPerAddress(ClientLimits limits)
{
    return next => async connection =>
    {
        string address = ClientAddress.Of((connection.RemoteEndPoint as IPEndPoint)?.Address);
        using IDisposable? lease = limits.Connections.TryEnter(address);
        if (lease is null)
        {
            connection.Abort();
            return;
        }

        await next(connection);
    };
}

static void PrintPinLines(RelayOptions options, X509Certificate2 certificate, AdmissionKey admission, bool created)
{
    string pin = RelayCertificate.Fingerprint(certificate);
    string port = new Uri(options.Urls.Split(';')[0].Replace("0.0.0.0", "localhost").Replace("+", "localhost").Replace("*", "localhost")).Port.ToString();
    var lines = new List<string> { "Enter one of these in the app (Devices > Own relay). The pin and the admission key are the same for all of them:" };
    foreach (string host in RelayCertificate.HostAddresses())
    {
        lines.Add($"  relay address: https://{host}:{port}   pin: {pin}");
    }

    lines.Add($"  admission key: {admission.Value}   (a device needs it to start a new group here; the app's join link carries it)");
    Console.WriteLine(created ? "Made a new self-signed certificate (relay.pfx)." : "Using the certificate in relay.pfx.");
    lines.ForEach(Console.WriteLine);
    if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
    {
        Console.WriteLine("Note: in Docker these names are the container's own. Use the host machine's IP address (or name) with this port and this pin.");
    }

    // A service has no console to read, so the same lines are kept next to the certificate.
    WriteAddressFile(options, lines);
}

// The text file holds the admission key, so it is private to its owner like the key file itself (Unix mode 0600 from the
// first byte, rewritten on each start); on Windows the data folder's ACL does that.
static void WriteAddressFile(RelayOptions options, List<string> lines)
{
    string path = Path.Combine(options.DataDirectory, "relay-address-and-pin.txt");
    File.Delete(path);
    var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
    if (!OperatingSystem.IsWindows())
    {
        fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    }

    using var writer = new StreamWriter(path, fileOptions);
    foreach (string line in lines)
    {
        writer.WriteLine(line);
    }
}

// Behind a reverse proxy the proxy has the certificate: the address is the proxy's, and there is no pin.
static void PrintProxyLines(RelayOptions options, AdmissionKey admission)
{
    var lines = new List<string>
    {
        "Behind a reverse proxy: enter the proxy's https address in the app (Devices > Own relay), with no pin.",
        $"  admission key: {admission.Value}   (a device needs it to start a new group here; the app's join link carries it)",
    };
    lines.ForEach(Console.WriteLine);
    WriteAddressFile(options, lines);
}
