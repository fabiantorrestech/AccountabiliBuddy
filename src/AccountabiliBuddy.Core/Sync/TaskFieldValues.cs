using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Reads and copies one <see cref="TaskField"/> of a task for the field-level merge (ADR 76): its value as one comparable text,
/// the hash of a title or note, and the task with one part taken from another copy.
/// </summary>
public static class TaskFieldValues
{
    private const string NullText = "\u0000";

    /// <summary>The fields in the order they merge.</summary>
    public static IReadOnlyList<TaskField> All { get; } = Enum.GetValues<TaskField>();

    /// <summary>The field's value as one text: equal texts are equal values. Never logged or shown.</summary>
    public static string Canonical(TaskItem task, TaskField field)
    {
        return field switch
        {
            TaskField.Title => task.Title,
            TaskField.Note => task.Note ?? NullText,
            TaskField.Steps => Json(task.Steps ?? []),
            TaskField.Label => task.LabelId?.ToString("D") ?? string.Empty,
            TaskField.Tags => Json(task.Tags ?? []),
            TaskField.Bin => Json(new { task.Bin, task.PrevBin, task.Position, task.DoneAt }),
            TaskField.Reminders => Json(task.Reminders ?? []),
            TaskField.Estimate => task.EstimateMinutes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            TaskField.Due => Json(new { task.DueOn, task.DueAt }),
            TaskField.Highlight => task.Highlight ? "1" : "0",
            TaskField.CantMiss => task.CantMiss ? "1" : "0",
            TaskField.Devices => Json(task.Devices ?? []),
            TaskField.Calendar => task.Calendar ? "1" : "0",
            TaskField.Number => Json(task.Number),
            TaskField.Plan => task.PlannedFor?.TieText ?? string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
    }

    /// <summary>
    /// A title's or note's hash for <see cref="TaskFieldStamp.Bases"/>: the first 16 lower-case hex digits of SHA-256 over the
    /// UTF-8 text, or "-" for no text (a removed note). The phone's twin computes the same.
    /// </summary>
    public static string Hash(string? value)
    {
        if (value is null)
        {
            return "-";
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(hash.AsSpan(0, 8));
    }

    /// <summary>The text a title or note field holds (null for no note), for <see cref="Hash"/> and conflicts.</summary>
    public static string? Text(TaskItem task, TaskField field)
    {
        return field switch
        {
            TaskField.Title => task.Title,
            TaskField.Note => task.Note,
            _ => throw new ArgumentOutOfRangeException(nameof(field), "Only the title and the note are texts."),
        };
    }

    /// <summary><paramref name="target"/> with <paramref name="field"/> taken from <paramref name="source"/>.</summary>
    public static TaskItem With(TaskItem target, TaskItem source, TaskField field)
    {
        return field switch
        {
            TaskField.Title => target with { Title = source.Title },
            TaskField.Note => target with { Note = source.Note },
            TaskField.Steps => target with { Steps = source.Steps },
            TaskField.Label => target with { LabelId = source.LabelId },
            TaskField.Tags => target with { Tags = source.Tags },
            TaskField.Bin => target with { Bin = source.Bin, PrevBin = source.PrevBin, Position = source.Position, DoneAt = source.DoneAt },
            TaskField.Reminders => target with { Reminders = source.Reminders },
            TaskField.Estimate => target with { EstimateMinutes = source.EstimateMinutes },
            TaskField.Due => target with { DueOn = source.DueOn, DueAt = source.DueAt },
            TaskField.Highlight => target with { Highlight = source.Highlight },
            TaskField.CantMiss => target with { CantMiss = source.CantMiss },
            TaskField.Devices => target with { Devices = source.Devices },
            TaskField.Calendar => target with { Calendar = source.Calendar },
            TaskField.Number => target with { Number = source.Number },
            TaskField.Plan => target with { PlannedFor = source.PlannedFor },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
    }

    private static string Json<T>(T value)
    {
        return JsonSerializer.Serialize(value, SyncJson.Options);
    }
}
