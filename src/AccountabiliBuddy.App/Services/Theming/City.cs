using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services.Theming;

/// <summary>A city from the bundled GeoNames list (Assets/Data/cities.tsv).</summary>
/// <param name="Admin1">GeoNames' first-level region code; many countries use numbers, which are not shown.</param>
public sealed record City(int Id, string Name, string Admin1, string CountryCode, GeoPoint Point)
{
    /// <summary>"Name, Region, CC", leaving out a region that is only a number.</summary>
    public string Display => Admin1.Length == 0 || Admin1.All(char.IsDigit)
        ? $"{Name}, {CountryCode}"
        : $"{Name}, {Admin1}, {CountryCode}";
}
