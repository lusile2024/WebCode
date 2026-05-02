param(
    [Parameter(Mandatory = $true)]
    [string]$StorageRoot,
    [int]$Port = 5057,
    [string]$DefaultVoiceId = "zh_female_default",
    [string]$PreferredDevice = "gpu-auto",
    [switch]$AllowSystemDrive
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($StorageRoot)) {
    throw "StorageRoot is required."
}

$resolvedRoot = [System.IO.Path]::GetFullPath($StorageRoot)
$rootPath = [System.IO.Path]::GetPathRoot($resolvedRoot)

if ($rootPath -and $rootPath.TrimEnd('\') -ieq 'C:' -and -not $AllowSystemDrive.IsPresent) {
    throw "Refusing to use a C: storage root without -AllowSystemDrive."
}

$directories = @{
    HF_HOME = Join-Path $resolvedRoot "cache\huggingface"
    TRANSFORMERS_CACHE = Join-Path $resolvedRoot "cache\transformers"
    TORCH_HOME = Join-Path $resolvedRoot "cache\torch"
    TEMP = Join-Path $resolvedRoot "temp"
    TMP = Join-Path $resolvedRoot "temp"
    PIP_CACHE_DIR = Join-Path $resolvedRoot "cache\pip"
}

foreach ($entry in $directories.GetEnumerator()) {
    New-Item -ItemType Directory -Force -Path $entry.Value | Out-Null
    Set-Item -Path "Env:$($entry.Key)" -Value $entry.Value
}

New-Item -ItemType Directory -Force -Path (Join-Path $resolvedRoot "logs") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $resolvedRoot "models") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $resolvedRoot "service") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $resolvedRoot "venv") | Out-Null

$env:MELOTTS_DEFAULT_VOICE_ID = $DefaultVoiceId
$env:MELOTTS_PREFERRED_DEVICE = $PreferredDevice
$env:MELOTTS_HOST = "127.0.0.1"
$env:MELOTTS_PORT = "$Port"
$env:MELOTTS_STORAGE_ROOT = $resolvedRoot

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $scriptRoot
try {
    python -m uvicorn app:app --host 127.0.0.1 --port $Port
}
finally {
    Pop-Location
}
