param(
    [Parameter(Mandatory = $true)]
    [string]$UnityEditorPath,

    [Parameter(Mandatory = $true)]
    [ValidateSet("2D", "3D")]
    [string]$Dimension,

    [ValidateRange(1, 2000)]
    [int]$BallCount = 128,

    [ValidateRange(0, 3600)]
    [int]$WarmupSeconds = 2,
    [ValidateRange(1, 3600)]
    [int]$MeasurementSeconds = 8,
    [ValidateRange(1024, 65535)]
    [int]$DashboardPort = 8765,
    [ValidateRange(30, 3600)]
    [int]$TimeoutSeconds = 900,
    [switch]$NoOpenDashboard,
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
$buildArguments = "-batchmode -nographics -projectPath `"$projectRoot`" -buildTarget StandaloneWindows64 -executeMethod GravityBenchmarkBuild.BuildGravityPlayer -quit -logFile `"$buildLog`""
$buildProcess = Start-Process -FilePath $unityPath -ArgumentList $buildArguments -Wait -PassThru
if ($buildProcess.ExitCode -ne 0) {
    throw "Unity player build failed with exit code $($buildProcess.ExitCode). See $buildLog"
}
if (-not (Test-Path -LiteralPath $playerPath)) {
    throw "Unity reported a successful build but the player was not found at $playerPath. See $buildLog"
}

Write-Host "Running $Dimension gravity benchmark without a graphics window..."
$playerArguments = "-batchmode -nographics -logFile `"$playerLog`" -gravity-benchmark-headless -gravity-benchmark-host-dashboard -gravity-benchmark-dimension $Dimension -gravity-benchmark-balls $BallCount -gravity-benchmark-port $DashboardPort -gravity-benchmark-warmup-seconds $WarmupSeconds -gravity-benchmark-measurement-seconds $MeasurementSeconds -gravity-benchmark-output `"$OutputPath`""
$runStartedUtc = [DateTime]::UtcNow
$playerProcess = Start-Process -FilePath $playerPath -ArgumentList $playerArguments -PassThru
$deadlineUtc = $runStartedUtc.AddSeconds($TimeoutSeconds)
$report = $null

while ([DateTime]::UtcNow -lt $deadlineUtc) {
    $playerProcess.Refresh()

    if (Test-Path -LiteralPath $OutputPath) {
        $reportFile = Get-Item -LiteralPath $OutputPath
        if ($reportFile.LastWriteTimeUtc -ge $runStartedUtc) {
            try {
                $candidate = Get-Content -LiteralPath $OutputPath -Raw | ConvertFrom-Json
                if ($candidate.status -eq "complete" -or $candidate.status -eq "error") {
                    $report = $candidate
                    break
                }
            } catch {
                # The player may still be writing the report; retry until it is valid JSON.
            }
        }
    }

    if ($playerProcess.HasExited) {
        throw "Headless benchmark player exited before writing a complete report. See $playerLog"
    }
    Start-Sleep -Milliseconds 250
}

if ($null -eq $report) {
    $playerProcess.Refresh()
    if (-not $playerProcess.HasExited) {
        Stop-Process -Id $playerProcess.Id -Force
    }
    throw "Headless benchmark did not finish within $TimeoutSeconds seconds. See $playerLog"
}
if ($report.status -ne "complete") {
    throw "Headless benchmark reported status '$($report.status)': $($report.phase). See $playerLog"
}

$dashboardUrl = "http://127.0.0.1:$DashboardPort/"
try {
    $null = Invoke-WebRequest -Uri $dashboardUrl -UseBasicParsing -TimeoutSec 5
    $dashboardState = Invoke-RestMethod -Uri "$dashboardUrl`state" -TimeoutSec 5
    if ($dashboardState.status -ne "complete") {
        throw "Dashboard state is '$($dashboardState.status)' instead of 'complete'."
    }
} catch {
    throw "Benchmark results were written to $OutputPath, but the web panel did not start at $dashboardUrl. See $playerLog. $($_.Exception.Message)"
}

Write-Host "JSON report: $OutputPath"
Write-Host "Web panel: $dashboardUrl"
Write-Host "Headless dashboard host PID: $($playerProcess.Id)"
if (-not $NoOpenDashboard) {
    Start-Process $dashboardUrl
}
