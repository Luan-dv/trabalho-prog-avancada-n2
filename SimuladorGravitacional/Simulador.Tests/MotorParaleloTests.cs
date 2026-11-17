using SimuladorGravitacional;
using SimuladorGravitacional.Motores;

namespace Simulador.Tests
{
    public class MotorParaleloTests
    {
        // Horizonte curto: a única diferença possível é o arredondamento da soma em outra ordem
        // (~1e-15 por interação), então a tolerância pode ser bem apertada.
        [Theory]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(8)]
        public void ParaleloProduzOMesmoResultadoQueOSequencialDaN1(int threads)
        {
            ComparacaoSimulacoes c = VerificacaoParalelismo.Comparar(
                corpos: 300, interacoes: 10, passo: 3600, semente: 42, threads: threads);

            Assert.True(c.MesmosCorpos, $"Quantidade/ordem de corpos diferente: {c.CorposSequencial} × {c.CorposParalelo}");
            Assert.True(c.Equivalente(1e-9), $"Diferença acima da tolerância: pos {c.MaiorDiferencaRelativaPosicao:E2}, vel {c.MaiorDiferencaRelativaVelocidade:E2}");
        }

        // Horizonte longo: o problema de N corpos é caótico — encontros próximos amplificam
        // exponencialmente qualquer diferença (inclusive a de arredondamento), então comparar
        // posições com tolerância fixa após centenas de interações não faz sentido físico.
        // O que precisa coincidir é a "história" da simulação: as mesmas fusões, entre os
        // mesmos corpos, nas mesmas interações.
        [Fact]
        public void HorizonteLongoTemAsMesmasFusoesNasMesmasInteracoes()
        {
            Universo seq = VerificacaoParalelismo.Criar(300, 200, 3600, 42, new MotorSequencial());
            Universo par = VerificacaoParalelismo.Criar(300, 200, 3600, 42, new MotorParalelo(4));

            while (!seq.Concluido)
            {
                seq.Avancar();
                par.Avancar();
                Assert.Equal(seq.FusoesDoUltimoPasso, par.FusoesDoUltimoPasso);
            }

            Assert.Equal(seq.Corpos.Select(c => c.Id), par.Corpos.Select(c => c.Id));
        }

        [Fact]
        public void AceleracoesDeUmPassoBatemComOSequencial()
        {
            Universo seq = VerificacaoParalelismo.Criar(500, 1, 3600, 7, new MotorSequencial());
            Universo par = VerificacaoParalelismo.Criar(500, 1, 3600, 7, new MotorParalelo(6));
            seq.Avancar();
            par.Avancar();

            Assert.Equal(seq.Corpos.Count, par.Corpos.Count);
            for (int i = 0; i < seq.Corpos.Count; i++)
            {
                AssertRelativo(seq.Corpos[i].AceleracaoX, par.Corpos[i].AceleracaoX, 1e-12);
                AssertRelativo(seq.Corpos[i].AceleracaoY, par.Corpos[i].AceleracaoY, 1e-12);
            }
        }

        // Cada corpo soma as forças sempre na mesma ordem (j = 0..n-1), seja qual for a thread
        // que o processa: o resultado não depende de quantas threads existem nem do agendamento.
        [Fact]
        public void ResultadoNaoDependeDoNumeroDeThreads()
        {
            Universo uma = VerificacaoParalelismo.Criar(400, 100, 3600, 123, new MotorParalelo(1));
            Universo oito = VerificacaoParalelismo.Criar(400, 100, 3600, 123, new MotorParalelo(8));
            while (!uma.Concluido) uma.Avancar();
            while (!oito.Concluido) oito.Avancar();

            Assert.Equal(uma.Corpos.Count, oito.Corpos.Count);
            for (int i = 0; i < uma.Corpos.Count; i++)
            {
                Assert.Equal(uma.Corpos[i].Id, oito.Corpos[i].Id);
                Assert.Equal(uma.Corpos[i].PosicaoX, oito.Corpos[i].PosicaoX);   // igualdade exata, bit a bit
                Assert.Equal(uma.Corpos[i].PosicaoY, oito.Corpos[i].PosicaoY);
                Assert.Equal(uma.Corpos[i].VelocidadeX, oito.Corpos[i].VelocidadeX);
            }
        }

        [Fact]
        public void BuscaDeColisaoDevolveOMesmoParNasDuasVersoes()
        {
            // Vários pares se tocando: os dois motores devem escolher o primeiro na ordem (i, j)
            var corpos = new List<Corpo>();
            for (int k = 0; k < 200; k++)
                corpos.Add(new Corpo(k + 1, $"C{k}", 1e24, 5000, k * 2e7, 0, 0, 0));   // 2e7 m > 2 raios (~7e6 m): sem contato
            corpos.Add(new Corpo(500, "Perto do 150", 1e24, 5000, 150 * 2e7 + 10, 0, 0, 0));
            corpos.Add(new Corpo(501, "Perto do 90", 1e24, 5000, 90 * 2e7 + 10, 0, 0, 0));

            double[] raios = corpos.Select(c => c.Raio).ToArray();

            bool achouSeq = new MotorSequencial().BuscarColisao(corpos, raios, 0, out int iSeq, out int jSeq);
            bool achouPar = new MotorParalelo(8).BuscarColisao(corpos, raios, 0, out int iPar, out int jPar);

            Assert.True(achouSeq);
            Assert.True(achouPar);
            Assert.Equal((iSeq, jSeq), (iPar, jPar));
            Assert.Equal(90, iSeq);   // o corpo de índice 90 é o primeiro i com contato
        }

        // A busca de colisões otimizada (retoma em vez de recomeçar) precisa gerar a mesma
        // sequência de fusões do laço original da N1. Cenários com muitos corpos têm centenas
        // de fusões encadeadas logo na 1ª interação, exercitando bem o caso (k, i).
        [Theory]
        [InlineData(1000, 2024)]
        [InlineData(2000, 7)]
        public void FusoesIguaisAoAlgoritmoQueRecomecaDoZero(int corpos, int semente)
        {
            Universo original = VerificacaoParalelismo.Criar(corpos, 20, 3600, semente, new MotorSequencial());
            original.BuscaDeColisoesComoNaN1 = true;
            Universo otimizado = VerificacaoParalelismo.Criar(corpos, 20, 3600, semente, new MotorSequencial());

            while (!original.Concluido)
            {
                original.Avancar();
                otimizado.Avancar();
                Assert.Equal(original.FusoesDoUltimoPasso, otimizado.FusoesDoUltimoPasso);
            }

            Assert.True(original.Corpos.Count < corpos, "o cenário deveria ter fusões");
            for (int i = 0; i < original.Corpos.Count; i++)
            {
                Assert.Equal(original.Corpos[i].Id, otimizado.Corpos[i].Id);
                Assert.Equal(original.Corpos[i].PosicaoX, otimizado.Corpos[i].PosicaoX);   // bit a bit
                Assert.Equal(original.Corpos[i].Massa, otimizado.Corpos[i].Massa);
            }
        }

        [Fact]
        public void FusaoConservaMassaEQuantidadeDeMovimento()
        {
            // Passo minúsculo: com dt = 1 s a atração (~1e14 m/s² a 1 m de distância) lançaria os
            // corpos para longe antes da checagem de colisão (tunelamento).
            var universo = new Universo { TotalIteracoes = 1, PassoDeTempo = 1e-12, Motor = new MotorParalelo(2) };
            universo.Corpos.Add(new Corpo(1, "A", 3e24, 5000, 0, 0, 100, 0));
            universo.Corpos.Add(new Corpo(2, "B", 1e24, 5000, 1, 0, -100, 50));

            double qx = 3e24 * 100 + 1e24 * -100;
            double qy = 1e24 * 50;

            universo.Avancar();

            Corpo c = Assert.Single(universo.Corpos);
            Assert.Equal(1, c.Id);   // herda o Id do mais massivo
            Assert.Equal(4e24, c.Massa);
            AssertRelativo(qx, c.Massa * c.VelocidadeX, 1e-9);
            AssertRelativo(qy, c.Massa * c.VelocidadeY, 1e-9);
            Assert.Equal(new Fusao(1, 2), Assert.Single(universo.FusoesDoUltimoPasso));
        }

        private static void AssertRelativo(double esperado, double obtido, double tolerancia)
        {
            double escala = Math.Max(Math.Abs(esperado), 1e-300);
            Assert.True(Math.Abs(esperado - obtido) / escala <= tolerancia,
                        $"esperado {esperado:R}, obtido {obtido:R}");
        }
    }
}
