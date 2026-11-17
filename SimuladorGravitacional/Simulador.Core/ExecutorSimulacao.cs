using System.Diagnostics;
using SimuladorGravitacional.Persistencia;

namespace SimuladorGravitacional
{
    /// <summary>
    /// Roda uma simulação do início ao fim medindo, separadamente, o tempo de cálculo e o de
    /// gravação. A interface e o benchmark usam este mesmo laço, então os tempos medidos no
    /// benchmark correspondem ao que o programa realmente faz.
    /// </summary>
    public static class ExecutorSimulacao
    {
        /// <param name="aoAvancar">
        /// Chamado na thread da simulação após cada interação (ex.: para a tela tirar uma cópia
        /// dos corpos ou esperar para a animação ficar mais lenta). Não entra em NENHUM
        /// cronômetro, nem no total: a espera usada para o usuário acompanhar a simulação não
        /// é processamento e distorceria a comparação de desempenho.
        /// </param>
        public static ResultadoExecucao Executar(
            Universo universo,
            DadosSimulacao dados,
            IGravadorSimulacao? gravador,
            Action<Universo>? aoAvancar = null,
            CancellationToken cancelamento = default)
        {
            var total = Stopwatch.StartNew();
            var calculo = new Stopwatch();
            var gravacao = new Stopwatch();
            bool interrompida = false;
            long corposProcessados = 0;   // soma, interação a interação, dos corpos calculados

            if (gravador != null)
            {
                gravacao.Start();
                gravador.Iniciar(dados, universo.Corpos);
                gravador.GravarInteracao(universo.IteracaoAtual, universo.Corpos, Array.Empty<Fusao>());
                gravacao.Stop();
            }

            while (!universo.Concluido)
            {
                if (cancelamento.IsCancellationRequested)
                {
                    interrompida = true;
                    break;
                }

                corposProcessados += universo.Corpos.Count;
                calculo.Start();
                universo.Avancar();
                calculo.Stop();

                if (gravador != null)
                {
                    gravacao.Start();
                    gravador.GravarInteracao(universo.IteracaoAtual, universo.Corpos, universo.FusoesDoUltimoPasso);
                    gravacao.Stop();
                }

                if (aoAvancar != null)
                {
                    total.Stop();
                    aoAvancar(universo);
                    total.Start();
                }
            }

            if (gravador != null)
            {
                gravacao.Start();
                gravador.Concluir();   // último lote + COMMIT fazem parte do custo de gravação
                gravacao.Stop();
            }

            total.Stop();

            var resultado = new ResultadoExecucao(
                universo.IteracaoAtual,
                universo.Corpos.Count,
                total.Elapsed,
                calculo.Elapsed,
                gravacao.Elapsed,
                gravador?.LinhasGravadas ?? 0,
                interrompida,
                universo.IteracaoAtual == 0 ? universo.Corpos.Count : (double)corposProcessados / universo.IteracaoAtual);

            gravador?.RegistrarResultado(resultado);
            return resultado;
        }
    }
}
