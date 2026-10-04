# Bootstrap for the 1 October 2026 end-to-end run. Dot-source at the top
# of every step:  . C:\Users\mlilo\source\repos\PharmaX\evidence\e2e-oct01\run.ps1
#
# Its own database, PharmaX_E2E_OCT01, created fresh for the run so the
# demonstration seeding - including the ten-of-everything top-up - is
# what is tested, and never the working PharmaX database. Borrows the
# admin run's helpers and then points every one of them here.
. 'C:\Users\mlilo\source\repos\PharmaX\evidence\e2e-admin\adm.ps1'

$script:E2E = 'C:\Users\mlilo\source\repos\PharmaX\evidence\e2e-oct01'
$script:DBNAME = 'PharmaX_E2E_OCT01'
$brandFile = Join-Path $script:E2E 'brand.txt'
$env:PHX_BRAND = if (Test-Path $brandFile) { (Get-Content $brandFile -Raw).Trim() } else { '' }
[Phx]::Brand = if ($env:PHX_BRAND) { $env:PHX_BRAND } else { $null }

$env:Database__SharedConnection = "Server=127.0.0.1;Port=3306;Database=$DBNAME;User ID=root;Password=root"
$env:Development__SeedTestUsers = 'true'

$script:SHOTS = Join-Path $E2E 'screenshots'
$script:PIDFILE = Join-Path $E2E 'pid.txt'
$script:RESULTS = Join-Path $E2E 'results.csv'
New-Item -ItemType Directory -Force (Join-Path $E2E 'receipts'), (Join-Path $E2E 'exports'), (Join-Path $E2E 'db'), $SHOTS | Out-Null

function SetBrand([string]$name) { Set-Content $brandFile $name; $env:PHX_BRAND = $name; [Phx]::Brand = $name }

function StartApp {
  $p = Start-Process -FilePath $EXE -WorkingDirectory (Split-Path $EXE) -PassThru
  Set-Content $PIDFILE $p.Id
  $deadline = (Get-Date).AddSeconds(150)
  while ((Get-Date) -lt $deadline) {
    if ([Phx]::Find($p.Id, 'Sign in') -ne [IntPtr]::Zero) { return $p.Id }
    if ($p.HasExited) { throw "app exited with $($p.ExitCode)" }
    Start-Sleep -Milliseconds 500
  }
  throw 'sign-in window never appeared'
}

function Sql([string]$query, [string]$name = '') {
  $out = & $MYSQL '--host=127.0.0.1' '--user=root' '--password=root' "--database=$DBNAME" "--execute=$query" 2>&1 |
    ForEach-Object { "$_" } | Where-Object { $_ -notmatch 'Using a password' }
  if ($name) { @("-- $query", $out) | Set-Content (Join-Path $E2E "db\$name.txt") }
  $out
}
