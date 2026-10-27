using Microsoft.Extensions.Hosting;

namespace AccountabiliBuddy.Relay;

/// <summary>Runs the 30-day age cleanup once at start and then every hour.</summary>
internal sealed class ExpiryCleanupService : BackgroundService
{
    private readonly GroupStore _store;

    public ExpiryCleanupService(GroupStore store)
    {
        _store = store;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RelayLimits.CleanupInterval);
        do
        {
            try
            {
                int removed = _store.RemoveExpired(DateTimeOffset.UtcNow);
                if (removed > 0)
                {
                    Console.WriteLine($"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ} cleanup removed {removed} expired file(s)");
                }
            }
            catch (IOException ex)
            {
                Console.WriteLine($"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ} cleanup failed: {ex.GetType().Name}");
            }
        }
        while (await WaitForNextAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitForNextAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
