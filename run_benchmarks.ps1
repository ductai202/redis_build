$ErrorActionPreference = "Continue"

$REDIS_SERVER    = ".\redis-win\redis-server.exe"
$REDIS_BENCHMARK = ".\redis-win\redis-benchmark.exe"
$HYPERION_EXE    = ".\src\Hyperion.Server\bin\Release\net10.0\Hyperion.Server.exe"
$N = 1000000   # 1M requests
$R = 1000000   # 1M key space
$C = 500       # 500 concurrent clients

function Await-Port {
    param([int]$Port, [int]$TimeoutMs = 15000)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        try {
            $tcp = New-Object System.Net.Sockets.TcpClient
            $tcp.Connect("127.0.0.1", $Port)
            $tcp.Close()
            Write-Host "  Port $Port ready after $($sw.ElapsedMilliseconds)ms"
            return $true
        } catch { Start-Sleep -Milliseconds 300 }
    }
    return $false
}

function Run-Bench {
    param([string]$Label, [string]$OutFile)
    Write-Host "  Benchmarking: $Label ..."
    $result = & $REDIS_BENCHMARK -p 3000 -t set,get -c $C -n $N -r $R -q 2>&1
    $result | Out-File $OutFile -Encoding utf8
    $summary = $result | Select-String "requests per second"
    Write-Host ($summary | Select-Object -Last 2 | ForEach-Object { $_.Line })
}

# ---------- 1. Origin Redis ----------
Write-Host "`n=== Origin Redis (SET/GET, 1M) ==="
$redisProc = Start-Process -FilePath $REDIS_SERVER -ArgumentList "--port 3000" -PassThru -WindowStyle Hidden
if (-not (Await-Port 3000)) { Write-Error "Redis did not start"; exit 1 }
Run-Bench -Label "Origin Redis 1M" -OutFile "bench_origin_1M.txt"
Stop-Process -Id $redisProc.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# ---------- 2. Hyperion Single Thread ----------
Write-Host "`n=== Hyperion Single-Thread (SET/GET, 1M) ==="
$hypSingle = Start-Process -FilePath $HYPERION_EXE -ArgumentList "--port 3000 --mode single" -PassThru -WindowStyle Hidden
if (-not (Await-Port 3000 20000)) { Write-Error "Hyperion single did not start"; exit 1 }
Run-Bench -Label "Hyperion Single 1M" -OutFile "bench_hyperion_single_1M.txt"
Stop-Process -Id $hypSingle.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# ---------- 3. Hyperion Multi Thread ----------
Write-Host "`n=== Hyperion Multi-Thread (SET/GET, 1M) ==="
$hypMulti = Start-Process -FilePath $HYPERION_EXE -ArgumentList "--port 3000 --mode multi" -PassThru -WindowStyle Hidden
if (-not (Await-Port 3000 20000)) { Write-Error "Hyperion multi did not start"; exit 1 }
Run-Bench -Label "Hyperion Multi 1M" -OutFile "bench_hyperion_multi_1M.txt"
Stop-Process -Id $hypMulti.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

Write-Host "`nAll benchmarks done. Results written to bench_*.txt"
