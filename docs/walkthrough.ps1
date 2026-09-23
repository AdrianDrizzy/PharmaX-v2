# Drives PharmaX through every menu entry as every role, capturing a
# screenshot of each screen that opens and recording what happened.
#
#   powershell -File docs\walkthrough.ps1 -Out evidence\walkthrough
#
# The outcome of each attempt is itself the evidence: a screen that opens
# is a capability the role has, and one that is absent from the menu is a
# permission the role is denied. Both are recorded rather than only the
# successes.

param(
    [string] $Out = 'C:\Users\mlilo\source\repos\PharmaX\evidence\walkthrough',
    [string[]] $Roles = @('test.admin','test.manager','test.pharmacist',
                          'test.assistant','test.cashier','test.stock')
)

$ErrorActionPreference = 'Continue'
. "$PSScriptRoot\uia-drive.ps1"

$shots = Join-Path $Out 'screenshots'
New-Item -ItemType Directory -Force $shots | Out-Null
$manifest = Join-Path $Out 'manifest.csv'

# menu, item, slug — the whole reachable surface from frmMain.
$CASES = @(
  @('File','Till Session','till-session'),
  @('File','Printer Setup','printer-setup'),
  @('File','Change Password','change-password'),

  @('Dispensary','Dispensing','dispensing'),
  @('Dispensary','Patients','patients'),
  @('Dispensary','Script Maintenance','script-maintenance'),
  @('Dispensary','Labels','labels'),
  @('Dispensary','Tariffs','tariffs'),
  @('Dispensary','Merge Patient Files','merge-patients'),
  @('Dispensary','Quotation','quotation'),
  @('Dispensary','Medical Aid Societies','schemes'),

  @('Point of Sale','Sell','pos-sell'),
  @('Point of Sale','Scripts to Collect','collect'),
  @('Point of Sale','Returns and Credit Notes','returns'),

  @('Inventory','Stock Alerts','stock-alerts'),
  @('Inventory','Departments','departments'),
  @('Inventory','Suppliers','suppliers'),
  @('Inventory','Purchase Orders','purchase-orders'),
  @('Inventory','Goods Receipt','goods-receipt'),
  @('Inventory','Manufacturers','manufacturers'),
  @('Inventory','Products','products'),
  @('Inventory','Price Changes','price-changes'),

  @('Accounts','Trial Balance','trial-balance'),
  @('Accounts','Journal Entry','journal'),
  @('Accounts','Patient Accounts','patient-accounts'),
  @('Accounts','Medical Aid Claims','claims'),
  @('Accounts','Chart of Accounts','chart-of-accounts'),

  @('Reports','Reports','reports'),

  @('Stocktake','New Stocktake','stocktake-new'),
  @('Stocktake','Capture','stocktake-capture'),

  @('Administration','VAT Codes','vat-codes'),
  @('Administration','Users','users'),
  @('Administration','Branches','branches'),
  @('Administration','Transaction Types','transaction-types'),
  @('Administration','Day-End Posting','day-end'),
  @('Administration','System Setup','system-setup'),
  @('Administration','System Console','system-console'),
  @('Administration','Control Variables','control-variables'),

  @('Help','About','about')
)

$rows = New-Object System.Collections.Generic.List[object]

function Record {
    param($Role, $Menu, $Item, $Slug, $Outcome, $Window, $File, $Note)
    $rows.Add([pscustomobject]@{
        role = $Role; menu = $Menu; item = $Item; slug = $Slug
        outcome = $Outcome; window = $Window; file = $File; note = $Note
        captured_utc = (Get-Date).ToUniversalTime().ToString('s')
    })
    $rows | Export-Csv -Path $manifest -NoTypeInformation -Encoding UTF8
    Write-Host ("  [{0,-8}] {1,-22} {2}" -f $Outcome, $Slug, $Window)
}

Write-Host "=== PharmaX walkthrough ==="
$appId = Start-PharmaX
Write-Host "launched pid=$appId"

foreach ($role in $Roles) {
    $short = $role -replace '^test\.',''
    Write-Host "`n--- $role ---"

    $signedIn = $false
    for ($try = 1; $try -le 2 -and -not $signedIn; $try++) {
        try { Sign-InAs $appId $role | Out-Null; $signedIn = $true }
        catch {
            Write-Host "  sign-in attempt $try failed: $_"
            if ($try -eq 1) {
                try { Stop-Process -Id $appId -Force -ErrorAction SilentlyContinue } catch {}
                Start-Sleep -Seconds 2
                $appId = Start-PharmaX
                Write-Host "  relaunched pid=$appId"
            }
        }
    }
    if (-not $signedIn) {
        Record $role 'File' 'Sign in' 'signin' 'ERROR' '' '' 'could not sign in after a restart'
        continue
    }

    $homeFile = Join-Path $shots "$short`_00-home.png"
    if (Save-Shot $appId 'PharmaX' $homeFile) {
        Record $role 'File' 'Home' 'home' 'OPENED' 'PharmaX' (Split-Path $homeFile -Leaf) 'role home screen'
    } else {
        Record $role 'File' 'Home' 'home' 'NOSHOT' 'PharmaX' '' 'capture failed'
    }

    foreach ($c in $CASES) {
        $menu, $item, $slug = $c
        $before = @(Get-Windows $appId | ForEach-Object { $_.Hwnd })

        try { Open-Menu $appId $menu $item 1800 }
        catch {
            $msg = "$_"
            if ($msg -match "no menu item") {
                Record $role $menu $item $slug 'DENIED' '' '' 'menu entry not present for this role'
            } else {
                Record $role $menu $item $slug 'ERROR' '' '' $msg
            }
            continue
        }

        $all = @(Get-Windows $appId | Where-Object { $before -notcontains $_.Hwnd })
        $new = @($all | Where-Object { $_.IsReal })

        if ($new.Count -eq 0) {
            # Everything that appeared was a WinForms helper window, so
            # nothing the operator would call a screen actually opened.
            foreach ($w in $all) { Close-Hwnd $w.Hwnd 1500 | Out-Null }
        }

        if ($new.Count -eq 0) {
            Record $role $menu $item $slug 'NOWINDOW' '' '' 'invoked but no new window appeared'
            continue
        }

        # A message box is a refusal, a warning or a question - not the
        # feature. Its text is the evidence, so it is read and recorded
        # rather than merely dismissed.
        $dialog = $new | Where-Object { $_.IsDialog } | Select-Object -Last 1
        $form   = $new | Where-Object { -not $_.IsDialog } | Select-Object -Last 1
        $target = if ($form) { $form } else { $dialog }

        $file = "$short`_$slug.png"
        $path = Join-Path $shots $file
        $note = ''
        $outcome = 'OPENED'

        if ($dialog) {
            $txt = Get-DialogMessage $dialog.Hwnd
            $btns = Get-DialogButtons $dialog.Hwnd
            $note = "message box [$($btns -join '/')]: $txt"
            if (-not $form) {
                # OK on its own is the application telling the operator
                # something - a refusal, or a note. Yes/No is a question,
                # and the walkthrough must never answer yes to one: it is
                # here to look, not to commit.
                $outcome = if ($btns -contains 'Yes' -or $btns -contains 'Cancel') {
                    'ASKED'
                } else { 'REFUSED' }
            }
        }

        if (-not (Save-ShotHwnd $target.Hwnd $path)) {
            $outcome = 'NOSHOT'
            $note = (($note, 'capture failed') | Where-Object { $_ }) -join '; '
            $file = ''
        }

        # Dismiss a question with No or Cancel, never Yes: nothing in this
        # run may commit a change. An OK-only box is simply acknowledged.
        if ($dialog) {
            $answered = Click-DialogButton $dialog.Hwnd @('No', 'Cancel', 'OK')
            if ($answered) {
                $note = "$note; dismissed with $answered"
                Start-Sleep -Milliseconds 400
            }
        }

        # Newest first, so a dialog owned by a form goes before the form.
        foreach ($w in ($new | Sort-Object Hwnd -Descending)) {
            Close-Hwnd $w.Hwnd | Out-Null
        }
        $still = @(Get-Windows $appId | Where-Object { $before -notcontains $_.Hwnd })
        foreach ($w in $still) { Close-Hwnd $w.Hwnd 4000 | Out-Null }

        if (-not (Wait-Responsive $appId 8000)) {
            $note = (($note, 'UIA still blocked after close') | Where-Object { $_ }) -join '; '
            $outcome = 'MODAL-STUCK'
        }
        Record $role $menu $item $slug $outcome $target.Title $file $note
    }

    # Back to the sign-in window for the next role. If this cannot be
    # done the application is restarted rather than carrying on, because
    # the alternative is attributing one role's evidence to another.
    if (-not (Log-Off $appId)) {
        Write-Host "  log off failed - restarting the application"
        try { Stop-Process -Id $appId -Force -ErrorAction SilentlyContinue } catch {}
        Start-Sleep -Seconds 2
        $appId = Start-PharmaX
        Write-Host "  relaunched pid=$appId"
    }
    Start-Sleep -Milliseconds 800
}

Write-Host "`n=== done: $($rows.Count) rows -> $manifest ==="
try { Stop-Process -Id $appId -Force -ErrorAction SilentlyContinue } catch {}
