using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Puts a sealed sync file back together from its QR links (<see cref="SyncQr.Links"/>), scanned or read from
/// pictures in any order. A link of another group, another count or another file (a new set of codes) starts over with
/// that one. Once every part is in, the parts are checked against the file's check; a set that doesn't match is
/// dropped. Opening the file (<see cref="SyncFileCodec.Open"/>) is the caller's step.
/// </summary>
public sealed class SyncQrCollector
{
    private byte[]?[] _parts = [];
    private string? _check;

    /// <summary>The group id of the codes being collected; null before the first.</summary>
    public string? GroupId { get; private set; }

    /// <summary>How many different codes of the current file are in.</summary>
    public int Received { get; private set; }

    /// <summary>How many codes the current file has; 0 before the first.</summary>
    public int Total => _parts.Length;

    /// <summary>The sealed file, once every code is in and the check matches; else null.</summary>
    public byte[]? Complete { get; private set; }

    /// <returns>
    /// True when the link is a sync code and was taken in (a code scanned again is taken but not counted twice);
    /// false when it isn't one, changing nothing, or when it completed a set that fails the check, which is dropped.
    /// </returns>
    public bool Add(string link)
    {
        if (!TryRead(link, out Part? part))
        {
            return false;
        }

        if (part.GroupId != GroupId || part.Check != _check || part.Total != Total)
        {
            Reset();
            GroupId = part.GroupId;
            _check = part.Check;
            _parts = new byte[part.Total][];
        }

        if (_parts[part.Index - 1] is null)
        {
            _parts[part.Index - 1] = part.Data;
            Received++;
        }

        if (Received == Total && Complete is null)
        {
            byte[] whole = _parts.SelectMany(data => data!).ToArray();
            if (SyncQr.CheckOf(whole) != _check)
            {
                Reset();
                return false;
            }

            Complete = whole;
        }

        return true;
    }

    /// <summary>Forgets every code, e.g. after the complete file didn't open.</summary>
    public void Reset()
    {
        _parts = [];
        _check = null;
        GroupId = null;
        Received = 0;
        Complete = null;
    }

    /// <summary>
    /// Reads a link: its start in any case (ASCII), then "&amp;"-separated names and values, each name once; g and f are 8
    /// lower-case hex characters, p is "i-n" (1 ≤ i ≤ n ≤ 99, digits only), d is base64url. Other names are ignored.
    /// </summary>
    private static bool TryRead(string link, [NotNullWhen(true)] out Part? part)
    {
        part = null;

        // ASCII letters only, so the phone reads exactly the links the PC does.
        if (link.Length < SyncQr.LinkStart.Length || !Ascii.EqualsIgnoreCase(link.AsSpan(0, SyncQr.LinkStart.Length), SyncQr.LinkStart))
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string pair in link[SyncQr.LinkStart.Length..].Split('&'))
        {
            int equals = pair.IndexOf('=');
            if (equals <= 0 || !values.TryAdd(pair[..equals], pair[(equals + 1)..]))
            {
                return false;
            }
        }

        if (!values.TryGetValue("g", out string? groupId) || !SyncQr.IsHexId(groupId)
            || !values.TryGetValue("f", out string? check) || !SyncQr.IsHexId(check)
            || !values.TryGetValue("p", out string? position) || !TryReadPosition(position, out int index, out int total)
            || !values.TryGetValue("d", out string? text) || SyncQr.FromBase64Url(text) is not byte[] data)
        {
            return false;
        }

        part = new Part(groupId, check, index, total, data);
        return true;
    }

    private static bool TryReadPosition(string text, out int index, out int total)
    {
        index = 0;
        total = 0;
        string[] numbers = text.Split('-');
        return numbers.Length == 2
            && TryReadCount(numbers[0], out index)
            && TryReadCount(numbers[1], out total)
            && index >= 1 && index <= total && total <= SyncQr.MaxParts;
    }

    /// <summary>One or two digits, nothing else.</summary>
    private static bool TryReadCount(string text, out int value)
    {
        value = 0;
        return text.Length is 1 or 2 && text.All(char.IsAsciiDigit)
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private sealed record Part(string GroupId, string Check, int Index, int Total, byte[] Data);
}
