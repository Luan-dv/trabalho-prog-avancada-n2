using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace SimuladorGravitacional
{
    /// <summary>
    /// Responsável só pelo visual da tela do universo: fundo espacial, estrelas, grade,
    /// corpos com brilho e legenda. Não altera nem calcula nada da simulação.
    /// </summary>
    public class RenderizadorUniverso
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

        private static readonly Color FundoTopo = Color.FromArgb(3, 5, 14);
        private static readonly Color FundoBase = Color.FromArgb(16, 12, 44);
        private static readonly Color CorGrade = Color.FromArgb(22, 120, 150, 255);
        private static readonly Color CorMoldura = Color.FromArgb(70, 90, 140);
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

        public void Desenhar(Graphics g, Size area, Universo? universo)
        {
            if (area.Width <= 0 || area.Height <= 0) return;   // janela minimizada

            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Font fonte = new Font("Segoe UI", 8f))
            {
                DesenharFundo(g, area);
                DesenharEstrelas(g, area);
                DesenharGrade(g, area);

                if (universo == null)
                    DesenharAviso(g, area, fonte);
                else
                    DesenharCorpos(g, area, universo, fonte);

                DesenharLegenda(g, area, fonte);
                DesenharMoldura(g, area);
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

        private static void DesenharGrade(Graphics g, Size area)
        {
            using (Pen linha = new Pen(CorGrade))
            {
                for (int i = 1; i < DivisoesGrade; i++)
                {
                    float x = (float)area.Width * i / DivisoesGrade;
                    float y = (float)area.Height * i / DivisoesGrade;
                    g.DrawLine(linha, x, 0, x, area.Height);
                    g.DrawLine(linha, 0, y, area.Width, y);
                }
            }
        }

        private static void DesenharCorpos(Graphics g, Size area, Universo universo, Font fonte)
        {
            double escalaX = area.Width / Universo.LadoEmMetros;
            double escalaY = area.Height / Universo.LadoEmMetros;
            double escalaRaio = Math.Min(escalaX, escalaY);   // mantém o corpo redondo, não achatado
            bool mostrarNomes = universo.Corpos.Count <= LimiteRotulos;

            using (SolidBrush pincelTexto = new SolidBrush(CorTexto))
            {
                foreach (Corpo c in universo.Corpos)
                {
                    float r = (float)Math.Max(RaioMinimoPx, c.Raio * escalaRaio * FatorRaioVisual);
                    float x = (float)(c.PosicaoX * escalaX);
                    float y = (float)(c.PosicaoY * escalaY);

                    // Corpos que saíram do universo (não há paredes) não precisam ser desenhados;
                    // a comparação negada também descarta valores NaN/infinitos
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

        private static Color CorDoCorpo(Corpo c)
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

        private static void DesenharAviso(Graphics g, Size area, Font fonte)
        {
            using (SolidBrush pincel = new SolidBrush(CorTexto))
            using (StringFormat centro = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString("Gere ou carregue um universo para começar", fonte, pincel,
                             new RectangleF(0, 0, area.Width, area.Height), centro);
            }
        }

        private static void DesenharLegenda(Graphics g, Size area, Font fonte)
        {
            double lado = Universo.LadoEmMetros / 1000;   // km
            using (SolidBrush pincel = new SolidBrush(CorTexto))
            {
                g.DrawString($"Universo: {lado:N0} km × {lado:N0} km", fonte, pincel,
                             8, area.Height - fonte.Height - 6);
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
