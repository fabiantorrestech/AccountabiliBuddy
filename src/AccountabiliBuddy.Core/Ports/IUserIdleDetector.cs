namespace AccountabiliBuddy.Core.Ports;

/// <summary>Tells how long ago the user last used the keyboard or mouse, so idle time is not logged as activity.</summary>
public interface IUserIdleDetector
{
    TimeSpan GetIdleTime();
}
