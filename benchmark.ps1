<#
.SYNOPSIS
    Roda o benchmark N1 (sequencial + arquivo) × N2 (paralelo + MySQL) e gera CSV e gráficos.

.DESCRIPTION
    Para cada nº de corpos, executa:
      - N1 uma vez (o algoritmo sequencial não usa threads);
      - N2 para cada nº de threads.
    Cada cenário é repetido -Repeticoes vezes, sempre com a mesma semente (mesmos dados de
    entrada). Ao final roda a verificação sequencial × paralelo e gera os gráficos.

    Saídas (em -Saida):
      resultados.csv      uma linha por repetição (tempo total, cálculo e gravação)
      resumo.csv          medianas e speedups por cenário
      verificacao.csv     diferença sequencial × paralelo interação a interação
      *.png               gráficos

.EXAMPLE
    .\benchmark.ps1
    .\benchmark.ps1 -Corpos 100,500 -Threads 1,2,4 -Interacoes 50 -Repeticoes 2
#>
param(
    [int[]]  $Corpos     = @(100, 500, 1000, 2000),
    [int[]]  $Threads    = @(1, 2, 4, 8),
    [int]    $Interacoes = 100,
    [int]    $Repeticoes = 3,
    [int]    $Semente    = 2024,
    [double] $Passo      = 3600,
    [string] $Saida      = "benchmark/resultados"
)

$ErrorActionPreference = "Stop"
$raiz = $PSScriptRoot
$projeto = Join-Path $raiz "SimuladorGravitacional/Simulador.Benchmark/Simulador.Benchmark.csproj"

# Release: sem isso o JIT não otimiza e os tempos não representam o programa de verdade
Write-Host "Compilando em Release..." -ForegroundColor Cyan
dotnet build $projeto -c Release -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Falha na compilação." }

$exe = Join-Path $raiz "SimuladorGravitacional/Simulador.Benchmark/bin/Release/net10.0/Simulador.Benchmark.exe"
$pastaSaida = Join-Path $raiz $Saida
New-Item -ItemType Directory -Force $pastaSaida | Out-Null
$csv = Join-Path $pastaSaida "resultados.csv"
if (Test-Path $csv) { Remove-Item $csv }   # cada rodada do script começa um CSV novo

$comuns = @("--interacoes", $Interacoes, "--repeticoes", $Repeticoes, "--semente", $Semente,
            "--passo", $Passo.ToString([Globalization.CultureInfo]::InvariantCulture), "--csv", $csv)

function Invoke-Benchmark([string[]] $argumentos) {
    & $exe @argumentos
    if ($LASTEXITCODE -ne 0) { throw "Benchmark falhou: $($argumentos -join ' ')" }
}

$inicio = Get-Date
foreach ($n in $Corpos) {
    Write-Host "`n=== $n corpos ===" -ForegroundColor Cyan
    Invoke-Benchmark (@("executar", "--modo", "n1", "--corpos", $n) + $comuns)
    foreach ($t in $Threads) {
        Invoke-Benchmark (@("executar", "--modo", "n2", "--corpos", $n, "--threads", $t) + $comuns)
    }
}

Write-Host "`n=== Verificação sequencial × paralelo ===" -ForegroundColor Cyan
$maxThreads = ($Threads | Measure-Object -Maximum).Maximum
& $exe verificar --corpos ($Corpos | Select-Object -Last 1) --interacoes $Interacoes --threads $maxThreads `
                 --semente $Semente --csv (Join-Path $pastaSaida "verificacao.csv")

Write-Host "`n=== Gráficos ===" -ForegroundColor Cyan
Invoke-Benchmark @("graficos", "--csv", $csv, "--saida", $pastaSaida)

$duracao = (Get-Date) - $inicio
Write-Host ("`nConcluído em {0:mm\:ss}. Resultados em {1}" -f $duracao, (Resolve-Path $pastaSaida)) -ForegroundColor Green
