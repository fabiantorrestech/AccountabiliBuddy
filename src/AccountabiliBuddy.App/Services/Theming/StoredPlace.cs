namespace AccountabiliBuddy.App.Services.Theming;

/// <summary>The private payload inside <see cref="StoredAppearance.ProtectedLocation"/>: never stored or logged in plain text.</summary>
internal sealed record StoredPlace(double Latitude, double Longitude, int? CityId, string? Label);
