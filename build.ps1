$ErrorActionPreference = 'Stop'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error 'dotnet was not found. Install the .NET 8 SDK (or newer) and run this script again.'
}

Push-Location $PSScriptRoot
try {
    $project = Join-Path $PSScriptRoot 'SMT.NQOL.csproj'
    dotnet restore $project
    dotnet build $project -c Release --no-restore

    $dll = Join-Path $PSScriptRoot 'bin\Release\netstandard2.1\SMT.NQOL.dll'
    if (-not (Test-Path $dll)) {
        throw "Build finished but DLL was not found at: $dll"
    }

    Write-Host ''
    Write-Host 'Built successfully:' -ForegroundColor Green
    Write-Host $dll
} finally {
    Pop-Location
}
