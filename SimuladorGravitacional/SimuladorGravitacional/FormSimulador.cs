using System.Diagnostics;
using SimuladorGravitacional.Motores;
using SimuladorGravitacional.Persistencia;

namespace SimuladorGravitacional
{
    public class FormSimulador : Form
    {
        // Intervalos de atualização da tela durante a simulação. A simulação roda o mais rápido
        // possível numa thread de fundo; redesenhar a cada interação desperdiçaria CPU que
        // deveria ir para o cálculo, então a tela recebe "fotografias" periódicas.
        private const int IntervaloQuadroMs = 66;     // ~15 quadros por segundo na velocidade máxima
        private const int IntervaloQuadroLentoMs = 16; // ~60 quadros por segundo com velocidade limitada
        private const int IntervaloTabelaMs = 500;    // a tabela é cara de reconstruir

        // Lido pela thread da simulação e alterado pela tela (pode mudar durante a execução).
        // volatile garante que a thread de fundo enxergue o valor novo assim que for alterado.
        private volatile int _iteracoesPorSegundo = 30;

        private Universo? _universo;
        private int _sementeAtual;
        private int _quantidadeInicial;                  // para contar as fusões
        private IReadOnlyList<EstadoCorpo>? _quadro;     // o que está sendo desenhado
        private CancellationTokenSource? _execucao;      // != null enquanto a simulação roda
        private bool _fecharAoTerminar;
        private readonly Stopwatch _relogioTabela = new Stopwatch();
        private readonly RenderizadorUniverso _renderizador = new RenderizadorUniverso();

        private readonly NumericUpDown nudCorpos = new NumericUpDown { Minimum = 1, Maximum = 5000, Value = 10, Width = 60 };
        private readonly NumericUpDown nudIteracoes = new NumericUpDown { Minimum = 1, Maximum = 10000000, Value = 5000, Width = 80 };
        private readonly NumericUpDown nudTempo = new NumericUpDown { Minimum = 0.01m, Maximum = 1000000000, DecimalPlaces = 2, Value = 3600, Width = 90 };
        private readonly NumericUpDown nudThreads = new NumericUpDown { Minimum = 1, Maximum = 256, Width = 50 };
        private readonly NumericUpDown nudVelocidade = new NumericUpDown { Minimum = 0, Maximum = 100000, Value = 30, Width = 70 };
        private readonly NumericUpDown nudSemente = new NumericUpDown { Minimum = 0, Maximum = int.MaxValue, Value = 0, Width = 90 };
        private readonly TextBox txtNome = new TextBox { Width = 160, PlaceholderText = "Nome da simulação" };
        private readonly CheckBox chkBanco = new CheckBox { Text = "Gravar no banco", Checked = true, AutoSize = true, Margin = new Padding(10, 5, 0, 0) };
        private readonly CheckBox chkZoom = new CheckBox { Text = "Zoom automático", Checked = true, AutoSize = true, Margin = new Padding(10, 5, 0, 0) };
        private readonly Button btnGerar = new Button { Text = "Gerar universo", AutoSize = true };
        private readonly Button btnSimular = new Button { Text = "Simular", AutoSize = true };
        private readonly Button btnReplay = new Button { Text = "Replay...", AutoSize = true };
        private readonly ProgressBar barra = new ProgressBar { Width = 140, Height = 18, Margin = new Padding(10, 6, 0, 0) };
        private readonly Label lblStatus = new Label { AutoSize = true, Margin = new Padding(10, 6, 0, 0) };
        private readonly PainelUniverso painel = new PainelUniverso { Dock = DockStyle.Fill, BackColor = Color.Black };
        private readonly DataGridView grade = TabelaCorpos.Criar(200);

        public FormSimulador()
        {
            Text = "Simulador gravitacional 2D";
            ClientSize = new Size(1150, 760);
            nudThreads.Value = Math.Min(Environment.ProcessorCount, (int)nudThreads.Maximum);

            MontarLayout();
            LigarEventos();
        }

        // ---------------------------------------------------------------- Montagem da tela

        private void MontarLayout()
        {
            FlowLayoutPanel topo = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(5) };
            topo.Controls.AddRange(new Control[]
            {
                Rotulo("Corpos:"), nudCorpos,
                Rotulo("Iterações:"), nudIteracoes,
                Rotulo("Tempo entre iterações (s):"), nudTempo,
                Rotulo("Threads:"), nudThreads,
                Rotulo("Iterações/s (0 = máxima):"), nudVelocidade,
                Rotulo("Semente (0 = aleatória):"), nudSemente,
                txtNome, chkBanco, chkZoom,
                btnGerar, btnSimular, btnReplay, barra, lblStatus
            });

            // O controle Fill deve ser adicionado primeiro
            Controls.Add(painel);
            Controls.Add(grade);
            Controls.Add(topo);
        }

        private void LigarEventos()
        {
            btnGerar.Click += (s, e) => Gerar();
            btnSimular.Click += async (s, e) => await SimularOuParar();
            btnReplay.Click += (s, e) => AbrirReplay();
            nudVelocidade.ValueChanged += (s, e) => _iteracoesPorSegundo = (int)nudVelocidade.Value;
            chkZoom.CheckedChanged += (s, e) =>
            {
                _renderizador.ZoomAutomatico = chkZoom.Checked;
                painel.Invalidate();
            };
            painel.Paint += (s, e) =>
                _renderizador.Desenhar(e.Graphics, painel.ClientSize, _quadro, "Gere um universo ou abra um replay para começar");
            FormClosing += AoFechar;
        }

        private static Label Rotulo(string texto)
        {
            return new Label { Text = texto, AutoSize = true, Margin = new Padding(8, 6, 0, 0) };
        }

        // ---------------------------------------------------------------- Ações

        private void Gerar()
        {
            // Semente explícita para que o mesmo universo possa ser recriado (ela vai para o banco)
            _sementeAtual = nudSemente.Value == 0 ? Random.Shared.Next(1, int.MaxValue) : (int)nudSemente.Value;

            var universo = new Universo();
            universo.GerarAleatoriamente((int)nudCorpos.Value, new Random(_sementeAtual));
            _universo = universo;
            _quantidadeInicial = universo.Corpos.Count;

            MostrarQuadro(Capturar(universo), universo.IteracaoAtual, (int)nudIteracoes.Value, atualizarTabela: true);
            lblStatus.Text += $"  |  Semente: {_sementeAtual}";
        }

        private async Task SimularOuParar()
        {
            if (_execucao != null)
            {
                _execucao.Cancel();   // o laço confere o token entre interações e para de forma limpa
                btnSimular.Enabled = false;
                return;
            }

            // Um universo já simulado não pode ser "continuado" com outros parâmetros: gera de novo
            if (_universo == null || _universo.IteracaoAtual > 0)
                Gerar();

            Universo universo = _universo!;
            universo.TotalIteracoes = (int)nudIteracoes.Value;
            universo.PassoDeTempo = (double)nudTempo.Value;
            int threads = (int)nudThreads.Value;
            universo.Motor = new MotorParalelo(threads);

            string nome = string.IsNullOrWhiteSpace(txtNome.Text)
                ? $"Simulação {DateTime.Now:dd/MM/yyyy HH:mm:ss}"
                : txtNome.Text.Trim();
            var dados = new DadosSimulacao(nome, null, "interface", universo.Corpos.Count,
                                           universo.TotalIteracoes, universo.PassoDeTempo, threads, _sementeAtual);

            IGravadorSimulacao? gravador = null;
            if (chkBanco.Checked)
            {
                try
                {
                    gravador = new GravadorMySql(Configuracao.Carregar());
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Configuração do banco", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            _execucao = new CancellationTokenSource();
            CancellationToken token = _execucao.Token;
            DefinirExecutando(true);
            barra.Maximum = universo.TotalIteracoes;

            // Chamado na thread da simulação. NÃO pode tocar em controles nem entregar o próprio
            // Universo à tela, porque a simulação continua alterando os corpos enquanto a tela
            // desenha. Por isso cria uma cópia imutável (EstadoCorpo[]) e a envia com BeginInvoke,
            // que executa o desenho na thread da interface sem bloquear a simulação.
            var relogio = Stopwatch.StartNew();
            var ritmo = Stopwatch.StartNew();
            void AoAvancar(Universo u)
            {
                // Velocidade limitada: espera o tempo que falta para a próxima interação. Medir
                // desde a anterior (e não usar um Sleep fixo) desconta o tempo que o próprio
                // cálculo e a gravação já gastaram. Esta espera fica fora dos cronômetros.
                int ips = _iteracoesPorSegundo;
                if (ips > 0)
                {
                    double faltaMs = 1000.0 / ips - ritmo.Elapsed.TotalMilliseconds;
                    if (faltaMs > 1) Thread.Sleep((int)faltaMs);
                }
                ritmo.Restart();

                if (relogio.ElapsedMilliseconds < (ips > 0 ? IntervaloQuadroLentoMs : IntervaloQuadroMs)) return;
                relogio.Restart();

                EstadoCorpo[] copia = Capturar(u);
                int iteracao = u.IteracaoAtual, total = u.TotalIteracoes;
                BeginInvoke(() => MostrarQuadro(copia, iteracao, total, atualizarTabela: false));
            }

            try
            {
                ResultadoExecucao resultado = await Task.Run(
                    () => ExecutorSimulacao.Executar(universo, dados, gravador, AoAvancar, token));

                MostrarQuadro(Capturar(universo), universo.IteracaoAtual, universo.TotalIteracoes, atualizarTabela: true);
                MostrarResumo(resultado, gravador as GravadorMySql);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "A simulação falhou:\n\n" + ex.Message, "Erro",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                gravador?.Dispose();
                _execucao.Dispose();
                _execucao = null;
                DefinirExecutando(false);
            }

            if (_fecharAoTerminar) Close();
        }

        private void MostrarResumo(ResultadoExecucao r, GravadorMySql? gravadorBanco)
        {
            string situacao = r.Interrompida ? "Simulação interrompida" : "Simulação concluída";
            string banco = gravadorBanco != null
                ? $"\nGravada no banco com id {gravadorBanco.SimulacaoId} ({r.LinhasGravadas:N0} estados)."
                : "\n(Não gravada no banco.)";

            MessageBox.Show(this,
                $"{situacao} após {r.InteracoesConcluidas:N0} interações ({r.CorposFinais} corpos restantes).\n\n" +
                $"Tempo total:    {r.TempoTotal.TotalSeconds:N3} s\n" +
                $"Cálculo:           {r.TempoCalculo.TotalSeconds:N3} s\n" +
                $"Gravação:         {r.TempoGravacao.TotalSeconds:N3} s" + banco,
                "Resultado", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void AbrirReplay()
        {
            Configuracao configuracao;
            try
            {
                configuracao = Configuracao.Carregar();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Configuração do banco", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            new FormReplay(new RepositorioSimulacoes(configuracao)).Show(this);
        }

        // Fechar no meio da simulação: pede o cancelamento e fecha quando a thread terminar,
        // para não deixar a transação do banco aberta pela metade.
        private void AoFechar(object? sender, FormClosingEventArgs e)
        {
            if (_execucao == null)
            {
                _renderizador.Dispose();
                return;
            }

            e.Cancel = true;
            _fecharAoTerminar = true;
            _execucao.Cancel();
        }

        // ---------------------------------------------------------------- Exibição

        private static EstadoCorpo[] Capturar(Universo universo) =>
            universo.Corpos.Select(c => c.CapturarEstado()).ToArray();

        private void DefinirExecutando(bool executando)
        {
            foreach (Control c in new Control[] { nudCorpos, nudIteracoes, nudTempo, nudThreads, nudSemente, txtNome, chkBanco, btnGerar })
                c.Enabled = !executando;

            btnSimular.Text = executando ? "Parar" : "Simular";
            btnSimular.Enabled = true;
        }

        private void MostrarQuadro(IReadOnlyList<EstadoCorpo> corpos, int iteracao, int total, bool atualizarTabela)
        {
            _quadro = corpos;
            lblStatus.Text = $"Iteração {iteracao:N0}/{total:N0}  |  " +
                             RenderizadorUniverso.DescreverCorpos(corpos, _quantidadeInicial);
            barra.Maximum = Math.Max(1, total);
            barra.Value = Math.Min(iteracao, barra.Maximum);

            if (atualizarTabela || !_relogioTabela.IsRunning || _relogioTabela.ElapsedMilliseconds >= IntervaloTabelaMs)
            {
                TabelaCorpos.Preencher(grade, corpos);
                _relogioTabela.Restart();
            }

            painel.Invalidate();
        }
    }
}
