function AnswerChain($answers, $shotPrefix) {
  $log = @(); $i = 0
  for ($k = 0; $k -lt 8; $k++) {
    Start-Sleep 1.2
    $n = @(Get-Windows (AppPid) | ? { $_.Title -notmatch 'TEST DATA|Chart of accounts|Journal|Trial|Day-end' -and ($_.IsReal -or $_.IsDialog) })[-1]
    if (-not $n) { break }
    if ($n.IsDialog) { $log += "MSG: " + (Get-DialogMessage $n.Hwnd); ShotH $n.Hwnd "$shotPrefix-msg$k" | Out-Null; Answer $n.Hwnd $(if ((Get-DialogButtons $n.Hwnd) -contains 'Yes') {'Yes'} else {'OK'}); continue }
    $w = $AE::FromHandle([IntPtr]$n.Hwnd)
    $lbl = try { (El $w 'lblPrompt' 800).Current.Name } catch { '' }
    $list = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty,[System.Windows.Automation.ControlType]::List)))
    if ($i -ge $answers.Count) { $log += "EXTRA PROMPT [$($n.Title)] $lbl - cancelled"; Close-Hwnd $n.Hwnd 2000 | Out-Null; break }
    $ans = $answers[$i]; $i++
    if ($list) { $opts = @($list.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition) | % { $_.Current.Name }); $log += "LIST [$($n.Title)] options: $($opts -join ', ') -> $ans"; ($list.FindFirst([System.Windows.Automation.TreeScope]::Children,(New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty,$ans)))).GetCurrentPattern($SELECT).Select(); Press $w 'OK' }
    else { $log += "PROMPT [$($n.Title)] $lbl -> $ans"; SetV $w 'txtPromptInput' $ans; Btn $w 'btnOk' }
  }
  return $log
}
