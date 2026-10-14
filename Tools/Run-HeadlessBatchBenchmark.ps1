param(
    [Parameter(Mandatory = $true)]
    [string]$UnityEditorPath,

    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$unityPath = (Resolve-Path $UnityEditorPath).Path
$buildDirectory = Join-Path $projectRoot "Build\Headless"
$playerPath = Join-Path $buildDirectory "BatchBenchmark.exe"
$buildLog = Join-Path $buildDirectory "batch-build.log"
$playerLog = Join-Path $buildDirectory "batch-player.log"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $buildDirectory "batch-benchmark.json"
} else {
    $parent = Split-Path -Parent $OutputPath
    if ($parent -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
}

New-Item -ItemType Directory -Path $buildDirectory -Force | Out-Null
Write-Host "Building the standalone batch comparison player..."
$buildArguments = "-batchmode -nographics -projectPath `"$projectRoot`" -buildTarget StandaloneWindows64 -executeMethod GravityBenchmarkBuild.BuildBatchPlayer -quit -logFile `"$buildLog`""
$buildProcess = Start-Process -FilePath $unityPath -ArgumentList $buildArguments -Wait -PassThru
if ($buildProcess.ExitCode -ne 0) {
    throw "Unity batch player build failed with exit code $($buildProcess.ExitCode). See $buildLog"
}
if (-not (Test-Path -LiteralPath $playerPath)) {
    throw "Unity reported a successful build but the player was not found at $playerPath. See $buildLog"
}

Write-Host "Running all six batch-processing methods without a graphics window..."
$playerArguments = "-batchmode -nographics -logFile `"$playerLog`" -gravity-benchmark-headless -gravity-benchmark-output `"$OutputPath`""
$playerProcess = Start-Process -FilePath $playerPath -ArgumentList $playerArguments -Wait -PassThru
if ($playerProcess.ExitCode -ne 0) {
    throw "Headless batch comparison failed with exit code $($playerProcess.ExitCode). See $playerLog"
}

Write-Host "JSON report: $OutputPath"
Write-Host ("CSV report:  {0}" -f [System.IO.Path]::ChangeExtension($OutputPath, ".csv"))
