using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SimuladorGravitacional
{
    public abstract class RepositorioUniverso
    {
        public abstract void Gravar(Universo universo);
        public abstract Universo Carregar();
    }

    /// <summary>
    /// Formato: 1ª linha = corpos;iterações restantes;tempo por iteração.
    /// Demais linhas = nome;massa;densidade;posX;posY;velX;velY (cultura invariante).
    /// </summary>
    public class RepositorioArquivoTexto : RepositorioUniverso
    {
        private const char Separador = ';';
        private static readonly CultureInfo Cultura = CultureInfo.InvariantCulture;

        private readonly string _caminho;

        public RepositorioArquivoTexto(string caminho)
        {
            _caminho = caminho;
        }

        public override void Gravar(Universo universo)
        {
            var linhas = new List<string> { MontarCabecalho(universo) };

            foreach (Corpo corpo in universo.Corpos)
                linhas.Add(MontarLinha(corpo));

            File.WriteAllLines(_caminho, linhas);
        }

        public override Universo Carregar()
        {
            string[] linhas = File.ReadAllLines(_caminho);
            string[] cabecalho = linhas[0].Split(Separador);

            int qtdCorpos = int.Parse(cabecalho[0]);
            var universo = new Universo
            {
                TotalIteracoes = int.Parse(cabecalho[1]),
                PassoDeTempo = double.Parse(cabecalho[2], Cultura)
            };

            for (int i = 1; i <= qtdCorpos; i++)
                universo.Corpos.Add(LerCorpo(linhas[i]));

            return universo;
        }

        // Grava as iterações que ainda faltam, para poder continuar depois
        private static string MontarCabecalho(Universo universo)
        {
            return string.Format(Cultura, "{0};{1};{2:R}",
                universo.Corpos.Count,
                universo.TotalIteracoes - universo.IteracaoAtual,
                universo.PassoDeTempo);
        }

        private static string MontarLinha(Corpo c)
        {
            return string.Format(Cultura, "{0};{1:R};{2:R};{3:R};{4:R};{5:R};{6:R}",
                c.Nome, c.Massa, c.Densidade, c.PosicaoX, c.PosicaoY, c.VelocidadeX, c.VelocidadeY);
        }

        private static Corpo LerCorpo(string linha)
        {
            string[] campos = linha.Split(Separador);

            return new Corpo(
                campos[0],
                double.Parse(campos[1], Cultura), double.Parse(campos[2], Cultura),
                double.Parse(campos[3], Cultura), double.Parse(campos[4], Cultura),
                double.Parse(campos[5], Cultura), double.Parse(campos[6], Cultura));
        }
    }
}
