[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$DatabasePath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$SnapshotPath
)

$ErrorActionPreference = 'Stop'
$databaseFullPath = [System.IO.Path]::GetFullPath($DatabasePath)
$snapshotFullPath = [System.IO.Path]::GetFullPath($SnapshotPath)

if (-not [System.IO.File]::Exists($databaseFullPath)) {
    throw "SQLite database not found: $databaseFullPath"
}

if (-not [System.IO.File]::Exists($snapshotFullPath)) {
    throw "Snapshot not found: $snapshotFullPath"
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $projectRoot 'ComponentCatalogCore.Tests/ComponentCatalogCore.Tests.csproj'
$env:CATALOG_CONSISTENCY_DATABASE = $databaseFullPath
$env:CATALOG_CONSISTENCY_SNAPSHOT = $snapshotFullPath

dotnet test $testProject --filter 'FullyQualifiedName~CatalogSemanticConsistencyCommandTests.VerifyProvidedPathsAsync' --no-restore --logger 'console;verbosity=detailed'
exit $LASTEXITCODE
