namespace AccountabiliBuddy.Core.Domain;

/// <summary>Which part of the observed activity a rule is matched against.</summary>
public enum MatchTarget
{
    /// <summary>Process name without extension, e.g. "chrome".</summary>
    ProcessName,
    WindowTitle,
    /// <summary>Browser URL, normalized (no scheme, no "www.", lowercase).</summary>
    Url,
    /// <summary>OCR text of the window. Only evaluated when no other rule matched.</summary>
    OcrText,
    /// <summary>Channel or author of the page, reported by the browser extension (e.g. a YouTube channel name).</summary>
    Channel,
}
