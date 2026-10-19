namespace AccountabiliBuddy.App.Services;

/// <summary>
/// A sound that can be picked for a session cue. Ids: "off", the built-in chimes ("chime.soft", "chime.rising",
/// "chime.bell"), "windows:&lt;file name&gt;" for a sound in Windows' Media folder, "file:&lt;path&gt;" for an added file.
/// </summary>
public sealed record SoundChoice(string Id, string Name)
{
    public const string OffId = "off";
    public const string SoftChimeId = "chime.soft";
    public const string RisingChimeId = "chime.rising";
    public const string BellId = "chime.bell";
    public const string WindowsPrefix = "windows:";
    public const string FilePrefix = "file:";

    /// <summary>Windows' own sounds, e.g. C:\Windows\Media.</summary>
    public static string WindowsMediaFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

    /// <summary>Off, the built-in chimes, the added file (if any), then Windows' sounds by name.</summary>
    public static IReadOnlyList<SoundChoice> List(string? customFile)
    {
        var choices = new List<SoundChoice>
        {
            new(OffId, "No sound"),
            new(SoftChimeId, "Soft chime (falling)"),
            new(RisingChimeId, "Soft chime (rising)"),
            new(BellId, "Bell"),
        };

        if (!string.IsNullOrWhiteSpace(customFile))
        {
            choices.Add(new SoundChoice(FilePrefix + customFile, $"File: {Path.GetFileName(customFile)}"));
        }

        try
        {
            choices.AddRange(Directory.EnumerateFiles(WindowsMediaFolder, "*.wav")
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(name => new SoundChoice(WindowsPrefix + name, $"Windows: {Path.GetFileNameWithoutExtension(name)}")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only the built-in chimes, then.
        }

        return choices;
    }
}
