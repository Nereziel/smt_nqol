@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: dotnet was not found. Install .NET 8 SDK or newer.
  exit /b 1
)

dotnet restore SMT.NQOL.csproj
if errorlevel 1 exit /b 1

dotnet build SMT.NQOL.csproj -c Release --no-restore
if errorlevel 1 exit /b 1

echo.
echo Built successfully:
echo %CD%\bin\Release\netstandard2.1\SMT.NQOL.dll
pause
endlocal
