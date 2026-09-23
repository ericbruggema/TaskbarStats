@echo off
setlocal
cd /d "%~dp0"

rem TaskbarStats draait als administrator; om het te kunnen afsluiten moet dit script dat ook doen.
net session >nul 2>&1
if errorlevel 1 (
    echo Administrator-rechten nodig, opnieuw starten...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

set WASRUNNING=0
tasklist /FI "IMAGENAME eq TaskbarStats.exe" 2>nul | find /I "TaskbarStats.exe" >nul
if not errorlevel 1 (
    set WASRUNNING=1
    echo TaskbarStats draait - afsluiten...
    taskkill /F /IM TaskbarStats.exe >nul
    timeout /t 1 /nobreak >nul
)

echo Bouwen (Release)...
dotnet build -c Release
if errorlevel 1 (
    echo.
    echo BUILD MISLUKT
    pause
    exit /b 1
)

echo.
echo Build gelukt: bin\Release\net8.0-windows\TaskbarStats.exe

echo Starten...
start "" "bin\Release\net8.0-windows\TaskbarStats.exe"
endlocal
