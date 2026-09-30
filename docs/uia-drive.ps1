# PharmaX UI-Automation driver for the acceptance walkthrough.
#
#   . .\docs\uia-drive.ps1
#   $pid = Start-PharmaX
#   Sign-InAs $pid test.cashier
#   Open-Menu  $pid 'Inventory' 'Suppliers'
#   Save-Shot  $pid 'Suppliers' docs\screenshots\x.png
#
# Everything drives UI Automation patterns rather than simulated
# keystrokes, and captures with PrintWindow rather than copying the
# screen. Both choices are deliberate: the walkthrough must not front
# windows or steal the keyboard, because that fights with whoever is
# using the machine and captures whatever they switched to instead.

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing

Add-Type @"
using System;using System.Drawing;using System.Text;
using System.Runtime.InteropServices;
public class Phx {
  [DllImport("user32.dll")] public static extern bool EnumWindows(P f, IntPtr l);
  public delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr v);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);

  // A modal form blocks the whole UIA provider for the process, so a
  // close driven through UIA times out on exactly the windows that most
  // need closing. WM_CLOSE is Win32 and reaches the window's message
  // queue regardless, which is why closing never goes through UIA here.
  public static void Close(IntPtr h) { PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); }

  [StructLayout(LayoutKind.Sequential)]
  public struct RECT { public int Left, Top, Right, Bottom; }

  // PER_MONITOR_AWARE_V2, before anything asks for a rectangle. Without
  // it GetWindowRect returns virtualised coordinates while the window is
  // drawn in physical ones, and the capture is cropped.
  public static void Dpi() { try { SetProcessDpiAwarenessContext((IntPtr)(-4)); } catch {} }

  // The pharmacy's trading name, which the application puts in place of
  // "PharmaX" at the start of every window title. Callers keep writing
  // "PharmaX - Reports"; Find looks for "<Brand> - Reports". Null leaves
  // titles as written, for a database still trading as PharmaX.
  public static string Brand = null;

  // Exact title first, substring only as a fallback. Every child form is
  // called "PharmaX - something", so a substring search for "PharmaX"
  // matches whichever window Windows happens to enumerate first - which
  // silently returns a dialog when the caller asked for the main window.
  public static IntPtr Find(int pid, string title) {
    if (!String.IsNullOrEmpty(Brand) && title.StartsWith("PharmaX"))
      title = Brand + title.Substring("PharmaX".Length);
    IntPtr exact = IntPtr.Zero, partial = IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p != pid || !IsWindowVisible(h)) return true;
      var sb = new StringBuilder(512); GetWindowText(h, sb, 512);
      string t = sb.ToString();
      if (t == title) { exact = h; return false; }
      if (partial == IntPtr.Zero && t.Contains(title)) partial = h;
      return true;
    }, IntPtr.Zero);
    return exact != IntPtr.Zero ? exact : partial; }

  public static string[] Titles(int pid) {
    var list = new System.Collections.Generic.List<string>();
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p != pid || !IsWindowVisible(h)) return true;
      var sb = new StringBuilder(512); GetWindowText(h, sb, 512);
      if (sb.Length > 0) list.Add(sb.ToString());
      return true;
    }, IntPtr.Zero);
    return list.ToArray(); }

  // Handles rather than titles, because a message box raised by the
  // application carries the application's own caption. Diffing titles
  // therefore misses exactly the modal dialogs that block UIA, and the
  // run looks as though nothing opened while everything after it fails.
  public static string[] Handles(int pid) {
    var list = new System.Collections.Generic.List<string>();
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p != pid || !IsWindowVisible(h)) return true;
      var sb = new StringBuilder(512); GetWindowText(h, sb, 512);
      var cls = new StringBuilder(256); GetClassName(h, cls, 256);
      RECT r; GetWindowRect(h, out r);
      list.Add(h.ToInt64() + "" + sb.ToString() + "" +
               cls.ToString() + "" + (r.Right - r.Left) + "" +
               (r.Bottom - r.Top));
      return true;
    }, IntPtr.Zero);
    return list.ToArray(); }

  [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, P f, IntPtr l);

  // Every button and label on a dialog, as "Class|Text". Read through
  // Win32 because a modal dialog blocks UIA for the whole process.
  public static string[] Children(long hwnd) {
    var list = new System.Collections.Generic.List<string>();
    EnumChildWindows(new IntPtr(hwnd), delegate(IntPtr h, IntPtr l) {
      var cls = new StringBuilder(128); GetClassName(h, cls, 128);
      var sb = new StringBuilder(1024); GetWindowText(h, sb, 1024);
      if (sb.Length > 0) list.Add(cls.ToString() + "|" + sb.ToString());
      return true;
    }, IntPtr.Zero);
    return list.ToArray(); }

  // Presses a named button by posting BM_CLICK to it. A posted message
  // goes to that control's own queue, so unlike a simulated keystroke it
  // cannot land on whatever the person at the keyboard is doing.
  public static bool ClickChild(long hwnd, string text) {
    IntPtr found = IntPtr.Zero;
    string want = text.Replace("&", "").Trim().ToLowerInvariant();
    EnumChildWindows(new IntPtr(hwnd), delegate(IntPtr h, IntPtr l) {
      var cls = new StringBuilder(128); GetClassName(h, cls, 128);
      if (cls.ToString() != "Button") return true;
      var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
      if (sb.ToString().Replace("&", "").Trim().ToLowerInvariant() == want) {
        found = h; return false; }
      return true;
    }, IntPtr.Zero);
    if (found == IntPtr.Zero) return false;
    PostMessage(found, 0x00F5, IntPtr.Zero, IntPtr.Zero);   // BM_CLICK
    return true; }

  public static bool ShotHwnd(long hwnd, string path) {
    return Shot(new IntPtr(hwnd), path); }

  public static void CloseHwnd(long hwnd) { Close(new IntPtr(hwnd)); }
  public static bool Alive(long hwnd) { return IsWindow(new IntPtr(hwnd)) && IsWindowVisible(new IntPtr(hwnd)); }

  // Escalating ways to dismiss a window, all posted to its own message
  // queue rather than typed at the desktop, so none of them steals focus
  // from whoever is using the machine.
  //   1 WM_CLOSE       - the polite ask
  //   2 WM_SYSCOMMAND  - the title bar's own close
  //   3 VK_ESCAPE      - a dialog with a CancelButton answers this
  public static void SysClose(long hwnd) {
    PostMessage(new IntPtr(hwnd), 0x0112, new IntPtr(0xF060), IntPtr.Zero); }

  public static void Escape(long hwnd) {
    IntPtr h = new IntPtr(hwnd);
    PostMessage(h, 0x0100, new IntPtr(0x1B), IntPtr.Zero);   // WM_KEYDOWN
    PostMessage(h, 0x0101, new IntPtr(0x1B), IntPtr.Zero); } // WM_KEYUP

  [DllImport("user32.dll", SetLastError=true)]
  public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w,
      IntPtr l, uint flags, uint timeout, out IntPtr result);

  // PrintWindow sends WM_PRINT synchronously, so capturing a window whose
  // UI thread is busy blocks forever - there is no timeout on it. This
  // asks first, with SMTO_ABORTIFHUNG, and the caller skips the capture
  // rather than hanging the whole run on one unresponsive form.
  public static bool Responsive(long hwnd, uint ms) {
    IntPtr r;
    IntPtr ok = SendMessageTimeout(new IntPtr(hwnd), 0x0000, IntPtr.Zero,
                                   IntPtr.Zero, 0x0002, ms, out r);
    return ok != IntPtr.Zero; }

  public static bool Shot(IntPtr h, string path) {
    RECT r; if (!GetWindowRect(h, out r)) return false;
    int w = r.Right - r.Left, ht = r.Bottom - r.Top;
    if (w <= 0 || ht <= 0) return false;
    using (var bmp = new Bitmap(w, ht))
    using (var g = Graphics.FromImage(bmp)) {
      IntPtr dc = g.GetHdc();
      PrintWindow(h, dc, 2);          // PW_RENDERFULLCONTENT
      g.ReleaseHdc(dc);
      bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
    return true; }
}
"@ -ReferencedAssemblies System.Drawing, System.Drawing.Primitives -ErrorAction SilentlyContinue

[Phx]::Dpi()

# Set PHX_BRAND to the trading name saved at the System Console, so that
# window lookups written against "PharmaX - ..." find the branded titles.
if ($env:PHX_BRAND) { [Phx]::Brand = $env:PHX_BRAND }

# InvokePattern.Invoke() does not return until the click handler does, and
# a handler that calls ShowDialog() does not return until the operator
# closes the dialog. UIA therefore blocks for its full 60-second timeout
# on every modal screen, which is most of them.
#
# The call is run on an abandoned background thread instead: the click
# still happens, and the caller goes straight on to look for the window
# that appeared. This is the same trick FlaUI-MCP uses for the WinForms
# modal-dialog trap.
Add-Type -ReferencedAssemblies UIAutomationClient, UIAutomationTypes, System.Threading @"
using System;
using System.Threading;
using System.Windows.Automation;

public static class PhxUia
{
    private static void Fire(ThreadStart body)
    {
        var t = new Thread(body);
        t.IsBackground = true;                 // never holds the process open
        t.SetApartmentState(ApartmentState.MTA);
        t.Start();
    }

    public static void InvokeAsync(AutomationElement e)
    {
        Fire(delegate {
            try {
                ((InvokePattern)e.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            } catch { }
        });
    }

    public static void ExpandAsync(AutomationElement e)
    {
        Fire(delegate {
            try {
                ((ExpandCollapsePattern)e.GetCurrentPattern(
                    ExpandCollapsePattern.Pattern)).Expand();
            } catch { }
        });
    }
}
"@ -ErrorAction SilentlyContinue

$script:AE     = [System.Windows.Automation.AutomationElement]
$script:VALUE  = [System.Windows.Automation.ValuePattern]::Pattern
$script:INVOKE = [System.Windows.Automation.InvokePattern]::Pattern
$script:EXPAND = [System.Windows.Automation.ExpandCollapsePattern]::Pattern
$script:SELECT = [System.Windows.Automation.SelectionItemPattern]::Pattern

function Start-PharmaX {
    param([string] $Exe = 'C:\Users\mlilo\source\repos\PharmaX\PharmaX.Desktop\bin\Release\net8.0-windows\PharmaX.Desktop.exe')
    $p = Start-Process -FilePath $Exe -WorkingDirectory (Split-Path $Exe) -PassThru
    for ($i = 0; $i -lt 60; $i++) {
        if ([Phx]::Find($p.Id, 'Sign in') -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 250
    }
    return $p.Id
}

function Get-Window {
    param([int] $Pid_, [string] $Title, [int] $TimeoutMs = 10000)
    $hwnd = [IntPtr]::Zero
    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    while ((Get-Date) -lt $deadline -and $hwnd -eq [IntPtr]::Zero) {
        $hwnd = [Phx]::Find($Pid_, $Title)
        if ($hwnd -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 200 }
    }
    if ($hwnd -eq [IntPtr]::Zero) { return $null }
    # FromHandle itself blocks while a modal is up, so a failure here is
    # reported rather than thrown: the caller decides whether to wait or
    # to fall back to Win32.
    try { return $AE::FromHandle($hwnd) } catch { return $null }
}

function Find-Elem {
    param($Root, [string] $AutomationId, [int] $TimeoutMs = 6000)
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::AutomationIdProperty, $AutomationId)
    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    while ((Get-Date) -lt $deadline) {
        $e = $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($e) { return $e }
        Start-Sleep -Milliseconds 150
    }
    return $null
}

# Menu entries are looked up by name AND control type. The home screen
# carries tiles labelled "Sell", "Products", "Patients" and "Reports",
# and a name-only search finds the tile as readily as the menu entry -
# then invokes a panel, which does nothing, and the case is recorded as
# though the screen had refused to open.
function Find-MenuItem {
    param($Root, [string] $Name, [int] $TimeoutMs = 2500)
    if (-not $Root) { return $null }
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            $AE::NameProperty, $Name)),
        (New-Object System.Windows.Automation.PropertyCondition(
            $AE::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::MenuItem)))
    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    while ((Get-Date) -lt $deadline) {
        try {
            $e = $Root.FindFirst(
                [System.Windows.Automation.TreeScope]::Descendants, $cond)
            if ($e) { return $e }
        } catch { }
        Start-Sleep -Milliseconds 150
    }
    return $null
}

function Find-ByName {
    param($Root, [string] $Name, [string] $Type = $null, [int] $TimeoutMs = 3000)
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::NameProperty, $Name)
    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    while ((Get-Date) -lt $deadline) {
        $e = $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($e) { return $e }
        Start-Sleep -Milliseconds 150
    }
    return $null
}

function Sign-InAs {
    param([int] $Pid_, [string] $User, [string] $Password = 'Test#1234')
    $win = Get-Window $Pid_ 'Sign in' 20000
    if (-not $win) { throw "no sign-in window for $User" }

    $u = Find-Elem $win 'txtUsername' 5000
    $p = Find-Elem $win 'txtPassword' 5000
    $b = Find-Elem $win 'btnLogin' 5000
    if (-not $u -or -not $p -or -not $b) {
        throw "sign-in window found but its controls were not (UIA blocked?)"
    }
    $u.GetCurrentPattern($VALUE).SetValue($User)
    $p.GetCurrentPattern($VALUE).SetValue($Password)

    # Fired and abandoned: sign-in closes this window and opens the main
    # one, so a synchronous Invoke waits on a window that is going away.
    [PhxUia]::InvokeAsync($b)

    $deadline = (Get-Date).AddSeconds(25)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 300
        $main = [Phx]::Find($Pid_, 'PharmaX')
        if ($main -ne [IntPtr]::Zero) {
            $t = @(Get-Windows $Pid_ | Where-Object { $_.Hwnd -eq $main.ToInt64() })
            if ($t.Count -gt 0 -and $t[0].Title -notmatch 'Sign in') {
                Start-Sleep -Milliseconds 800
                return (Get-Window $Pid_ 'PharmaX' 8000)
            }
        }
    }
    throw "signed in as $User but the main window never appeared"
}

# Returns to the sign-in screen and confirms it is there. Logging off is
# the one step the whole run depends on: a role that cannot be left
# silently attributes the next role's evidence to the wrong user.
# The buttons on a dialog, e.g. @('Yes','No') or @('OK').
function Get-DialogButtons {
    param([int64] $Hwnd)
    try {
        return @([Phx]::Children($Hwnd) |
                 Where-Object { $_ -like 'Button|*' } |
                 ForEach-Object { ($_ -split '\|', 2)[1].Replace('&', '').Trim() })
    } catch { return @() }
}

function Get-DialogMessage {
    param([int64] $Hwnd)
    try {
        return (@([Phx]::Children($Hwnd) |
                  Where-Object { $_ -like 'Static|*' } |
                  ForEach-Object { ($_ -split '\|', 2)[1] }) -join ' ').Trim()
    } catch { return '' }
}

function Click-DialogButton {
    param([int64] $Hwnd, [string[]] $Prefer = @('Yes', 'OK'))
    foreach ($b in $Prefer) {
        if ([Phx]::ClickChild($Hwnd, $b)) { return $b }
    }
    return $null
}

# Returns to the sign-in screen and confirms it is there. Logging off is
# the one step the whole run depends on: a role that cannot be left
# silently attributes the next role's evidence to the wrong user.
#
# LogOff() asks "Sign <user> out?" first, and a WM_CLOSE on that question
# answers No. The Yes button is therefore pressed explicitly.
function Log-Off {
    param([int] $Pid_, [int] $TimeoutMs = 20000)
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $known = @(Get-Windows $Pid_ | ForEach-Object { $_.Hwnd })
        try { Open-Menu $Pid_ 'File' 'Log Off' 1200 } catch { }

        # Answer the confirmation if it appeared.
        $deadline = (Get-Date).AddSeconds(5)
        while ((Get-Date) -lt $deadline) {
            $dlg = @(Get-Windows $Pid_ |
                     Where-Object { $known -notcontains $_.Hwnd -and $_.IsDialog })
            if ($dlg.Count -gt 0) {
                Click-DialogButton $dlg[-1].Hwnd @('Yes', 'OK') | Out-Null
                break
            }
            Start-Sleep -Milliseconds 200
        }

        $deadline = (Get-Date).AddMilliseconds($TimeoutMs / 3)
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 250
            if ([Phx]::Find($Pid_, 'Sign in') -ne [IntPtr]::Zero) { return $true }
        }

        # Still here: something else is in the way. Clear every window
        # that is not the main one and try again.
        foreach ($w in (Get-Windows $Pid_ | Where-Object { $_.Title -ne 'PharmaX' })) {
            Close-Hwnd $w.Hwnd 2000 | Out-Null
        }
    }
    return ([Phx]::Find($Pid_, 'Sign in') -ne [IntPtr]::Zero)
}

# Expands a top-level menu and invokes a child by name. Expand and Invoke
# are UIA patterns, so no keystroke reaches the desktop and the window is
# never fronted.
function Open-Menu {
    param([int] $Pid_, [string] $TopMenu, [string] $Item, [int] $SettleMs = 1500)
    $main = Get-Window $Pid_ 'PharmaX' 4000
    if (-not $main) { throw 'no main window (UIA blocked - modal still open?)' }

    $topEl = Find-MenuItem $main $TopMenu 3000
    if (-not $topEl) { throw "no menu '$TopMenu'" }

    # A WinForms ToolStrip dropdown runs its own modal message loop while
    # open, so Expand() blocks for the same reason Invoke() does.
    $preDrop = @(Get-Windows $Pid_ | ForEach-Object { $_.Hwnd })
    [PhxUia]::ExpandAsync($topEl)

    # The dropdown is its own top-level window, not a child of the main
    # form, and the item must be found inside it. Searching the main form
    # instead finds the home-screen tile of the same name - "Sell",
    # "Products", "Patients" - and invoking a tile does nothing at all.
    $drop = $null
    $deadline = (Get-Date).AddSeconds(3)
    while ((Get-Date) -lt $deadline -and -not $drop) {
        Start-Sleep -Milliseconds 150
        $drop = @(Get-Windows $Pid_ |
                  Where-Object { $preDrop -notcontains $_.Hwnd -and
                                 $_.Class -like 'WindowsForms10.Window*' }) |
                Select-Object -Last 1
    }

    $childEl = $null
    if ($drop) {
        $dropEl = $AE::FromHandle([IntPtr] $drop.Hwnd)
        $childEl = Find-MenuItem $dropEl $Item 2000
    }
    if (-not $childEl) {
        # No dropdown window seen; fall back to the main form. Still a
        # MenuItem, so a tile cannot stand in for the menu entry.
        $childEl = Find-MenuItem $main $Item 1500
    }
    if (-not $childEl) {
        throw "no menu item '$Item' under '$TopMenu'"
    }
    # A top-level entry and its child can share a name ("Reports" sits
    # under "Reports"). Invoking the parent only reopens the dropdown.
    if ($drop -and $childEl -and $topEl) {
        try {
            if ($childEl.Current.AutomationId -eq $topEl.Current.AutomationId -and
                $childEl.Current.Name -eq $topEl.Current.Name -and
                $childEl.Current.BoundingRectangle.Equals(
                    $topEl.Current.BoundingRectangle)) {
                throw "menu item '$Item' resolved to its own parent"
            }
        } catch [System.Management.Automation.RuntimeException] { throw }
          catch { }
    }
    # Fired and abandoned - see PhxUia. Success is then judged by a window
    # appearing, not by the call returning, because on a modal screen it
    # never will.
    $known = @(Get-Windows $Pid_ | ForEach-Object { $_.Hwnd })
    [PhxUia]::InvokeAsync($childEl)

    # Only a real screen counts. WinForms raises invisible helper windows
    # first, and returning on one of those hands the caller a phantom to
    # photograph while the actual form is still being built.
    $deadline = (Get-Date).AddMilliseconds($SettleMs + 6000)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 200
        $now = @(Get-Windows $Pid_ |
                 Where-Object { $known -notcontains $_.Hwnd -and $_.IsReal })
        if ($now.Count -gt 0) {
            Start-Sleep -Milliseconds 700      # let it finish painting
            return
        }
    }
    # Nothing opened. That is a legitimate outcome (an inert entry, or a
    # command that acts without a screen) and the caller records it.
}

function Save-Shot {
    param([int] $Pid_, [string] $Title, [string] $Out, [int] $TimeoutMs = 8000)
    $dir = Split-Path $Out
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
    $hwnd = [IntPtr]::Zero
    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    while ((Get-Date) -lt $deadline -and $hwnd -eq [IntPtr]::Zero) {
        $hwnd = [Phx]::Find($Pid_, $Title)
        if ($hwnd -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 200 }
    }
    if ($hwnd -eq [IntPtr]::Zero) { return $false }
    Start-Sleep -Milliseconds 350
    return [Phx]::Shot($hwnd, $Out)
}

function Get-Titles { param([int] $Pid_) return [Phx]::Titles($Pid_) }

# Every visible window of the process as {Hwnd, Title, Class}. Identity is
# the handle, so a dialog sharing the main window's caption is still seen.
function Get-Windows {
    param([int] $Pid_)
    $out = @()
    # [char]1 rather than a `u escape: Windows PowerShell 5.1 has no `u.
    $sep = [char]1
    foreach ($s in [Phx]::Handles($Pid_)) {
        $parts = $s.Split($sep)
        $w = [int] $parts[3]; $h = [int] $parts[4]
        $out += [pscustomobject]@{
            Hwnd  = [int64] $parts[0]
            Title = $parts[1]
            Class = $parts[2]
            Width = $w
            Height = $h
            # WinForms message boxes and common dialogs report class #32770.
            IsDialog = ($parts[2] -eq '#32770')
            # WinForms creates invisible helper windows - shadow, layered
            # and parking windows - which are visible to EnumWindows but
            # are not screens. They have no caption and almost no size, and
            # capturing one yields a small black rectangle.
            IsReal = ($parts[1].Length -gt 0 -and $w -ge 200 -and $h -ge 120)
        }
    }
    return $out
}

function Test-HwndResponsive {
    param([int64] $Hwnd, [int] $TimeoutMs = 4000)
    try { return [Phx]::Responsive($Hwnd, [uint32] $TimeoutMs) } catch { return $false }
}

function Save-ShotHwnd {
    param([int64] $Hwnd, [string] $Out, [int] $SettleMs = 4000)
    $dir = Split-Path $Out
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }

    # Give a form that is still loading a chance to finish, then refuse to
    # capture one that is not answering: PrintWindow cannot be timed out.
    $deadline = (Get-Date).AddMilliseconds($SettleMs)
    while ((Get-Date) -lt $deadline) {
        if (Test-HwndResponsive $Hwnd 1000) { break }
        Start-Sleep -Milliseconds 200
    }
    if (-not (Test-HwndResponsive $Hwnd 1500)) { return $false }

    Start-Sleep -Milliseconds 300
    try { return [Phx]::ShotHwnd($Hwnd, $Out) } catch { return $false }
}

function Close-Hwnd {
    param([int64] $Hwnd, [int] $TimeoutMs = 4500)
    $steps = @(
        { [Phx]::CloseHwnd($Hwnd) },   # WM_CLOSE
        { [Phx]::SysClose($Hwnd) },    # WM_SYSCOMMAND / SC_CLOSE
        { [Phx]::Escape($Hwnd) }       # VK_ESCAPE, for a dialog with CancelButton
    )
    foreach ($step in $steps) {
        if (-not [Phx]::Alive($Hwnd)) { return $true }
        & $step
        $deadline = (Get-Date).AddMilliseconds($TimeoutMs / $steps.Count)
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 120
            if (-not [Phx]::Alive($Hwnd)) { return $true }
        }
    }
    return (-not [Phx]::Alive($Hwnd))
}

# Reads the text out of a message box without UIA, which is blocked while
# the box is up. The static labels are child windows; their text is the
# message the operator was shown.
function Get-DialogText {
    param([int64] $Hwnd)
    Add-Type -Namespace PhxDlg -Name W -MemberDefinition @"
[DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, Proc f, IntPtr l);
public delegate bool Proc(IntPtr h, IntPtr l);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, System.Text.StringBuilder s, int n);
"@ -ErrorAction SilentlyContinue
    $texts = New-Object System.Collections.ArrayList
    $cb = [PhxDlg.W+Proc] {
        param($h, $l)
        $cls = New-Object System.Text.StringBuilder 256
        [PhxDlg.W]::GetClassNameW($h, $cls, 256) | Out-Null
        $sb = New-Object System.Text.StringBuilder 1024
        [PhxDlg.W]::GetWindowTextW($h, $sb, 1024) | Out-Null
        if ($sb.Length -gt 0) { [void] $texts.Add("$($cls.ToString())|$($sb.ToString())") }
        return $true
    }
    try { [PhxDlg.W]::EnumChildWindows([IntPtr]$Hwnd, $cb, [IntPtr]::Zero) | Out-Null } catch {}
    return $texts
}

# Closes a child window with WM_CLOSE and waits for it to go. Purely
# Win32: a modal child blocks UIA for the whole process, so a UIA-driven
# close would time out on exactly the windows that most need closing.
function Close-Window {
    param([int] $Pid_, [string] $Title, [int] $TimeoutMs = 6000)
    $gone = $false
    for ($attempt = 0; $attempt -lt 3 -and -not $gone; $attempt++) {
        $hwnd = [Phx]::Find($Pid_, $Title)
        if ($hwnd -eq [IntPtr]::Zero) { return $true }
        [Phx]::Close($hwnd)
        $deadline = (Get-Date).AddMilliseconds($TimeoutMs / 3)
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 150
            if ([Phx]::Find($Pid_, $Title) -eq [IntPtr]::Zero) { $gone = $true; break }
        }
    }
    return $gone
}

# True once the process answers UIA again, i.e. no modal is blocking it.
function Wait-Responsive {
    param([int] $Pid_, [int] $TimeoutMs = 8000)
    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    while ((Get-Date) -lt $deadline) {
        $w = Get-Window $Pid_ 'PharmaX' 500
        if ($w) { try { $null = $w.Current.Name; return $true } catch {} }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

# Every AutomationId on a window, for the testability evidence.
function Get-AutomationIds {
    param([int] $Pid_, [string] $Title)
    $win = Get-Window $Pid_ $Title 5000
    if (-not $win) { return @() }
    $all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    $out = @()
    foreach ($e in $all) {
        $out += [pscustomobject]@{
            AutomationId = $e.Current.AutomationId
            Name         = $e.Current.Name
            ControlType  = $e.Current.ControlType.ProgrammaticName -replace 'ControlType\.',''
            Enabled      = $e.Current.IsEnabled
        }
    }
    return $out
}
