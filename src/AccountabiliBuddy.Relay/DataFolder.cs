using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace AccountabiliBuddy.Relay;

/// <summary>
/// Makes the relay's data folder (the private key, the admission key and the stored files) private to the account that
/// runs the relay, when the relay makes the folder itself: mode 0700 on Unix; on Windows an access list that holds only
/// the current user and SYSTEM and doesn't inherit (a folder made by hand under C:\ would otherwise let every local
/// account in). A folder that already exists is left as the operator set it (the install script locks its own down).
/// </summary>
internal static class DataFolder
{
    public static void EnsurePrivate(string path)
    {
        if (Directory.Exists(path))
        {
            return;
        }

        string? parent = Path.GetDirectoryName(Path.GetFullPath(path));
        if (parent is not null)
        {
            Directory.CreateDirectory(parent);
        }

        if (OperatingSystem.IsWindows())
        {
            CreateForOwnerOnWindows(path);
        }
        else
        {
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void CreateForOwnerOnWindows(string path)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        SecurityIdentifier? owner = WindowsIdentity.GetCurrent().User;
        if (owner is not null)
        {
            security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        }

        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).Create(security);
    }
}
