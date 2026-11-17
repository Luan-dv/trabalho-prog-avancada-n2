namespace SimuladorGravitacional.Persistencia
{
    /// <summary>Parâmetros de uma execução, gravados junto com os resultados.</summary>
    public sealed record DadosSimulacao(
        string Nome,
        string? Descricao,
        string Origem,            // "interface" ou "benchmark"
        int QtdCorpos,
        int QtdInteracoes,
        double PassoDeTempo,
        int Threads,
        int? Semente);

    /// <summary>Resultado de uma execução, com os tempos medidos separadamente.</summary>
    public sealed record ResultadoExecucao(
        int InteracoesConcluidas,
        int CorposFinais,
        TimeSpan TempoTotal,
        TimeSpan TempoCalculo,
        TimeSpan TempoGravacao,
        long LinhasGravadas,
        bool Interrompida,
        double MediaCorposPorInteracao);   // as fusões reduzem os corpos: este é o trabalho real médio

    /// <summary>
    /// Destino dos estados da simulação. A N1 grava em arquivo texto e a N2 no MySQL; as duas
    /// implementam esta interface para que o executor e o benchmark tratem ambas igualmente.
    /// </summary>
    public interface IGravadorSimulacao : IDisposable
    {
        /// <summary>Total de linhas de estado gravadas até agora.</summary>
        long LinhasGravadas { get; }

        /// <summary>Registra os parâmetros e a lista de corpos iniciais.</summary>
        void Iniciar(DadosSimulacao dados, IReadOnlyList<Corpo> corpos);

        /// <summary>
        /// Grava o estado de todos os corpos após a interação informada (0 = estado inicial)
        /// e as fusões que aconteceram nela.
        /// </summary>
        void GravarInteracao(int interacao, IReadOnlyList<Corpo> corpos, IReadOnlyList<Fusao> fusoes);

        /// <summary>Descarrega o que estiver em buffer e confirma a gravação.</summary>
        void Concluir();

        /// <summary>Anota os tempos finais (fora da medição do tempo de gravação).</summary>
        void RegistrarResultado(ResultadoExecucao resultado);
    }
}
