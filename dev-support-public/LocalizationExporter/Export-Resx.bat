@echo off
setlocal
cd /d "%~dp0"
title BookStitch - RESX Development Export

echo ===============================================
echo  BookStitch RESX Export - DEVELOPMENT
 echo ===============================================
echo.
echo Dieser Export schreibt RESX-Dateien und das Exportmanifest
 echo im BookStitch/Resources-Ordner.
echo.
echo Er aktualisiert KEINE Uebersetzungs-Baselines.
echo Der Release-Modus wird NICHT gestartet.
echo.
if not exist "%~dp0Export-Resx.ps1" (
    echo FEHLER: Export-Resx.ps1 fehlt in diesem Ordner.
    pause
    exit /b 1
)
choice /C JN /N /M "Jetzt Development-Export starten? [J/N]: "
if errorlevel 2 (
    echo Export abgebrochen. Keine Aenderungen ausgefuehrt.
    pause
    exit /b 0
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Export-Resx.ps1" -Mode Development
set "RESULT=%ERRORLEVEL%"
echo.
if "%RESULT%"=="0" (
    echo ENTWICKLUNGS-EXPORT TECHNISCH ERFOLGREICH.
    echo Hinweis: Offene Warnungen bedeuten keine Sprachfreigabe.
) else (
    echo EXPORT FEHLGESCHLAGEN - Exitcode %RESULT%.
    echo Bitte RESX-Dateien NICHT als neu exportiert betrachten.
)
echo.
echo.
echo Pruefbericht: LocalizationExporter\reports\BookStitch.LocalizationValidation.txt
echo [E] Berichtordner oeffnen   [B] Fenster beenden
choice /C EB /N /M "Auswahl [E/B]: "
if errorlevel 2 exit /b %RESULT%
start "" "%~dp0reports"
echo.
set /p "CLOSE=Zum Schliessen EINGABE druecken: "
exit /b %RESULT%
