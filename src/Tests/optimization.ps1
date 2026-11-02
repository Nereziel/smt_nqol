$ErrorActionPreference = 'Stop'

$pluginSource = Get-Content (Join-Path $PSScriptRoot '../Plugin.cs') -Raw
$holdSource = Get-Content (Join-Path $PSScriptRoot '../CheckoutHoldController.cs') -Raw

if ($pluginSource -match 'FindObjectsOfType<Camera>') {
    throw 'Camera fallback must use the non-obsolete FindObjectsByType API.'
}

if ($holdSource -match '\.OrderBy\s*\(' -or $holdSource -match '\.ToArray\s*\(') {
    throw 'Held checkout product selection must not sort/copy the full scene product list.'
}

if ($holdSource -imatch '^using System\.Linq;') {
    throw 'CheckoutHoldController no longer needs LINQ after single-pass product selection.'
}

Write-Output 'PASS: reviewed hot paths avoid obsolete camera lookup and LINQ sorting/copying during held scanning.'
