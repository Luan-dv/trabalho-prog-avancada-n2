using System.Diagnostics;
using SimuladorGravitacional.Persistencia;

namespace SimuladorGravitacional
{
    /// <summary>
    /// Lista as simulações gravadas no MySQL e reproduz uma delas interação por interação,
    /// com o mesmo renderizador da tela de simulação.
    /// </summary>
    public class FormReplay : Form
    {
        private const int IntervaloTimerMs = 33;              // ~30 quadros por segundo
        private const int LinhasPorBloco = 200_000;           // estados carregados por consulta
        private const int MaximoInteracoesPorBloco = 500;
        private const int IntervaloTabelaMs = 300;

        private readonly RepositorioSimulacoes _repositorio;
        private readonly RenderizadorUniverso _renderizador = new RenderizadorUniverso();
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = IntervaloTimerMs };
        private readonly Stopwatch _relogioTabela = Stopwatch.StartNew();

        // Simulação aberta
        private ResumoSimulacao? _simulacao;
        private Dictionary<int, string> _nomes = new();
        private ILookup<int, FusaoGravada> _fusoesPorInteracao = Array.Empty<FusaoGravada>().ToLookup(f => f.Interacao);
        private int _tamanhoBloco = 1;

        // Bloco de interações em memória: _bloco[k] = corpos da interação (_inicioBloco + k)
        private List<EstadoCorpo>[]? _bloco;
        private int _inicioBloco;
        private Task<List<EstadoCorpo>[]>? _proximoBloco;     // pré-carregamento do bloco seguinte
        private int _inicioProximoBloco = -1;

        private int _interacaoAtual;
        private bool _carregando;
        private int? _interacaoPendente;          // pedido feito enquanto um bloco carregava
        private double _acumuladorQuadros;        // fração de interação acumulada entre ticks
        private bool _ajustandoBarra;             // evita tratar como "arrasto" a mudança feita pelo código

        private readonly DataGridView gradeSimulacoes = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells
        };
        private readonly Button btnAtualizar = new Button { Text = "Atualizar lista", AutoSize = true };
        private readonly Button btnAbrir = new Button { Text = "Abrir replay", AutoSize = true };
        private readonly Button btnExcluir = new Button { Text = "Excluir", AutoSize = true };
        private readonly Button btnPlay = new Button { Text = "▶ Reproduzir", AutoSize = true, Enabled = false };
        private readonly TrackBar barraInteracao = new TrackBar { Dock = DockStyle.Fill, TickStyle = TickStyle.None, Enabled = false };
        private readonly NumericUpDown nudVelocidade = new NumericUpDown { Minimum = 1, Maximum = 2000, Value = 20, Width = 60 };
        private readonly CheckBox chkZoom = new CheckBox { Text = "Zoom automático", Checked = true, AutoSize = true, Margin = new Padding(8, 7, 0, 0) };
        private readonly Label lblInteracao = new Label { AutoSize = true, Margin = new Padding(8, 8, 0, 0) };
        private readonly Label lblEventos = new Label { AutoSize = true, Margin = new Padding(8, 8, 0, 0), ForeColor = Color.DarkOrange };
        private readonly PainelUniverso painel = new PainelUniverso { Dock = DockStyle.Fill, BackColor = Color.Black };
        private readonly DataGridView gradeCorpos = TabelaCorpos.Criar(170);

        public FormReplay(RepositorioSimulacoes repositorio)
        {
            _repositorio = repositorio;
            Text = "Replay de simulações (MySQL)";
            ClientSize = new Size(1150, 860);

            MontarLayout();
            LigarEventos();
        }

        private void MontarLayout()
        {
            var botoesLista = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(3) };
            botoesLista.Controls.AddRange(new Control[] { btnAtualizar, btnAbrir, btnExcluir });

            var painelLista = new Panel { Dock = DockStyle.Top, Height = 190 };
            painelLista.Controls.Add(gradeSimulacoes);
            painelLista.Controls.Add(botoesLista);

            var controles = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false };
            controles.Controls.AddRange(new Control[]
            {
                btnPlay, new Label { Text = "Interações/s:", AutoSize = true, Margin = new Padding(8, 8, 0, 0) },
                nudVelocidade, chkZoom, lblInteracao
            });

            var barraControles = new Panel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(3) };
            barraControles.Controls.Add(barraInteracao);   // Fill primeiro
            barraControles.Controls.Add(controles);

            var barraEventos = new Panel { Dock = DockStyle.Bottom, Height = 26 };
            barraEventos.Controls.Add(lblEventos);

            Controls.Add(painel);
            Controls.Add(barraEventos);
            Controls.Add(barraControles);
            Controls.Add(gradeCorpos);
            Controls.Add(painelLista);
        }

        private void LigarEventos()
        {
            Load += async (s, e) => await AtualizarListaAsync();
            btnAtualizar.Click += async (s, e) => await AtualizarListaAsync();
            btnAbrir.Click += async (s, e) => await AbrirSelecionadaAsync();
            gradeSimulacoes.CellDoubleClick += async (s, e) => { if (e.RowIndex >= 0) await AbrirSelecionadaAsync(); };
            btnExcluir.Click += async (s, e) => await ExcluirSelecionadaAsync();
            btnPlay.Click += (s, e) => AlternarReproducao();
            _timer.Tick += async (s, e) => await AvancarReproducaoAsync();
            barraInteracao.ValueChanged += async (s, e) =>
            {
                if (!_ajustandoBarra) await IrParaAsync(barraInteracao.Value);
            };
            chkZoom.CheckedChanged += (s, e) =>
            {
                _renderizador.ZoomAutomatico = chkZoom.Checked;
                painel.Invalidate();
            };
            painel.Paint += (s, e) =>
                _renderizador.Desenhar(e.Graphics, painel.ClientSize, QuadroAtual(), "Escolha uma simulação na lista e clique em \"Abrir replay\"");
            FormClosed += (s, e) =>
            {
                _timer.Dispose();
                _renderizador.Dispose();
            };
        }

        // ---------------------------------------------------------------- Lista de simulações

        private async Task AtualizarListaAsync()
        {
            await Executar(async () =>
            {
                List<ResumoSimulacao> lista = await _repositorio.ListarAsync();
                gradeSimulacoes.DataSource = lista.Select(s => new LinhaSimulacao(s)).ToList();
            });
        }

        private ResumoSimulacao? Selecionada() =>
            (gradeSimulacoes.CurrentRow?.DataBoundItem as LinhaSimulacao)?.Resumo;

        private async Task AbrirSelecionadaAsync()
        {
            ResumoSimulacao? simulacao = Selecionada();
            if (simulacao == null) return;

            if (simulacao.Status == "em_andamento")
            {
                MessageBox.Show(this, "Esta simulação ainda está sendo gravada (ou foi abortada por um erro). " +
                                      "O replay mostrará apenas o que já foi confirmado no banco.",
                                "Simulação incompleta", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            Pausar();
            await Executar(async () =>
            {
                _simulacao = simulacao;
                _nomes = await _repositorio.CarregarNomesAsync(simulacao.Id);
                _fusoesPorInteracao = (await _repositorio.CarregarFusoesAsync(simulacao.Id)).ToLookup(f => f.Interacao);

                // Blocos com ~200 mil linhas: poucas consultas e memória controlada, seja qual for
                // a quantidade de corpos
                _tamanhoBloco = Math.Clamp(LinhasPorBloco / Math.Max(1, simulacao.QtdCorpos), 1, MaximoInteracoesPorBloco);
                _bloco = null;
                _proximoBloco = null;
                _inicioProximoBloco = -1;

                int ultima = UltimaInteracao();
                _ajustandoBarra = true;
                barraInteracao.Maximum = Math.Max(0, ultima);
                barraInteracao.Value = 0;
                barraInteracao.LargeChange = Math.Max(1, ultima / 20);
                _ajustandoBarra = false;
                barraInteracao.Enabled = btnPlay.Enabled = true;

                Text = $"Replay — {simulacao.Nome} (id {simulacao.Id})";
                await IrParaAsync(0);
            });
        }

        private async Task ExcluirSelecionadaAsync()
        {
            ResumoSimulacao? simulacao = Selecionada();
            if (simulacao == null) return;

            if (MessageBox.Show(this, $"Excluir a simulação \"{simulacao.Nome}\" (id {simulacao.Id}) e todos os seus estados?",
                                "Confirmar exclusão", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            if (_simulacao?.Id == simulacao.Id)
            {
                Pausar();
                _simulacao = null;
                _bloco = null;
                barraInteracao.Enabled = btnPlay.Enabled = false;
                painel.Invalidate();
            }

            await Executar(async () =>
            {
                await _repositorio.ExcluirAsync(simulacao.Id);
                await AtualizarListaAsync();
            });
        }

        // ---------------------------------------------------------------- Reprodução

        // Uma simulação interrompida tem menos interações gravadas do que as pedidas
        private int UltimaInteracao() =>
            _simulacao == null ? 0
            : _simulacao.Status == "em_andamento" ? _simulacao.QtdInteracoes
            : _simulacao.InteracoesConcluidas;

        private void AlternarReproducao()
        {
            if (_timer.Enabled)
            {
                Pausar();
                return;
            }

            if (_interacaoAtual >= UltimaInteracao())
                _ = IrParaAsync(0);   // chegou ao fim: recomeça

            _acumuladorQuadros = 0;
            _timer.Start();
            btnPlay.Text = "⏸ Pausar";
        }

        private void Pausar()
        {
            _timer.Stop();
            btnPlay.Text = "▶ Reproduzir";
            if (_bloco != null) AtualizarTabela(forcar: true);
        }

        private async Task AvancarReproducaoAsync()
        {
            if (_carregando || _simulacao == null) return;

            // Converte "interações por segundo" em interações por tick, acumulando a fração
            // para que velocidades baixas (ex.: 5/s com 30 ticks/s) também funcionem.
            _acumuladorQuadros += (double)nudVelocidade.Value * IntervaloTimerMs / 1000.0;
            int passos = (int)_acumuladorQuadros;
            if (passos == 0) return;
            _acumuladorQuadros -= passos;

            int destino = Math.Min(_interacaoAtual + passos, UltimaInteracao());
            await IrParaAsync(destino);

            if (_interacaoAtual >= UltimaInteracao())
                Pausar();
        }

        private async Task IrParaAsync(int interacao)
        {
            if (_simulacao == null) return;

            // Se já há uma consulta em andamento, guarda só o pedido mais recente
            // (arrastar a barra gera dezenas de eventos por segundo).
            if (_carregando)
            {
                _interacaoPendente = interacao;
                return;
            }

            if (!EstaNoBloco(interacao))
            {
                _carregando = true;
                try
                {
                    await CarregarBlocoAsync(interacao);
                }
                catch (Exception ex)
                {
                    Pausar();
                    MessageBox.Show(this, "Erro ao carregar estados:\n\n" + ex.Message, "Banco de dados",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                finally
                {
                    _carregando = false;
                }

                if (_interacaoPendente is int pendente)
                {
                    _interacaoPendente = null;
                    await IrParaAsync(pendente);
                    return;
                }
            }

            _interacaoAtual = interacao;
            PreCarregarProximoBloco();
            MostrarInteracao();
        }

        private bool EstaNoBloco(int interacao) =>
            _bloco != null && interacao >= _inicioBloco && interacao < _inicioBloco + _bloco.Length;

        private async Task CarregarBlocoAsync(int interacao)
        {
            long id = _simulacao!.Id;

            if (_proximoBloco != null && _inicioProximoBloco == interacao)
            {
                _bloco = await _proximoBloco;   // já vinha sendo buscado em segundo plano
                _inicioBloco = _inicioProximoBloco;
            }
            else
            {
                int fim = Math.Min(interacao + _tamanhoBloco - 1, UltimaInteracao());
                _bloco = await _repositorio.CarregarInteracoesAsync(id, interacao, fim, _nomes);
                _inicioBloco = interacao;
            }

            _proximoBloco = null;
            _inicioProximoBloco = -1;
        }

        // Quando a reprodução passa de 3/4 do bloco atual, já pede o seguinte ao banco. Assim a
        // consulta roda enquanto os últimos quadros são exibidos e a animação não "engasga".
        private void PreCarregarProximoBloco()
        {
            if (_bloco == null || _proximoBloco != null || _simulacao == null) return;

            int inicioSeguinte = _inicioBloco + _bloco.Length;
            if (inicioSeguinte > UltimaInteracao()) return;
            if (_interacaoAtual < _inicioBloco + _bloco.Length * 3 / 4) return;

            int fim = Math.Min(inicioSeguinte + _tamanhoBloco - 1, UltimaInteracao());
            _inicioProximoBloco = inicioSeguinte;
            _proximoBloco = _repositorio.CarregarInteracoesAsync(_simulacao.Id, inicioSeguinte, fim, _nomes);
        }

        private IReadOnlyList<EstadoCorpo>? QuadroAtual() =>
            EstaNoBloco(_interacaoAtual) ? _bloco![_interacaoAtual - _inicioBloco] : null;

        private void MostrarInteracao()
        {
            IReadOnlyList<EstadoCorpo> corpos = QuadroAtual() ?? Array.Empty<EstadoCorpo>();
            double dias = _interacaoAtual * _simulacao!.PassoDeTempo / 86400.0;

            lblInteracao.Text = $"Interação {_interacaoAtual:N0}/{UltimaInteracao():N0}  |  " +
                                RenderizadorUniverso.DescreverCorpos(corpos, _simulacao.QtdCorpos) +
                                $"  |  Tempo simulado: {dias:N2} dias";

            if (_fusoesPorInteracao.Contains(_interacaoAtual))
            {
                lblEventos.Text = "Fusões nesta interação: " + string.Join("; ",
                    _fusoesPorInteracao[_interacaoAtual].Select(f => $"{Nome(f.AbsorvedorId)} absorveu {Nome(f.AbsorvidoId)}"));
            }
            else if (!_timer.Enabled)
            {
                lblEventos.Text = "";
            }

            _ajustandoBarra = true;
            barraInteracao.Value = Math.Min(_interacaoAtual, barraInteracao.Maximum);
            _ajustandoBarra = false;

            AtualizarTabela(forcar: !_timer.Enabled);
            painel.Invalidate();
        }

        private void AtualizarTabela(bool forcar)
        {
            if (!forcar && _relogioTabela.ElapsedMilliseconds < IntervaloTabelaMs) return;

            TabelaCorpos.Preencher(gradeCorpos, QuadroAtual() ?? Array.Empty<EstadoCorpo>());
            _relogioTabela.Restart();
        }

        private string Nome(int id) => _nomes.TryGetValue(id, out string? nome) ? nome : $"Corpo {id}";

        // Mostra erros de banco sem derrubar a janela
        private async Task Executar(Func<Task> acao)
        {
            UseWaitCursor = true;
            try
            {
                await acao();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Banco de dados", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        /// <summary>Linha exibida na grade de simulações (colunas com nomes legíveis).</summary>
        private sealed class LinhaSimulacao
        {
            public LinhaSimulacao(ResumoSimulacao resumo) => Resumo = resumo;

            [System.ComponentModel.Browsable(false)]
            public ResumoSimulacao Resumo { get; }

            public long Id => Resumo.Id;
            public string Nome => Resumo.Nome;
            public string Criada => Resumo.CriadaEm.ToString("dd/MM/yyyy HH:mm:ss");
            public string Origem => Resumo.Origem;
            public string Status => Resumo.Status;
            public string Corpos => Resumo.QtdCorposFinal is int final ? $"{Resumo.QtdCorpos} → {final}" : Resumo.QtdCorpos.ToString();
            public string Interacoes => $"{Resumo.InteracoesConcluidas:N0}/{Resumo.QtdInteracoes:N0}";
            public string Passo => $"{Resumo.PassoDeTempo:G6} s";
            public int Threads => Resumo.Threads;
            public string Semente => Resumo.Semente?.ToString() ?? "";
            public string Total => Segundos(Resumo.TempoTotalMs);
            public string Calculo => Segundos(Resumo.TempoCalculoMs);
            public string Gravacao => Segundos(Resumo.TempoGravacaoMs);

            private static string Segundos(double? ms) => ms is double v ? $"{v / 1000:N3} s" : "";
        }
    }
}
