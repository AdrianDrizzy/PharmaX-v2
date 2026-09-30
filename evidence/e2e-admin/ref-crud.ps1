# Full create / validate / duplicate / search / edit / retire / bring back
# round on one reference list (frmReferenceList + frmReferenceEdit).
# Dot-source after adm.ps1.
function RefCrud {
  param([string]$Case, [string]$Top, [string]$Item, [string]$Plural, [string]$Singular,
        [string]$Code, [string]$Name, [string]$Detail, [string]$Search, [string]$Renamed)

  $listTitle = "PharmaX - $Plural"
  PMenu $Top $Item
  $l = Win $listTitle 8000
  if (-not $l) { throw "no $listTitle" }
  $rows = GridRows $l 'grdItems'
  $first = ($rows | Where-Object { $_ -match '^Row 0:' }) -replace '^Row 0: ', ''
  $existing = ($first -split ' \| ')[0]
  Shot $listTitle "$Case-01_list" | Out-Null
  Step $Case "$Item > open the list" 'Every entry listed with code, name and whether it is in use' "$(@($rows | ? { $_ -match '^Row' }).Count) rows; first: $first" 'PASS' "$Case-01_list"

  # blank name
  Btn $l 'btnAdd'; Start-Sleep 1
  $e = Win "PharmaX - New $Singular" 6000
  SetV $e 'txtCode' $Code; SetV $e 'txtName' ''
  $pre = Snap; Btn $e 'btnSave'; $t = Box $pre 'OK' "$Case-02_blank-name"
  Step $Case "Add with the name blank" 'Refused' "Message: `"$t`"" $(if ($t) {'PASS'} else {'FAIL'}) "$Case-02_blank-name"

  # duplicate code
  SetV $e 'txtCode' $existing; SetV $e 'txtName' 'Duplicate test'
  $pre = Snap; Btn $e 'btnSave'; $t = Box $pre 'OK' "$Case-03_duplicate"
  Step $Case "Add with the existing code $existing" 'Refused, naming the entry that holds the code' "Message: `"$t`"" $(if ($t -match 'already') {'PASS'} else {'FAIL'}) "$Case-03_duplicate"

  # create
  SetV $e 'txtCode' $Code; SetV $e 'txtName' $Name
  $det = El $e 'txtDetail' 800
  if ($det -and $Detail) { SetV $e 'txtDetail' $Detail }
  Shot "PharmaX - New $Singular" "$Case-04_new" | Out-Null
  $pre = Snap; Btn $e 'btnSave'; Start-Sleep 1; $t = Box $pre 'OK' '' 1500
  $gone = ([Phx]::Find((AppPid), "PharmaX - New $Singular") -eq [IntPtr]::Zero)
  $row = (GridRows $l 'grdItems') | Where-Object { $_ -match [regex]::Escape($Code) }
  Step $Case "Add $Code `"$Name`"" 'Saved and listed as in use' "Form closed=$gone; message=[$t]; list: $row" $(if ($gone -and $row) {'PASS'} else {'FAIL'}) "$Case-04_new"

  # search
  SetV $l 'txtSearch' $Search; Start-Sleep 1.5
  $found = @((GridRows $l 'grdItems') | Where-Object { $_ -match '^Row' })
  Shot $listTitle "$Case-05_search" | Out-Null
  Step $Case "Search `"$Search`"" 'Only matching entries' "$($found.Count) row(s): $($found -join ' / ')" $(if ($found.Count -ge 1 -and ($found -join '') -match [regex]::Escape($Code)) {'PASS'} else {'FAIL'}) "$Case-05_search"

  # edit
  $idx = [int](([regex]"Row (\d+): $([regex]::Escape($Code)) ").Match((GridRows $l 'grdItems') -join "`n").Groups[1].Value)
  PickRow $l 'grdItems' $idx
  Btn $l 'btnEdit'; Start-Sleep 1
  $e = Win "PharmaX - Edit $Singular" 6000
  SetV $e 'txtName' $Renamed
  $pre = Snap; Btn $e 'btnSave'; Start-Sleep 1; Box $pre 'OK' '' 1500 | Out-Null
  Btn $l 'btnRefresh'; Start-Sleep 1.2
  $after = (GridRows $l 'grdItems') | Where-Object { $_ -match [regex]::Escape($Code) }
  Shot $listTitle "$Case-06_edited" | Out-Null
  $ok = $after -match [regex]::Escape($Renamed)
  Step $Case "Edit $Code, rename to `"$Renamed`", Save, Refresh" 'List shows the new name' "List after Refresh: $after$(if (-not $ok) {' - stale, ADM-DEF-01'})" $(if ($ok) {'PASS'} else {'FAIL'}) "$Case-06_edited"

  # The list holds stale copies after an edit (ADM-DEF-01), and retiring
  # from it then fails on the row version, so it is reopened first.
  Btn $l 'btnClose'; WaitGone $listTitle
  PMenu $Top $Item
  $l = Win $listTitle 8000
  $after = (GridRows $l 'grdItems') | Where-Object { $_ -match [regex]::Escape($Code) }
  Step $Case "Close and reopen $Item" 'Edited name listed' "List: $after" $(if ($after -match [regex]::Escape($Renamed)) {'PASS'} else {'FAIL'}) ''

  # retire
  $idx = [int](([regex]"Row (\d+): $([regex]::Escape($Code)) ").Match((GridRows $l 'grdItems') -join "`n").Groups[1].Value)
  PickRow $l 'grdItems' $idx
  $pre = Snap; Btn $l 'btnToggle'; $t = Box $pre 'Yes' '' 10000; Start-Sleep 2
  $row = (GridRows $l 'grdItems') | Where-Object { $_ -match [regex]::Escape($Code) }
  Step $Case "Retire $Code" 'Asks first; kept but not in use' "Confirm: `"$t`" -> Yes; list: $row" $(if ($t -and $row -match '\| No$') {'PASS'} else {'FAIL'}) "$Case-07_retired"
  Shot $listTitle "$Case-07_retired" | Out-Null

  # bring back
  $idx = [int](([regex]"Row (\d+): $([regex]::Escape($Code)) ").Match((GridRows $l 'grdItems') -join "`n").Groups[1].Value)
  PickRow $l 'grdItems' $idx
  $pre = Snap; Btn $l 'btnToggle'; $t = Box $pre 'Yes' '' 10000; Start-Sleep 2
  $row = (GridRows $l 'grdItems') | Where-Object { $_ -match [regex]::Escape($Code) }
  Step $Case "Bring back $Code" 'Asks first; in use again' "Confirm: `"$t`" -> Yes; list: $row" $(if ($t -and $row -match '\| Yes$') {'PASS'} else {'FAIL'}) ''

  Btn $l 'btnClose'; WaitGone $listTitle
}

# Waits until no window has this title, so a later menu click is not
# queued behind a screen that is still closing.
function WaitGone([string]$title, [int]$ms = 8000) {
  $deadline = (Get-Date).AddMilliseconds($ms)
  while ((Get-Date) -lt $deadline) {
    if ([Phx]::Find((AppPid), $title) -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 400; return $true }
    Start-Sleep -Milliseconds 250
  }
  return $false
}
