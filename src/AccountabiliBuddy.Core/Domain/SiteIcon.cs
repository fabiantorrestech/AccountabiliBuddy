namespace AccountabiliBuddy.Core.Domain;

/// <summary>A website's icon as the browser extension found it in the browser's own icon cache.</summary>
/// <param name="Url">A page of the site (its host names the site, as in the activity history).</param>
/// <param name="Image">The image file (usually PNG).</param>
public sealed record SiteIcon(string Url, byte[] Image);
