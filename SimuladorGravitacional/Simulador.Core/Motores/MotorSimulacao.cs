namespace SimuladorGravitacional.Motores
{
    /// <summary>
    /// Estratégia de cálculo de um passo da simulação. Existe para que a mesma classe
    /// <see cref="Universo"/> rode tanto com o algoritmo sequencial da N1 quanto com o paralelo
    /// da N2: assim a comparação de desempenho mede só a diferença do cálculo, e não diferenças
    /// em outras partes do código.
    /// </summary>
    public abstract class MotorSimulacao
    {
        /// <summary>Quantidade de threads usadas (1 no sequencial).</summary>
        public abstract int Threads { get; }

        public abstract string Descricao { get; }

        /// <summary>
        /// Preenche ax/ay (já zerados, com pelo menos corpos.Count posições) com a aceleração
        /// gravitacional resultante sobre cada corpo.
        /// </summary>
        public abstract void CalcularAceleracoes(List<Corpo> corpos, double[] ax, double[] ay);

        /// <summary>Atualiza velocidades e, em seguida, posições (Euler semi-implícito).</summary>
        public abstract void AplicarMovimento(List<Corpo> corpos, double[] ax, double[] ay, double passoDeTempo);

        /// <summary>
        /// Primeiro par (i &lt; j, na ordem dos índices, com i ≥ linhaInicial) cujos raios se
        /// tocam. As duas versões precisam devolver exatamente o mesmo par; caso contrário a
        /// ordem das fusões mudaria e as simulações sequencial e paralela divergiriam.
        /// </summary>
        public abstract bool BuscarColisao(List<Corpo> corpos, double[] raios, int linhaInicial,
                                           out int indiceA, out int indiceB);

        /// <summary>Mesma fórmula nas duas versões, para que o critério de contato seja idêntico.</summary>
        public static bool EstaoEmContato(Corpo a, double raioA, Corpo b, double raioB)
        {
            double dx = b.PosicaoX - a.PosicaoX;
            double dy = b.PosicaoY - a.PosicaoY;
            double distancia = Math.Sqrt(dx * dx + dy * dy);

            return distancia <= raioA + raioB;
        }
    }
}
