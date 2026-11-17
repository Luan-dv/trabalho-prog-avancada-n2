using System.Globalization;
using ScottPlot;

namespace Simulador.Benchmark
{
    /// <summary>
    /// Gera os gráficos do relatório a partir do CSV do benchmark.
    ///
    /// Usa a MEDIANA das repetições, e não a média: uma repetição atrapalhada por algo externo
    /// (antivírus, atualização do Windows, outro processo usando o disco) puxaria a média,
    /// mas quase não mexe na mediana.
    ///
    /// Speedup = tempo da N1 sequencial ÷ tempo da N2 com T threads, para o mesmo nº de corpos.
    /// </summary>
    public static class Graficos
    {
        private const int Largura = 1000, Altura = 620;

        public static int Gerar(string caminhoCsv, string pastaSaida)
        {
            List<LinhaResultado> linhas = ResultadosCsv.Ler(caminhoCsv);
            if (linhas.Count == 0) throw new InvalidOperationException("CSV vazio.");
            Directory.CreateDirectory(pastaSaida);

            var n1 = linhas.Where(l => l.Modo == "n1").ToList();
            var n2 = linhas.Where(l => l.Modo == "n2").ToList();
            int[] corpos = linhas.Select(l => l.Corpos).Distinct().Order().ToArray();
            int[] threads = n2.Select(l => l.Threads).Distinct().Order().ToArray();

            GraficoTempoPorCorpos(pastaSaida, "tempo_total_vs_corpos.png", "Tempo total × nº de corpos",
                                  n1, n2, corpos, threads, l => l.TempoTotalMs);
            GraficoTempoPorCorpos(pastaSaida, "tempo_calculo_vs_corpos.png", "Tempo de cálculo × nº de corpos",
                                  n1, n2, corpos, threads, l => l.TempoCalculoMs);
            GraficoGravacao(pastaSaida, n1, n2, corpos);
            GraficoSpeedup(pastaSaida, "speedup_calculo_vs_threads.png", "Speedup do cálculo × nº de threads (base: N1 sequencial)",
                           n1, n2, corpos, threads, l => l.TempoCalculoMs);
            GraficoSpeedup(pastaSaida, "speedup_total_vs_threads.png", "Speedup do tempo total × nº de threads (base: N1 sequencial + arquivo)",
                           n1, n2, corpos, threads, l => l.TempoTotalMs);
            EscreverResumo(Path.Combine(pastaSaida, "resumo.csv"), n1, n2, corpos, threads);

            Console.WriteLine($"Gráficos e resumo.csv gerados em {Path.GetFullPath(pastaSaida)}");
            return 0;
        }

        private static void GraficoTempoPorCorpos(string pasta, string arquivo, string titulo,
                                                  List<LinhaResultado> n1, List<LinhaResultado> n2,
                                                  int[] corpos, int[] threads, Func<LinhaResultado, double> tempoMs)
        {
            var plot = new Plot();
            AdicionarSerie(plot, "N1 sequencial + arquivo", corpos, c => Mediana(n1.Where(l => l.Corpos == c), tempoMs) / 1000, tracejada: true);

            foreach (int t in threads)
                AdicionarSerie(plot, $"N2 paralelo {t} thread(s) + MySQL", corpos,
                               c => Mediana(n2.Where(l => l.Corpos == c && l.Threads == t), tempoMs) / 1000);

            Finalizar(plot, titulo, "Nº de corpos", "Tempo (s)", Path.Combine(pasta, arquivo));
        }

        // A gravação não depende do nº de threads (só o cálculo é paralelo), então a N2 usa a
        // mediana de todas as execuções daquele nº de corpos.
        private static void GraficoGravacao(string pasta, List<LinhaResultado> n1, List<LinhaResultado> n2, int[] corpos)
        {
            var plot = new Plot();
            AdicionarSerie(plot, "Arquivo texto (N1)", corpos, c => Mediana(n1.Where(l => l.Corpos == c), l => l.TempoGravacaoMs) / 1000);
            AdicionarSerie(plot, "MySQL em lote (N2)", corpos, c => Mediana(n2.Where(l => l.Corpos == c), l => l.TempoGravacaoMs) / 1000);
            Finalizar(plot, "Tempo de gravação × nº de corpos (arquivo × banco)", "Nº de corpos", "Tempo (s)",
                      Path.Combine(pasta, "tempo_gravacao_vs_corpos.png"));
        }

        private static void GraficoSpeedup(string pasta, string arquivo, string titulo,
                                           List<LinhaResultado> n1, List<LinhaResultado> n2,
                                           int[] corpos, int[] threads, Func<LinhaResultado, double> tempoMs)
        {
            if (threads.Length == 0) return;
            var plot = new Plot();

            // Referência: speedup ideal (linear) seria igual ao nº de threads
            AdicionarSerie(plot, "Ideal (linear)", threads, t => t, tracejada: true);

            foreach (int c in corpos)
            {
                double baseN1 = Mediana(n1.Where(l => l.Corpos == c), tempoMs);
                AdicionarSerie(plot, $"{c} corpos", threads,
                               t => baseN1 / Mediana(n2.Where(l => l.Corpos == c && l.Threads == t), tempoMs));
            }

            double[] posicoes = threads.Select(t => (double)t).ToArray();
            string[] rotulos = threads.Select(t => t.ToString(CultureInfo.InvariantCulture)).ToArray();
            plot.Axes.Bottom.SetTicks(posicoes, rotulos);

            Finalizar(plot, titulo, "Nº de threads", "Speedup (×)", Path.Combine(pasta, arquivo));
        }

        private static void EscreverResumo(string caminho, List<LinhaResultado> n1, List<LinhaResultado> n2, int[] corpos, int[] threads)
        {
            var c = CultureInfo.InvariantCulture;
            var saida = new List<string>
            {
                "modo,corpos,corpos_medios,threads,repeticoes,mediana_total_ms,mediana_calculo_ms,mediana_gravacao_ms,speedup_calculo,speedup_total"
            };

            foreach (int qtd in corpos)
            {
                var base1 = n1.Where(l => l.Corpos == qtd).ToList();
                double calcN1 = Mediana(base1, l => l.TempoCalculoMs), totalN1 = Mediana(base1, l => l.TempoTotalMs);
                if (base1.Count > 0)
                    saida.Add(string.Join(',', "n1", qtd, F(base1.Average(l => l.CorposMedios)), 1, base1.Count, F(totalN1), F(calcN1),
                                          F(Mediana(base1, l => l.TempoGravacaoMs)), "1.00", "1.00"));

                foreach (int t in threads)
                {
                    var g = n2.Where(l => l.Corpos == qtd && l.Threads == t).ToList();
                    if (g.Count == 0) continue;
                    double calc = Mediana(g, l => l.TempoCalculoMs), total = Mediana(g, l => l.TempoTotalMs);
                    saida.Add(string.Join(',', "n2", qtd, F(g.Average(l => l.CorposMedios)), t, g.Count, F(total), F(calc), F(Mediana(g, l => l.TempoGravacaoMs)),
                                          (calcN1 / calc).ToString("F2", c), (totalN1 / total).ToString("F2", c)));
                }
            }

            File.WriteAllLines(caminho, saida);

            string F(double v) => v.ToString("F1", c);
        }

        private static void AdicionarSerie(Plot plot, string legenda, int[] xs, Func<int, double> y, bool tracejada = false)
        {
            // Pontos sem dado (cenário não executado) ficam de fora em vez de virar zero no gráfico
            var pontos = xs.Select(x => (x: (double)x, y: y(x))).Where(p => double.IsFinite(p.y)).ToArray();
            if (pontos.Length == 0) return;

            var serie = plot.Add.Scatter(pontos.Select(p => p.x).ToArray(), pontos.Select(p => p.y).ToArray());
            serie.LegendText = legenda;
            serie.LineWidth = 2;
            serie.MarkerSize = 7;
            if (tracejada) serie.LinePattern = LinePattern.Dashed;
        }

        private static void Finalizar(Plot plot, string titulo, string eixoX, string eixoY, string caminho)
        {
            plot.Title(titulo);
            plot.XLabel(eixoX);
            plot.YLabel(eixoY);
            plot.ShowLegend(Alignment.UpperLeft);
            // Eixo Y começando em zero para não exagerar visualmente as diferenças
            plot.Axes.AutoScale();
            plot.Axes.SetLimitsY(0, plot.Axes.GetLimits().Top * 1.05);
            plot.SavePng(caminho, Largura, Altura);
        }

        private static double Mediana(IEnumerable<LinhaResultado> linhas, Func<LinhaResultado, double> valor)
        {
            double[] v = linhas.Select(valor).Order().ToArray();
            if (v.Length == 0) return double.NaN;
            return v.Length % 2 == 1 ? v[v.Length / 2] : (v[v.Length / 2 - 1] + v[v.Length / 2]) / 2;
        }
    }
}
