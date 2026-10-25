# Builds the PharmaX installer.
#
#   .\installer\build.ps1
#
# Publishes the desktop application as one self-contained executable,
# builds the pharmacy's database from the previous system's export
# (-SeedFrom, the folder the export was saved to) into dist\seed, and
# then compiles installer\PharmaX.iss into dist\PharmaX-Setup-<version>.exe.
#
# The database is built by the published executable's own --import, so
# this needs a machine PharmaX has been set up on (it refuses scripted
# runs anywhere else). It writes only to dist\seed and the import report.
#
# Inno Setup 6 is needed for the second half. Without it, the publish
# still runs and the script says where the executable landed, so a build
# machine that has not got Inno can still produce something installable
# by hand.

[CmdletBinding()]
param(
    [string] $Configuration = 'Release',

    # The latest export from the previous system, taken 23 October 2026.
    # The one before it (6 October) is in Downloads\Exports\Exports.
    [string] $SeedFrom = (Join-Path $env:USERPROFILE 'Documents\Exports from DIspenseware as 23102026'),

    # The pharmacy this installer is for. Its name and address go into the
    # database the installer ships, so every till prints them on its
    # receipts and labels from the first sale; the export holds the
    # patients' addresses, not the pharmacy's. Address lines split by |.
    [string] $TradingName = 'Bethrapha Pharmacy',
    [string] $Address = '28 Woodstock Road|Thorngrove|Bulawayo',

    # For trying PharmaX out, never for the pharmacy: the first
    # administrator signs in as admin / changeme#1 and is not made to
    # change it. Builds dist\PharmaX-Setup-<version>-test.exe.
    [switch] $TestBuild,

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
    -p:DebugType=none `
    "-p:PharmaXTestBuild=$($TestBuild.IsPresent.ToString().ToLowerInvariant())"

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

# The database the installer puts in place: migrated, seeded with the
# reference data and holding the previous system's records, so a newly
# installed PharmaX opens on the pharmacy's own stock, patients and
# history. Made fresh every build, never copied from a machine that has
# been trading or testing on its database.
if (-not (Test-Path $SeedFrom)) {
    throw "The previous system's export was not found at $SeedFrom. " +
          "Pass -SeedFrom with the folder it was saved to."
}

$seed = Join-Path $dist 'seed'
$seedDb = Join-Path $seed 'pharmax.db'
if (Test-Path $seed) { Remove-Item $seed -Recurse -Force }
New-Item -ItemType Directory $seed | Out-Null

Write-Host "Building the database from $SeedFrom..." -ForegroundColor Cyan

# Pointed at the new file and nothing else: no shared database, and none
# of a developer's test accounts or demo data. Run as the pharmacy it is
# for, whatever this machine's own setup says.
$overrides = 'Database__LocalConnection', 'Database__SharedConnection',
             'Development__SeedTestUsers', 'Pharmacy__TradingName',
             'Pharmacy__Address'
$kept = @{}
foreach ($name in $overrides) {
    $kept[$name] = [Environment]::GetEnvironmentVariable($name)
    [Environment]::SetEnvironmentVariable($name, $null)
}
$env:Database__LocalConnection = "Data Source=$seedDb"
$env:Pharmacy__TradingName = $TradingName
$env:Pharmacy__Address = ($Address -split '\|' | ForEach-Object { $_.Trim() }) -join "`n"

$started = Get-Date
try {
    $import = Start-Process -FilePath $exe -ArgumentList '--import', "`"$SeedFrom`"" `
        -Wait -PassThru
}
finally {
    foreach ($name in $overrides) {
        [Environment]::SetEnvironmentVariable($name, $kept[$name])
    }
}

switch ($import.ExitCode) {
    0 { }
    2 { throw "PharmaX has not been set up on this machine, so it will not " +
              "run an import here. Set it up once, then build again." }
    default { throw "The import did not agree with the export (exit code " +
                    "$($import.ExitCode)). Its report is in " +
                    "$env:ProgramData\PharmaX\logs." }
}

$report = Get-ChildItem (Join-Path $env:ProgramData 'PharmaX\logs') -Filter 'import-*.txt' |
    Where-Object LastWriteTime -ge $started |
    Sort-Object LastWriteTime | Select-Object -Last 1

if (-not (Test-Path $seedDb) -or -not $report) {
    throw "The import finished but left no database or no report."
}

# SQLite keeps its latest writes in a -wal file beside the database until
# the last connection closes. One left behind means the file alone - the
# one that ships - is missing them.
if (Test-Path "$seedDb-wal") {
    throw "The import left $seedDb-wal behind, so the database is not " +
          "complete on its own. Build again."
}

Copy-Item $report.FullName (Join-Path $seed 'import-report.txt')

# The address is what every receipt and label will print, so a build that
# did not record it is not one to ship.
$recorded = Select-String -Path $report.FullName -SimpleMatch -Quiet `
    -Pattern "The pharmacy's address, for every till"

if (-not $recorded) {
    throw "The import did not record the pharmacy's address. Its report is " +
          "$($report.FullName)."
}

Write-Host ("Database built: {0} ({1} MB). Every total agrees with the export." -f
    $seedDb, [math]::Round((Get-Item $seedDb).Length / 1MB, 1)) -ForegroundColor Green

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

# MariaDB for the "main computer" option. Pinned to the series PharmaX's
# database layer is written against (Persistence: MariaDbServerVersion
# 10.11), downloaded once into installer\redist and checked against the
# hash MariaDB publishes, so a corrupt or substituted file never ships.
$mariaDbVersion = '10.11.19'
$mariaDbSha256 = '83af9d523bf7ba989ea0031d4ebca7e4dc21e49f435122570ddc87f0620b3a5c'
$mariaDbMsi = Join-Path $PSScriptRoot 'redist\mariadb.msi'

function Test-MariaDb {
    (Test-Path $mariaDbMsi) -and
    ((Get-FileHash $mariaDbMsi -Algorithm SHA256).Hash -eq $mariaDbSha256)
}

if (-not (Test-MariaDb)) {
    $url = "https://archive.mariadb.org/mariadb-$mariaDbVersion/" +
           "winx64-packages/mariadb-$mariaDbVersion-winx64.msi"

    Write-Host "Downloading MariaDB $mariaDbVersion..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force (Split-Path $mariaDbMsi) | Out-Null
    Invoke-WebRequest $url -OutFile $mariaDbMsi -UseBasicParsing

    if (-not (Test-MariaDb)) {
        Remove-Item $mariaDbMsi -Force
        throw "The MariaDB download does not match its published hash."
    }
}

Write-Host "MariaDB $mariaDbVersion checked." -ForegroundColor Green

Write-Host "Building the installer..." -ForegroundColor Cyan

$arguments = @(Join-Path $PSScriptRoot 'PharmaX.iss')
if ($TestBuild) { $arguments = @('/DTestBuild') + $arguments }

& $iscc $arguments

if ($LASTEXITCODE -ne 0) { throw "ISCC failed." }

Get-ChildItem $dist -Filter 'PharmaX-Setup-*.exe' |
    ForEach-Object {
        Write-Host ("Installer: {0} ({1} MB)" -f $_.FullName,
            [math]::Round($_.Length / 1MB, 1)) -ForegroundColor Green
    }
