using System;
using System.Collections.Generic;

namespace SimuladorGravitacional
{
    /// <summary>
    /// Conjunto de corpos em interação gravitacional, avançado passo a passo no tempo.
    /// </summary>
    public class Universo
    {
        public const double LadoEmMetros = 1e9;   // lado (quadrado) do universo

        // Faixas usadas na geração aleatória
        private const double ExpoenteMassaBase = 24;       // massa entre 1e24 ...
        private const double ExpoenteMassaFaixa = 3;       // ... e 1e27 kg
        private const double DensidadeBase = 500;          // kg/m³
        private const double DensidadeFaixa = 7500;        // kg/m³
        private const double VelocidadeFaixa = 10000;      // m/s (centrada em zero)

        public List<Corpo> Corpos { get; } = new List<Corpo>();
        public int TotalIteracoes { get; set; }
        public double PassoDeTempo { get; set; }    // segundos entre iterações
        public int IteracaoAtual { get; private set; }
        public bool Concluido => IteracaoAtual >= TotalIteracoes;

        public void GerarAleatoriamente(int quantidade, Random rnd)
        {
            Corpos.Clear();
            IteracaoAtual = 0;

            for (int i = 0; i < quantidade; i++)
                Corpos.Add(CriarCorpoAleatorio(i + 1, rnd));
        }

        private static Corpo CriarCorpoAleatorio(int numero, Random rnd)
        {
            // A ordem das chamadas a rnd define o resultado para uma mesma semente
            double massa = Math.Pow(10, ExpoenteMassaBase + rnd.NextDouble() * ExpoenteMassaFaixa);
            double densidade = DensidadeBase + rnd.NextDouble() * DensidadeFaixa;
            double posX = rnd.NextDouble() * LadoEmMetros;
            double posY = rnd.NextDouble() * LadoEmMetros;
            double velX = (rnd.NextDouble() - 0.5) * VelocidadeFaixa;
            double velY = (rnd.NextDouble() - 0.5) * VelocidadeFaixa;

            return new Corpo($"Corpo {numero}", massa, densidade, posX, posY, velX, velY);
        }

        public void Avancar()
        {
            if (Concluido) return;

            var (ax, ay) = CalcularAceleracoes();
            AplicarMovimento(ax, ay);
            TratarColisoes();
            IteracaoAtual++;
        }

        // Interação gravitacional entre cada par de corpos
        private (double[] ax, double[] ay) CalcularAceleracoes()
        {
            int n = Corpos.Count;
            double[] ax = new double[n];
            double[] ay = new double[n];

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    Corpo a = Corpos[i], b = Corpos[j];
                    double dx = b.PosicaoX - a.PosicaoX;
                    double dy = b.PosicaoY - a.PosicaoY;
                    double d2 = dx * dx + dy * dy;
                    double d = Math.Sqrt(d2);
                    if (d == 0) continue;

                    double f = Corpo.ConstanteGravitacional * a.Massa * b.Massa / d2;   // F = G·m1·m2/d²
                    double fx = f * dx / d;
                    double fy = f * dy / d;

                    ax[i] += fx / a.Massa;    // a = F/m
                    ay[i] += fy / a.Massa;
                    ax[j] -= fx / b.Massa;
                    ay[j] -= fy / b.Massa;
                }
            }

            return (ax, ay);
        }

        // Atualiza velocidades e, em seguida, posições
        private void AplicarMovimento(double[] ax, double[] ay)
        {
            for (int i = 0; i < Corpos.Count; i++)
            {
                Corpo c = Corpos[i];
                c.VelocidadeX += ax[i] * PassoDeTempo;
                c.VelocidadeY += ay[i] * PassoDeTempo;
                c.PosicaoX += c.VelocidadeX * PassoDeTempo;
                c.PosicaoY += c.VelocidadeY * PassoDeTempo;
            }
        }

        // Funde pares em colisão, sempre recomeçando a busca após cada fusão
        private void TratarColisoes()
        {
            while (BuscarColisao(out int i, out int j))
            {
                Corpos[i] = Fundir(Corpos[i], Corpos[j]);
                Corpos.RemoveAt(j);
            }
        }

        // Primeiro par (na ordem dos índices) cujos raios se tocam
        private bool BuscarColisao(out int indiceA, out int indiceB)
        {
            for (int i = 0; i < Corpos.Count; i++)
            {
                for (int j = i + 1; j < Corpos.Count; j++)
                {
                    if (EstaoEmContato(Corpos[i], Corpos[j]))
                    {
                        indiceA = i;
                        indiceB = j;
                        return true;
                    }
                }
            }

            indiceA = indiceB = -1;
            return false;
        }

        private static bool EstaoEmContato(Corpo a, Corpo b)
        {
            double dx = b.PosicaoX - a.PosicaoX;
            double dy = b.PosicaoY - a.PosicaoY;
            double distancia = Math.Sqrt(dx * dx + dy * dy);

            return distancia <= a.Raio + b.Raio;
        }

        // Colisão perfeitamente inelástica: conserva a quantidade de movimento (Q = m·v) em X e Y
        private static Corpo Fundir(Corpo a, Corpo b)
        {
            double massa = a.Massa + b.Massa;
            double volume = a.Massa / a.Densidade + b.Massa / b.Densidade;
            Corpo maior = a.Massa >= b.Massa ? a : b;

            return new Corpo(
                maior.Nome, massa, massa / volume,
                (a.Massa * a.PosicaoX + b.Massa * b.PosicaoX) / massa,
                (a.Massa * a.PosicaoY + b.Massa * b.PosicaoY) / massa,
                (a.Massa * a.VelocidadeX + b.Massa * b.VelocidadeX) / massa,
                (a.Massa * a.VelocidadeY + b.Massa * b.VelocidadeY) / massa);
        }
    }
}
