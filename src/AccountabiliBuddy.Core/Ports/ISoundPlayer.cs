namespace AccountabiliBuddy.Core.Ports;

/// <summary>Plays a short sound without waiting for it; a new sound stops the one still playing.</summary>
public interface ISoundPlayer
{
    /// <summary>Plays a whole WAV file held in memory.</summary>
    void PlayWave(byte[] wave);

    /// <summary>Plays a WAV file from disk; does nothing if it can't be played.</summary>
    void PlayFile(string path);
}
