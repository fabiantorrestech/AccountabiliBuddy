using System.Runtime.InteropServices;
using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.Platform.Windows.Audio;

/// <summary>
/// An output's master volume and mute through Windows Core Audio (<c>IAudioEndpointVolume</c>), by hand-written COM interop. Any
/// failure of any kind (no output, a remote session, a locked-down PC, a hardware mute that can't be overridden) reads as "can't":
/// <see cref="Read()"/> gives null and <see cref="Apply"/> gives false, never an exception. Call from one thread at a time (the UI thread).
/// </summary>
public sealed class WindowsMasterVolume : IMasterVolume
{
    private const uint ClsctxAll = 0x17;
    private static readonly Guid EndpointVolumeId = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    public MasterVolumeState? Read() => ReadDevice(null);

    public MasterVolumeState? Read(string deviceId) => ReadDevice(deviceId);

    public bool Apply(string deviceId, float level, bool muted)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioEndpointVolume? volume = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            if (enumerator.GetDevice(deviceId, out device) != 0 || device is null || (volume = VolumeOf(device)) is null)
            {
                return false;
            }

            Guid context = Guid.Empty;
            int levelResult = volume.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref context);
            int muteResult = volume.SetMute(muted, ref context);
            // S_FALSE (1) also means it worked (nothing needed changing).
            return levelResult >= 0 && muteResult >= 0;
        }
        catch (Exception)
        {
            // Nothing from the interop may reach the UI thread.
            return false;
        }
        finally
        {
            Release(volume);
            Release(device);
            Release(enumerator);
        }
    }

    // The default output when deviceId is null, otherwise that output.
    private static MasterVolumeState? ReadDevice(string? deviceId)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioEndpointVolume? volume = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            int found = deviceId is null
                ? enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device)
                : enumerator.GetDevice(deviceId, out device);
            if (found != 0 || device is null)
            {
                return null;
            }

            if (device.GetId(out string id) != 0 || (volume = VolumeOf(device)) is null)
            {
                return null;
            }

            if (volume.GetMasterVolumeLevelScalar(out float level) != 0 || volume.GetMute(out bool muted) != 0)
            {
                return null;
            }

            return new MasterVolumeState(id, level, muted);
        }
        catch (Exception)
        {
            // Nothing from the interop may reach the UI thread.
            return null;
        }
        finally
        {
            Release(volume);
            Release(device);
            Release(enumerator);
        }
    }

    private static IAudioEndpointVolume? VolumeOf(IMMDevice device)
    {
        Guid iid = EndpointVolumeId;
        return device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out object endpoint) == 0 ? endpoint as IAudioEndpointVolume : null;
    }

    private static void Release(object? com)
    {
        try
        {
            if (com is not null && Marshal.IsComObject(com))
            {
                Marshal.ReleaseComObject(com);
            }
        }
        catch (Exception)
        {
            // A release that fails leaves nothing to do.
        }
    }
}
