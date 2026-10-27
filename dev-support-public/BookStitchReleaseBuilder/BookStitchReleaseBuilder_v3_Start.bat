@echo off
setlocal
set "HERE=%~dp0"
where pwsh.exe >nul 2>nul
if not errorlevel 1 (
  pwsh.exe -STA -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%HERE%BookStitchReleaseBuilder_v3.ps1"
) else (
  powershell.exe -STA -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%HERE%BookStitchReleaseBuilder_v3.ps1"
)
set "RESULT=%ERRORLEVEL%"
echo.
if not "%RESULT%"=="0" (
  echo BUILD FEHLGESCHLAGEN. Exitcode: %RESULT%
) else (
  echo BUILD ABGESCHLOSSEN.
)
pause
exit /b %RESULT%
