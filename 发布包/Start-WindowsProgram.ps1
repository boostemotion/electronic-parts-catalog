[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ExecutablePath,

    [string]$StartupLogPath,

    [string]$RepositoryProject,

    [string]$DotnetPath,

    [int]$StartupTimeoutSeconds = 15,

    [int]$StartupStabilizationMilliseconds = 750,

    [int]$MutexTimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'

function Stop-TargetProcess {
    param(
        [Parameter(Mandatory)]
        [string]$TargetExecutablePath
    )

    $processName = [System.IO.Path]::GetFileNameWithoutExtension($TargetExecutablePath)
    $targetProcesses = Get-Process -Name $processName -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                $_.Path -eq $TargetExecutablePath
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

function Get-NewStartupLogContent {
    param(
        [string]$LogPath,
        [long]$Offset
    )

    if (-not $LogPath -or -not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
        return ''
    }

    $stream = [System.IO.File]::Open($LogPath, 'Open', 'Read', 'ReadWrite')
    try {
        if ($stream.Length -le $Offset) {
            return ''
        }

        $stream.Seek($Offset, [System.IO.SeekOrigin]::Begin) | Out-Null
        $reader = [System.IO.StreamReader]::new($stream)
        try {
            return $reader.ReadToEnd().Trim()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Throw-StartupExit {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$LogPath,
        [long]$LogOffset
    )

    $newLog = Get-NewStartupLogContent -LogPath $LogPath -Offset $LogOffset
    $details = if ($newLog) { "`n$newLog" } else { '' }
    throw "Windows app exited during startup with code $($Process.ExitCode).$details"
}

if ($StartupTimeoutSeconds -le 0) {
    throw 'StartupTimeoutSeconds must be greater than zero.'
}

if ($StartupStabilizationMilliseconds -lt 0) {
    throw 'StartupStabilizationMilliseconds cannot be negative.'
}

if ($MutexTimeoutSeconds -le 0) {
    throw 'MutexTimeoutSeconds must be greater than zero.'
}

$executable = [System.IO.Path]::GetFullPath($ExecutablePath)
$mutex = [System.Threading.Mutex]::new($false, 'Local\ComponentCatalogWinUI.WindowsLauncher')
$mutexAcquired = $false

try {
    try {
        $mutexAcquired = $mutex.WaitOne([TimeSpan]::FromSeconds($MutexTimeoutSeconds))
    }
    catch [System.Threading.AbandonedMutexException] {
        Write-Warning 'Recovered an abandoned Windows launcher mutex.'
        $mutexAcquired = $true
    }

    if (-not $mutexAcquired) {
        throw "Timed out after $MutexTimeoutSeconds seconds waiting for another Windows launch/build operation to finish."
    }

    if ($RepositoryProject) {
        $repositoryProjectPath = [System.IO.Path]::GetFullPath($RepositoryProject)
        if (-not (Test-Path -LiteralPath $repositoryProjectPath -PathType Leaf)) {
            throw "WinUI project was not found: $repositoryProjectPath"
        }

        if (-not $DotnetPath) {
            $DotnetPath = 'dotnet'
        }
        elseif ([System.IO.Path]::IsPathRooted($DotnetPath) -and -not (Test-Path -LiteralPath $DotnetPath -PathType Leaf)) {
            throw ".NET SDK was not found: $DotnetPath"
        }

        Stop-TargetProcess -TargetExecutablePath $executable

        Write-Host 'Building the latest Windows Release (win-x64)...'
        & $DotnetPath build $repositoryProjectPath --configuration Release --runtime win-x64 --verbosity minimal
        if ($LASTEXITCODE -ne 0) {
            throw "Release build failed with exit code $LASTEXITCODE."
        }
    }

    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "Windows executable was not found: $executable"
    }

    if (-not $RepositoryProject) {
        Stop-TargetProcess -TargetExecutablePath $executable
    }

    $logOffset = 0L
    if ($StartupLogPath) {
        $StartupLogPath = [System.IO.Path]::GetFullPath($StartupLogPath)
        if (Test-Path -LiteralPath $StartupLogPath -PathType Leaf) {
            $logOffset = (Get-Item -LiteralPath $StartupLogPath).Length
        }
    }

    $workingDirectory = Split-Path -Parent $executable
    $process = Start-Process -FilePath $executable -WorkingDirectory $workingDirectory -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)

    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 200
        $process.Refresh()

        if ($process.HasExited) {
            Throw-StartupExit -Process $process -LogPath $StartupLogPath -LogOffset $logOffset
        }

        if ($process.MainWindowHandle -ne 0) {
            Start-Sleep -Milliseconds $StartupStabilizationMilliseconds
            $process.Refresh()

            if ($process.HasExited) {
                Throw-StartupExit -Process $process -LogPath $StartupLogPath -LogOffset $logOffset
            }

            if ($process.MainWindowHandle -ne 0) {
                Write-Host 'Windows app started successfully.'
                Write-Host "PID: $($process.Id)"
                Write-Host "Executable: $executable"
                return
            }
        }
    }

    $process.Refresh()
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }

    throw "Windows app did not create a stable main window within $StartupTimeoutSeconds seconds."
}
finally {
    if ($mutexAcquired) {
        $mutex.ReleaseMutex()
    }

    $mutex.Dispose()
}
