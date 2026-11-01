[CmdletBinding()]
param(
    [ValidateSet('FrameworkDependent', 'SelfContained', 'Both')]
    [string]$Deployment = 'Both'
)

$ErrorActionPreference = 'Stop'

function Stop-TargetProcess {
    param(
        [Parameter(Mandatory)]
        [string]$ExecutablePath
    )

    $targetExecutable = [System.IO.Path]::GetFullPath($ExecutablePath)
    $processName = [System.IO.Path]::GetFileNameWithoutExtension($targetExecutable)
    $targetProcesses = Get-Process -Name $processName -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                $_.Path -eq $targetExecutable
            }
            catch {
                $false
            }
        }

    foreach ($targetProcess in $targetProcesses) {
        Stop-Process -Id $targetProcess.Id -Force -ErrorAction Stop
        $targetProcess.WaitForExit()
    }
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $projectRoot '.dotnet-sdk\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = 'dotnet'
}

$project = Join-Path $projectRoot 'ComponentCatalogWinUI\ComponentCatalogWinUI.csproj'
$repositoryReleaseExecutable = Join-Path $projectRoot 'ComponentCatalogWinUI\bin\Release\net10.0-windows10.0.26100.0\win-x64\ComponentCatalogWinUI.exe'
$launcher = Join-Path $PSScriptRoot '启动Windows程序.bat'
if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
    throw "Windows launcher BAT was not found: $launcher"
}

$requiredFiles = @(
    'ComponentCatalogWinUI.exe',
    'App.xbf',
    'MainWindow.xbf',
    'ComponentCatalogWinUI.pri',
    'Pages\AboutPage.xbf',
    'Pages\HomePage.xbf',
    'Pages\ImportPage.xbf',
    'Pages\SettingsPage.xbf',
    'Start-WindowsProgram.ps1',
    '启动Windows程序.bat'
)

$deploymentConfigurations = switch ($Deployment) {
    'FrameworkDependent' { @(@{ Name = 'fdd'; SelfContained = 'false' }) }
    'SelfContained' { @(@{ Name = 'self-contained'; SelfContained = 'true' }) }
    default {
        @(
            @{ Name = 'fdd'; SelfContained = 'false' },
            @{ Name = 'self-contained'; SelfContained = 'true' }
        )
    }
}

foreach ($deploymentConfiguration in $deploymentConfigurations) {
    $publishDirectory = Join-Path $projectRoot (".hermes\verify-publish-{0}-{1}" -f $deploymentConfiguration.Name, [guid]::NewGuid().ToString('N'))
    $executable = Join-Path $publishDirectory 'ComponentCatalogWinUI.exe'
    $verificationCatalogRoot = Join-Path $publishDirectory '.catalog-data'
    $originalCatalogRoot = [Environment]::GetEnvironmentVariable('COMPONENT_CATALOG_ROOT', 'Process')

    if (Test-Path -LiteralPath $publishDirectory) {
        throw "Refusing to reuse a publish verification directory: $publishDirectory"
    }

    try {
        New-Item -ItemType Directory -Path $publishDirectory -ErrorAction Stop | Out-Null

        Stop-TargetProcess -ExecutablePath $repositoryReleaseExecutable
        & $dotnet publish $project --no-restore --configuration Release --runtime win-x64 --self-contained $deploymentConfiguration.SelfContained --output $publishDirectory --verbosity minimal "-p:WindowsAppSDKSelfContained=$($deploymentConfiguration.SelfContained)"
        if ($LASTEXITCODE -ne 0) {
            throw "Publish failed for $($deploymentConfiguration.Name) with exit code $LASTEXITCODE."
        }

        $missingFiles = $requiredFiles | Where-Object {
            -not (Test-Path -LiteralPath (Join-Path $publishDirectory $_))
        }
        if ($missingFiles) {
            throw "Publish output is missing: $($missingFiles -join ', ')"
        }

        $publishedLauncher = Join-Path $publishDirectory '启动Windows程序.bat'
        [Environment]::SetEnvironmentVariable('COMPONENT_CATALOG_ROOT', $verificationCatalogRoot, 'Process')
        & $publishedLauncher
        $launcherExitCode = $LASTEXITCODE
        if ($launcherExitCode -ne 0) {
            throw "Published $($deploymentConfiguration.Name) launcher failed with exit code $launcherExitCode."
        }

        Write-Host "PASS: $($deploymentConfiguration.Name) publish contains WinUI XAML/PRI resources and passed the real launcher startup check."
    }
    finally {
        $cleanupFailure = $null
        try {
            Stop-TargetProcess -ExecutablePath $executable
            if (Test-Path -LiteralPath $publishDirectory) {
                Remove-Item -LiteralPath $publishDirectory -Recurse -Force -ErrorAction Stop
            }

            if (Test-Path -LiteralPath $publishDirectory) {
                throw "Publish verification directory still exists after cleanup: $publishDirectory"
            }
        }
        catch {
            $cleanupFailure = $_
        }
        finally {
            [Environment]::SetEnvironmentVariable('COMPONENT_CATALOG_ROOT', $originalCatalogRoot, 'Process')
        }

        if ($null -ne $cleanupFailure) {
            throw "Failed to clean publish verification directory '$publishDirectory': $($cleanupFailure.Exception.Message)"
        }
    }
}
