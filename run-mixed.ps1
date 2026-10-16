param(
    [string]$Matrix = 'data/matrix-benchmark-64.txt',
    [ValidateRange(1, 2147483647)][int]$Repeat = 5,
    [string]$Result = 'results/mixed.json'
)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
& dotnet build .\DistributedRank.csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
& docker compose -f docker-compose.linux-workers.yml build
if ($LASTEXITCODE -ne 0) { throw 'Docker build failed. Use a project path with Latin letters.' }
New-Item -ItemType Directory -Path 'logs' -Force | Out-Null
$dll = Join-Path $PSScriptRoot 'bin\Release\net8.0\DistributedRank.dll'
$argsText = '"' + $dll + '" master --matrix "' + $Matrix + '" --workers 3 --port 5000 --repeat ' + $Repeat + ' --result "' + $Result + '"'
$master = Start-Process -FilePath 'dotnet' -ArgumentList $argsText -PassThru -WindowStyle Hidden -RedirectStandardOutput 'logs\mixed.out' -RedirectStandardError 'logs\mixed.err'
try {
    & docker compose -f docker-compose.linux-workers.yml up --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Docker workers failed.' }
    if (-not $master.WaitForExit(15000)) { throw 'Master did not finish; inspect logs/mixed.err.' }
    if ($master.ExitCode -ne 0) { throw 'Master failed; inspect logs/mixed.err.' }
    $containerStates = & docker compose -f docker-compose.linux-workers.yml ps --all --format json
    if ($LASTEXITCODE -ne 0) { throw 'Could not check worker exits.' }
    $failedWorkers = @($containerStates | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object { $_.ExitCode -ne 0 })
    if ($failedWorkers.Count -gt 0) { throw 'A Linux worker exited with an error.' }
    & docker compose -f docker-compose.linux-workers.yml logs --no-color | Out-File -LiteralPath 'logs\mixed-workers.out' -Encoding utf8
    Get-Content -LiteralPath 'logs\mixed.out' -Encoding utf8
} finally {
    if (-not $master.HasExited) { Stop-Process -Id $master.Id -Force -ErrorAction SilentlyContinue }
}
