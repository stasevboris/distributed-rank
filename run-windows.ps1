param(
    [string]$Matrix = 'data/matrix.txt',
    [ValidateRange(1, 65535)][int]$Port = 5000,
    [ValidateRange(1, 2147483647)][int]$Workers = 3,
    [ValidateRange(1, 2147483647)][int]$Repeat = 5,
    [string]$Result = 'results/windows.json'
)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
& dotnet build .\DistributedRank.csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
New-Item -ItemType Directory -Path 'logs' -Force | Out-Null
$dll = Join-Path $PSScriptRoot 'bin\Release\net8.0\DistributedRank.dll'
$children = @()
try {
    foreach ($index in 1..$Workers) {
        $argsText = '"' + $dll + '" worker --host 127.0.0.1 --port ' + $Port + ' --id win-worker-' + $index
        $children += Start-Process -FilePath 'dotnet' -ArgumentList $argsText -PassThru -WindowStyle Hidden -RedirectStandardOutput "logs\worker-$index.out" -RedirectStandardError "logs\worker-$index.err"
    }
    & dotnet $dll master --matrix $Matrix --workers $Workers --port $Port --repeat $Repeat --result $Result | Tee-Object -FilePath 'logs\windows-latest.out'
    if ($LASTEXITCODE -ne 0) { throw 'Master failed; inspect logs.' }
    foreach ($child in $children) {
        if (-not $child.WaitForExit(10000)) { throw 'Worker did not finish in time.' }
        if ($child.ExitCode -ne 0) { throw 'Worker failed; inspect logs.' }
    }
} finally {
    foreach ($child in $children) {
        if (-not $child.HasExited) { Stop-Process -Id $child.Id -Force -ErrorAction SilentlyContinue }
    }
}
