namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What the review does with a row.</summary>
public enum ReviewChoice
{
    /// <summary>Nothing: left as it is (it may come up again).</summary>
    None,

    /// <summary>A block rule, at once.</summary>
    Distracting,

    /// <summary>An allow rule (needs the card's reason).</summary>
    Fine,

    /// <summary>Never proposed again.</summary>
    Ignore,
}
