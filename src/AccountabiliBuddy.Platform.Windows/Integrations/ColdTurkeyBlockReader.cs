using AccountabiliBuddy.Core.Import;
using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.Platform.Windows.Integrations;

/// <summary>
/// Reads Cold Turkey's block states from a copy of its settings database (it keeps the original open). The file is
/// read again only when its write time or size changed, so checking every minute costs one file lookup.
/// </summary>
public sealed class ColdTurkeyBlockReader : IColdTurkeyBlockReader
{
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Cold Turkey", "data-app.db");

    private string? _readStamp;
    private ColdTurkeyBlocks? _lastRead;

    public ColdTurkeyBlocks? Read()
    {
        var file = new FileInfo(_settingsPath);
        if (!file.Exists)
        {
            _readStamp = null;
            _lastRead = null;
            return null;
        }

        string stamp = $"{file.LastWriteTimeUtc.Ticks}:{file.Length}";
        if (stamp == _readStamp)
        {
            return _lastRead;
        }

        string copy = Path.Combine(Path.GetTempPath(), $"AccountabiliBuddy-coldturkey-{Guid.NewGuid():N}.db");
        try
        {
            using (var source = new FileStream(_settingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (FileStream target = File.Create(copy))
            {
                source.CopyTo(target);
            }

            _lastRead = ColdTurkeySettingsReader.ReadBlockStates(copy);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            // Unreadable right now (or a Cold Turkey version with another format): shown as unknown, tried again
            // when the file changes.
            _lastRead = null;
        }
        finally
        {
            File.Delete(copy);
        }

        _readStamp = stamp;
        return _lastRead;
    }
}
