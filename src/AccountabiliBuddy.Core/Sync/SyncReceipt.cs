using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// One entry of a sync file's <c>receipts</c> (ADR 86): what this device holds and applied of ONE other device's files, so that device
/// can show, per sync method and per part, whether its newest file arrived and was taken. Ids, times and fixed codes only (the
/// vocabularies of <see cref="SyncReceiptParts"/>): never a name, title, file name, path or address. Also the shape of one sender's
/// row in the receiver-local record (<see cref="SyncReceiptsLocal"/>).
/// </summary>
/// <param name="From">The sender being acknowledged (never the writing device itself, never a removed device).</param>
/// <param name="Held">The newest <see cref="SyncDeviceCard.WrittenAt"/> held from <paramref name="From"/>, however it arrived (the card's <c>seen</c> value).</param>
/// <param name="ByMethod">Per method (<see cref="SyncReceiptParts.Methods"/>): the newest <c>WrittenAt</c> of <paramref name="From"/>'s files that arrived by it.</param>
/// <param name="Failed">Per method: why the last attempt on <paramref name="From"/>'s file failed (<see cref="SyncReceiptParts.Failures"/>); absent after a success.</param>
/// <param name="Parts">Per part (<see cref="SyncReceiptParts.Codes"/>): the outcome of the last accept of <paramref name="From"/>'s file.</param>
/// <param name="History">The goal history month files merged from <paramref name="From"/> (the last <see cref="SyncReceiptParts.MaxMonths"/> months; years stay in <c>goalYearsMerged</c>).</param>
public sealed record SyncReceipt(
    Guid From,
    DateTimeOffset Held,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, DateTimeOffset>? ByMethod = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Failed = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, SyncPartReceipt>? Parts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncHistoryReceipt>? History = null);
