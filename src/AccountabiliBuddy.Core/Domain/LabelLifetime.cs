namespace AccountabiliBuddy.Core.Domain;

/// <summary>How long a classify-prompt answer applies.</summary>
public enum LabelLifetime
{
    /// <summary>Saved as a rule in the profile (or profiles).</summary>
    Permanent,

    /// <summary>Allowed for the running session only; no rule is saved.</summary>
    Session,
}
