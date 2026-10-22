namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What a classify-prompt answer covers, i.e. which kind of rule it creates.</summary>
public enum ClassifyScope
{
    /// <summary>Process name rule.</summary>
    WholeApp,

    /// <summary>URL domain rule (browser tabs only): the whole site.</summary>
    Site,

    /// <summary>Window title "contains" rule.</summary>
    Title,

    /// <summary>The part of the site the page is in (<see cref="AccountabiliBuddy.Core.Engine.SiteParts.PartOf"/>): a section, channel or path.</summary>
    Part,

    /// <summary>The page alone (<see cref="AccountabiliBuddy.Core.Engine.SiteParts.PageOf"/>).</summary>
    Page,
}
