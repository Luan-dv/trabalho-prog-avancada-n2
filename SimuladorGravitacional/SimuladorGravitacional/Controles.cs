using System.Globalization;

namespace SimuladorGravitacional
{
    /// <summary>Painel com double buffering para desenhar sem piscar.</summary>
    public class PainelUniverso : Panel
    {
        public PainelUniverso()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }

    /// <summary>Tabela de corpos usada tanto na simulação quanto no replay.</summary>
    public static class TabelaCorpos
    {
        public static DataGridView Criar(int altura) => new DataGridView
        {
            Dock = DockStyle.Bottom,
            Height = altura,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };

        public static void Preencher(DataGridView grade, IReadOnlyList<EstadoCorpo> corpos)
        {
            grade.DataSource = corpos.Select(c => new
            {
                c.Id,
                c.Nome,
                Massa = Formatar(c.Massa),
                Densidade = Formatar(c.Densidade),
                Raio = Formatar(c.Raio),
                PosX = Formatar(c.PosicaoX),
                PosY = Formatar(c.PosicaoY),
                VelX = Formatar(c.VelocidadeX),
                VelY = Formatar(c.VelocidadeY),
                AcelX = Formatar(c.AceleracaoX),
                AcelY = Formatar(c.AceleracaoY)
            }).ToList();
        }

        // NaN representa "sem valor" (ex.: aceleração da interação 0, gravada como NULL)
        private static string Formatar(double valor) =>
            double.IsNaN(valor) ? "—" : valor.ToString("E3", CultureInfo.CurrentCulture);
    }
}
