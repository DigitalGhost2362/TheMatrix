<#
.SYNOPSIS
Publishes the Matrix server for every shipped platform and writes Server~/<rid>/server-payload.zip.

.DESCRIPTION
Run from anywhere on a machine with the .NET SDK from global.json (cross-publishing needs the runtime packs from NuGet,
so be online the first time). Each zip holds the same four files as the win-x64 one:
  matrix-server[.exe]   the published feeder-mcp-server, renamed
  feeder-mcp-server.staticwebassets.endpoints.json
  web.config
  version               the version from Feeder.Matrix.csproj
The Editor writes the exec bit after extracting on macOS and Linux, so the zip needs none.
Bump the version in Feeder.Matrix.csproj, Program.cs and McpServerManager.ServerVersion first, then run this.

.EXAMPLE
pwsh -File Matrix~/tools/publish-payloads.ps1
pwsh -File Matrix~/tools/publish-payloads.ps1 -Rids osx-arm64
#>
param(
    [string[]] $Rids = @('win-x64', 'osx-x64', 'osx-arm64', 'linux-x64'),
    [string] $PackageRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string] $WorkRoot = (Join-Path ([System.IO.Path]::GetTempPath()) 'matrix-payloads')
)

$ErrorActionPreference = 'Stop'
$Rids = @($Rids | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$matrixRoot = Join-Path $PackageRoot 'Matrix~'
$project = Join-Path $matrixRoot 'src\Feeder.Matrix\Feeder.Matrix.csproj'
$version = ([xml](Get-Content -Raw $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> in $project" }
Write-Host "Matrix server $version"

New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$summary = @()

foreach ($rid in $Rids) {
    $isWindows = $rid.StartsWith('win')
    $publishedName = if ($isWindows) { 'feeder-mcp-server.exe' } else { 'feeder-mcp-server' }
    $shippedName = if ($isWindows) { 'matrix-server.exe' } else { 'matrix-server' }
    $out = Join-Path $WorkRoot "publish-$rid"
    $stage = Join-Path $WorkRoot "stage-$rid"
    Remove-Item -Recurse -Force $out, $stage -ErrorAction SilentlyContinue

    Write-Host "== $rid"
    dotnet publish $project -c Release -r $rid --self-contained true -p:PublishSingleFile=true -p:NuGetAudit=false -o $out
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid" }

    New-Item -ItemType Directory -Force $stage | Out-Null
    Copy-Item (Join-Path $out $publishedName) (Join-Path $stage $shippedName)
    foreach ($extra in 'feeder-mcp-server.staticwebassets.endpoints.json', 'web.config') {
        $source = Join-Path $out $extra
        if (Test-Path $source) { Copy-Item $source $stage }
    }
    [System.IO.File]::WriteAllText((Join-Path $stage 'version'), $version, (New-Object System.Text.UTF8Encoding $false))

    $target = Join-Path $PackageRoot "Server~\$rid"
    New-Item -ItemType Directory -Force $target | Out-Null
    $zip = Join-Path $target 'server-payload.zip'
    Remove-Item -Force $zip -ErrorAction SilentlyContinue
    $archive = [System.IO.Compression.ZipFile]::Open($zip, 'Create')
    try {
        foreach ($file in Get-ChildItem $stage -File | Sort-Object Name) {
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $file.Name, [System.IO.Compression.CompressionLevel]::Optimal)
        }
    }
    finally { $archive.Dispose() }

    $hash = (Get-FileHash -Algorithm SHA256 (Join-Path $stage $shippedName)).Hash.ToLowerInvariant()
    $summary += [pscustomobject]@{ Rid = $rid; ZipMB = [math]::Round((Get-Item $zip).Length / 1MB, 1); ExeSha256 = $hash }
}

# restore may rewrite the lock files for the new RIDs; the repository keeps them as committed
git -C $PackageRoot checkout -- '*packages.lock.json' 2>$null

$summary | Format-Table -AutoSize
Write-Host "Put the hashes into Matrix~/README.md (Shipped payload) and keep McpServerManager.ServerVersion = $version."
