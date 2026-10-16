using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <param name="SkippedLines">Lines that could not be turned into a rule.</param>
public sealed record BlockListParseResult(IReadOnlyList<Rule> Rules, IReadOnlyList<string> SkippedLines);
