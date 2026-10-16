namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What a classify-prompt answer covers, i.e. which kind of rule it creates.</summary>
public enum ClassifyScope
{
    /// <summary>Process name rule.</summary>
    WholeApp,

    /// <summary>URL domain rule (browser tabs only).</summary>
    Site,

    /// <summary>Window title "contains" rule.</summary>
    Title,

    /// <summary>Channel rule (YouTube videos and channel pages only).</summary>
    Channel,
}
