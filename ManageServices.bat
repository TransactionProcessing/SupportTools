
@echo off
setlocal

if /I "%~1"=="start" goto run
if /I "%~1"=="stop" goto run
if /I "%~1"=="restart" goto run

echo Usage: %~nx0 start^|stop^|restart
exit /b 1

:run
echo Managing Transaction Processing services...
echo Action: %~1
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
  "$services = @(Get-Service -DisplayName 'Transaction Processing -*' | Sort-Object DisplayName);" ^
  "if ($services.Count -eq 0) { Write-Error 'No matching services found'; exit 1 };" ^
  "foreach ($svc in $services) {" ^
  "  try {" ^
  "    Write-Host ('%~1 : ' + $svc.DisplayName);" ^
  "    switch ('%~1') {" ^
  "      'start'   { if ($svc.Status -ne 'Running') { Start-Service -Name $svc.Name -ErrorAction Stop } }" ^
  "      'stop'    { if ($svc.Status -ne 'Stopped') { Stop-Service -Name $svc.Name -ErrorAction Stop } }" ^
  "      'restart' { Restart-Service -Name $svc.Name -ErrorAction Stop }" ^
  "    }" ^
  "  } catch { Write-Warning $_.Exception.Message; $failed = $true }" ^
  "};" ^
  "if ($failed) { exit 1 }"

exit /b %errorlevel%
