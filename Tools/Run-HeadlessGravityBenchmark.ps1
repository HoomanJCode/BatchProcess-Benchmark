param(
    [Parameter(Mandatory = $true)]
    [string]$UnityEditorPath,

    [Parameter(Mandatory = $true)]
    [ValidateSet("2D", "3D")]
    [string]$Dimension,

    [ValidateRange(0, 3600)]
    [int]$WarmupSeconds = 2,
    [ValidateRange(1, 3600)]
    [int]$MeasurementSeconds = 8,
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$unityPath = (Resolve-Path $UnityEditorPath).Path
$buildDirectory = Join-Path $projectRoot "Build\Headless"
$playerPath = Join-Path $buildDirectory "GravityBenchmark.exe"
$buildLog = Join-Path $buildDirectory "build.log"
$playerLog = Join-Path $buildDirectory "player.log"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $buildDirectory ("gravity-benchmark-{0}.json" -f $Dimension.ToLowerInvariant())
} else {
    $parent = Split-Path -Parent $OutputPath
    if ($parent -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
}

New-Item -ItemType Directory -Path $buildDirectory -Force | Out-Null
Write-Host "Building the standalone Unity player..."
& $unityPath -batchmode -nographics -projectPath $projectRoot -buildTarget StandaloneWindows64 -executeMethod GravityBenchmarkBuild.BuildWindowsPlayer -quit -logFile $buildLog
if ($LASTEXITCODE -ne 0) {
    throw "Unity player build failed with exit code $LASTEXITCODE. See $buildLog"
}
if (-not (Test-Path -LiteralPath $playerPath)) {
    throw "Unity reported a successful build but the player was not found at $playerPath. See $buildLog"
}

Write-Host "Running $Dimension gravity benchmark without a graphics window..."
& $playerPath -batchmode -nographics -logFile $playerLog -gravity-benchmark-headless -gravity-benchmark-dimension $Dimension -gravity-benchmark-warmup-seconds $WarmupSeconds -gravity-benchmark-measurement-seconds $MeasurementSeconds -gravity-benchmark-output $OutputPath
if ($LASTEXITCODE -ne 0) {
    throw "Headless benchmark failed with exit code $LASTEXITCODE. See $playerLog"
}

Write-Host "JSON report: $OutputPath"
Write-Host ("CSV report:  {0}" -f [System.IO.Path]::ChangeExtension($OutputPath, ".csv"))
