#Requires -Version 7.0
<#
.SYNOPSIS
  Runs the test suite with code coverage and fails if coverage of the production assembly is below the thresholds.
.DESCRIPTION
  Uses Microsoft.Testing.Extensions.CodeCoverage (the coverage extension that works with xunit.v3 4.x / Microsoft Testing
  Platform v2). Only InventoryAggregation.Api.dll is measured (see coverage.settings.xml). Uncovered lines are listed so a
  gap is easy to close. Run it from anywhere:  pwsh ./scripts/coverage.ps1
#>
[CmdletBinding()]
param(
    [double]$LineThreshold = 100,
    [double]$BranchThreshold = 100
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$resultsDir = Join-Path $repoRoot 'TestResults'

if (Test-Path $resultsDir) { Remove-Item $resultsDir -Recurse -Force }

Push-Location $repoRoot
try {
    # Debug build on purpose: optimised builds merge/inline lines and make line coverage less precise.
    dotnet test `
        --coverage `
        --coverage-output-format cobertura `
        --coverage-output coverage.cobertura.xml `
        --coverage-settings coverage.settings.xml `
        --results-directory $resultsDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed (exit code $LASTEXITCODE)." }

    $report = Get-ChildItem $resultsDir -Recurse -Filter 'coverage.cobertura.xml' | Select-Object -First 1
    if (-not $report) { throw "No Cobertura report found under $resultsDir." }

    [xml]$xml = Get-Content $report.FullName
    $lineRate = [double]$xml.coverage.GetAttribute('line-rate') * 100
    $branchRate = [double]$xml.coverage.GetAttribute('branch-rate') * 100
    $branchesValid = $xml.coverage.GetAttribute('branches-valid')
    $hasBranches = -not [string]::IsNullOrEmpty($branchesValid) -and [int]$branchesValid -gt 0

    Write-Host ''
    Write-Host ('Line coverage:   {0,6:N2}% (required {1}%)' -f $lineRate, $LineThreshold)
    if ($hasBranches) {
        Write-Host ('Branch coverage: {0,6:N2}% (required {1}%)' -f $branchRate, $BranchThreshold)
    }

    $uncovered = foreach ($class in $xml.SelectNodes('//class')) {
        foreach ($line in $class.SelectNodes('lines/line[@hits="0"]')) {
            '{0}:{1}' -f $class.GetAttribute('filename'), $line.GetAttribute('number')
        }
    }
    if ($uncovered) {
        Write-Host ''
        Write-Host 'Uncovered lines:'
        $uncovered | Sort-Object -Unique | ForEach-Object { Write-Host "  $_" }
    }

    $failed = ($lineRate -lt $LineThreshold) -or ($hasBranches -and $branchRate -lt $BranchThreshold)
    if ($failed) { throw 'Coverage is below the required threshold.' }

    Write-Host ''
    Write-Host "Coverage gate passed. Report: $($report.FullName)" -ForegroundColor Green
}
finally {
    Pop-Location
}
