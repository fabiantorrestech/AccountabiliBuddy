using System.Security.Cryptography.X509Certificates;
using AccountabiliBuddy.Relay;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The sealed relay (ADR 46). It stores opaque bytes and tells connected devices when a file changed; it never
// decrypts anything and never learns the group code or the key.
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

X509Certificate2? certificate = null;
if (!options.BehindProxy)
{
    certificate = RelayCertificate.LoadOrCreate(options.DataDirectory, out bool created);
    PrintPinLines(options, certificate, created);
}

WebApplicationBuilder builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseUrls(options.Urls);
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.AddServerHeader = false;
    kestrel.Limits.MaxRequestBodySize = RelayLimits.MaxFileBytes;
    kestrel.Limits.MaxConcurrentConnections = 512;
    if (certificate is not null)
    {
        kestrel.ConfigureHttpsDefaults(https => https.ServerCertificate = certificate);
    }
});

var store = new GroupStore(options.DataDirectory);
var broadcaster = new ChangeBroadcaster();
builder.Services.AddSingleton(store);
builder.Services.AddHostedService<ExpiryCleanupService>();

WebApplication app = builder.Build();
app.UseRequestLog();
app.UseRateLimit(new RequestRateLimiter(), options.BehindProxy);
new RelayEndpoints(store, broadcaster).Map(app);

Console.WriteLine($"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ} relay starting on {options.Urls}, data in {options.DataDirectory}");
app.Run();
return 0;

static void PrintPinLines(RelayOptions options, X509Certificate2 certificate, bool created)
{
    string pin = RelayCertificate.Fingerprint(certificate);
    string port = new Uri(options.Urls.Split(';')[0].Replace("0.0.0.0", "localhost").Replace("+", "localhost").Replace("*", "localhost")).Port.ToString();
    var lines = new List<string> { "Enter one of these in the app (Devices > Own relay). The pin is the same for all of them:" };
    foreach (string host in RelayCertificate.HostAddresses())
    {
        lines.Add($"  relay address: https://{host}:{port}   pin: {pin}");
    }

    Console.WriteLine(created ? "Made a new self-signed certificate (relay.pfx)." : "Using the certificate in relay.pfx.");
    lines.ForEach(Console.WriteLine);

    // A service has no console to read, so the same lines are kept next to the certificate.
    File.WriteAllLines(Path.Combine(options.DataDirectory, "relay-address-and-pin.txt"), lines);
}
