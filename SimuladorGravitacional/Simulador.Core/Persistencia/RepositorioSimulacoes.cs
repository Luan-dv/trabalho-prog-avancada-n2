using MySqlConnector;

namespace SimuladorGravitacional.Persistencia
{
    /// <summary>Linha da listagem de simulações gravadas.</summary>
    public sealed record ResumoSimulacao(
        long Id,
        string Nome,
        string? Descricao,
        string Origem,
        DateTime CriadaEm,
        int QtdCorpos,
        int? QtdCorposFinal,
        int QtdInteracoes,
        int InteracoesConcluidas,
        double PassoDeTempo,
        double LadoUniverso,
        int Threads,
        int? Semente,
        string Status,
        double? TempoTotalMs,
        double? TempoCalculoMs,
        double? TempoGravacaoMs);

    public sealed record FusaoGravada(int Interacao, int AbsorvedorId, int AbsorvidoId);

    /// <summary>
    /// Consultas usadas pelo replay. Os métodos são assíncronos porque são chamados pela tela:
    /// enquanto o banco responde, a interface continua desenhando e respondendo a cliques.
    /// </summary>
    public sealed class RepositorioSimulacoes
    {
        private readonly string _stringConexao;

        public RepositorioSimulacoes(Configuracao configuracao)
        {
            _stringConexao = configuracao.MySql.MontarStringConexao();
        }

        public async Task<List<ResumoSimulacao>> ListarAsync(CancellationToken ct = default)
        {
            const string sql =
                "SELECT id, nome, descricao, origem, criada_em, qtd_corpos, qtd_corpos_final, qtd_interacoes, " +
                "interacoes_concluidas, passo_tempo_s, lado_universo_m, threads, semente, status, " +
                "tempo_total_ms, tempo_calculo_ms, tempo_gravacao_ms " +
                "FROM simulacao ORDER BY criada_em DESC, id DESC";   // usa ix_simulacao_criada_em

            await using var conexao = await AbrirAsync(ct);
            await using var cmd = new MySqlCommand(sql, conexao);
            await using var leitor = await cmd.ExecuteReaderAsync(ct);

            var lista = new List<ResumoSimulacao>();
            while (await leitor.ReadAsync(ct))
            {
                lista.Add(new ResumoSimulacao(
                    leitor.GetInt64(0),
                    leitor.GetString(1),
                    leitor.IsDBNull(2) ? null : leitor.GetString(2),
                    leitor.GetString(3),
                    leitor.GetDateTime(4),
                    leitor.GetInt32(5),
                    leitor.IsDBNull(6) ? null : leitor.GetInt32(6),
                    leitor.GetInt32(7),
                    leitor.GetInt32(8),
                    leitor.GetDouble(9),
                    leitor.GetDouble(10),
                    leitor.GetInt32(11),
                    leitor.IsDBNull(12) ? null : leitor.GetInt32(12),
                    leitor.GetString(13),
                    leitor.IsDBNull(14) ? null : leitor.GetDouble(14),
                    leitor.IsDBNull(15) ? null : leitor.GetDouble(15),
                    leitor.IsDBNull(16) ? null : leitor.GetDouble(16)));
            }
            return lista;
        }

        public async Task<Dictionary<int, string>> CarregarNomesAsync(long simulacaoId, CancellationToken ct = default)
        {
            await using var conexao = await AbrirAsync(ct);
            await using var cmd = new MySqlCommand(
                "SELECT corpo_id, nome FROM corpo WHERE simulacao_id = @id", conexao);
            cmd.Parameters.AddWithValue("@id", simulacaoId);
            await using var leitor = await cmd.ExecuteReaderAsync(ct);

            var nomes = new Dictionary<int, string>();
            while (await leitor.ReadAsync(ct))
                nomes[leitor.GetInt32(0)] = leitor.GetString(1);
            return nomes;
        }

        public async Task<List<FusaoGravada>> CarregarFusoesAsync(long simulacaoId, CancellationToken ct = default)
        {
            await using var conexao = await AbrirAsync(ct);
            await using var cmd = new MySqlCommand(
                "SELECT interacao, corpo_absorvedor_id, corpo_absorvido_id FROM fusao " +
                "WHERE simulacao_id = @id ORDER BY interacao", conexao);   // usa ix_fusao_interacao
            cmd.Parameters.AddWithValue("@id", simulacaoId);
            await using var leitor = await cmd.ExecuteReaderAsync(ct);

            var fusoes = new List<FusaoGravada>();
            while (await leitor.ReadAsync(ct))
                fusoes.Add(new FusaoGravada(leitor.GetInt32(0), leitor.GetInt32(1), leitor.GetInt32(2)));
            return fusoes;
        }

        /// <summary>
        /// Carrega os estados das interações [inicio, fim]. Devolve um vetor em que a posição
        /// k contém os corpos da interação (inicio + k).
        ///
        /// O replay pede blocos de interações em vez da simulação inteira porque o volume pode
        /// ser enorme (2000 corpos × 5000 interações = 10 milhões de linhas, alguns GB em
        /// memória). O filtro "simulacao_id = ? AND interacao BETWEEN ? AND ?" casa com o
        /// prefixo da chave primária clusterizada, então o MySQL lê só o trecho pedido, já
        /// ordenado, sem varrer a tabela e sem ordenar (o ORDER BY sai de graça pelo índice).
        /// </summary>
        public async Task<List<EstadoCorpo>[]> CarregarInteracoesAsync(
            long simulacaoId, int inicio, int fim, IReadOnlyDictionary<int, string> nomes, CancellationToken ct = default)
        {
            var quadros = new List<EstadoCorpo>[fim - inicio + 1];
            for (int k = 0; k < quadros.Length; k++)
                quadros[k] = new List<EstadoCorpo>();

            await using var conexao = await AbrirAsync(ct);
            await using var cmd = new MySqlCommand(
                "SELECT interacao, corpo_id, massa_kg, densidade_kg_m3, raio_m, pos_x_m, pos_y_m, " +
                "vel_x_m_s, vel_y_m_s, acel_x_m_s2, acel_y_m_s2 " +
                "FROM estado_corpo " +
                "WHERE simulacao_id = @id AND interacao BETWEEN @inicio AND @fim " +
                "ORDER BY interacao, corpo_id", conexao);
            cmd.Parameters.AddWithValue("@id", simulacaoId);
            cmd.Parameters.AddWithValue("@inicio", inicio);
            cmd.Parameters.AddWithValue("@fim", fim);

            await using var leitor = await cmd.ExecuteReaderAsync(ct);
            while (await leitor.ReadAsync(ct))
            {
                int interacao = leitor.GetInt32(0);
                int corpoId = leitor.GetInt32(1);

                quadros[interacao - inicio].Add(new EstadoCorpo(
                    corpoId,
                    nomes.TryGetValue(corpoId, out string? nome) ? nome : $"Corpo {corpoId}",
                    leitor.GetDouble(2),
                    leitor.GetDouble(3),
                    leitor.GetDouble(4),
                    LerDouble(leitor, 5),
                    LerDouble(leitor, 6),
                    LerDouble(leitor, 7),
                    LerDouble(leitor, 8),
                    LerDouble(leitor, 9),
                    LerDouble(leitor, 10)));
            }

            return quadros;
        }

        public async Task ExcluirAsync(long simulacaoId, CancellationToken ct = default)
        {
            await using var conexao = await AbrirAsync(ct);
            await using var cmd = new MySqlCommand("DELETE FROM simulacao WHERE id = @id", conexao);
            cmd.Parameters.AddWithValue("@id", simulacaoId);
            await cmd.ExecuteNonQueryAsync(ct);   // ON DELETE CASCADE remove corpos, estados e fusões
        }

        /// <summary>Versão síncrona, usada pelo benchmark para limpar o que gravou.</summary>
        public void Excluir(long simulacaoId)
        {
            using var conexao = new MySqlConnection(_stringConexao);
            conexao.Open();
            using var cmd = new MySqlCommand("DELETE FROM simulacao WHERE id = @id", conexao);
            cmd.Parameters.AddWithValue("@id", simulacaoId);
            cmd.ExecuteNonQuery();
        }

        // NULL no banco (valor não finito ou aceleração da interação 0) vira NaN no programa
        private static double LerDouble(MySqlDataReader leitor, int coluna) =>
            leitor.IsDBNull(coluna) ? double.NaN : leitor.GetDouble(coluna);

        private async Task<MySqlConnection> AbrirAsync(CancellationToken ct)
        {
            var conexao = new MySqlConnection(_stringConexao);
            await conexao.OpenAsync(ct);
            return conexao;
        }
    }
}
