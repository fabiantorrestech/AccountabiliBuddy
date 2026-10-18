using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>The user's answer to the classify prompt: the rule it created and the activity it was about.</summary>
public sealed record ClassifyAnswer(Rule Rule, ActivitySnapshot Snapshot);
