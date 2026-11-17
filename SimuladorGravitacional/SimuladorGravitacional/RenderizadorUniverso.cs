using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace SimuladorGravitacional
{
    /// <summary>
    /// Responsável só pelo visual da tela do universo: fundo espacial, estrelas, grade,
    /// corpos com brilho e legenda. Não altera nem calcula nada da simulação.
    /// Recebe uma lista de <see cref="EstadoCorpo"/> (cópias) em vez do Universo, para servir
    /// igualmente à simulação ao vivo e ao replay dos dados gravados no banco.
    /// </summary>
    public sealed class RenderizadorUniverso : IDisposable
    {
        // Tamanho do corpo na tela: o raio real é minúsculo perto do universo, então é ajustado só para exibição
        private const double FatorRaioVisual = 0.3;
        private const double RaioMinimoPx = 1.5;
        private const float RaioMinimoParaEsfera = 3f;   // abaixo disso o corpo é só um ponto colorido
        private const float FatorHalo = 3f;              // tamanho do brilho em relação ao raio

        // Cor do corpo: densidade baixa = azulado, densidade alta = alaranjado
        private const double DensidadeFria = 500;
        private const double DensidadeQuente = 8000;

        private const int DivisoesGrade = 10;
        private const int QuantidadeEstrelas = 220;
        private const int LimiteRotulos = 25;            // acima disso os nomes poluiriam a tela
        private const float LadoMinimoBordaPx = 10f;     // tamanho mínimo da borda do universo na tela

        private static readonly Color FundoTopo = Color.FromArgb(3, 5, 14);
        private static readonly Color FundoBase = Color.FromArgb(16, 12, 44);
        private static readonly Color CorGrade = Color.FromArgb(22, 120, 150, 255);
        private static readonly Color CorMoldura = Color.FromArgb(70, 90, 140);
        private static readonly Color CorLimite = Color.FromArgb(150, 120, 160, 230);
        private static readonly Color CorTexto = Color.FromArgb(190, 200, 215, 240);
        private static readonly Color CorFria = Color.FromArgb(90, 170, 255);
        private static readonly Color CorQuente = Color.FromArgb(255, 170, 80);

        private struct Estrela
        {
            public float X;        // 0..1, proporcional à largura
            public float Y;        // 0..1, proporcional à altura
            public int Tamanho;    // px
            public int Brilho;     // 0..255
        }

        // Semente fixa: o céu é sempre o mesmo, não muda a cada execução
        private readonly Estrela[] _estrelas = GerarEstrelas(new Random(2024));

        private static Estrela[] GerarEstrelas(Random rnd)
        {
            var estrelas = new Estrela[QuantidadeEstrelas];
            for (int i = 0; i < estrelas.Length; i++)
            {
                estrelas[i].X = (float)rnd.NextDouble();
                estrelas[i].Y = (float)rnd.NextDouble();
                estrelas[i].Tamanho = rnd.NextDouble() < 0.85 ? 1 : 2;
                estrelas[i].Brilho = 60 + rnd.Next(196);
            }
            return estrelas;
        }

        // Criada uma vez: a tela é redesenhada várias vezes por segundo durante a simulação/replay
        private readonly Font _fonte = new Font("Segoe UI", 8f);

        // ------------------------------------------------------------ Câmera (zoom automático)
        //
        // O universo não tem paredes. Num encontro muito próximo, o passo de tempo grande
        // (3600 s) faz a atração virar um "chute" enorme num único passo, e o corpo é lançado
        // para fora da área inicial. Com a escala fixa da N1 esses corpos simplesmente sumiam
        // da tela, parecendo ter desaparecido. A câmera abaixo enquadra todos os corpos e
        // mantém a borda do universo desenhada como referência. Só a EXIBIÇÃO muda; a física
        // continua idêntica à da N1.

        private const double MargemVista = 0.06;          // folga em volta dos corpos (6% de cada lado)
        private const double SuavizacaoReducao = 0.15;    // fração do caminho percorrida por quadro ao aproximar

        /// <summary>Se falso, mostra só a área original do universo (como na N1).</summary>
        public bool ZoomAutomatico { get; set; } = true;

        // Região quadrada do espaço (em metros) que está sendo mostrada
        private double _vistaX, _vistaY, _vistaLado;
        private bool _vistaIniciada;

        /// <summary>Converte coordenadas do espaço (m) em pixels, com a mesma escala em X e Y.</summary>
        private readonly struct Transformacao
        {
            public readonly double Escala;              // pixels por metro
            private readonly double _origemX, _origemY;  // canto da vista, em metros
            private readonly double _deslocX, _deslocY;  // centraliza o quadrado na área do painel

            public Transformacao(Size area, double vistaX, double vistaY, double vistaLado)
            {
                Escala = Math.Min(area.Width, area.Height) / vistaLado;
                _origemX = vistaX;
                _origemY = vistaY;
                _deslocX = (area.Width - vistaLado * Escala) / 2;
                _deslocY = (area.Height - vistaLado * Escala) / 2;
            }

            public float X(double metros) => (float)(_deslocX + (metros - _origemX) * Escala);
            public float Y(double metros) => (float)(_deslocY + (metros - _origemY) * Escala);
        }

        /// <summary>Quantos corpos estão dentro e fora da área original do universo.</summary>
        public static (int Dentro, int Fora) ContarPorArea(IReadOnlyList<EstadoCorpo> corpos)
        {
            int dentro = 0;
            foreach (EstadoCorpo c in corpos)
            {
                if (c.PosicaoX >= 0 && c.PosicaoX <= Universo.LadoEmMetros &&
                    c.PosicaoY >= 0 && c.PosicaoY <= Universo.LadoEmMetros)
                    dentro++;
            }
            return (dentro, corpos.Count - dentro);
        }

        /// <summary>
        /// Texto da barra de status. Separa os dois motivos pelos quais um corpo "some": fusão
        /// (deixou de existir) e lançamento para fora da borda (continua existindo).
        /// </summary>
        public static string DescreverCorpos(IReadOnlyList<EstadoCorpo> corpos, int quantidadeInicial)
        {
            var (dentro, fora) = ContarPorArea(corpos);
            return $"Corpos: {corpos.Count} (dentro da borda: {dentro}, fora: {fora})  |  " +
                   $"Fusões: {Math.Max(0, quantidadeInicial - corpos.Count)}";
        }

        public void Desenhar(Graphics g, Size area, IReadOnlyList<EstadoCorpo>? corpos, string aviso)
        {
            if (area.Width <= 0 || area.Height <= 0) return;   // janela minimizada

            g.SmoothingMode = SmoothingMode.AntiAlias;
            DesenharFundo(g, area);
            DesenharEstrelas(g, area);

            AtualizarVista(corpos);
            var t = new Transformacao(area, _vistaX, _vistaY, _vistaLado);
            DesenharGrade(g, t, _fonte);

            if (corpos == null)
                DesenharAviso(g, area, _fonte, aviso);
            else
                DesenharCorpos(g, area, t, corpos, _fonte);

            DesenharLegenda(g, area, t, corpos, _fonte);
            DesenharMoldura(g, area);
        }

        public void Dispose() => _fonte.Dispose();

        // Quando um corpo sai do enquadramento a câmera se afasta NA HORA (para nenhum corpo
        // ficar invisível); quando todos voltam para perto ela se aproxima AOS POUCOS, evitando
        // que a imagem fique "pulando" a cada quadro.
        private void AtualizarVista(IReadOnlyList<EstadoCorpo>? corpos)
        {
            double minX = 0, minY = 0, maxX = Universo.LadoEmMetros, maxY = Universo.LadoEmMetros;

            if (ZoomAutomatico && corpos != null)
            {
                foreach (EstadoCorpo c in corpos)
                {
                    if (!double.IsFinite(c.PosicaoX) || !double.IsFinite(c.PosicaoY)) continue;
                    minX = Math.Min(minX, c.PosicaoX);
                    maxX = Math.Max(maxX, c.PosicaoX);
                    minY = Math.Min(minY, c.PosicaoY);
                    maxY = Math.Max(maxY, c.PosicaoY);
                }
            }

            // Mantém a vista quadrada para os corpos não ficarem achatados
            double lado = Math.Max(maxX - minX, maxY - minY);
            if (ZoomAutomatico) lado *= 1 + 2 * MargemVista;
            double alvoX = (minX + maxX - lado) / 2;
            double alvoY = (minY + maxY - lado) / 2;

            bool cabeNaVistaAtual = _vistaIniciada &&
                alvoX >= _vistaX && alvoY >= _vistaY &&
                alvoX + lado <= _vistaX + _vistaLado && alvoY + lado <= _vistaY + _vistaLado;

            if (!ZoomAutomatico || !cabeNaVistaAtual)
            {
                _vistaX = alvoX;
                _vistaY = alvoY;
                _vistaLado = lado;
                _vistaIniciada = true;
            }
            else
            {
                _vistaX += (alvoX - _vistaX) * SuavizacaoReducao;
                _vistaY += (alvoY - _vistaY) * SuavizacaoReducao;
                _vistaLado += (lado - _vistaLado) * SuavizacaoReducao;
            }
        }

        private static void DesenharFundo(Graphics g, Size area)
        {
            Rectangle retangulo = new Rectangle(0, 0, area.Width, area.Height);
            using (LinearGradientBrush fundo = new LinearGradientBrush(retangulo, FundoTopo, FundoBase, LinearGradientMode.Vertical))
            {
                g.FillRectangle(fundo, retangulo);
            }
        }

        private void DesenharEstrelas(Graphics g, Size area)
        {
            using (SolidBrush pincel = new SolidBrush(Color.White))
            {
                foreach (Estrela e in _estrelas)
                {
                    pincel.Color = Color.FromArgb(e.Brilho, 255, 255, 255);
                    g.FillRectangle(pincel, (int)(e.X * area.Width), (int)(e.Y * area.Height), e.Tamanho, e.Tamanho);
                }
            }
        }

        // A grade e a borda ocupam só a área ORIGINAL do universo: quando a câmera se afasta,
        // elas encolhem e mostram onde os corpos começaram.
        private static void DesenharGrade(Graphics g, Transformacao t, Font fonte)
        {
            float x0 = t.X(0), y0 = t.Y(0);
            float x1 = t.X(Universo.LadoEmMetros), y1 = t.Y(Universo.LadoEmMetros);

            using (Pen linha = new Pen(CorGrade))
            {
                for (int i = 1; i < DivisoesGrade; i++)
                {
                    float x = x0 + (x1 - x0) * i / DivisoesGrade;
                    float y = y0 + (y1 - y0) * i / DivisoesGrade;
                    g.DrawLine(linha, x, y0, x, y1);
                    g.DrawLine(linha, x0, y, x1, y);
                }
            }

            using (Pen borda = new Pen(CorLimite) { DashStyle = DashStyle.Dash })
            {
                // Com a câmera muito afastada a área original pode ficar menor que um pixel;
                // um tamanho mínimo e um rótulo mantêm visível onde os corpos começaram.
                float lado = x1 - x0;
                if (lado < LadoMinimoBordaPx)
                {
                    float cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
                    g.DrawRectangle(borda, cx - LadoMinimoBordaPx / 2, cy - LadoMinimoBordaPx / 2, LadoMinimoBordaPx, LadoMinimoBordaPx);
                    using (SolidBrush pincel = new SolidBrush(CorLimite))
                        g.DrawString("área inicial", fonte, pincel, cx + LadoMinimoBordaPx / 2 + 3, cy - fonte.Height / 2f);
                }
                else
                {
                    g.DrawRectangle(borda, x0, y0, lado, y1 - y0);
                }
            }
        }

        private static void DesenharCorpos(Graphics g, Size area, Transformacao t, IReadOnlyList<EstadoCorpo> corpos, Font fonte)
        {
            bool mostrarNomes = corpos.Count <= LimiteRotulos;

            using (SolidBrush pincelTexto = new SolidBrush(CorTexto))
            {
                foreach (EstadoCorpo c in corpos)
                {
                    float r = (float)Math.Max(RaioMinimoPx, c.Raio * t.Escala * FatorRaioVisual);
                    float x = t.X(c.PosicaoX);
                    float y = t.Y(c.PosicaoY);

                    // Com o zoom automático todos ficam dentro do painel; a checagem ainda serve
                    // para o modo sem zoom e, por ser negada, também descarta NaN/infinito
                    float margem = r * FatorHalo + 50;
                    if (!(x >= -margem && x <= area.Width + margem && y >= -margem && y <= area.Height + margem))
                        continue;

                    DesenharCorpo(g, x, y, r, CorDoCorpo(c));

                    if (mostrarNomes)
                        g.DrawString(c.Nome, fonte, pincelTexto, x + r + 4, y - fonte.Height / 2f);
                }
            }
        }

        private static void DesenharCorpo(Graphics g, float x, float y, float r, Color cor)
        {
            // Brilho ao redor (esmaece até ficar transparente)
            float raioHalo = r * FatorHalo;
            using (GraphicsPath caminhoHalo = new GraphicsPath())
            {
                caminhoHalo.AddEllipse(x - raioHalo, y - raioHalo, 2 * raioHalo, 2 * raioHalo);
                using (PathGradientBrush halo = new PathGradientBrush(caminhoHalo))
                {
                    halo.CenterColor = Color.FromArgb(110, cor);
                    halo.SurroundColors = new Color[] { Color.FromArgb(0, cor) };
                    g.FillPath(halo, caminhoHalo);
                }
            }

            if (r < RaioMinimoParaEsfera)
            {
                using (SolidBrush ponto = new SolidBrush(Mesclar(cor, Color.White, 0.4)))
                {
                    g.FillEllipse(ponto, x - r, y - r, 2 * r, 2 * r);
                }
                return;
            }

            // Esfera com luz vindo do canto superior esquerdo
            using (GraphicsPath caminhoEsfera = new GraphicsPath())
            {
                caminhoEsfera.AddEllipse(x - r, y - r, 2 * r, 2 * r);
                using (PathGradientBrush esfera = new PathGradientBrush(caminhoEsfera))
                {
                    esfera.CenterPoint = new PointF(x - r * 0.35f, y - r * 0.35f);
                    esfera.CenterColor = Mesclar(cor, Color.White, 0.75);
                    esfera.SurroundColors = new Color[] { Mesclar(cor, Color.Black, 0.55) };
                    g.FillPath(esfera, caminhoEsfera);
                }
            }
        }

        private static Color CorDoCorpo(EstadoCorpo c)
        {
            double t = Math.Clamp((c.Densidade - DensidadeFria) / (DensidadeQuente - DensidadeFria), 0.0, 1.0);
            return Mesclar(CorFria, CorQuente, t);
        }

        // Mistura duas cores: t = 0 devolve a, t = 1 devolve b
        private static Color Mesclar(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private static void DesenharAviso(Graphics g, Size area, Font fonte, string aviso)
        {
            using (SolidBrush pincel = new SolidBrush(CorTexto))
            using (StringFormat centro = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(aviso, fonte, pincel,
                             new RectangleF(0, 0, area.Width, area.Height), centro);
            }
        }

        private void DesenharLegenda(Graphics g, Size area, Transformacao t, IReadOnlyList<EstadoCorpo>? corpos, Font fonte)
        {
            double lado = Universo.LadoEmMetros / 1000;   // km
            double kmPorPixel = 1 / t.Escala / 1000;
            string texto = $"Universo (borda tracejada): {lado:N0} km × {lado:N0} km  |  1 px ≈ {kmPorPixel:N0} km";

            if (corpos != null)
            {
                int fora = ContarPorArea(corpos).Fora;
                if (fora > 0)
                    texto += ZoomAutomatico
                        ? $"  |  {fora} corpo(s) lançado(s) para fora da borda"
                        : $"  |  {fora} corpo(s) fora da borda (ligue o zoom automático para vê-los)";
            }

            using (SolidBrush pincel = new SolidBrush(CorTexto))
            {
                g.DrawString(texto, fonte, pincel, 8, area.Height - fonte.Height - 6);
            }
        }

        private static void DesenharMoldura(Graphics g, Size area)
        {
            using (Pen moldura = new Pen(CorMoldura))
            {
                g.DrawRectangle(moldura, 0, 0, area.Width - 1, area.Height - 1);
            }
        }
    }
}
