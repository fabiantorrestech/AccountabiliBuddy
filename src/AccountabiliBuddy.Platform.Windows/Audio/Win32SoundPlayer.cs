using System.Runtime.InteropServices;
using AccountabiliBuddy.Core.Ports;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;

namespace AccountabiliBuddy.Platform.Windows.Audio;

/// <summary>
/// Plays sounds with PlaySound (winmm), asynchronously. A sound played from memory must stay in place while it
/// plays, so the last one is kept pinned until the next sound replaces it.
/// </summary>
public sealed class Win32SoundPlayer : ISoundPlayer, IDisposable
{
    private readonly object _gate = new();
    private GCHandle _playing;

    public unsafe void PlayWave(byte[] wave)
    {
        lock (_gate)
        {
            // Stop the current sound before its memory is let go.
            PInvoke.PlaySound(default(PCWSTR), HMODULE.Null, 0);
            ReleasePinned();

            _playing = GCHandle.Alloc(wave, GCHandleType.Pinned);
            var sound = new PCWSTR((char*)_playing.AddrOfPinnedObject());
            PInvoke.PlaySound(sound, HMODULE.Null, SND_FLAGS.SND_MEMORY | SND_FLAGS.SND_ASYNC | SND_FLAGS.SND_NODEFAULT);
        }
    }

    public void PlayFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        lock (_gate)
        {
            PInvoke.PlaySound(default(PCWSTR), HMODULE.Null, 0);
            ReleasePinned();
            PInvoke.PlaySound(path, null, SND_FLAGS.SND_FILENAME | SND_FLAGS.SND_ASYNC | SND_FLAGS.SND_NODEFAULT);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            PInvoke.PlaySound(default(PCWSTR), HMODULE.Null, 0);
            ReleasePinned();
        }
    }

    private void ReleasePinned()
    {
        if (_playing.IsAllocated)
        {
            _playing.Free();
        }
    }
}
