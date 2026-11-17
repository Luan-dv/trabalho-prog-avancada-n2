namespace SimuladorGravitacional
{
    /// <summary>
    /// Corpo celeste esférico com massa, densidade, posição e velocidade em 2D (unidades do SI).
    /// </summary>
    public class Corpo
    {
        public const double ConstanteGravitacional = 6.67430e-11;   // m³/(kg·s²)
        public const double VelocidadeDaLuz = 299792458.0;          // m/s

        private double _densidadeInformada;

        /// <summary>
        /// Identificador estável dentro da simulação. Na N1 o corpo era identificado só pelo nome,
        /// mas o banco precisa de uma chave numérica que não mude entre interações para ligar
        /// cada estado gravado ao mesmo corpo (o replay e a tabela de fusões dependem disso).
        /// </summary>
        public int Id { get; set; }

        public string Nome { get; set; }
        public double Massa { get; set; }          // kg
        public double PosicaoX { get; set; }       // m
        public double PosicaoY { get; set; }       // m
        public double VelocidadeX { get; set; }    // m/s
        public double VelocidadeY { get; set; }    // m/s

        /// <summary>
        /// Aceleração aplicada no último passo (m/s²). Na N1 ela era descartada logo após o
        /// cálculo; agora fica guardada porque o requisito pede gravar aceleração/força de cada
        /// corpo em cada interação. Força = Massa × Aceleração, então não precisa de campo próprio.
        /// </summary>
        public double AceleracaoX { get; set; }
        public double AceleracaoY { get; set; }

        public double ForcaX => Massa * AceleracaoX;   // N
        public double ForcaY => Massa * AceleracaoY;   // N

        /// <summary>Densidade em kg/m³, limitada ao máximo físico (buraco negro).</summary>
        public double Densidade
        {
            get => Math.Min(_densidadeInformada, DensidadeMaxima);
            set => _densidadeInformada = value;
        }

        /// <summary>Densidade em que o raio do corpo se iguala ao raio de Schwarzschild (2GM/c²).</summary>
        public double DensidadeMaxima =>
            3 * Math.Pow(VelocidadeDaLuz, 6) /
            (32 * Math.PI * Math.Pow(ConstanteGravitacional, 3) * Massa * Massa);

        /// <summary>Raio em metros, a partir de massa e densidade (esfera): V = m/ρ.</summary>
        public double Raio => Math.Cbrt(3 * Massa / (4 * Math.PI * Densidade));

        public Corpo(int id, string nome, double massa, double densidade,
                     double posicaoX, double posicaoY,
                     double velocidadeX, double velocidadeY)
        {
            Id = id;
            Nome = nome;
            Massa = massa;
            Densidade = densidade;
            PosicaoX = posicaoX;
            PosicaoY = posicaoY;
            VelocidadeX = velocidadeX;
            VelocidadeY = velocidadeY;
        }

        /// <summary>Cópia imutável do estado atual, para entregar a outra thread (ex.: a tela).</summary>
        public EstadoCorpo CapturarEstado() =>
            new EstadoCorpo(Id, Nome, Massa, Densidade, Raio,
                            PosicaoX, PosicaoY, VelocidadeX, VelocidadeY, AceleracaoX, AceleracaoY);
    }

    /// <summary>
    /// Fotografia de um corpo numa interação. É imutável de propósito: a simulação roda numa
    /// thread de fundo e a tela desenha em outra; entregar cópias evita que a tela leia um
    /// corpo pela metade enquanto a simulação o altera. O replay também usa este tipo, de modo
    /// que a mesma rotina de desenho serve para a simulação ao vivo e para os dados do banco.
    /// </summary>
    public readonly record struct EstadoCorpo(
        int Id, string Nome, double Massa, double Densidade, double Raio,
        double PosicaoX, double PosicaoY, double VelocidadeX, double VelocidadeY,
        double AceleracaoX, double AceleracaoY);

    /// <summary>Registro de que um corpo foi absorvido por outro numa colisão.</summary>
    public readonly record struct Fusao(int AbsorvedorId, int AbsorvidoId);
}
