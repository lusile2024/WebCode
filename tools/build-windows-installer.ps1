[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$ProjectPath,
    [string]$OutputRoot
)

$ErrorActionPreference = "Stop"

if (-not $ProjectPath) {
    $ProjectPath = Join-Path $PSScriptRoot "..\WebCodeCli\WebCodeCli.csproj"
}

if (-not $OutputRoot) {
    $OutputRoot = Join-Path $PSScriptRoot "..\artifacts\windows-installer"
}

function Get-RepoRoot {
    param([string]$ScriptRoot)

    return (Resolve-Path (Join-Path $ScriptRoot "..")).Path
}

function Get-BuildVersion {
    param(
        [string]$RequestedVersion,
        [string]$RepoRoot
    )

    if ($RequestedVersion) {
        return $RequestedVersion.TrimStart("v")
    }

    [xml]$props = Get-Content -Path (Join-Path $RepoRoot "Directory.Build.props")
    $resolvedVersion = $props.Project.PropertyGroup.Version
    if (-not $resolvedVersion) {
        throw "Could not resolve version from Directory.Build.props."
    }

    return $resolvedVersion.TrimStart("v")
}

function Update-PublishAppSettings {
    param([string]$PublishDirectory)

    $settingsPath = Join-Path $PublishDirectory "appsettings.json"
    if (-not (Test-Path $settingsPath)) {
        throw "Published appsettings.json was not found at $settingsPath"
    }

    $settings = Get-Content -Path $settingsPath -Raw | ConvertFrom-Json

    if ($settings.Logging -and $settings.Logging.LogLevel) {
        $settings.Logging.LogLevel.Default = "Information"
    }

    if ($settings.DBConnection) {
        $settings.DBConnection.ConnectionStrings = "Data Source=data/WebCodeCli.db"
        $settings.DBConnection.VectorConnection = "data/WebCodeCliMem.db"
    }

    if ($settings.CliTools) {
        $settings.CliTools.TempWorkspaceRoot = "workspaces"
    }

    if ($settings.Workspace) {
        $settings.Workspace.AllowedRoots = @("workspaces")
        $settings.Workspace.AutoCreateMissingDirectories = $true
    }

    $json = $settings | ConvertTo-Json -Depth 20
    [System.IO.File]::WriteAllText($settingsPath, $json, [System.Text.UTF8Encoding]::new($false))
}

function Copy-ReplyTtsServiceAssets {
    param(
        [string]$RepoRoot,
        [string]$PublishDirectory
    )

    $sourceRoot = Join-Path $RepoRoot "tools\sherpa-kokoro-service"
    if (-not (Test-Path $sourceRoot)) {
        throw "Reply TTS service assets were not found at $sourceRoot"
    }

    $destinationRoot = Join-Path $PublishDirectory "tools\sherpa-kokoro-service"
    if (Test-Path $destinationRoot) {
        Remove-Item -Recurse -Force $destinationRoot
    }

    New-Item -ItemType Directory -Force -Path $destinationRoot | Out-Null

    foreach ($relativePath in @(
        "README.md",
        "requirements.txt",
        "app.py",
        "start.ps1",
        "start.sh")) {
        $sourcePath = Join-Path $sourceRoot $relativePath
        if (-not (Test-Path $sourcePath)) {
            throw "Required Reply TTS service asset was not found at $sourcePath"
        }

        $destinationPath = Join-Path $destinationRoot $relativePath
        $destinationParent = Split-Path -Parent $destinationPath
        if (-not (Test-Path $destinationParent)) {
            New-Item -ItemType Directory -Force -Path $destinationParent | Out-Null
        }

        Copy-Item -Path $sourcePath -Destination $destinationPath -Force
    }
}

function Get-LaunchUrlForReleaseNotes {
    param([string]$PublishDirectory)

    $settingsPath = Join-Path $PublishDirectory "appsettings.json"
    if (-not (Test-Path $settingsPath)) {
        throw "Published appsettings.json was not found at $settingsPath"
    }

    $settings = Get-Content -Path $settingsPath -Raw | ConvertFrom-Json
    $configuredUrls = $settings.urls
    if (-not $configuredUrls) {
        return "http://localhost:5000"
    }

    $firstUrl = ($configuredUrls -split ';' | Where-Object { $_ -and $_.Trim() } | Select-Object -First 1).Trim()
    if (-not $firstUrl) {
        return "http://localhost:5000"
    }

    return ($firstUrl -replace '://(\*|\+|0\.0\.0\.0)', '://localhost')
}

function Get-InnoSetupCompilerPath {
    $candidatePaths = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    ) | Where-Object { $_ }

    foreach ($candidatePath in $candidatePaths) {
        if (Test-Path $candidatePath) {
            return $candidatePath
        }
    }

    throw "ISCC.exe was not found. Install Inno Setup 6 before building the installer."
}

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
$projectFullPath = (Resolve-Path $ProjectPath).Path
$resolvedVersion = Get-BuildVersion -RequestedVersion $Version -RepoRoot $repoRoot
$assemblyVersion = "$resolvedVersion.0"

if ($resolvedVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "Resolved version '$resolvedVersion' is not in major.minor.patch format."
}

$versionTag = "v$resolvedVersion"
if ([System.IO.Path]::IsPathRooted($OutputRoot)) {
    $outputRootFullPath = [System.IO.Path]::GetFullPath($OutputRoot)
}
else {
    $outputRootFullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
}
$releaseRoot = Join-Path $outputRootFullPath $versionTag
$publishDirectory = Join-Path $releaseRoot "publish"
$portableDirectoryName = "WebCode-$versionTag-$RuntimeIdentifier-portable"
$portableStageDirectory = Join-Path $releaseRoot $portableDirectoryName
$portableZipPath = Join-Path $releaseRoot "$portableDirectoryName.zip"
$installerOutputDirectory = Join-Path $releaseRoot "installer"
$installerBaseFileName = "WebCode-Setup-$versionTag-$RuntimeIdentifier"
$installerPath = Join-Path $installerOutputDirectory "$installerBaseFileName.exe"
$checksumsPath = Join-Path $releaseRoot "SHA256SUMS.txt"
$releaseNotesPath = Join-Path $releaseRoot "RELEASE_NOTES.md"

if (Test-Path $releaseRoot) {
    Remove-Item -Recurse -Force $releaseRoot
}

New-Item -ItemType Directory -Force -Path $publishDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $installerOutputDirectory | Out-Null

Write-Host "Publishing WebCode $resolvedVersion for $RuntimeIdentifier ..."
dotnet publish $projectFullPath `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    -o $publishDirectory `
    /p:PublishSingleFile=false `
    /p:Version=$resolvedVersion `
    /p:AssemblyVersion=$assemblyVersion `
    /p:FileVersion=$assemblyVersion `
    /p:InformationalVersion=$resolvedVersion

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

$publishedExePath = Join-Path $publishDirectory "WebCodeCli.exe"
if (-not (Test-Path $publishedExePath)) {
    throw "Expected published executable was not found at $publishedExePath"
}

Update-PublishAppSettings -PublishDirectory $publishDirectory
Copy-ReplyTtsServiceAssets -RepoRoot $repoRoot -PublishDirectory $publishDirectory
$launchUrl = Get-LaunchUrlForReleaseNotes -PublishDirectory $publishDirectory

if (Test-Path $portableStageDirectory) {
    Remove-Item -Recurse -Force $portableStageDirectory
}
New-Item -ItemType Directory -Force -Path $portableStageDirectory | Out-Null
Copy-Item -Path (Join-Path $publishDirectory "*") -Destination $portableStageDirectory -Recurse -Force

if (Test-Path $portableZipPath) {
    Remove-Item -Force $portableZipPath
}
Compress-Archive -Path $portableStageDirectory -DestinationPath $portableZipPath -Force

$isccPath = Get-InnoSetupCompilerPath
$installerScriptPath = Join-Path $repoRoot "installer\windows\WebCode.iss"

Write-Host "Compiling Windows installer with Inno Setup ..."
& $isccPath `
    "/DMyAppVersion=$resolvedVersion" `
    "/DPublishDir=$publishDirectory" `
    "/DOutputDir=$installerOutputDirectory" `
    "/DMyAppInstallerFileName=$installerBaseFileName" `
    "/DMyAppSourceExe=WebCodeCli.exe" `
    $installerScriptPath

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed."
}

if (-not (Test-Path $installerPath)) {
    throw "Expected installer was not found at $installerPath"
}

$assets = @($portableZipPath, $installerPath)
$checksumLines = foreach ($asset in $assets) {
    $hash = (Get-FileHash -Algorithm SHA256 -Path $asset).Hash.ToLowerInvariant()
    "$hash *$(Split-Path -Path $asset -Leaf)"
}
[System.IO.File]::WriteAllLines($checksumsPath, $checksumLines, [System.Text.UTF8Encoding]::new($false))

$releaseNotes = @"
# WebCode $versionTag

## Assets
- $([System.IO.Path]::GetFileName($installerPath))
- $([System.IO.Path]::GetFileName($portableZipPath))
- $([System.IO.Path]::GetFileName($checksumsPath))

## Packaging notes
- Built from commit $(git -C $repoRoot rev-parse --short HEAD)
- Self-contained $RuntimeIdentifier build, no separate .NET runtime installation required
- The installer keeps an existing appsettings.json on upgrade
- Default install path is `%LOCALAPPDATA%\Programs\WebCode`
- Default runtime data paths are `data/` and `workspaces/` under the install directory
- Includes the local Kokoro/sherpa-onnx Reply TTS wrapper under 'tools/sherpa-kokoro-service'
- The bundled Reply TTS wrapper still requires Python dependencies, models, ffmpeg, and a writable non-system storage root before use
- After launch, open $launchUrl in the browser
"@
[System.IO.File]::WriteAllText($releaseNotesPath, $releaseNotes.Trim() + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))

Write-Host ""
Write-Host "Build completed."
Write-Host "Installer: $installerPath"
Write-Host "Portable ZIP: $portableZipPath"
Write-Host "Checksums: $checksumsPath"
Write-Host "Release notes: $releaseNotesPath"
