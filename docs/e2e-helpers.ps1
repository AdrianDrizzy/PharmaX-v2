# Helpers for the end-to-end run: posted-message clicks, menu hover,
# prompt answering, Print to PDF, grid editing and result recording.
# Dot-source after setting $E2E to the evidence folder, e.g.
#   $E2E = "...\evidence\e2e-retest"; . docs\e2e-helpers.ps1
$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot 'uia-drive.ps1')
$env:Database__SharedConnection = 'Server=127.0.0.1;Port=3306;Database=PharmaX_E2E;User ID=root;Password=root'
if (-not $script:E2E) { $script:E2E = Join-Path (Split-Path $PSScriptRoot) 'evidence\e2e-retest' }
$script:SHOTS = Join-Path $E2E 'screenshots'
New-Item -ItemType Directory -Force $SHOTS | Out-Null
$script:PIDFILE = Join-Path $script:E2E 'pid.txt'
function AppPid { [int](Get-Content $PIDFILE) }
function Win($title,$t=8000){ Get-Window (AppPid) $title $t }
function El($root,$id,$t=4000){ Find-Elem $root $id $t }
function SetV($root,$id,$v){ $e = El $root $id; if(-not $e){ throw "no element $id" }; $e.GetCurrentPattern($VALUE).SetValue([string]$v) }
function Click($root,$id){ $e = El $root $id; if(-not $e){ throw "no element $id" }; [PhxUia]::InvokeAsync($e); Start-Sleep -Milliseconds 900 }
function Shot($title,$name){ $p = Join-Path $SHOTS "$name.png"; if (Save-Shot (AppPid) $title $p) { "shot $name" } else { "NOSHOT $name" } }
function Wins { Get-Windows (AppPid) | ? IsReal | ft Hwnd,Title,Class,Width,Height -auto | Out-String }
function Ids($title){ Get-AutomationIds (AppPid) $title | ? { $_.AutomationId } | ft AutomationId,Name,ControlType,Enabled -auto | Out-String -Width 200 }

Add-Type -Namespace PhxK -Name K -MemberDefinition @"
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
[DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
"@ -ErrorAction SilentlyContinue

function Hwnd($el){ [IntPtr]$el.Current.NativeWindowHandle }
# Types characters into a control through its own message queue (WM_CHAR),
# which is what a keyboard wedge scanner looks like to the ScanBar.
function Chars($el, [string]$text, [switch]$Enter){
  $h = Hwnd $el
  foreach($c in $text.ToCharArray()){ [PhxK.K]::PostMessage($h, 0x0102, [IntPtr][int]$c, [IntPtr]1) | Out-Null; Start-Sleep -Milliseconds 15 }
  if($Enter){ [PhxK.K]::PostMessage($h, 0x0102, [IntPtr]13, [IntPtr]1) | Out-Null }
  Start-Sleep -Milliseconds 700
}
function Key($el, [int]$vk){ $h = Hwnd $el; [PhxK.K]::PostMessage($h,0x0100,[IntPtr]$vk,[IntPtr]1)|Out-Null; [PhxK.K]::PostMessage($h,0x0101,[IntPtr]$vk,[IntPtr]0xC0000001)|Out-Null; Start-Sleep -Milliseconds 600 }
function ByName($root,[string]$name,$t=4000){ Find-ByName $root $name $null $t }
function ClickName($root,[string]$name){ $e = ByName $root $name; if(-not $e){ throw "no element named $name" }; [PhxUia]::InvokeAsync($e); Start-Sleep -Milliseconds 900 }
function Combo($root,$id,[string]$item){
  $c = El $root $id; if(-not $c){ throw "no combo $id" }
  try { $c.GetCurrentPattern($EXPAND).Expand() } catch {}
  Start-Sleep -Milliseconds 300
  $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $item)
  $li = $c.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
  if(-not $li){ try { $c.GetCurrentPattern($EXPAND).Collapse() } catch {}; throw "no item '$item' in $id" }
  $li.GetCurrentPattern($SELECT).Select()
  try { $c.GetCurrentPattern($EXPAND).Collapse() } catch {}
  Start-Sleep -Milliseconds 400
}
function ComboItems($root,$id){ $c = El $root $id; $c.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem))) | % { $_.Current.Name } }
# Grid: rows are children of the DataGridView; cells are children of rows.
function GridRows($root,$id){
  $g = El $root $id; if(-not $g){ throw "no grid $id" }
  $rows = $g.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
  $out=@(); foreach($r in $rows){ $n=$r.Current.Name; if($n -match '^Row \d+' -or $n -match 'Row'){ $cells = $r.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition); $vals = @($cells | % { try { $_.GetCurrentPattern($VALUE).Current.Value } catch { $_.Current.Name } }); $out += ,@($n,($vals -join ' | ')) } }
  $out | % { "$($_[0]): $($_[1])" }
}
function GridRow($root,$id,[int]$index){
  $g = El $root $id
  $rows = @($g.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition) | ? { $_.Current.Name -match '^Row ' })
  return $rows[$index]
}
function SelectRow($root,$id,[int]$index){
  $r = GridRow $root $id $index
  try { $r.GetCurrentPattern($SELECT).Select() } catch {
    $cell = $r.FindFirst([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition)
    try { $cell.GetCurrentPattern([System.Windows.Automation.LegacyIAccessiblePattern]::Pattern).DoDefaultAction() } catch { $cell.SetFocus() }
  }
  Start-Sleep -Milliseconds 500
}
function SetCell($root,$id,[int]$row,[string]$col,[string]$val){
  $r = GridRow $root $id $row
  $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, "$col Row $row")
  $cell = $r.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
  if(-not $cell){ $names = ($r.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition) | % { $_.Current.Name }) -join ', '; throw "no cell '$col' in row $row ($names)" }
  $cell.GetCurrentPattern($VALUE).SetValue($val); Start-Sleep -Milliseconds 300
}
# New dialogs (message boxes) since a snapshot of handles.
function Snap { @(Get-Windows (AppPid) | % { $_.Hwnd }) }
function NewWins($before){ @(Get-Windows (AppPid) | ? { $before -notcontains $_.Hwnd -and ($_.IsReal -or $_.IsDialog) }) }
function MsgBox($before,$t=6000){
  $deadline=(Get-Date).AddMilliseconds($t)
  while((Get-Date) -lt $deadline){ $d = @(NewWins $before | ? IsDialog); if($d.Count){ $m=$d[-1]; return [pscustomobject]@{Hwnd=$m.Hwnd; Text=(Get-DialogMessage $m.Hwnd); Buttons=(Get-DialogButtons $m.Hwnd)} }; Start-Sleep -Milliseconds 200 }
  return $null
}
function ShotH($hwnd,$name){ $p = Join-Path $SHOTS "$name.png"; if(Save-ShotHwnd $hwnd $p){ "shot $name" } else { "NOSHOT $name" } }
function Answer($hwnd,$btn){ Click-DialogButton $hwnd @($btn) | Out-Null; Start-Sleep -Milliseconds 700 }
function Tree($el,[int]$depth=3,[string]$indent=''){
  if($depth -lt 0){return}
  $kids = $el.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition)
  foreach($k in $kids){ "$indent$($k.Current.ControlType.ProgrammaticName -replace 'ControlType\.','') id=[$($k.Current.AutomationId)] name=[$($k.Current.Name)]"; Tree $k ($depth-1) "$indent  " }
}
function SignIn($user,$pw='Test#1234'){ Sign-InAs (AppPid) $user $pw | Out-Null; "signed in $user" }
function Menu($top,$item){ Open-Menu (AppPid) $top $item 1500 }
function CloseTitle($title){ Close-Window (AppPid) $title | Out-Null }
$script:RESULTS = Join-Path $script:E2E 'results.csv'
function Rec($case,$role,$step,$expected,$actual,$verdict,$shot=''){
  [pscustomobject]@{case=$case;role=$role;step=$step;expected=$expected;actual=$actual;verdict=$verdict;shot=$shot;at=(Get-Date -Format s)} | Export-Csv -Append -NoTypeInformation -Encoding UTF8 $RESULTS
  "[$verdict] $case $step"
}

Add-Type -Namespace PhxM -Name M -MemberDefinition @"
[StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
[DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr h, ref POINT p);
[DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
public static void Click(IntPtr h, int sx, int sy) {
  POINT p; p.X = sx; p.Y = sy; ScreenToClient(h, ref p);
  IntPtr l = (IntPtr)((p.Y << 16) | (p.X & 0xFFFF));
  PostMessage(h, 0x0200, IntPtr.Zero, l);
  PostMessage(h, 0x0201, (IntPtr)1, l);
  PostMessage(h, 0x0202, IntPtr.Zero, l);
}
"@ -ErrorAction SilentlyContinue

# Nearest ancestor (or self) that owns a window handle.
function OwnerHwnd($el){
  $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
  $e = $el
  while($e -and $e.Current.NativeWindowHandle -eq 0){ $e = $walker.GetParent($e) }
  if($e){ [IntPtr]$e.Current.NativeWindowHandle } else { [IntPtr]::Zero }
}
# Clicks an element with posted mouse messages at its centre: the click is
# handled by the application's own message loop, so a modal it opens does
# not leave UI Automation blocked inside a pending Invoke.
function PClick($el){
  $r = $el.Current.BoundingRectangle
  $x = [int]($r.X + $r.Width/2); $y = [int]($r.Y + $r.Height/2)
  $h = OwnerHwnd $el
  [PhxM.M]::Click($h, $x, $y); Start-Sleep -Milliseconds 900
}
function PMenu($top,$item){
  $main = Win 'PharmaX  -  TEST DATA'
  $t = Find-MenuItem $main $top 3000; if(-not $t){ throw "no menu $top" }
  $pre = Snap
  PClick $t
  $drop=$null; $deadline=(Get-Date).AddSeconds(3)
  while(-not $drop -and (Get-Date) -lt $deadline){ Start-Sleep -Milliseconds 150; $drop = @(Get-Windows (AppPid) | ? { $pre -notcontains $_.Hwnd -and $_.Class -like 'WindowsForms10.Window*' }) | Select -Last 1 }
  if(-not $drop){ throw "no dropdown for $top" }
  $de = $AE::FromHandle([IntPtr]$drop.Hwnd)
  $i = Find-MenuItem $de $item 2000; if(-not $i){ Key $main 27; throw "no item $item under $top" }
  $r = $i.Current.BoundingRectangle
  [PhxM.M]::Click([IntPtr]$drop.Hwnd, [int]($r.X+$r.Width/2), [int]($r.Y+$r.Height/2))
  Start-Sleep -Milliseconds 2000
}
# Buttons: BM_CLICK posted to the button's own handle.
function Btn($root,$id){ $e = El $root $id; if(-not $e){ throw "no button $id" }; [PhxK.K]::PostMessage((Hwnd $e),0x00F5,[IntPtr]::Zero,[IntPtr]::Zero)|Out-Null; Start-Sleep -Milliseconds 1200 }
function BtnName($root,$name){ $e = ByName $root $name; if(-not $e){ throw "no button named $name" }; $h = Hwnd $e; if($h -ne [IntPtr]::Zero){ [PhxK.K]::PostMessage($h,0x00F5,[IntPtr]::Zero,[IntPtr]::Zero)|Out-Null } else { PClick $e }; Start-Sleep -Milliseconds 1200 }

Add-Type -Namespace PhxM2 -Name M -MemberDefinition @"
[StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
[DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr h, ref POINT p);
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
public static void Move(IntPtr h, int sx, int sy) {
  POINT p; p.X = sx; p.Y = sy; ScreenToClient(h, ref p);
  PostMessage(h, 0x0200, IntPtr.Zero, (IntPtr)((p.Y << 16) | (p.X & 0xFFFF)));
}
"@ -ErrorAction SilentlyContinue
function PHover($el){ $r=$el.Current.BoundingRectangle; [PhxM2.M]::Move((OwnerHwnd $el), [int]($r.X+$r.Width/2), [int]($r.Y+$r.Height/2)) }
function PMenu($top,$item){
  $main = Win 'PharmaX  -  TEST DATA'
  $t = Find-MenuItem $main $top 3000; if(-not $t){ throw "no menu $top" }
  $pre = Snap
  PHover $t
  $drop=$null; $deadline=(Get-Date).AddSeconds(3)
  while(-not $drop -and (Get-Date) -lt $deadline){ Start-Sleep -Milliseconds 150; $drop = @(Get-Windows (AppPid) | ? { $pre -notcontains $_.Hwnd -and $_.Class -like 'WindowsForms10.Window*' }) | Select -Last 1 }
  if(-not $drop){ throw "no dropdown for $top" }
  $de = $AE::FromHandle([IntPtr]$drop.Hwnd)
  $i = Find-MenuItem $de $item 2000; if(-not $i){ throw "no item $item under $top" }
  if(-not $i.Current.IsEnabled){ [Phx]::Escape($drop.Hwnd); throw "item $item under $top is disabled" }
  $r = $i.Current.BoundingRectangle
  [PhxM.M]::Click([IntPtr]$drop.Hwnd, [int]($r.X+$r.Width/2), [int]($r.Y+$r.Height/2))
  Start-Sleep -Milliseconds 2000
}

# Answers one of the application's own prompt forms (frmTextPrompt,
# frmNumberPrompt): fills its edit box and presses OK.
function Prompt([string]$title,[string]$val,[string]$shot=''){
  $h = [Phx]::Find((AppPid), $title); $deadline=(Get-Date).AddSeconds(6)
  while($h -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline){ Start-Sleep -Milliseconds 200; $h = [Phx]::Find((AppPid), $title) }
  if($h -eq [IntPtr]::Zero){ throw "no prompt '$title'" }
  $p = $AE::FromHandle($h)
  $ed = $p.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty,[System.Windows.Automation.ControlType]::Edit)))
  if($ed){ $ed.GetCurrentPattern($VALUE).SetValue($val) }
  if($shot){ ShotH ([int64]$h) $shot | Out-Null }
  $ok = $p.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty,'OK')))
  [PhxK.K]::PostMessage((Hwnd $ok),0x00F5,[IntPtr]::Zero,[IntPtr]::Zero)|Out-Null; Start-Sleep -Milliseconds 1200
}


# "Microsoft Print to PDF" asks where to save. The answer is always the
# evidence folder, so a receipt or label becomes a file the report can cite.
function SavePrint([string]$name,[int]$t=8000){
  $deadline=(Get-Date).AddMilliseconds($t)
  while((Get-Date) -lt $deadline){
    $d = @(Get-Windows (AppPid) | ? { $_.IsDialog -and $_.Title -eq 'Save Print Output As' })
    if($d.Count){
      $h=[IntPtr]$d[-1].Hwnd; $dl=$AE::FromHandle($h)
      $cb = Find-Elem $dl '1001' 4000; $ed = if($cb.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit){ $cb } else { $cb.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty,[System.Windows.Automation.ControlType]::Edit))) }
      $path = Join-Path "$E2E\receipts" "$name.pdf"; if(Test-Path $path){ Remove-Item $path }
      $ed.GetCurrentPattern($VALUE).SetValue($path)
      Start-Sleep -Milliseconds 300
      [Phx]::ClickChild($d[-1].Hwnd,'Save') | Out-Null
      Start-Sleep -Seconds 3
      return (Test-Path $path)
    }
    Start-Sleep -Milliseconds 300
  }
  return $false
}



Add-Type @"
using System;using System.Text;using System.Runtime.InteropServices;
public static class PhxSave {
  delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, P f, IntPtr l);
  [DllImport("user32.dll")] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, string l);
  static string Cls(IntPtr h){ var sb=new StringBuilder(128); GetClassName(h,sb,128); return sb.ToString(); }
  public static bool SetFileName(long dlg, string path){
    IntPtr found = IntPtr.Zero;
    EnumChildWindows(new IntPtr(dlg), delegate(IntPtr h, IntPtr l){
      if(Cls(h)=="Edit"){ var p=GetParent(h); if(Cls(p)=="ComboBox" && Cls(GetParent(p))=="ComboBoxEx32"){ found=h; return false; } }
      return true; }, IntPtr.Zero);
    if(found==IntPtr.Zero) return false;
    SendMessage(found, 0x000C, IntPtr.Zero, path); return true; }
}
"@ -ErrorAction SilentlyContinue
function SavePrint([string]$name,[int]$t=8000){
  $deadline=(Get-Date).AddMilliseconds($t)
  while((Get-Date) -lt $deadline){
    $d = @(Get-Windows (AppPid) | ? { $_.IsDialog -and $_.Title -eq 'Save Print Output As' })
    if($d.Count){
      $path = Join-Path "$E2E\receipts" "$name.pdf"; if(Test-Path $path){ Remove-Item $path }
      if(-not [PhxSave]::SetFileName($d[-1].Hwnd, $path)){ return $false }
      Start-Sleep -Milliseconds 300
      [Phx]::ClickChild($d[-1].Hwnd,'Save') | Out-Null
      $dl=(Get-Date).AddSeconds(8); while(-not (Test-Path $path) -and (Get-Date) -lt $dl){ Start-Sleep -Milliseconds 300 }
      Start-Sleep 1
      return (Test-Path $path)
    }
    Start-Sleep -Milliseconds 300
  }
  return $false
}

# Makes a grid row current the way an operator does: a click on one of
# its cells, posted to the grid's own window.
function Pick($grid,[int]$row){
  $r = @($grid.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition) | ? { $_.Current.Name -match "^Row $row(,|$)" })[0]
  if(-not $r){ throw "no row $row" }
  $cell = $r.FindFirst([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition)
  PClick $cell
}
function GridOf($root){ $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty,[System.Windows.Automation.ControlType]::DataGrid))) }
function PickRow($root,$id,[int]$row){ Pick (El $root $id) $row }
function SetCell($root,$id,[int]$row,[string]$col,[string]$val){
  $g = El $root $id
  $r = @($g.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition) | ? { $_.Current.Name -match "^Row $row(,|$)" })[0]
  if(-not $r){ throw "no row $row in $id" }
  $cell = @($r.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition) | ? { $_.Current.Name -like "$col Row $row*" })[0]
  if(-not $cell){ throw "no cell '$col' in row $row" }
  $cell.GetCurrentPattern($VALUE).SetValue($val); Start-Sleep -Milliseconds 400
}
function ClickBtnIn($root,[string]$name){ $b = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.AndCondition((New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty,$name)),(New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty,[System.Windows.Automation.ControlType]::Button))))); if(-not $b){ throw "no button $name" }; [PhxK.K]::PostMessage((Hwnd $b),0x00F5,[IntPtr]::Zero,[IntPtr]::Zero)|Out-Null; Start-Sleep -Milliseconds 1200 }
function Dialogs($pre,$shot=''){ foreach($x in (NewWins $pre)){ if($x.IsDialog){ $t = Get-DialogMessage $x.Hwnd; "MSG: $t [" + ((Get-DialogButtons $x.Hwnd) -join '/') + "]"; if($shot){ ShotH $x.Hwnd $shot | Out-Null } } else { "WIN: $($x.Title)" } } }
function Ok($pre){ foreach($x in (NewWins $pre | ? IsDialog)){ Click-DialogButton $x.Hwnd @('OK') | Out-Null; Start-Sleep -Milliseconds 700 } }

Add-Type @"
using System;using System.Text;using System.Runtime.InteropServices;
public static class PhxSave2 {
  delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, P f, IntPtr l);
  [DllImport("user32.dll")] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
  [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, string l);
  static string Cls(IntPtr h){ var sb=new StringBuilder(128); GetClassName(h,sb,128); return sb.ToString(); }
  public static string SetFileName(long dlg, string path){
    IntPtr found = IntPtr.Zero; var seen = new StringBuilder();
    EnumChildWindows(new IntPtr(dlg), delegate(IntPtr h, IntPtr l){
      if(Cls(h)=="Edit"){ var p=GetParent(h); seen.Append(Cls(p)+"/"+GetDlgCtrlID(p)+";"); if(Cls(p)=="ComboBox" && GetDlgCtrlID(p)==1148 || found==IntPtr.Zero && Cls(p)=="ComboBox"){ found=h; } }
      return true; }, IntPtr.Zero);
    if(found==IntPtr.Zero) return "none:"+seen;
    SendMessage(found, 0x000C, IntPtr.Zero, path); return "ok:"+seen; }
}
"@ -ErrorAction SilentlyContinue
function SavePrint([string]$name,[int]$t=15000){
  $deadline=(Get-Date).AddMilliseconds($t)
  while((Get-Date) -lt $deadline){
    $d = @(Get-Windows (AppPid) | ? { $_.IsDialog -and $_.Title -eq 'Save Print Output As' })
    if($d.Count){
      $path = Join-Path "$E2E\receipts" "$name.pdf"; if(Test-Path $path){ Remove-Item $path }
      $r = [PhxSave2]::SetFileName($d[-1].Hwnd, $path)
      if($r -notlike 'ok:*'){ Write-Host "filename edit not found: $r"; return $false }
      Start-Sleep -Milliseconds 400
      [Phx]::ClickChild($d[-1].Hwnd,'Save') | Out-Null
      $dl=(Get-Date).AddSeconds(10); while(-not (Test-Path $path) -and (Get-Date) -lt $dl){ Start-Sleep -Milliseconds 300 }
      Start-Sleep 1
      return (Test-Path $path)
    }
    Start-Sleep -Milliseconds 300
  }
  return $false
}

function Drain([int]$quietTicks=4){ $count=0; $quiet=0; while($quiet -lt $quietTicks -and $count -lt 80){ $d = @(Get-Windows (AppPid) | ? { $_.IsDialog -and $_.Title -like 'DataGridView*' }); if($d.Count){ $quiet=0; foreach($x in $d){ Click-DialogButton $x.Hwnd @('OK') | Out-Null; $count++ } } else { $quiet++ }; Start-Sleep -Milliseconds 400 }; return $count }

Add-Type @"
using System;using System.Text;using System.Runtime.InteropServices;
public static class PhxEd {
  delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, P f, IntPtr l);
  [DllImport("user32.dll")] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  public static long EditorOf(long grid){
    IntPtr found = IntPtr.Zero;
    EnumChildWindows(new IntPtr(grid), delegate(IntPtr h, IntPtr l){
      var sb=new StringBuilder(128); GetClassName(h,sb,128);
      var c = sb.ToString().ToUpperInvariant();
      if((c.Contains("EDIT") || c.Contains("COMBOBOX")) && IsWindowVisible(h)){ found=h; return false; }
      return true; }, IntPtr.Zero);
    return found.ToInt64(); }
}
"@ -ErrorAction SilentlyContinue
function CellEl($grid,[int]$row,[string]$col){
  $r = @($grid.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition) | ? { $_.Current.Name -match "^Row $row(,|$)" })[0]
  if(-not $r){ throw "no row $row" }
  $c = @($r.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition) | ? { $_.Current.Name -like "$col Row $row*" })[0]
  if(-not $c){ throw "no cell $col in row $row" }; return $c
}
function PostKey([IntPtr]$h,[int]$vk){ [PhxK.K]::PostMessage($h,0x0100,[IntPtr]$vk,[IntPtr]1)|Out-Null; [PhxK.K]::PostMessage($h,0x0101,[IntPtr]$vk,[IntPtr]0xC0000001)|Out-Null }
# Operator-style cell edit: click, F2, clear, type, Enter.
function TypeCell($root,$id,[int]$row,[string]$col,[string]$text){
  $g = El $root $id; $gh = Hwnd $g
  PClick (CellEl $g $row $col); Start-Sleep -Milliseconds 300
  PostKey $gh 0x71; Start-Sleep -Milliseconds 500   # F2
  $ed = [PhxEd]::EditorOf([int64]$gh)
  if($ed -eq 0){ throw "no editing control on $id" }
  $eh = [IntPtr]$ed
  [PhxK.K]::SendMessage($eh,0x00B1,[IntPtr]0,[IntPtr](-1)) | Out-Null  # EM_SETSEL all
  [PhxK.K]::PostMessage($eh,0x0102,[IntPtr]8,[IntPtr]1) | Out-Null     # backspace
  Start-Sleep -Milliseconds 100
  foreach($c in $text.ToCharArray()){ [PhxK.K]::PostMessage($eh,0x0102,[IntPtr][int]$c,[IntPtr]1)|Out-Null; Start-Sleep -Milliseconds 20 }
  Start-Sleep -Milliseconds 200
  PostKey $eh 0x0D; Start-Sleep -Milliseconds 800  # Enter commits
}

function Tab($root,[string]$name){ $t = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.AndCondition((New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty,$name)),(New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty,[System.Windows.Automation.ControlType]::TabItem))))); if(-not $t){ throw "no tab $name" }; $t.GetCurrentPattern($SELECT).Select(); Start-Sleep -Milliseconds 700 }

Add-Type -Namespace PhxT -Name T -MemberDefinition @"
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, string l);
"@ -ErrorAction SilentlyContinue
function TypeCell($root,$id,[int]$row,[string]$col,[string]$text){
  $g = El $root $id; $gh = Hwnd $g
  PClick (CellEl $g $row $col); Start-Sleep -Milliseconds 300
  PostKey $gh 0x71; Start-Sleep -Milliseconds 500   # F2 begins the edit
  $ed = [PhxEd]::EditorOf([int64]$gh)
  if($ed -eq 0){ throw "no editing control on $id" }
  $eh = [IntPtr]$ed
  [PhxT.T]::SendMessage($eh,0x000C,[IntPtr]::Zero,$text) | Out-Null   # WM_SETTEXT raises TextChanged, so the grid sees the edit
  Start-Sleep -Milliseconds 200
  PostKey $eh 0x0D; Start-Sleep -Milliseconds 800  # Enter commits
}

function PMenu($top,$item){
  $main = Win 'PharmaX  -  TEST DATA'
  $t = Find-MenuItem $main $top 3000; if(-not $t){ throw "no menu $top" }
  PHover $t
  $i=$null; $drop=$null; $deadline=(Get-Date).AddSeconds(4)
  while(-not $i -and (Get-Date) -lt $deadline){
    Start-Sleep -Milliseconds 200
    foreach($w in @(Get-Windows (AppPid) | ? { $_.Class -like 'WindowsForms10.Window*' -and $_.Title -eq '' -and $_.Width -gt 60 })){
      $de = $AE::FromHandle([IntPtr]$w.Hwnd); $c = Find-MenuItem $de $item 300; if($c){ $i=$c; $drop=$w; break } }
  }
  if(-not $i){ throw "no item $item under $top" }
  if(-not $i.Current.IsEnabled){ [Phx]::Escape($drop.Hwnd); throw "item $item under $top is disabled" }
  $r = $i.Current.BoundingRectangle
  [PhxM.M]::Click([IntPtr]$drop.Hwnd, [int]($r.X+$r.Width/2), [int]($r.Y+$r.Height/2))
  Start-Sleep -Milliseconds 2000
}

function El($root,$id,$t=4000){ if(-not $root){ return $null }; Find-Elem $root $id $t }

function SaveAs([string]$path,[int]$t=10000){
  $deadline=(Get-Date).AddMilliseconds($t)
  while((Get-Date) -lt $deadline){
    $d = @(Get-Windows (AppPid) | ? { $_.IsDialog -and ([Phx]::Children($_.Hwnd) -match 'Button\|&Save') })
    if($d.Count){
      if(Test-Path $path){ Remove-Item $path }
      $r = [PhxSave2]::SetFileName($d[-1].Hwnd, $path); if($r -notlike 'ok:*'){ Write-Host "no filename edit: $r"; return $false }
      Start-Sleep -Milliseconds 400; [Phx]::ClickChild($d[-1].Hwnd,'Save') | Out-Null
      $dl=(Get-Date).AddSeconds(10); while(-not (Test-Path $path) -and (Get-Date) -lt $dl){ Start-Sleep -Milliseconds 300 }
      Start-Sleep 1; return (Test-Path $path)
    }
    Start-Sleep -Milliseconds 300
  }
  return $false
}

Add-Type -Namespace PhxR -Name R -MemberDefinition @"
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
"@ -ErrorAction SilentlyContinue
# A real click, for the one grid that will only edit with genuine focus.
function RealClick($el,[IntPtr]$top){
  [PhxR.R]::SetForegroundWindow($top) | Out-Null; [PhxR.R]::BringWindowToTop($top) | Out-Null; Start-Sleep -Milliseconds 300
  $r=$el.Current.BoundingRectangle; [PhxR.R]::SetCursorPos([int]($r.X+$r.Width/2),[int]($r.Y+$r.Height/2)) | Out-Null
  [PhxR.R]::mouse_event(2,0,0,0,[IntPtr]::Zero); [PhxR.R]::mouse_event(4,0,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 600
}

function RunReport([string]$name,[string]$shot){
  $r = Win 'PharmaX - Reports'
  $ti = $r.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.AndCondition((New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty,$name)),(New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty,[System.Windows.Automation.ControlType]::TreeItem)))))
  if(-not $ti){ throw "no report $name" }
  $ti.GetCurrentPattern($SELECT).Select(); Start-Sleep 0.8
  Btn $r 'btnPreview'; Start-Sleep 3
  foreach($x in (Get-Windows (AppPid) | ? IsDialog)){ "MSG " + (Get-DialogMessage $x.Hwnd) }
  GridRows $r 'grdPreview' | select -First 14
  Shot 'PharmaX - Reports' $shot | Out-Null
}

Add-Type @"
using System;using System.Text;using System.Runtime.InteropServices;
public static class PhxCls {
  delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, P f, IntPtr l);
  [DllImport("user32.dll")] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  public static long First(long parent, string part){ IntPtr f=IntPtr.Zero; EnumChildWindows(new IntPtr(parent), delegate(IntPtr h, IntPtr l){ var sb=new StringBuilder(128); GetClassName(h,sb,128); if(sb.ToString().Contains(part)){ f=h; return false; } return true; }, IntPtr.Zero); return f.ToInt64(); }
}
"@ -ErrorAction SilentlyContinue
