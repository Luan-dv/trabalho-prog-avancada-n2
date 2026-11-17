using SimuladorGravitacional.Motores;

namespace SimuladorGravitacional
{
    /// <summary>
    /// Conjunto de corpos em interação gravitacional, avançado passo a passo no tempo.
    /// O cálculo de cada passo é delegado a um <see cref="MotorSimulacao"/> (sequencial ou paralelo).
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

        // Vetores de trabalho reaproveitados entre interações (evita alocar a cada passo)
        private double[] _ax = Array.Empty<double>();
        private double[] _ay = Array.Empty<double>();
        private double[] _raios = Array.Empty<double>();

        public List<Corpo> Corpos { get; } = new List<Corpo>();
        public int TotalIteracoes { get; set; }
        public double PassoDeTempo { get; set; }    // segundos entre iterações
        public int IteracaoAtual { get; private set; }
        public bool Concluido => IteracaoAtual >= TotalIteracoes;

        public MotorSimulacao Motor { get; set; } = new MotorSequencial();

        /// <summary>
        /// Usa o laço original da N1 (recomeça a busca do zero após cada fusão). Só existe para
        /// os testes provarem que a busca otimizada produz exatamente as mesmas fusões.
        /// </summary>
        internal bool BuscaDeColisoesComoNaN1 { get; set; }

        /// <summary>Fusões ocorridas no último <see cref="Avancar"/> (para gravar no banco).</summary>
        public List<Fusao> FusoesDoUltimoPasso { get; } = new List<Fusao>();

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

            return new Corpo(numero, $"Corpo {numero}", massa, densidade, posX, posY, velX, velY);
        }

        public void Avancar()
        {
            if (Concluido) return;

            FusoesDoUltimoPasso.Clear();
            int n = Corpos.Count;
            GarantirCapacidade(n);

            // O motor sequencial acumula com += e -=, então os vetores precisam começar zerados
            Array.Clear(_ax, 0, n);
            Array.Clear(_ay, 0, n);

            Motor.CalcularAceleracoes(Corpos, _ax, _ay);
            for (int i = 0; i < n; i++)
            {
                Corpos[i].AceleracaoX = _ax[i];
                Corpos[i].AceleracaoY = _ay[i];
            }

            Motor.AplicarMovimento(Corpos, _ax, _ay, PassoDeTempo);
            TratarColisoes();
            IteracaoAtual++;
        }

        // Regra da N1: funde sempre o PRIMEIRO par em contato (menor i, depois menor j) e repete
        // até não haver mais contatos. A N1 recomeçava a busca do zero após cada fusão, o que
        // custa uma varredura O(n²) por fusão; na 1ª interação de 1000 corpos ocorrem centenas
        // de fusões, e esse trecho praticamente sequencial dominava o tempo, anulando o ganho
        // do paralelismo (Lei de Amdahl).
        //
        // Aqui a busca é retomada em vez de recomeçada, chegando EXATAMENTE à mesma sequência
        // de fusões. Após fundir (i, j) no índice i:
        //   - as linhas anteriores a i não tinham contato algum, e seus corpos não mudaram;
        //     o único par novo possível entre elas é (k, i) com o corpo recém-fundido;
        //   - então basta testar as linhas k < i contra o corpo i (O(n)). Se alguma tocar, o
        //     próximo par da regra é (k, i), o menor possível; senão, a busca continua da linha i.
        // O teste FusoesIguaisAoAlgoritmoQueRecomecaDoZero compara com a forma original.
        private void TratarColisoes()
        {
            CalcularRaios();

            if (BuscaDeColisoesComoNaN1)
            {
                while (Motor.BuscarColisao(Corpos, _raios, 0, out int a, out int b))
                    FundirPar(a, b);
                return;
            }

            int linha = 0;

            while (Motor.BuscarColisao(Corpos, _raios, linha, out int i, out int j))
            {
                while (true)
                {
                    FundirPar(i, j);

                    int anterior = PrimeiraLinhaAnteriorEmContato(i);
                    if (anterior < 0) break;

                    j = i;          // o corpo recém-fundido passa a ser o segundo do par
                    i = anterior;
                }

                linha = i;
            }
        }

        private void FundirPar(int i, int j)
        {
            Corpo fundido = Fundir(Corpos[i], Corpos[j]);
            Corpo absorvido = fundido.Id == Corpos[i].Id ? Corpos[j] : Corpos[i];
            FusoesDoUltimoPasso.Add(new Fusao(fundido.Id, absorvido.Id));

            Corpos[i] = fundido;
            Corpos.RemoveAt(j);

            // Mantém o vetor de raios alinhado com a lista: atualiza i e desloca o que vem após j
            _raios[i] = fundido.Raio;
            Array.Copy(_raios, j + 1, _raios, j, Corpos.Count - j);
        }

        private int PrimeiraLinhaAnteriorEmContato(int i)
        {
            for (int k = 0; k < i; k++)
            {
                if (MotorSimulacao.EstaoEmContato(Corpos[k], _raios[k], Corpos[i], _raios[i]))
                    return k;
            }
            return -1;
        }

        private void CalcularRaios()
        {
            for (int i = 0; i < Corpos.Count; i++)
                _raios[i] = Corpos[i].Raio;
        }

        // Colisão perfeitamente inelástica: conserva a quantidade de movimento (Q = m·v) em X e Y.
        // O corpo resultante herda o Id e o nome do mais massivo, para o replay continuar
        // acompanhando o mesmo corpo.
        private static Corpo Fundir(Corpo a, Corpo b)
        {
            double massa = a.Massa + b.Massa;
            double volume = a.Massa / a.Densidade + b.Massa / b.Densidade;
            Corpo maior = a.Massa >= b.Massa ? a : b;

            return new Corpo(
                maior.Id, maior.Nome, massa, massa / volume,
                (a.Massa * a.PosicaoX + b.Massa * b.PosicaoX) / massa,
                (a.Massa * a.PosicaoY + b.Massa * b.PosicaoY) / massa,
                (a.Massa * a.VelocidadeX + b.Massa * b.VelocidadeX) / massa,
                (a.Massa * a.VelocidadeY + b.Massa * b.VelocidadeY) / massa)
            {
                // A força resultante é a soma das forças (m·a); dividida pela nova massa dá a aceleração
                AceleracaoX = (a.Massa * a.AceleracaoX + b.Massa * b.AceleracaoX) / massa,
                AceleracaoY = (a.Massa * a.AceleracaoY + b.Massa * b.AceleracaoY) / massa
            };
        }

        private void GarantirCapacidade(int n)
        {
            if (_ax.Length >= n) return;

            _ax = new double[n];
            _ay = new double[n];
            _raios = new double[n];
        }
    }
}
