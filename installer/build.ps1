# Builds the PharmaX installer.
#
#   .\installer\build.ps1
#
# Publishes the desktop application as one self-contained executable and
# then compiles installer\PharmaX.iss into dist\PharmaX-Setup-<version>.exe.
#
# Inno Setup 6 is needed for the second half. Without it, the publish
# still runs and the script says where the executable landed, so a build
# machine that has not got Inno can still produce something installable
# by hand.

[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [switch] $SkipInstaller
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
$publish = Join-Path $dist 'publish'

Write-Host "Publishing PharmaX ($Configuration)..." -ForegroundColor Cyan

# Cleared rather than published over: a stale file from a previous build
# is the kind of thing that ships and nobody notices for a month.
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

& dotnet publish (Join-Path $root 'PharmaX.Desktop\PharmaX.Desktop.csproj') `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $publish `
    -p:DebugType=none

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$exe = Join-Path $publish 'PharmaX.Desktop.exe'
if (-not (Test-Path $exe)) { throw "Published executable not found at $exe." }

$total = (Get-ChildItem $publish -Recurse -File |
    Measure-Object -Property Length -Sum).Sum

Write-Host ("Published {0} ({1} MB across {2} files)" -f $publish,
    [math]::Round($total / 1MB, 1),
    (Get-ChildItem $publish -Recurse -File).Count) -ForegroundColor Green

# A published build must not carry a developer's appsettings.Development
# file. It holds a real database password, and PharmaX skips its
# first-run wizard whenever it finds one - so a build shipping this file
# would arrive at a pharmacy already configured as somebody else's
# machine, with their credentials in it. The csproj marks it
# CopyToPublishDirectory=Never; this is the check that it stayed that way.
$leaked = Get-ChildItem $publish -Recurse -Filter 'appsettings.*.json'

if ($leaked) {
    $leaked | ForEach-Object { Remove-Item $_.FullName -Force }

    throw ("Publish output contained {0}. Removed, but the build is " +
           "stopped: a shipped build must carry no developer settings." -f
           ($leaked.Name -join ', '))
}

Write-Host "No developer settings in the publish output." -ForegroundColor Green

# The shipped appsettings.json must not carry a real workstation
# identity. Checked here rather than trusted, because the failure it
# guards against - three tills sharing one id and colliding on their
# first receipt - would not show up until the pharmacy was trading.
$settings = Get-Content (Join-Path $root 'PharmaX.Desktop\appsettings.json') -Raw |
    ConvertFrom-Json

$shipped = $settings.Workstation.WorkstationId

if ($shipped -ne '00000000-0000-0000-0000-000000000001') {
    throw "appsettings.json carries WorkstationId $shipped. The shipped " +
          "file must keep the placeholder; real identities are generated " +
          "per machine by the first-run wizard."
}

if ($settings.Database.SharedConnection -ne '') {
    throw "appsettings.json carries a SharedConnection. The shipped file " +
          "must not name a database server; the first-run wizard asks."
}

Write-Host "Shipped settings carry no machine identity." -ForegroundColor Green

if ($SkipInstaller) {
    Write-Host "Skipping the installer as asked." -ForegroundColor Yellow
    return
}

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    # Where winget puts it when installed for the current user only.
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Host ""
    Write-Host "Inno Setup 6 is not installed, so no installer was built." `
        -ForegroundColor Yellow
    Write-Host "Get it from https://jrsoftware.org/isdl.php and run this " `
        -NoNewline
    Write-Host "script again."
    Write-Host ""
    Write-Host "The published application is ready at:"
    Write-Host "  $exe"
    return
}

Write-Host "Building the installer..." -ForegroundColor Cyan

& $iscc (Join-Path $PSScriptRoot 'PharmaX.iss')

if ($LASTEXITCODE -ne 0) { throw "ISCC failed." }

Get-ChildItem $dist -Filter 'PharmaX-Setup-*.exe' |
    ForEach-Object {
        Write-Host ("Installer: {0} ({1} MB)" -f $_.FullName,
            [math]::Round($_.Length / 1MB, 1)) -ForegroundColor Green
    }
