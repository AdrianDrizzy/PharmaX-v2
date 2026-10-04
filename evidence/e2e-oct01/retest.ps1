# Re-test of the P1 and P2 fixes, 1 October 2026, on the run's own
# database PharmaX_E2E_OCT01 - seeded before the fixes, so the upgrade of an
# existing database is part of what is tested. Dot-source at every step:
#   . C:\Users\mlilo\source\repos\PharmaX\evidence\e2e-oct01\retest.ps1
. 'C:\Users\mlilo\source\repos\PharmaX\evidence\e2e-oct01\run.ps1'
$script:RESULTS = Join-Path $E2E 'retest.csv'
$script:SHOTS = Join-Path $E2E 'retest-screenshots'
New-Item -ItemType Directory -Force $SHOTS | Out-Null
