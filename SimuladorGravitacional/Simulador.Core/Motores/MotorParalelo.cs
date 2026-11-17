using System.Collections.Concurrent;

namespace SimuladorGravitacional.Motores
{
    /// <summary>
    /// Versão paralela (N2) usando a Task Parallel Library (Parallel.For/ForEach), que é o
    /// recurso padrão do .NET para paralelismo de dados: reaproveita as threads do pool em vez
    /// de criar threads novas a cada interação, e o limite de threads é configurável por
    /// <see cref="ParallelOptions.MaxDegreeOfParallelism"/>.
    ///
    /// Estratégia geral contra condições de corrida: em cada fase, todos os dados compartilhados
    /// são APENAS LIDOS, e cada thread ESCREVE somente nas posições dos corpos que recebeu.
    /// Como nenhuma posição de memória é escrita por duas threads, não há corrida e não é
    /// preciso lock (um lock dentro do laço O(n²) serializaria tudo e anularia o ganho).
    /// O fim de cada Parallel.For funciona como barreira: a fase seguinte só começa depois que
    /// todas as threads terminaram a anterior, então ninguém lê um dado ainda sendo escrito.
    /// </summary>
    public sealed class MotorParalelo : MotorSimulacao
    {
        private readonly ParallelOptions _opcoes;

        // Cópias das posições e massas em vetores simples (reaproveitados entre interações para
        // não gerar lixo para o GC). Ler de arrays contíguos é bem mais rápido que acessar as
        // propriedades de objetos espalhados na memória, e o laço interno roda n² vezes.
        private double[] _x = Array.Empty<double>();
        private double[] _y = Array.Empty<double>();
        private double[] _m = Array.Empty<double>();
        private int[] _primeiroContato = Array.Empty<int>();

        public MotorParalelo(int threads)
        {
            if (threads < 1)
                throw new ArgumentOutOfRangeException(nameof(threads), "Use pelo menos 1 thread.");

            Threads = threads;
            _opcoes = new ParallelOptions { MaxDegreeOfParallelism = threads };
        }

        public override int Threads { get; }

        public override string Descricao => $"Paralelo ({Threads} thread{(Threads > 1 ? "s" : "")})";

        // Aqui NÃO se usa a 3ª lei de Newton como no sequencial. Lá, o par (i, j) escreve em
        // ax[i] e em ax[j]; dividindo os i entre threads, duas threads poderiam somar em ax[j]
        // ao mesmo tempo e perder uma das somas (corrida clássica de leitura-modificação-escrita).
        // Em vez disso, cada corpo i soma sozinho a atração de TODOS os outros e grava só ax[i].
        // O custo é fazer o dobro de contas (n·(n-1) em vez de n·(n-1)/2), mas o trabalho fica
        // totalmente independente e escala com o número de threads sem nenhuma sincronização.
        public override void CalcularAceleracoes(List<Corpo> corpos, double[] ax, double[] ay)
        {
            int n = corpos.Count;
            GarantirCapacidade(n);

            // Cópia sequencial: é O(n), desprezível perto do O(n²) abaixo.
            for (int i = 0; i < n; i++)
            {
                _x[i] = corpos[i].PosicaoX;
                _y[i] = corpos[i].PosicaoY;
                _m[i] = corpos[i].Massa;
            }

            double[] x = _x, y = _y, m = _m;
            const double G = Corpo.ConstanteGravitacional;

            ParaCadaFaixa(n, (inicio, fim) =>
            {
                for (int i = inicio; i < fim; i++)
                {
                    double xi = x[i], yi = y[i];
                    double somaX = 0, somaY = 0;   // acumuladores locais: nada compartilhado durante o laço

                    for (int j = 0; j < n; j++)
                    {
                        if (j == i) continue;

                        double dx = x[j] - xi;
                        double dy = y[j] - yi;
                        double d2 = dx * dx + dy * dy;
                        if (d2 == 0) continue;    // corpos sobrepostos: mesmo critério da N1

                        // a = G·m_j/d², projetada em x e y (dx/d, dy/d) => G·m_j·dx/d³
                        double d = Math.Sqrt(d2);
                        double fator = G * m[j] / (d2 * d);
                        somaX += fator * dx;
                        somaY += fator * dy;
                    }

                    // Única escrita em memória compartilhada: posição i, que só esta thread recebeu.
                    ax[i] = somaX;
                    ay[i] = somaY;
                }
            });
        }

        // Cada corpo é atualizado só com os próprios dados e a própria aceleração:
        // as faixas de corpos são disjuntas, então as threads nunca tocam o mesmo objeto.
        public override void AplicarMovimento(List<Corpo> corpos, double[] ax, double[] ay, double passoDeTempo)
        {
            ParaCadaFaixa(corpos.Count, (inicio, fim) =>
            {
                for (int i = inicio; i < fim; i++)
                {
                    Corpo c = corpos[i];
                    c.VelocidadeX += ax[i] * passoDeTempo;
                    c.VelocidadeY += ay[i] * passoDeTempo;
                    c.PosicaoX += c.VelocidadeX * passoDeTempo;
                    c.PosicaoY += c.VelocidadeY * passoDeTempo;
                }
            });
        }

        // A busca de colisão também é O(n²) e, se ficasse sequencial, limitaria o ganho total
        // (Lei de Amdahl). O desafio é devolver o MESMO par que a versão sequencial devolveria:
        // o primeiro i (menor índice) que colide e, para ele, o primeiro j.
        //
        // Usa-se ParallelLoopState.Break(): quando a iteração i encontra contato, ela chama
        // Break(). O .NET garante que todas as iterações com índice MENOR que i ainda serão
        // concluídas, e LowestBreakIteration informa o menor i que chamou Break. Esse é
        // exatamente o "primeiro i" da busca sequencial. Iterações maiores deixam de ser
        // iniciadas, então o trabalho extra é pequeno.
        //
        // Cada iteração i grava apenas _primeiroContato[i] (posição própria), sem corrida.
        public override bool BuscarColisao(List<Corpo> corpos, double[] raios, int linhaInicial,
                                           out int indiceA, out int indiceB)
        {
            int n = corpos.Count;
            GarantirCapacidade(n);
            int[] primeiroContato = _primeiroContato;

            ParallelLoopResult resultado = Parallel.For(linhaInicial, n, _opcoes, (i, estado) =>
            {
                Corpo a = corpos[i];
                double raioA = raios[i];

                for (int j = i + 1; j < n; j++)
                {
                    if (EstaoEmContato(a, raioA, corpos[j], raios[j]))
                    {
                        primeiroContato[i] = j;
                        estado.Break();
                        return;
                    }
                }
            });

            if (resultado.LowestBreakIteration is long menor)
            {
                indiceA = (int)menor;
                indiceB = primeiroContato[indiceA];
                return true;
            }

            indiceA = indiceB = -1;
            return false;
        }

        // Divide 0..n em faixas contíguas e entrega cada faixa a uma thread. Chamar o delegate uma
        // vez por faixa (e não uma vez por corpo) reduz o custo de agendamento quando há poucos
        // corpos. As faixas são menores que n/threads para que uma thread que termine antes
        // possa pegar outra faixa (balanceamento de carga). Faixas contíguas também fazem cada
        // thread escrever em regiões diferentes de ax/ay, reduzindo o "false sharing" de cache.
        private void ParaCadaFaixa(int n, Action<int, int> corpo)
        {
            if (n == 0) return;

            int tamanhoFaixa = Math.Max(16, n / (Threads * 4));
            Parallel.ForEach(Partitioner.Create(0, n, tamanhoFaixa), _opcoes,
                             faixa => corpo(faixa.Item1, faixa.Item2));
        }

        private void GarantirCapacidade(int n)
        {
            if (_x.Length >= n) return;

            _x = new double[n];
            _y = new double[n];
            _m = new double[n];
            _primeiroContato = new int[n];
        }
    }
}
