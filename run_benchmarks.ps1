$ErrorActionPreference = "Continue"

$REDIS_SERVER    = ".\redis-win\redis-server.exe"
$REDIS_BENCHMARK = ".\redis-win\redis-benchmark.exe"
$HYPERION_EXE    = ".\src\Hyperion.Server\bin\Release\net10.0\Hyperion.Server.exe"

# ---- Benchmark parameters (identical to Golang project) ----
$N       = 1000000  # 1M requests
$R       = 1000000  # 1M key space
$C       = 500      # 500 concurrent clients
$THREADS = 3        # benchmark client threads (same as Go project)

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
    Write-Host ""
    Write-Host "  Benchmarking: $Label ..."

    # Run WITHOUT -q so we get the full Summary + latency block (same as Golang project)
    $result = & $REDIS_BENCHMARK `
        -h 127.0.0.1 -p 3000 `
        -t set,get `
        -c $C `
        -n $N `
        -r $R `
        --threads $THREADS `
        2>&1

    $result | Out-File $OutFile -Encoding utf8

    # --- Parse and pretty-print Summary blocks ---
    $inSummary  = $false
    $inLatency  = $false
    $headerLine = ""
    $pending    = @()

    foreach ($line in $result) {
        $l = $line.ToString().Trim()

        if ($l -match "^====") {
            Write-Host ""
            Write-Host $l
            $inSummary = $false
            $inLatency = $false
            continue
        }
        if ($l -match "Summary:") {
            $inSummary = $true
            Write-Host "Summary:"
            continue
        }
        if ($inSummary -and $l -match "throughput summary") {
            Write-Host "  $l"
            continue
        }
        if ($inSummary -and $l -match "latency summary") {
            Write-Host "  $l"
            $inLatency = $true
            continue
        }
        if ($inLatency -and $l -match "avg") {
            Write-Host "          $l"
            continue
        }
        if ($inLatency -and $l -match "^\d") {
            Write-Host "        $l"
            $inLatency  = $false
            $inSummary  = $false
            continue
        }
    }
}

# Build Release first
Write-Host "=== Building Hyperion (Release) ==="
dotnet build .\src\Hyperion.Server\Hyperion.Server.csproj -c Release -v quiet
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit 1 }
Write-Host "  Build OK"

# ---------- 1. Origin Redis ----------
Write-Host ""
Write-Host "=== Origin Redis (SET/GET, 1M, $C clients, $THREADS threads) ==="
$redisProc = Start-Process -FilePath $REDIS_SERVER -ArgumentList "--port 3000" -PassThru -WindowStyle Hidden
if (-not (Await-Port 3000)) { Write-Error "Redis did not start"; exit 1 }
Run-Bench -Label "Origin Redis 1M" -OutFile "bench_Origin_Redis_1M.txt"
Stop-Process -Id $redisProc.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# ---------- 2. Hyperion Single Thread ----------
Write-Host ""
Write-Host "=== Hyperion Single-Thread (SET/GET, 1M, $C clients, $THREADS threads) ==="
$hypSingle = Start-Process -FilePath $HYPERION_EXE -ArgumentList "--port 3000 --mode single" -PassThru -WindowStyle Hidden
if (-not (Await-Port 3000 20000)) { Write-Error "Hyperion single did not start"; exit 1 }
Run-Bench -Label "Hyperion Single 1M" -OutFile "bench_Hyperion_Single_1M.txt"
Stop-Process -Id $hypSingle.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# ---------- 3. Hyperion Multi Thread ----------
Write-Host ""
Write-Host "=== Hyperion Multi-Thread (SET/GET, 1M, $C clients, $THREADS threads) ==="
$hypMulti = Start-Process -FilePath $HYPERION_EXE -ArgumentList "--port 3000 --mode multi" -PassThru -WindowStyle Hidden
if (-not (Await-Port 3000 20000)) { Write-Error "Hyperion multi did not start"; exit 1 }
Run-Bench -Label "Hyperion Multi 1M" -OutFile "bench_Hyperion_Multi_1M.txt"
Stop-Process -Id $hypMulti.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

Write-Host ""
Write-Host "All benchmarks done. Results written to bench_*.txt"
Write-Host "Tip: Open the .txt files for the full latency histograms."
