# Simulador Gravitacional 2D — N2

Simulação de N corpos em 2D (C# / .NET 10 / Windows Forms) com:

- **persistência em MySQL** de todos os estados de todos os corpos em cada interação;
- **replay** das simulações gravadas;
- **cálculo paralelo** (Task Parallel Library) com nº de threads configurável;
- **benchmark** comparando a versão N1 (sequencial + arquivo) com a N2 (paralela + MySQL).

## Estrutura

```
schema.sql                  modelo do banco (tabelas, chaves, índices)
docker-compose.yml          MySQL 8.4 local opcional (executa o schema.sql sozinho)
appsettings.example.json    modelo da configuração de conexão
benchmark.ps1               roda a matriz de cenários e gera CSV + gráficos
SimuladorGravitacional/
  Simulador.Core/           física, motores sequencial/paralelo, gravação (arquivo e MySQL)
  SimuladorGravitacional/   interface Windows Forms (simulação + replay)
  Simulador.Benchmark/      linha de comando: executar, verificar, graficos
  Simulador.Tests/          testes automatizados (xUnit)
```

A versão N1 original está preservada na tag git **`n1`** (`git checkout n1`). Para o
benchmark ela também está disponível pelo parâmetro `--modo n1`: algoritmo sequencial da
N1 com gravação em arquivo texto.

## 1. Instalar as dependências

1. **.NET SDK 10**: https://dotnet.microsoft.com/download (confira com `dotnet --list-sdks`).
2. **MySQL 8.x**, de uma das formas abaixo:
   - **MySQL instalado no Windows** (MySQL Installer / MySQL Server 8.0+); ou
   - **Docker**: `docker compose up -d`. Se já existir um MySQL usando a porta 3306, use
     outra porta: `$env:MYSQL_PORTA=3307; docker compose up -d`.
3. Os pacotes NuGet (MySqlConnector, ScottPlot, xUnit) são baixados no primeiro build:
   ```powershell
   cd SimuladorGravitacional
   dotnet build
   ```

## 2. Criar o banco com o schema.sql

**MySQL instalado:** num terminal com o cliente `mysql` no PATH (ou pelo MySQL Workbench,
abrindo e executando o arquivo):

```powershell
Get-Content schema.sql | mysql -u root -p
```

Depois crie um usuário para a aplicação (recomendado, para não usar o root):

```sql
CREATE USER 'simulador'@'localhost' IDENTIFIED BY 'escolha_uma_senha';
GRANT SELECT, INSERT, UPDATE, DELETE ON simulador.* TO 'simulador'@'localhost';
```

**Docker:** nada a fazer. O `docker-compose.yml` executa o `schema.sql` na primeira
subida e cria o usuário `simulador` / `simulador_dev`.

### Modelo

| Tabela | Conteúdo | Chaves e índices |
|---|---|---|
| `simulacao` | parâmetros (corpos, interações, passo, threads, semente), status e tempos (total, cálculo, gravação) | PK `id`; índice `criada_em` (listagem do replay) |
| `corpo` | nome de cada corpo (não muda com o tempo) | PK `(simulacao_id, corpo_id)`; FK → `simulacao` |
| `estado_corpo` | massa, densidade, raio, posição, velocidade, aceleração e força de cada corpo em cada interação | PK `(simulacao_id, interacao, corpo_id)`, que é o índice clusterizado usado pelo replay; índice `(simulacao_id, corpo_id, interacao)` para trajetórias; FK → `corpo` |
| `fusao` | qual corpo absorveu qual, e em que interação | PK `(simulacao_id, corpo_absorvido_id)`; índice `(simulacao_id, interacao)`; FKs → `corpo` |

Todas as FKs usam `ON DELETE CASCADE`: excluir uma simulação apaga tudo dela. As
justificativas de cada decisão estão comentadas no próprio `schema.sql`.

## 3. Configurar a conexão

Copie o modelo e ajuste host, porta, usuário, senha e banco:

```powershell
Copy-Item appsettings.example.json appsettings.json
```

O `appsettings.json` fica **na raiz do repositório** e está no `.gitignore`, porque contém
a senha. Os programas o procuram subindo a partir da pasta atual e da pasta do executável.
Para usar outro arquivo, defina a variável de ambiente `SIMULADOR_CONFIG` com o caminho
dele.

Também há a seção `Gravacao`, com o tamanho do lote: `LinhasPorInsert` (linhas por comando
INSERT) e `LinhasPorTransacao` (linhas por COMMIT).

## 4. Rodar a simulação

```powershell
cd SimuladorGravitacional
dotnet run --project SimuladorGravitacional -c Release
```

1. Escolha nº de corpos, iterações, passo de tempo, **threads** e **semente**
   (0 = aleatória; a semente usada é gravada e permite repetir o mesmo universo).
2. **Gerar universo** mostra o estado inicial.
3. **Simular** roda em segundo plano, gravando no MySQL (se "Gravar no banco" estiver
   marcado). **Iterações/s** controla a velocidade da animação (padrão 30; pode ser mudado
   durante a simulação). Com **0**, roda o mais rápido possível. A espera da animação não
   entra nos tempos medidos. **Parar** interrompe e grava o que já foi calculado, com status
   `interrompida`.
4. **Zoom automático** afasta a câmera para mostrar os corpos lançados para fora da área
   inicial (borda tracejada). A barra de status separa os corpos dentro e fora da borda e
   conta as fusões.
5. Ao terminar, aparece o resumo com os tempos e o id da simulação no banco.

## 5. Replay

Clique em **Replay...** na tela principal.

- A lista mostra as simulações gravadas: parâmetros, status e tempos.
- Selecione uma e clique em **Abrir replay** (ou dê duplo clique).
- **▶ Reproduzir** / **⏸ Pausar**, velocidade em interações por segundo, e a barra para
  ir direto a qualquer interação. As fusões da interação aparecem abaixo do desenho.
- **Excluir** remove a simulação e todos os seus estados.

Os estados são carregados em blocos de ~200 mil linhas (com pré-carregamento do bloco
seguinte), então simulações enormes não precisam caber inteiras na memória.

## 6. Benchmark

Com o banco configurado:

```powershell
.\benchmark.ps1
```

Por padrão roda 100, 500, 1000 e 2000 corpos; 100 interações; 1, 2, 4 e 8 threads; 3
repetições; semente fixa 2024. Tudo pode ser alterado:

```powershell
.\benchmark.ps1 -Corpos 500,2000 -Threads 1,2,4,8,12 -Interacoes 50 -Repeticoes 5
```

Saídas em `benchmark/resultados/`:

| Arquivo | Conteúdo |
|---|---|
| `resultados.csv` | uma linha por repetição: `tempo_total_ms`, `tempo_calculo_ms`, `tempo_gravacao_ms`, linhas gravadas, corpos finais e `corpos_medios` |
| `resumo.csv` | medianas por cenário e speedups em relação à N1 |
| `verificacao.csv` | diferença entre sequencial e paralelo, interação por interação |
| `tempo_total_vs_corpos.png`, `tempo_calculo_vs_corpos.png`, `tempo_gravacao_vs_corpos.png` | tempo × nº de corpos |
| `speedup_calculo_vs_threads.png`, `speedup_total_vs_threads.png` | speedup × nº de threads |

O benchmark apaga do banco as simulações que gravou (fora da medição). Para mantê-las,
use `--manter-dados` direto na linha de comando. Comandos avulsos:

```powershell
$b = "SimuladorGravitacional/Simulador.Benchmark/bin/Release/net10.0/Simulador.Benchmark.exe"
& $b executar --modo n1 --corpos 1000 --interacoes 100 --repeticoes 3 --csv r.csv
& $b executar --modo n2 --corpos 1000 --interacoes 100 --threads 8 --repeticoes 3 --csv r.csv
& $b verificar --corpos 1000 --interacoes 100 --threads 8
& $b graficos --csv r.csv --saida graficos
```

## 7. Testes

```powershell
cd SimuladorGravitacional
dotnet test
```

Os testes cobrem:
- paralelo × sequencial (tolerância 1e-9 em 10 interações; mesmas fusões em 200);
- resultado idêntico com 1 e 8 threads;
- mesmo par de colisão nas duas versões;
- busca de colisões otimizada × laço original da N1 (fusões idênticas);
- conservação de massa e momento na fusão;
- formato do arquivo da N1.

## Decisões de projeto (resumo)

- **Paralelização:** `Parallel.ForEach` sobre faixas de corpos, com `MaxDegreeOfParallelism`
  igual ao nº de threads. Cada thread só escreve nas posições dos seus próprios corpos, e
  os dados compartilhados são apenas lidos durante cada fase. Por isso não há condição de
  corrida nem locks. A busca de colisão usa `ParallelLoopState.Break()` para achar o mesmo
  "primeiro par" da versão sequencial. Detalhes nos comentários de
  `Simulador.Core/Motores/MotorParalelo.cs`.
- **Colisões:** a N1 recomeçava a busca do zero após cada fusão (O(n²) por fusão, centenas
  de fusões na 1ª interação). A N2 retoma a busca de onde estava e chega à mesma sequência
  de fusões. Sem isso, esse trecho dominava o tempo e anulava o paralelismo (Lei de
  Amdahl).
- **Gravação em lote:** INSERT de várias linhas + transações periódicas. Detalhes e
  alternativas consideradas em `Simulador.Core/Persistencia/GravadorMySql.cs`.
- **Interface:** a simulação roda numa thread de fundo e entrega à tela cópias imutáveis
  dos corpos (`EstadoCorpo`). A tela nunca lê objetos que a simulação está alterando.
