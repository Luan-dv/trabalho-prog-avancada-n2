using System.Globalization;
using SimuladorGravitacional;
using SimuladorGravitacional.Motores;
using SimuladorGravitacional.Persistencia;

namespace Simulador.Benchmark
{
    /// <summary>
    /// Ferramenta de linha de comando para medir desempenho.
    ///
    ///   executar  --modo n1|n2 --corpos N --interacoes I [--threads T] [--repeticoes R]
    ///             [--semente S] [--passo P] [--csv arquivo] [--pasta-arquivos dir] [--manter-dados]
    ///   verificar --corpos N --interacoes I [--threads T] [--semente S] [--passo P] [--csv arquivo]
    ///   graficos  --csv resultados.csv [--saida pasta]
    ///
    /// Modos:
    ///   n1 = algoritmo sequencial da N1 + gravação em arquivo texto (linha de base)
    ///   n2 = algoritmo paralelo com T threads + gravação no MySQL
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            // Saída e CSV sempre com ponto decimal, independentemente do Windows estar em pt-BR
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Console.OutputEncoding = System.Text.Encoding.UTF8;   // acentos corretos no terminal do Windows

            if (args.Length == 0)
            {
                Console.WriteLine("Uso: Simulador.Benchmark executar|verificar|graficos [opções]  (veja o README)");
                return 1;
            }

            try
            {
                var opcoes = Opcoes.Ler(args.Skip(1));
                return args[0] switch
                {
                    "executar" => Executar(opcoes),
                    "verificar" => Verificar(opcoes),
                    "graficos" => Graficos.Gerar(opcoes.Texto("csv", "resultados.csv"), opcoes.Texto("saida", ".")),
                    _ => throw new ArgumentException($"Comando desconhecido: {args[0]}")
                };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERRO: " + ex.Message);
                return 2;
            }
        }

        private static int Executar(Opcoes o)
        {
            string modo = o.Texto("modo", "n2").ToLowerInvariant();
            if (modo != "n1" && modo != "n2") throw new ArgumentException("--modo deve ser n1 ou n2");

            int corpos = o.Inteiro("corpos", 100);
            int interacoes = o.Inteiro("interacoes", 100);
            int threads = modo == "n1" ? 1 : o.Inteiro("threads", Environment.ProcessorCount);
            int repeticoes = o.Inteiro("repeticoes", 3);
            int semente = o.Inteiro("semente", 2024);
            double passo = o.Real("passo", 3600);
            string csv = o.Texto("csv", "resultados.csv");
            bool manterDados = o.Tem("manter-dados");

            // Por padrão os arquivos do modo N1 vão para a pasta temporária do sistema: pastas
            // sincronizadas (OneDrive, Dropbox) disputariam o disco durante a medição.
            string pastaArquivos = o.Texto("pasta-arquivos", Path.Combine(Path.GetTempPath(), "simulador_benchmark"));
            Directory.CreateDirectory(pastaArquivos);

            Configuracao? configuracao = modo == "n2" ? Configuracao.Carregar() : null;

            Console.WriteLine($"[{modo}] {corpos} corpos, {interacoes} interações, {threads} thread(s), {repeticoes} repetição(ões)");

            // Aquecimento: a primeira execução paga a compilação JIT e a abertura da conexão
            // (que depois fica no pool). Sem isso a 1ª repetição sairia artificialmente lenta.
            RodarUmaVez(modo, 50, 5, threads, semente, passo, pastaArquivos, configuracao, manterDados: false);

            for (int r = 1; r <= repeticoes; r++)
            {
                // Coleta antes de medir, para o GC não disparar no meio por lixo da repetição anterior
                GC.Collect();
                GC.WaitForPendingFinalizers();

                ResultadoExecucao res = RodarUmaVez(modo, corpos, interacoes, threads, semente, passo,
                                                    pastaArquivos, configuracao, manterDados);

                Console.WriteLine($"  rep {r}: total {res.TempoTotal.TotalMilliseconds,10:F1} ms | " +
                                  $"cálculo {res.TempoCalculo.TotalMilliseconds,10:F1} ms | " +
                                  $"gravação {res.TempoGravacao.TotalMilliseconds,10:F1} ms | " +
                                  $"{res.LinhasGravadas:N0} linhas | média de {res.MediaCorposPorInteracao:F0} corpos/interação");

                ResultadosCsv.Acrescentar(csv, new LinhaResultado(
                    DateTime.Now, modo, corpos, interacoes, threads, r,
                    res.TempoTotal.TotalMilliseconds, res.TempoCalculo.TotalMilliseconds,
                    res.TempoGravacao.TotalMilliseconds, res.LinhasGravadas, res.CorposFinais, res.MediaCorposPorInteracao));
            }

            return 0;
        }

        private static ResultadoExecucao RodarUmaVez(string modo, int corpos, int interacoes, int threads, int semente,
                                                     double passo, string pastaArquivos, Configuracao? configuracao,
                                                     bool manterDados)
        {
            // Mesma semente => exatamente o mesmo universo inicial nos dois modos
            MotorSimulacao motor = modo == "n1" ? new MotorSequencial() : new MotorParalelo(threads);
            Universo universo = VerificacaoParalelismo.Criar(corpos, interacoes, passo, semente, motor);
            var dados = new DadosSimulacao(
                $"Benchmark {modo.ToUpperInvariant()} {corpos} corpos {threads}T", null, "benchmark",
                corpos, interacoes, passo, threads, semente);

            if (modo == "n1")
            {
                string arquivo = Path.Combine(pastaArquivos, $"n1_{corpos}_{Guid.NewGuid():N}.txt");
                ResultadoExecucao resultado;
                using (var gravador = new GravadorArquivoTexto(arquivo))
                    resultado = ExecutorSimulacao.Executar(universo, dados, gravador);

                if (!manterDados) File.Delete(arquivo);
                return resultado;
            }
            else
            {
                ResultadoExecucao resultado;
                long simulacaoId;
                using (var gravador = new GravadorMySql(configuracao!))
                {
                    resultado = ExecutorSimulacao.Executar(universo, dados, gravador);
                    simulacaoId = gravador.SimulacaoId;
                }

                // A limpeza fica fora da medição; sem ela o banco cresceria milhões de linhas por rodada
                if (!manterDados) new RepositorioSimulacoes(configuracao!).Excluir(simulacaoId);
                return resultado;
            }
        }

        // Mostra como a diferença entre sequencial e paralelo evolui ao longo das interações
        private static int Verificar(Opcoes o)
        {
            int corpos = o.Inteiro("corpos", 500);
            int interacoes = o.Inteiro("interacoes", 100);
            int threads = o.Inteiro("threads", Environment.ProcessorCount);
            int semente = o.Inteiro("semente", 2024);
            double passo = o.Real("passo", 3600);
            string? csv = o.Tem("csv") ? o.Texto("csv", "") : null;

            Console.WriteLine($"Comparando N1 sequencial × N2 paralelo ({threads} threads): {corpos} corpos, " +
                              $"{interacoes} interações, semente {semente}");
            Console.WriteLine(" interação | corpos seq/par | dif. posição (rel. ao universo) | dif. velocidade (rel.)");

            var linhas = new List<string> { "interacao,corpos_sequencial,corpos_paralelo,mesmos_corpos,dif_rel_posicao,dif_rel_velocidade" };
            bool fusoesIguais = true;

            ComparacaoSimulacoes final = VerificacaoParalelismo.Comparar(corpos, interacoes, passo, semente, threads, (k, c) =>
            {
                fusoesIguais &= c.MesmosCorpos;
                linhas.Add(string.Join(',', k, c.CorposSequencial, c.CorposParalelo, c.MesmosCorpos ? 1 : 0,
                                       c.MaiorDiferencaRelativaPosicao.ToString("E3"), c.MaiorDiferencaRelativaVelocidade.ToString("E3")));

                if (k <= 10 || k % Math.Max(1, interacoes / 10) == 0)
                    Console.WriteLine($" {k,9} | {c.CorposSequencial,6}/{c.CorposParalelo,-7} | " +
                                      $"{c.MaiorDiferencaRelativaPosicao,30:E2} | {c.MaiorDiferencaRelativaVelocidade,22:E2}");
            });

            if (csv != null) File.WriteAllLines(csv, linhas);

            Console.WriteLine();
            Console.WriteLine(fusoesIguais
                ? "OK: mesmas fusões e mesmos corpos em todas as interações."
                : "ATENÇÃO: as versões divergiram em quais corpos existem.");
            Console.WriteLine("Diferenças de ~1e-15 vêm do arredondamento (soma em outra ordem); o crescimento posterior é");
            Console.WriteLine("a sensibilidade caótica do problema de N corpos amplificando esse arredondamento.");

            return fusoesIguais && final.MesmosCorpos ? 0 : 3;
        }
    }

    /// <summary>Parser simples de "--nome valor" e "--flag".</summary>
    internal sealed class Opcoes
    {
        private readonly Dictionary<string, string?> _valores = new(StringComparer.OrdinalIgnoreCase);

        public static Opcoes Ler(IEnumerable<string> args)
        {
            var o = new Opcoes();
            string[] lista = args.ToArray();
            for (int i = 0; i < lista.Length; i++)
            {
                if (!lista[i].StartsWith("--")) throw new ArgumentException($"Opção inválida: {lista[i]}");
                string nome = lista[i][2..];
                string? valor = i + 1 < lista.Length && !lista[i + 1].StartsWith("--") ? lista[++i] : null;
                o._valores[nome] = valor;
            }
            return o;
        }

        public bool Tem(string nome) => _valores.ContainsKey(nome);

        public string Texto(string nome, string padrao) =>
            _valores.TryGetValue(nome, out string? v) && v != null ? v : padrao;

        public int Inteiro(string nome, int padrao) =>
            _valores.TryGetValue(nome, out string? v) && v != null ? int.Parse(v, CultureInfo.InvariantCulture) : padrao;

        public double Real(string nome, double padrao) =>
            _valores.TryGetValue(nome, out string? v) && v != null ? double.Parse(v, CultureInfo.InvariantCulture) : padrao;
    }
}
