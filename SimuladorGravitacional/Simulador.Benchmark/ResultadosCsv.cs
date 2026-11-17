using System.Globalization;

namespace Simulador.Benchmark
{
    public sealed record LinhaResultado(
        DateTime DataHora, string Modo, int Corpos, int Interacoes, int Threads, int Repeticao,
        double TempoTotalMs, double TempoCalculoMs, double TempoGravacaoMs, long LinhasGravadas, int CorposFinais,
        double CorposMedios);

    /// <summary>CSV padrão (vírgula como separador, ponto decimal), fácil de abrir em Python/R/planilhas.</summary>
    public static class ResultadosCsv
    {
        public const string Cabecalho =
            "data_hora,modo,corpos,interacoes,threads,repeticao,tempo_total_ms,tempo_calculo_ms,tempo_gravacao_ms,linhas_gravadas,corpos_finais,corpos_medios";

        private static readonly CultureInfo Cultura = CultureInfo.InvariantCulture;

        public static void Acrescentar(string caminho, LinhaResultado l)
        {
            string? pasta = Path.GetDirectoryName(Path.GetFullPath(caminho));
            if (pasta != null) Directory.CreateDirectory(pasta);

            bool novo = !File.Exists(caminho) || new FileInfo(caminho).Length == 0;
            using var escritor = new StreamWriter(caminho, append: true);
            if (novo) escritor.WriteLine(Cabecalho);

            escritor.WriteLine(string.Join(',',
                l.DataHora.ToString("yyyy-MM-dd HH:mm:ss", Cultura), l.Modo,
                l.Corpos.ToString(Cultura), l.Interacoes.ToString(Cultura), l.Threads.ToString(Cultura), l.Repeticao.ToString(Cultura),
                l.TempoTotalMs.ToString("F3", Cultura), l.TempoCalculoMs.ToString("F3", Cultura), l.TempoGravacaoMs.ToString("F3", Cultura),
                l.LinhasGravadas.ToString(Cultura), l.CorposFinais.ToString(Cultura), l.CorposMedios.ToString("F1", Cultura)));
        }

        public static List<LinhaResultado> Ler(string caminho)
        {
            return File.ReadLines(caminho)
                .Skip(1)
                .Where(linha => !string.IsNullOrWhiteSpace(linha))
                .Select(linha =>
                {
                    string[] c = linha.Split(',');
                    return new LinhaResultado(
                        DateTime.ParseExact(c[0], "yyyy-MM-dd HH:mm:ss", Cultura), c[1],
                        int.Parse(c[2], Cultura), int.Parse(c[3], Cultura), int.Parse(c[4], Cultura), int.Parse(c[5], Cultura),
                        double.Parse(c[6], Cultura), double.Parse(c[7], Cultura), double.Parse(c[8], Cultura),
                        long.Parse(c[9], Cultura), int.Parse(c[10], Cultura),
                        c.Length > 11 ? double.Parse(c[11], Cultura) : double.NaN);
                })
                .ToList();
        }
    }
}
