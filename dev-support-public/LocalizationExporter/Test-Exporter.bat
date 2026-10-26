@echo off
setlocal
cd /d "%~dp0"
title BookStitch - Localization Exporter Test

echo ===============================================
echo  BookStitch LocalizationExporter - Smoke-Test
echo ===============================================
echo.
echo Testet mit einer temporaeren Kopie des Language Masters.
echo Die produktiven RESX-Dateien bleiben unveraendert.
echo.
if not exist "%~dp0Test-Exporter.ps1" (
    echo FEHLER: Test-Exporter.ps1 fehlt in diesem Ordner.
    pause
    exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Test-Exporter.ps1"
set "RESULT=%ERRORLEVEL%"
echo.
if "%RESULT%"=="0" (
    echo TEST BESTANDEN. Detailbericht unter LocalizationExporter\reports\Test-...\TestSummary.txt
) else (
    echo TEST FEHLGESCHLAGEN - Exitcode %RESULT%. Bitte TestSummary.txt im reports-Ordner pruefen.
)
echo.
echo.
echo [E] Berichtordner oeffnen   [B] Fenster beenden
choice /C EB /N /M "Auswahl [E/B]: "
if errorlevel 2 exit /b %RESULT%
start "" "%~dp0reports"
:wait_for_close
echo.
set /p "CLOSE=Zum Schliessen EINGABE druecken: "
exit /b %RESULT%
