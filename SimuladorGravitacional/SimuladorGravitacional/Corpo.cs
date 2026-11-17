using System;

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

        public string Nome { get; set; }
        public double Massa { get; set; }          // kg
        public double PosicaoX { get; set; }       // m
        public double PosicaoY { get; set; }       // m
        public double VelocidadeX { get; set; }    // m/s
        public double VelocidadeY { get; set; }    // m/s

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

        public Corpo(string nome, double massa, double densidade,
                     double posicaoX, double posicaoY,
                     double velocidadeX, double velocidadeY)
        {
            Nome = nome;
            Massa = massa;
            Densidade = densidade;
            PosicaoX = posicaoX;
            PosicaoY = posicaoY;
            VelocidadeX = velocidadeX;
            VelocidadeY = velocidadeY;
        }
    }
}
