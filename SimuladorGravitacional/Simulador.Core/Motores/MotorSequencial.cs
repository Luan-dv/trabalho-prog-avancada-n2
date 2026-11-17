namespace SimuladorGravitacional.Motores
{
    /// <summary>
    /// Algoritmo da N1, mantido sem alterações de lógica para servir de referência
    /// (resultado "correto") e de linha de base no benchmark.
    /// </summary>
    public sealed class MotorSequencial : MotorSimulacao
    {
        public override int Threads => 1;

        public override string Descricao => "Sequencial (N1)";

        // Percorre cada par uma única vez (j > i) e usa a 3ª lei de Newton (ação e reação):
        // a força calculada para o par é somada em i e subtraída em j. Isso faz metade das
        // contas, mas escreve em ax[j], que pertence a outro corpo; por isso essa forma
        // não pode ser dividida entre threads sem sincronização (ver MotorParalelo).
        public override void CalcularAceleracoes(List<Corpo> corpos, double[] ax, double[] ay)
        {
            int n = corpos.Count;

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    Corpo a = corpos[i], b = corpos[j];
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
        }

        public override void AplicarMovimento(List<Corpo> corpos, double[] ax, double[] ay, double passoDeTempo)
        {
            for (int i = 0; i < corpos.Count; i++)
            {
                Corpo c = corpos[i];
                c.VelocidadeX += ax[i] * passoDeTempo;
                c.VelocidadeY += ay[i] * passoDeTempo;
                c.PosicaoX += c.VelocidadeX * passoDeTempo;
                c.PosicaoY += c.VelocidadeY * passoDeTempo;
            }
        }

        public override bool BuscarColisao(List<Corpo> corpos, double[] raios, int linhaInicial,
                                           out int indiceA, out int indiceB)
        {
            for (int i = linhaInicial; i < corpos.Count; i++)
            {
                for (int j = i + 1; j < corpos.Count; j++)
                {
                    if (EstaoEmContato(corpos[i], raios[i], corpos[j], raios[j]))
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
    }
}
