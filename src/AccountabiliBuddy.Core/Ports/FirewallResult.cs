namespace AccountabiliBuddy.Core.Ports;

/// <summary>How a firewall change that needs administrator rights ended.</summary>
public enum FirewallResult
{
    /// <summary>The rule is now as asked.</summary>
    Done,

    /// <summary>The user said no to Windows' permission prompt; nothing changed.</summary>
    Declined,

    /// <summary>The change didn't happen for another reason.</summary>
    Failed,
}
