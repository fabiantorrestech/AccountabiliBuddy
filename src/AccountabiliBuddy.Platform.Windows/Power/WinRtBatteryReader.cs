using AccountabiliBuddy.Core.Ports;
using Windows.Devices.Power;
using Windows.System.Power;

namespace AccountabiliBuddy.Platform.Windows.Power;

/// <summary>
/// This computer's battery through Windows' aggregate battery, which raises an event when the report changes (no polling).
/// A computer with no battery reports none. Read locally; nothing is stored or sent.
/// </summary>
public sealed class WinRtBatteryReader : IBatteryReader, IDisposable
{
    private readonly Battery? _battery;
    private readonly object _gate = new();
    private BatteryLevel? _current;

    public WinRtBatteryReader()
    {
        try
        {
            _battery = Battery.AggregateBattery;
            _battery.ReportUpdated += OnReportUpdated;
            _current = Read(_battery);
        }
        catch (Exception)
        {
            _battery = null; // No battery service (some servers and virtual machines).
        }
    }

    public BatteryLevel? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public event EventHandler? Changed;

    public void Dispose()
    {
        if (_battery is not null)
        {
            _battery.ReportUpdated -= OnReportUpdated;
        }
    }

    private void OnReportUpdated(Battery sender, object args)
    {
        BatteryLevel? level = Read(sender);
        lock (_gate)
        {
            if (level == _current)
            {
                return;
            }

            _current = level;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static BatteryLevel? Read(Battery battery)
    {
        BatteryReport report = battery.GetReport();
        if (report.Status == BatteryStatus.NotPresent
            || report.FullChargeCapacityInMilliwattHours is not int full || full <= 0
            || report.RemainingCapacityInMilliwattHours is not int remaining)
        {
            return null;
        }

        int percent = Math.Clamp((int)Math.Round(remaining * 100.0 / full), 0, 100);
        return new BatteryLevel(percent, report.Status == BatteryStatus.Charging);
    }
}
