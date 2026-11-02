@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: dotnet nebyl nalezen. Nainstaluj .NET 8 SDK nebo novejsi.
  exit /b 1
)

dotnet restore
if errorlevel 1 exit /b 1

dotnet build -c Release --no-restore
if errorlevel 1 exit /b 1

echo.
echo Hotovo:
echo %CD%\bin\Release\netstandard2.1\SMT.PriceGun200.dll
pause
endlocal
