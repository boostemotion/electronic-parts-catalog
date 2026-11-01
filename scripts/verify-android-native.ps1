[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$androidSource = Join-Path $repositoryRoot 'Android\android'
$capacitorAndroidSource = Join-Path $repositoryRoot 'Android\node_modules\@capacitor\android\capacitor'
$nativeStageRoot = 'D:\AI\partshelf-android-native-verify'
$androidProject = Join-Path $nativeStageRoot 'Android\android'
$capacitorAndroidStage = Join-Path $nativeStageRoot 'Android\node_modules\@capacitor\android\capacitor'
$jdkHome = 'C:\Program Files\Java\jdk-21.0.10'
$gradleUserHome = 'D:\AI\partshelf-gradle-user-home'
$javaExecutable = Join-Path $jdkHome 'bin\java.exe'
$gradleWrapper = Join-Path $androidProject 'gradlew.bat'

if (-not (Test-Path -LiteralPath $javaExecutable -PathType Leaf)) {
    throw "Pinned JDK was not found: $javaExecutable"
}
if (-not (Test-Path -LiteralPath (Join-Path $androidSource 'gradlew.bat') -PathType Leaf)) {
    throw "Gradle wrapper was not found: $androidSource"
}
if (-not (Test-Path -LiteralPath $capacitorAndroidSource -PathType Container)) {
    throw "Capacitor Android source was not found: $capacitorAndroidSource"
}
if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot 'version.json') -PathType Leaf)) {
    throw "Version source was not found: $repositoryRoot\version.json"
}

New-Item -ItemType Directory -Path $androidProject -Force | Out-Null
& robocopy $androidSource $androidProject /E /XD build .gradle | Out-Null
if ($LASTEXITCODE -gt 7) {
    throw "Failed to stage the Android native project to the ASCII-only verification directory (robocopy exit code $LASTEXITCODE)."
}
& robocopy $capacitorAndroidSource $capacitorAndroidStage /E /XD build .gradle | Out-Null
if ($LASTEXITCODE -gt 7) {
    throw "Failed to stage the Capacitor Android project to the ASCII-only verification directory (robocopy exit code $LASTEXITCODE)."
}
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'version.json') -Destination (Join-Path $nativeStageRoot 'version.json') -Force

New-Item -ItemType Directory -Path $gradleUserHome -Force | Out-Null
$env:JAVA_HOME = $jdkHome
$env:GRADLE_USER_HOME = $gradleUserHome
$env:PATH = "$jdkHome\bin;$env:PATH"

& $javaExecutable -version
if ($LASTEXITCODE -ne 0) {
    throw "Pinned JDK failed its version check with exit code $LASTEXITCODE."
}

function Invoke-AndroidGradle {
    param([string[]]$Arguments)

    & $gradleWrapper -p $androidProject @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Gradle failed: $($Arguments -join ' ')"
    }
}

Invoke-AndroidGradle @('testDebugUnitTest', '--no-daemon')
Invoke-AndroidGradle @('assembleRelease', '--no-daemon')

$mergedManifest = Join-Path $androidProject 'app\build\intermediates\merged_manifests\release\processReleaseManifest\AndroidManifest.xml'
if (-not (Test-Path -LiteralPath $mergedManifest -PathType Leaf)) {
    throw "Release merged manifest was not produced: $mergedManifest"
}

[xml]$manifest = Get-Content -LiteralPath $mergedManifest -Raw
$androidNamespace = 'http://schemas.android.com/apk/res/android'
$namespaceManager = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
$namespaceManager.AddNamespace('android', $androidNamespace)
$application = $manifest.SelectSingleNode('/manifest/application')
if ($null -eq $application) {
    throw 'Release merged manifest does not contain an application element.'
}
if ($application.GetAttribute('allowBackup', $androidNamespace) -ne 'false') {
    throw 'Release merged manifest must set android:allowBackup="false".'
}
if ($application.GetAttribute('networkSecurityConfig', $androidNamespace) -ne '@xml/network_security_config') {
    throw 'Release merged manifest must reference @xml/network_security_config.'
}

$requiredPermissions = @(
    'android.permission.INTERNET',
    'android.permission.ACCESS_WIFI_STATE',
    'android.permission.CHANGE_WIFI_MULTICAST_STATE',
    'android.permission.REQUEST_INSTALL_PACKAGES'
)
foreach ($permissionName in $requiredPermissions) {
    $permission = $manifest.SelectSingleNode("/manifest/uses-permission[@android:name='$permissionName']", $namespaceManager)
    if ($null -eq $permission) {
        throw "Release merged manifest is missing required discovery permission: $permissionName"
    }
}

$fileProvider = $manifest.SelectSingleNode("/manifest/application/provider[@android:name='androidx.core.content.FileProvider']", $namespaceManager)
if ($null -eq $fileProvider) {
    throw 'Release merged manifest does not contain the FileProvider.'
}
if ($fileProvider.GetAttribute('exported', $androidNamespace) -ne 'false' -or
    $fileProvider.GetAttribute('grantUriPermissions', $androidNamespace) -ne 'true') {
    throw 'Release FileProvider must be non-exported and grant read URIs explicitly.'
}
$fileProviderPaths = $fileProvider.SelectSingleNode("meta-data[@android:name='android.support.FILE_PROVIDER_PATHS']", $namespaceManager)
if ($null -eq $fileProviderPaths -or $fileProviderPaths.GetAttribute('resource', $androidNamespace) -ne '@xml/file_paths') {
    throw 'Release FileProvider must reference @xml/file_paths.'
}

$activitySource = Get-Content -LiteralPath (Join-Path $androidProject 'app\src\main\java\com\partshelf\catalog\MainActivity.java') -Raw
$appUpdateRegistration = $activitySource.IndexOf('registerPlugin(AppUpdatePlugin.class)')
$discoveryRegistration = $activitySource.IndexOf('registerPlugin(CatalogDiscoveryPlugin.class)')
$bridgeInitialization = $activitySource.IndexOf('super.onCreate(savedInstanceState)')
if ($appUpdateRegistration -lt 0 -or $discoveryRegistration -lt 0 -or $bridgeInitialization -lt 0 -or
    $appUpdateRegistration -gt $bridgeInitialization -or $discoveryRegistration -gt $bridgeInitialization) {
    throw 'MainActivity must register AppUpdatePlugin and CatalogDiscoveryPlugin before bridge initialization.'
}

$fileProviderPathsSource = Get-Content -LiteralPath (Join-Path $androidProject 'app\src\main\res\xml\file_paths.xml') -Raw
if ($fileProviderPathsSource -notmatch '<cache-path name="updates" path="updates/"\s*/>' -or
    $fileProviderPathsSource -match '<(?:external|files|root)-path\b') {
    throw 'FileProvider paths must expose only the cache updates directory.'
}

Write-Host 'Native Android verification passed: isolated Gradle tests, release build, merged manifest, plugin registration, and FileProvider scope.'
