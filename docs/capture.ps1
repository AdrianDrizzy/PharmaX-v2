# Captures one PharmaX window to a PNG.
#
#   .\docs\capture.ps1 -Title 'PharmaX - Sign in' -Out docs\screenshots\x.png
#
# Two things this has to get right, both learned the hard way.
#
# The process must declare itself per-monitor DPI aware before it asks
# Windows anything. Without it, GetWindowRect comes back in virtualised
# coordinates while the window is drawn in physical ones, and the capture
# is cropped to roughly the top-left two thirds of the picture.
#
# And it captures with PrintWindow rather than copying the screen. Copying
# the screen means fronting the window first, which fights with whoever is
# using the machine - and captures whatever they switched to instead.
# PrintWindow asks the window to draw itself, so it works while it is
# behind something else and interrupts nobody.

param(
    [Parameter(Mandatory)] [string] $Title,
    [Parameter(Mandatory)] [string] $Out
)

Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Drawing;
using System.Text;
using System.Runtime.InteropServices;

public class Cap {
    [DllImport("user32.dll")] public static extern bool EnumWindows(Proc f, IntPtr l);
    public delegate bool Proc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr value);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    // PER_MONITOR_AWARE_V2. Must run before anything asks for a rectangle.
    public static void BecomeDpiAware() {
        try { SetProcessDpiAwarenessContext((IntPtr)(-4)); } catch { }
    }

    public static IntPtr Find(string title) {
        IntPtr found = IntPtr.Zero;

        EnumWindows(delegate(IntPtr h, IntPtr l) {
            if (!IsWindowVisible(h)) return true;

            var sb = new StringBuilder(512);
            GetWindowText(h, sb, 512);

            if (sb.ToString().Contains(title)) { found = h; return false; }
            return true;
        }, IntPtr.Zero);

        return found;
    }

    public static string TitleOf(IntPtr h) {
        var sb = new StringBuilder(512);
        GetWindowText(h, sb, 512);
        return sb.ToString();
    }

    public static string Save(IntPtr h, string path) {
        RECT r;
        GetWindowRect(h, out r);

        int w = r.Right - r.Left, ht = r.Bottom - r.Top;
        if (w <= 0 || ht <= 0) return "zero size";

        using (var bmp = new Bitmap(w, ht))
        {
            using (var g = Graphics.FromImage(bmp)) {
                IntPtr dc = g.GetHdc();
                PrintWindow(h, dc, 2);   // PW_RENDERFULLCONTENT
                g.ReleaseHdc(dc);
            }

            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            return w + "x" + ht;
        }
    }
}
"@ -ReferencedAssemblies System.Drawing

[Cap]::BecomeDpiAware()

$hwnd = [Cap]::Find($Title)

if ($hwnd -eq [IntPtr]::Zero) {
    Write-Error "No visible window with a title containing '$Title'."
    exit 1
}

$dir = Split-Path -Parent $Out
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory $dir | Out-Null }

$size = [Cap]::Save($hwnd, (Join-Path (Get-Location) $Out))

# The matched title is reported, not just the one asked for: a substring
# match can find the wrong window, and a wrong screenshot in a document
# is worse than a missing one.
Write-Output ("{0}  [{1}]  {2}" -f $Out, [Cap]::TitleOf($hwnd), $size)
