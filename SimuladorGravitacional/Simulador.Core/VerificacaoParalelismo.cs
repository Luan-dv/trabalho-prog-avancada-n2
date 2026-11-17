using SimuladorGravitacional.Motores;

namespace SimuladorGravitacional
{
    /// <summary>Resultado da comparação entre a simulação sequencial e a paralela.</summary>
    public sealed record ComparacaoSimulacoes(
        int CorposSequencial,
        int CorposParalelo,
        bool MesmosCorpos,
        double MaiorDiferencaPosicao,     // metros
        double MaiorDiferencaRelativaPosicao,  // em relação ao lado do universo
        double MaiorDiferencaRelativaVelocidade)
    {
        /// <summary>
        /// Tolerância: a soma em ponto flutuante não é associativa, então somar as mesmas forças
        /// em outra ordem (o paralelo soma por corpo, o sequencial por par) muda os últimos bits
        /// do resultado (~1e-16 relativo por operação). Esses erros crescem ao longo das
        /// interações, mas continuam muitas ordens de grandeza abaixo de qualquer efeito físico.
        /// </summary>
        public bool Equivalente(double tolerancia = 1e-9) =>
            MesmosCorpos && MaiorDiferencaRelativaPosicao <= tolerancia && MaiorDiferencaRelativaVelocidade <= tolerancia;
    }

    /// <summary>
    /// Roda a mesma simulação (mesma semente = mesmos dados de entrada) com o motor sequencial
    /// da N1 e com o paralelo, e mede a diferença entre os resultados finais.
    /// </summary>
    public static class VerificacaoParalelismo
    {
        /// <param name="acompanhar">Opcional: recebe a comparação após cada interação (para ver a evolução do erro).</param>
        public static ComparacaoSimulacoes Comparar(int corpos, int interacoes, double passo, int semente, int threads,
                                                    Action<int, ComparacaoSimulacoes>? acompanhar = null)
        {
            Universo sequencial = Criar(corpos, interacoes, passo, semente, new MotorSequencial());
            Universo paralelo = Criar(corpos, interacoes, passo, semente, new MotorParalelo(threads));

            // Avançam lado a lado para permitir comparar interação por interação
            while (!sequencial.Concluido)
            {
                sequencial.Avancar();
                paralelo.Avancar();
                acompanhar?.Invoke(sequencial.IteracaoAtual, Comparar(sequencial.Corpos, paralelo.Corpos));
            }

            return Comparar(sequencial.Corpos, paralelo.Corpos);
        }

        public static ComparacaoSimulacoes Comparar(IReadOnlyList<Corpo> a, IReadOnlyList<Corpo> b)
        {
            // Mesmas fusões nas mesmas interações => mesma lista de Ids na mesma ordem
            bool mesmosCorpos = a.Count == b.Count && a.Select(c => c.Id).SequenceEqual(b.Select(c => c.Id));
            if (!mesmosCorpos)
                return new ComparacaoSimulacoes(a.Count, b.Count, false, double.NaN, double.NaN, double.NaN);

            double maiorPos = 0, maiorVelRel = 0;
            for (int i = 0; i < a.Count; i++)
            {
                double dPos = Math.Max(Math.Abs(a[i].PosicaoX - b[i].PosicaoX), Math.Abs(a[i].PosicaoY - b[i].PosicaoY));
                maiorPos = Math.Max(maiorPos, dPos);

                double escalaVel = Math.Max(1.0, Math.Max(Math.Abs(a[i].VelocidadeX), Math.Abs(a[i].VelocidadeY)));
                double dVel = Math.Max(Math.Abs(a[i].VelocidadeX - b[i].VelocidadeX), Math.Abs(a[i].VelocidadeY - b[i].VelocidadeY));
                maiorVelRel = Math.Max(maiorVelRel, dVel / escalaVel);
            }

            return new ComparacaoSimulacoes(a.Count, b.Count, true, maiorPos, maiorPos / Universo.LadoEmMetros, maiorVelRel);
        }

        public static Universo Criar(int corpos, int interacoes, double passo, int semente, MotorSimulacao motor)
        {
            var universo = new Universo { TotalIteracoes = interacoes, PassoDeTempo = passo, Motor = motor };
            universo.GerarAleatoriamente(corpos, new Random(semente));
            return universo;
        }
    }
}
