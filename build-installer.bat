@echo off
setlocal
cd /d "%~dp0"

echo [1/4] Plugin-host publiceren (aparte, niet-verhoogde exe, ingekort)...
rem altijd schoon publiceren: een incrementele publish liet de map defaults\ leeg (installer faalt dan op defaults\*)
if exist plugins\TaskbarStats.Plugins\publish rmdir /s /q plugins\TaskbarStats.Plugins\publish
dotnet publish plugins\TaskbarStats.Plugins -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o plugins\TaskbarStats.Plugins\publish
if errorlevel 1 goto :fail

echo [2/4] Publiceren (self-contained, een bestand)...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
if errorlevel 1 goto :fail

echo [3/4] Inno Setup zoeken...
set ISCC=
for %%P in ("%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" "%ProgramFiles%\Inno Setup 6\ISCC.exe" "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe") do (
    if exist %%P set ISCC=%%~P
)
if "%ISCC%"=="" (
    echo.
    echo Inno Setup is niet gevonden. Installeer het eenmalig met:
    echo     winget install JRSoftware.InnoSetup
    echo of download het van https://jrsoftware.org/isdl.php  en draai dit script opnieuw.
    goto :fail
)

echo [4/4] Installer bouwen...
"%ISCC%" "installer\TaskbarStats.iss"
if errorlevel 1 goto :fail

echo.
echo Klaar: installer\Output\  (TaskbarStats-Setup-versie.exe)
start "" "installer\Output"
endlocal
exit /b 0

:fail
echo.
echo MISLUKT
pause
endlocal
exit /b 1
