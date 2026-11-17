using SimuladorGravitacional;
using SimuladorGravitacional.Motores;
using SimuladorGravitacional.Persistencia;

namespace Simulador.Tests
{
    public class GravadorArquivoTextoTests
    {
        [Fact]
        public void GravaUmaLinhaPorCorpoEmCadaInteracao()
        {
            string caminho = Path.Combine(Path.GetTempPath(), $"simulador_teste_{Guid.NewGuid():N}.txt");
            try
            {
                Universo universo = VerificacaoParalelismo.Criar(20, 10, 3600, 1, new MotorSequencial());
                var dados = new DadosSimulacao("teste", null, "teste", 20, 10, 3600, 1, 1);

                ResultadoExecucao r;
                using (var gravador = new GravadorArquivoTexto(caminho))
                    r = ExecutorSimulacao.Executar(universo, dados, gravador);

                string[] linhas = File.ReadAllLines(caminho);
                Assert.Equal("20;10;3600", linhas[0]);

                int estados = linhas.Count(l => l.StartsWith("E;"));
                Assert.Equal(r.LinhasGravadas, estados);
                if (!linhas.Any(l => l.StartsWith("F;")))
                    Assert.Equal(20 * 11, estados);   // interações 0..10 sem fusões: 20 corpos em cada
                Assert.Equal(10, r.InteracoesConcluidas);

                // 15 campos por estado; na interação 0 aceleração e força ficam vazias
                string primeira = linhas.First(l => l.StartsWith("E;0;"));
                Assert.Equal(15, primeira.Split(';').Length);
                Assert.EndsWith(";;;;", primeira);
            }
            finally
            {
                File.Delete(caminho);
            }
        }
    }
}
