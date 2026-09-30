# Bootstrap for the Administrator end-to-end run. Dot-source at the top of
# every step:  . C:\Users\mlilo\source\repos\PharmaX\evidence\e2e-admin\adm.ps1
#
# Points the helpers at the run's own database, PharmaX_E2E_ADMIN, and at
# this folder for evidence. brand.txt holds the trading name once it has
# been saved at the System Console, so window lookups written against
# "PharmaX - ..." find the branded titles in every later step.
$script:E2E = 'C:\Users\mlilo\source\repos\PharmaX\evidence\e2e-admin'
$brandFile = Join-Path $script:E2E 'brand.txt'
if (Test-Path $brandFile) { $env:PHX_BRAND = (Get-Content $brandFile -Raw).Trim() }
. 'C:\Users\mlilo\source\repos\PharmaX\docs\e2e-helpers.ps1'
$env:Database__SharedConnection = 'Server=127.0.0.1;Port=3306;Database=PharmaX_E2E_ADMIN;User ID=root;Password=root'
$env:Development__SeedTestUsers = 'true'
New-Item -ItemType Directory -Force (Join-Path $E2E 'receipts'), (Join-Path $E2E 'exports'), (Join-Path $E2E 'db') | Out-Null

$script:EXE = 'C:\Users\mlilo\source\repos\PharmaX\PharmaX.Desktop\bin\Release\net8.0-windows\PharmaX.Desktop.exe'
$script:MYSQL = 'C:\Program Files\MySQL\MySQL Server 8.0\bin\mysql.exe'
$script:ROLE = 'test.admin'

function SetBrand([string]$name) { Set-Content $brandFile $name; $env:PHX_BRAND = $name; [Phx]::Brand = $name }

# Starts the app on the run's database and remembers its process id.
function StartApp {
  $p = Start-Process -FilePath $EXE -WorkingDirectory (Split-Path $EXE) -PassThru
  Set-Content $PIDFILE $p.Id
  $deadline = (Get-Date).AddSeconds(90)
  while ((Get-Date) -lt $deadline) {
    if ([Phx]::Find($p.Id, 'Sign in') -ne [IntPtr]::Zero) { return $p.Id }
    Start-Sleep -Milliseconds 400
  }
  throw 'sign-in window never appeared'
}
function StopApp { $id = AppPid; Get-Process -Id $id -ErrorAction SilentlyContinue | Stop-Process -Force }

# The shell, whichever user is signed in: only it carries TEST DATA.
function Main { Win 'PharmaX  -  TEST DATA' 6000 }

# SQL against the run's database; the query and its output are kept as
# evidence under db\<name>.txt when a name is given.
function Sql([string]$query, [string]$name = '') {
  # Long options: PowerShell splits "-h127.0.0.1" at the first dot.
  $out = & $MYSQL '--host=127.0.0.1' '--user=root' '--password=root' '--database=PharmaX_E2E_ADMIN' "--execute=$query" 2>&1 |
    ForEach-Object { "$_" } | Where-Object { $_ -notmatch 'Using a password' }
  if ($name) { @("-- $query", $out) | Set-Content (Join-Path $E2E "db\$name.txt") }
  $out
}

# Records one step for the admin role.
function Step($case, $step, $expected, $actual, $verdict, $shot = '') { Rec $case $ROLE $step $expected $actual $verdict $shot }

# The newest message box since a snapshot: its text, then press a button.
function Box($pre, [string]$press = 'OK', [string]$shot = '', [int]$t = 6000) {
  $m = MsgBox $pre $t
  if (-not $m) { return $null }
  if ($shot) { ShotH $m.Hwnd $shot | Out-Null }
  $text = $m.Text
  if ($press) { Answer $m.Hwnd $press }
  return $text
}

# Every visible window of the app, one line each.
function W { Get-Windows (AppPid) | Where-Object { $_.IsReal -or $_.IsDialog } | ForEach-Object { "$($_.Hwnd) [$($_.Title)] $($_.Class) $($_.Width)x$($_.Height)" } }

# Closes every window except the shell.
function CloseAllButMain {
  $m = [Phx]::Find((AppPid), 'PharmaX  -  TEST DATA')
  foreach ($w in @(Get-Windows (AppPid) | Where-Object { ($_.IsReal -or $_.IsDialog) -and $_.Hwnd -ne $m.ToInt64() })) { Close-Hwnd $w.Hwnd 3000 | Out-Null }
}

# Chooses an entry in a combo box by its displayed name.
function PickCombo($root, [string]$id, [string]$name) {
  $cb = El $root $id; if (-not $cb) { throw "no combo $id" }
  try { $cb.GetCurrentPattern($EXPAND).Expand() } catch {}; Start-Sleep -Milliseconds 400
  $li = $cb.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)))
  if (-not $li) { try { $cb.GetCurrentPattern($EXPAND).Collapse() } catch {}; throw "no '$name' in $id" }
  $li.GetCurrentPattern($SELECT).Select()
  try { $cb.GetCurrentPattern($EXPAND).Collapse() } catch {}; Start-Sleep -Milliseconds 400
}
# Sets the text box inside a NumericUpDown.
function SetSpin($root, [string]$id, [string]$v) {
  $ed = (El $root $id).FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))
  $ed.GetCurrentPattern($VALUE).SetValue($v)
}
function Toggle($root, [string]$id) { (El $root $id).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 300 }
function Val($root, [string]$id) { try { (El $root $id).GetCurrentPattern($VALUE).Current.Value } catch { (El $root $id).Current.Name } }

# Waits until no window has this title, so a later menu click is not
# queued behind a screen that is still closing.
function WaitGone([string]$title, [int]$ms = 8000) {
  $deadline = (Get-Date).AddMilliseconds($ms)
  while ((Get-Date) -lt $deadline) {
    if ([Phx]::Find((AppPid), $title) -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 500; return $true }
    Start-Sleep -Milliseconds 250
  }
  return $false
}
# Closes a window by title and waits for it to go.
function Shut([string]$title) { CloseTitle $title; WaitGone $title | Out-Null }
# Runs whatever preset the Reports window is on; returns preset, message, provenance and rows.
function RunCurrent($shot) {
  $r = Win 'PharmaX - Reports' 8000
  $sel = @($r.FindAll([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty,[System.Windows.Automation.ControlType]::TreeItem))) | ? { try { $_.GetCurrentPattern($SELECT).Current.IsSelected } catch { $false } } | % { $_.Current.Name }) -join ','
  $pre = Snap; Btn $r 'btnPreview'; Start-Sleep 3; $m = Box $pre 'OK' '' 1500
  $prov = (El $r 'lblProvenance').Current.Name
  $rows = @((GridRows $r 'grdPreview') | ? { $_ -match '^Row' })
  if ($shot) { Shot 'PharmaX - Reports' $shot | Out-Null }
  [pscustomobject]@{ Preset = $sel; Message = $m; Provenance = $prov; Rows = $rows }
}

# Every entry a combo box offers.
function ComboList($root, [string]$id) {
  $cb = El $root $id; try { $cb.GetCurrentPattern($EXPAND).Expand() } catch {}; Start-Sleep -Milliseconds 400
  $names = @($cb.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem))) | ForEach-Object { $_.Current.Name })
  try { $cb.GetCurrentPattern($EXPAND).Collapse() } catch {}; Start-Sleep -Milliseconds 300
  return $names
}
# Column headers of a grid.
function Headers($root, [string]$id) { @((El $root $id).FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Header))) | ForEach-Object { $_.Current.Name }) -join ' | ' }
function Rows($root, [string]$id) { @((GridRows $root $id) | Where-Object { $_ -match '^Row' }) }

# Rows of a grid element (for grids with no AutomationId).
function RowsOf($grid) {
  @($grid.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -match '^Row \d+' } | ForEach-Object {
    $r = $_; $_.Current.Name + ': ' + ((@($r.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { try { $_.GetCurrentPattern($VALUE).Current.Value } catch { '' } })) -join ' | ') })
}
# Posted click on a row of a grid element.
function PickOf($grid, [int]$row) { Pick $grid $row }
# A button by caption, clicked with BM_CLICK.
function Press($root, [string]$name) { $b = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)), (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button))))); if (-not $b) { throw "no button $name" }; [PhxK.K]::PostMessage((Hwnd $b), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null; Start-Sleep -Milliseconds 1200 }
