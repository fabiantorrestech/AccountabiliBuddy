<#
Rebuilds src/AccountabiliBuddy.App/Assets/Data/cities.tsv, the city list behind Settings -> Appearance -> Find a city (ADR 49).

Source: GeoNames cities15000.zip (https://download.geonames.org/export/dump/), CC BY 4.0, credited on the About page.
Output: one city per line, tab separated: geonameid, name, admin1 code, country code, latitude, longitude.
Latitude and longitude are rounded to 2 decimals (about 1 km). Largest cities first, so a search lists them first.

Usage: pwsh scripts/build-cities.ps1
#>
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'src/AccountabiliBuddy.App/Assets/Data/cities.tsv'
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('cities-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null

try {
    $zip = Join-Path $work 'cities15000.zip'
    Invoke-WebRequest -Uri 'https://download.geonames.org/export/dump/cities15000.zip' -OutFile $zip
    Expand-Archive -Path $zip -DestinationPath $work

    $invariant = [System.Globalization.CultureInfo]::InvariantCulture
    $lines = Get-Content -Path (Join-Path $work 'cities15000.txt') -Encoding UTF8 | ForEach-Object {
        # GeoNames columns: 0 id, 1 name, 4 latitude, 5 longitude, 8 country code, 10 admin1 code, 14 population.
        $f = $_ -split "`t"
        [pscustomobject]@{
            Population = [long]$f[14]
            Line = ($f[0], $f[1], $f[10], $f[8],
                ([double]::Parse($f[4], $invariant)).ToString('0.00', $invariant),
                ([double]::Parse($f[5], $invariant)).ToString('0.00', $invariant)) -join "`t"
        }
    } | Sort-Object -Property Population -Descending | ForEach-Object { $_.Line }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $output) | Out-Null
    [System.IO.File]::WriteAllLines($output, [string[]]$lines, (New-Object System.Text.UTF8Encoding($false)))
    "{0} cities, {1:N0} bytes -> {2}" -f $lines.Count, (Get-Item $output).Length, $output
}
finally {
    Remove-Item -Recurse -Force $work
}
