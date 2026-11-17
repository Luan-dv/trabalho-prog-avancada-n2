using System.Globalization;
using System.Text;
using MySqlConnector;

namespace SimuladorGravitacional.Persistencia
{
    /// <summary>
    /// Grava a simulação no MySQL (N2), em lotes e dentro de transações.
    ///
    /// POR QUE EM LOTE: o volume é corpos × interações (2000 corpos × 100 interações já são
    /// 200 mil linhas). Um INSERT por linha, com o autocommit padrão, custaria por linha:
    ///   - uma ida e volta pela rede até o servidor;
    ///   - a análise (parse) de um comando SQL;
    ///   - um COMMIT, que obriga o InnoDB a forçar o log de transações para o disco (fsync),
    ///     de longe a operação mais lenta.
    /// Agrupando:
    ///   - INSERT com várias linhas (LinhasPorInsert, padrão 1000) divide os dois primeiros
    ///     custos por mil;
    ///   - uma transação a cada LinhasPorTransacao (padrão 50 mil) faz um único fsync por
    ///     transação em vez de um por linha.
    /// Por que não uma transação única para a simulação inteira: transações enormes ocupam
    /// muito undo log e, se algo falhar, nada fica gravado. Commits periódicos limitam isso.
    ///
    /// Alternativa considerada: MySqlBulkCopy (LOAD DATA LOCAL INFILE) é ainda mais rápido,
    /// mas exige habilitar local_infile no servidor, o que nem sempre é permitido (ex.: MySQL
    /// do laboratório). O INSERT multi-linha funciona em qualquer instalação padrão.
    ///
    /// O tamanho de cada comando (~1000 linhas × ~300 bytes ≈ 300 KB) fica bem abaixo do
    /// max_allowed_packet padrão do MySQL 8 (64 MB).
    /// </summary>
    public sealed class GravadorMySql : IGravadorSimulacao
    {
        private const string ColunasEstado =
            "INSERT INTO estado_corpo (simulacao_id, interacao, corpo_id, massa_kg, densidade_kg_m3, raio_m, " +
            "pos_x_m, pos_y_m, vel_x_m_s, vel_y_m_s, acel_x_m_s2, acel_y_m_s2, forca_x_n, forca_y_n) VALUES ";

        private static readonly CultureInfo Cultura = CultureInfo.InvariantCulture;

        private readonly string _stringConexao;
        private readonly int _linhasPorInsert;
        private readonly int _linhasPorTransacao;

        private MySqlConnection? _conexao;
        private MySqlTransaction? _transacao;

        // Comando INSERT sendo montado. É reaproveitado (Clear) para não alocar um novo a cada lote.
        private readonly StringBuilder _sql = new StringBuilder(capacity: 512 * 1024);
        private int _linhasNoComando;
        private int _linhasNaTransacao;

        public GravadorMySql(Configuracao configuracao)
        {
            _stringConexao = configuracao.MySql.MontarStringConexao();
            _linhasPorInsert = Math.Max(1, configuracao.Gravacao.LinhasPorInsert);
            _linhasPorTransacao = Math.Max(_linhasPorInsert, configuracao.Gravacao.LinhasPorTransacao);
        }

        /// <summary>Id gerado pelo banco para a simulação (disponível após <see cref="Iniciar"/>).</summary>
        public long SimulacaoId { get; private set; }

        public long LinhasGravadas { get; private set; }

        public void Iniciar(DadosSimulacao dados, IReadOnlyList<Corpo> corpos)
        {
            _conexao = new MySqlConnection(_stringConexao);
            _conexao.Open();

            // Fora de transação de propósito: a linha da simulação fica visível (status
            // 'em_andamento') mesmo se o programa cair no meio, facilitando o diagnóstico.
            using (var cmd = new MySqlCommand(
                "INSERT INTO simulacao (nome, descricao, origem, qtd_corpos, qtd_interacoes, passo_tempo_s, " +
                "lado_universo_m, threads, semente) " +
                "VALUES (@nome, @descricao, @origem, @corpos, @interacoes, @passo, @lado, @threads, @semente)", _conexao))
            {
                // Textos digitados pelo usuário sempre vão como parâmetro (nunca concatenados)
                // para evitar injeção de SQL e problemas com aspas.
                cmd.Parameters.AddWithValue("@nome", dados.Nome);
                cmd.Parameters.AddWithValue("@descricao", dados.Descricao);
                cmd.Parameters.AddWithValue("@origem", dados.Origem);
                cmd.Parameters.AddWithValue("@corpos", dados.QtdCorpos);
                cmd.Parameters.AddWithValue("@interacoes", dados.QtdInteracoes);
                cmd.Parameters.AddWithValue("@passo", dados.PassoDeTempo);
                cmd.Parameters.AddWithValue("@lado", Universo.LadoEmMetros);
                cmd.Parameters.AddWithValue("@threads", dados.Threads);
                cmd.Parameters.AddWithValue("@semente", dados.Semente);
                cmd.ExecuteNonQuery();
                SimulacaoId = cmd.LastInsertedId;
            }

            _transacao = _conexao.BeginTransaction();
            GravarCorpos(corpos);
        }

        // Nomes são texto, então vão parametrizados, também em lotes de várias linhas.
        private void GravarCorpos(IReadOnlyList<Corpo> corpos)
        {
            const int CorposPorComando = 500;

            for (int inicio = 0; inicio < corpos.Count; inicio += CorposPorComando)
            {
                int fim = Math.Min(inicio + CorposPorComando, corpos.Count);
                using var cmd = new MySqlCommand { Connection = _conexao, Transaction = _transacao };
                var sql = new StringBuilder("INSERT INTO corpo (simulacao_id, corpo_id, nome) VALUES ");

                for (int i = inicio; i < fim; i++)
                {
                    if (i > inicio) sql.Append(',');
                    sql.Append(Cultura, $"({SimulacaoId},{corpos[i].Id},@n{i})");
                    cmd.Parameters.AddWithValue($"@n{i}", corpos[i].Nome);
                }

                cmd.CommandText = sql.ToString();
                cmd.ExecuteNonQuery();
            }
        }

        public void GravarInteracao(int interacao, IReadOnlyList<Corpo> corpos, IReadOnlyList<Fusao> fusoes)
        {
            if (fusoes.Count > 0)
            {
                // Fusões são raras: gravadas na hora, na mesma transação dos estados
                var sql = new StringBuilder(
                    "INSERT INTO fusao (simulacao_id, corpo_absorvido_id, corpo_absorvedor_id, interacao) VALUES ");
                for (int i = 0; i < fusoes.Count; i++)
                {
                    if (i > 0) sql.Append(',');
                    sql.Append(Cultura, $"({SimulacaoId},{fusoes[i].AbsorvidoId},{fusoes[i].AbsorvedorId},{interacao})");
                }
                Executar(sql.ToString());
            }

            for (int i = 0; i < corpos.Count; i++)
                AdicionarEstado(interacao, corpos[i]);
        }

        // Os estados são montados como texto SQL em vez de parâmetros. Isso é seguro aqui
        // porque só entram NÚMEROS gerados pelo próprio programa, formatados em cultura
        // invariante (ponto decimal), sem nenhum texto vindo do usuário. Com parâmetros seriam
        // 14 objetos MySqlParameter por linha (14 mil por comando), o que custa mais CPU do que
        // o próprio envio e deixaria a gravação mais lenta.
        private void AdicionarEstado(int interacao, Corpo c)
        {
            if (_linhasNoComando == 0)
                _sql.Append(ColunasEstado);
            else
                _sql.Append(',');

            _sql.Append('(');
            _sql.Append(SimulacaoId).Append(',').Append(interacao).Append(',').Append(c.Id);
            AnexarNumero(c.Massa);
            AnexarNumero(c.Densidade);
            AnexarNumero(c.Raio);
            AnexarNumero(c.PosicaoX);
            AnexarNumero(c.PosicaoY);
            AnexarNumero(c.VelocidadeX);
            AnexarNumero(c.VelocidadeY);

            if (interacao == 0)
            {
                _sql.Append(",NULL,NULL,NULL,NULL");   // estado inicial: nenhuma aceleração aplicada ainda
            }
            else
            {
                AnexarNumero(c.AceleracaoX);
                AnexarNumero(c.AceleracaoY);
                AnexarNumero(c.ForcaX);
                AnexarNumero(c.ForcaY);
            }
            _sql.Append(')');

            _linhasNoComando++;
            LinhasGravadas++;

            if (_linhasNoComando >= _linhasPorInsert)
                EnviarLote();
        }

        private void AnexarNumero(double valor)
        {
            _sql.Append(',');

            // O MySQL não aceita NaN/Infinito em DOUBLE; um corpo que "explodiu" numericamente
            // é gravado com NULL em vez de abortar a simulação inteira.
            if (!double.IsFinite(valor))
            {
                _sql.Append("NULL");
                return;
            }

            Span<char> buffer = stackalloc char[32];
            valor.TryFormat(buffer, out int escritos, "R", Cultura);
            _sql.Append(buffer[..escritos]);
        }

        private void EnviarLote()
        {
            if (_linhasNoComando == 0) return;

            Executar(_sql.ToString());
            _linhasNaTransacao += _linhasNoComando;
            _linhasNoComando = 0;
            _sql.Clear();

            if (_linhasNaTransacao >= _linhasPorTransacao)
            {
                _transacao!.Commit();
                _transacao.Dispose();
                _transacao = _conexao!.BeginTransaction();
                _linhasNaTransacao = 0;
            }
        }

        private void Executar(string sql)
        {
            using var cmd = new MySqlCommand(sql, _conexao, _transacao);
            cmd.ExecuteNonQuery();
        }

        public void Concluir()
        {
            EnviarLote();
            _transacao?.Commit();
            _transacao?.Dispose();
            _transacao = null;
        }

        public void RegistrarResultado(ResultadoExecucao resultado)
        {
            using var cmd = new MySqlCommand(
                "UPDATE simulacao SET status = @status, interacoes_concluidas = @concluidas, " +
                "qtd_corpos_final = @corposFinais, tempo_total_ms = @total, tempo_calculo_ms = @calculo, " +
                "tempo_gravacao_ms = @gravacao WHERE id = @id", _conexao);

            cmd.Parameters.AddWithValue("@status", resultado.Interrompida ? "interrompida" : "concluida");
            cmd.Parameters.AddWithValue("@concluidas", resultado.InteracoesConcluidas);
            cmd.Parameters.AddWithValue("@corposFinais", resultado.CorposFinais);
            cmd.Parameters.AddWithValue("@total", resultado.TempoTotal.TotalMilliseconds);
            cmd.Parameters.AddWithValue("@calculo", resultado.TempoCalculo.TotalMilliseconds);
            cmd.Parameters.AddWithValue("@gravacao", resultado.TempoGravacao.TotalMilliseconds);
            cmd.Parameters.AddWithValue("@id", SimulacaoId);
            cmd.ExecuteNonQuery();
        }

        public void Dispose()
        {
            // Se chegou aqui com transação aberta, houve erro antes de Concluir():
            // desfaz o lote pendente em vez de deixar dados pela metade sem aviso.
            _transacao?.Dispose();
            _conexao?.Dispose();
        }
    }
}
