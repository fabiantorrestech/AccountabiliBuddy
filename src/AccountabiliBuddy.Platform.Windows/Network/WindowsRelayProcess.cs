using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AccountabiliBuddy.Core.Ports;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.JobObjects;

namespace AccountabiliBuddy.Platform.Windows.Network;

/// <summary>
/// Runs the relay program as a hidden child process inside a Windows job object marked "kill on close". The app holds
/// the only handle to the job, so when the app exits, crashes or is killed, Windows closes the handle and ends the
/// relay with it. The child's two output streams are read line by line. Only one child at a time.
/// </summary>
public sealed class WindowsRelayProcess : IRelayProcess
{
    private readonly object _gate = new();
    private Microsoft.Win32.SafeHandles.SafeFileHandle? _job;
    private Child? _current;
    private bool _disposed;

    public event EventHandler<string>? OutputLine;

    public event EventHandler<string>? ErrorLine;

    public event EventHandler<int>? Exited;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _current is not null;
            }
        }
    }

    public void Start(string executablePath, IReadOnlyList<string> arguments)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_current is not null)
            {
                throw new InvalidOperationException("The relay is already running.");
            }

            var info = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
            };
            foreach (string argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            var process = new Process { StartInfo = info, EnableRaisingEvents = true };
            var child = new Child(process);
            process.OutputDataReceived += (_, e) => RaiseLine(child, e.Data, OutputLine);
            process.ErrorDataReceived += (_, e) => RaiseLine(child, e.Data, ErrorLine);
            process.Exited += (_, _) => OnExited(child);
            try
            {
                process.Start();
            }
            catch (Win32Exception ex)
            {
                process.Dispose();
                throw new IOException($"Couldn't start the relay program: {ex.Message}", ex);
            }

            // Tied to the app straight after it starts. If that fails the relay must not keep running on its own.
            try
            {
                AssignToJob(process);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                child.Detached = true;
                KillQuietly(process);
                process.Dispose();
                throw new IOException("Couldn't tie the relay program to the app, so it was not left running.", ex);
            }

            _current = child;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
    }

    public void Stop()
    {
        Child? child;
        lock (_gate)
        {
            child = _current;
            _current = null;
        }

        if (child is null)
        {
            return;
        }

        // Detached first: the exit this causes is not news.
        child.Detached = true;
        KillQuietly(child.Process);
        child.Process.Dispose();
    }

    public void Dispose()
    {
        Stop();
        lock (_gate)
        {
            _disposed = true;
            // Closing the job ends anything still in it.
            _job?.Dispose();
            _job = null;
        }
    }

    private void RaiseLine(Child child, string? line, EventHandler<string>? handler)
    {
        if (line is not null && !child.Detached)
        {
            handler?.Invoke(this, line);
        }
    }

    private void OnExited(Child child)
    {
        if (child.Detached)
        {
            return;
        }

        int code;
        lock (_gate)
        {
            if (!ReferenceEquals(_current, child))
            {
                return;
            }

            _current = null;
        }

        child.Detached = true;
        try
        {
            // The last lines of output are read before the exit is announced.
            child.Process.WaitForExit();
            code = child.Process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            code = -1;
        }

        child.Process.Dispose();
        Exited?.Invoke(this, code);
    }

    private unsafe void AssignToJob(Process process)
    {
        if (_job is null)
        {
            Microsoft.Win32.SafeHandles.SafeFileHandle job = PInvoke.CreateJobObject(null, (string?)null);
            if (job.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            if (!PInvoke.SetInformationJobObject(new HANDLE(job.DangerousGetHandle()), JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation, &limits, (uint)sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)))
            {
                int error = Marshal.GetLastWin32Error();
                job.Dispose();
                throw new Win32Exception(error);
            }

            _job = job;
        }

        if (!PInvoke.AssignProcessToJobObject(new HANDLE(_job.DangerousGetHandle()), new HANDLE(process.SafeHandle.DangerousGetHandle())))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already gone.
        }
    }

    // One started program and whether its events still count.
    private sealed class Child(Process process)
    {
        public Process Process { get; } = process;

        public volatile bool Detached;
    }
}
