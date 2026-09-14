# Signs in to a running PharmaX through UI Automation.
#
#   .\docs\signin.ps1 -Pid 1234 -User test.cashier
#
# Used by the walkthrough to move between roles without a human at the
# keyboard. Everything here goes through UIA patterns rather than
# simulated keystrokes, so it works while the window is behind something
# else and does not fight with whoever is using the machine.

param(
    [Parameter(Mandatory)] [int] $ProcessId,
    [Parameter(Mandatory)] [string] $User,
    [string] $Password = 'Test#1234'
)

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

Add-Type @"
using System;using System.Text;using System.Runtime.InteropServices;
public class SignIn {
 [DllImport("user32.dll")] public static extern bool EnumWindows(P f, IntPtr l);
 public delegate bool P(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out uint p);
 [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);

 public static IntPtr Find(int pid, string title) {
   IntPtr found = IntPtr.Zero;
   EnumWindows(delegate(IntPtr h, IntPtr l) {
     uint p; GetWindowThreadProcessId(h, out p);
     if (p != pid || !IsWindowVisible(h)) return true;
     var sb = new StringBuilder(512); GetWindowText(h, sb, 512);
     if (sb.ToString().Contains(title)) { found = h; return false; }
     return true;
   }, IntPtr.Zero);
   return found; }
}
"@

function Element($root, $automationId) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $automationId)

    return $root.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants, $cond)
}

# The sign-in window can take a moment to appear after a log off.
$hwnd = [IntPtr]::Zero

for ($i = 0; $i -lt 40 -and $hwnd -eq [IntPtr]::Zero; $i++) {
    $hwnd = [SignIn]::Find($ProcessId, 'Sign in')
    if ($hwnd -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 250 }
}

if ($hwnd -eq [IntPtr]::Zero) { Write-Error "No sign-in window."; exit 1 }

$win = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

$value = [System.Windows.Automation.ValuePattern]::Pattern

(Element $win 'txtUsername').GetCurrentPattern($value).SetValue($User)
(Element $win 'txtPassword').GetCurrentPattern($value).SetValue($Password)

(Element $win 'btnLogin').GetCurrentPattern(
    [System.Windows.Automation.InvokePattern]::Pattern).Invoke()

Start-Sleep -Seconds 3
Write-Output "signed in as $User"
