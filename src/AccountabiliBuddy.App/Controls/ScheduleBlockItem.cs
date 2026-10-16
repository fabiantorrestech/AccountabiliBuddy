using AccountabiliBuddy.Core.Domain;
using Avalonia.Media;

namespace AccountabiliBuddy.App.Controls;

/// <summary>A schedule block plus what the grid needs to draw it.</summary>
public sealed record ScheduleBlockItem(ScheduleBlock Block, string ProfileName, Color Color);
