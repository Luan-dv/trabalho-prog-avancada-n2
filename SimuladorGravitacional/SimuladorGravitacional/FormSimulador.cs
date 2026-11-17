using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SimuladorGravitacional
{
    /// <summary>Painel com double buffering para desenhar sem piscar.</summary>
    public class PainelUniverso : Panel
    {
        public PainelUniverso()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }

    public class FormSimulador : Form
    {
        private const string ArquivoUniversoInicial = "universo_inicial.txt";
        private const string FiltroArquivo = "Arquivo texto|*.txt";
        private const int IntervaloTimerMs = 50;

        private Universo? _universo;
        private readonly Random _rnd = new Random();
        private readonly RenderizadorUniverso _renderizador = new RenderizadorUniverso();
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = IntervaloTimerMs };

        private readonly NumericUpDown nudCorpos = new NumericUpDown { Minimum = 1, Maximum = 500, Value = 10, Width = 60 };
        private readonly NumericUpDown nudIteracoes = new NumericUpDown { Minimum = 1, Maximum = 10000000, Value = 5000, Width = 80 };
        private readonly NumericUpDown nudTempo = new NumericUpDown { Minimum = 1, Maximum = 1000000000, Value = 3600, Width = 90 };
        private readonly Button btnGerar = new Button { Text = "Gerar universo", AutoSize = true };
        private readonly Button btnIniciar = new Button { Text = "Iniciar", AutoSize = true };
        private readonly Button btnSalvar = new Button { Text = "Salvar", AutoSize = true };
        private readonly Button btnCarregar = new Button { Text = "Carregar", AutoSize = true };
        private readonly Label lblStatus = new Label { AutoSize = true, Margin = new Padding(15, 6, 0, 0) };
        private readonly PainelUniverso painel = new PainelUniverso { Dock = DockStyle.Fill, BackColor = Color.Black };
        private readonly DataGridView grade = new DataGridView
        {
            Dock = DockStyle.Bottom,
            Height = 220,
            ReadOnly = true,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };

        public FormSimulador()
        {
            Text = "Simulador gravitacional 2D";
            ClientSize = new Size(1000, 720);

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
                btnGerar, btnIniciar, btnSalvar, btnCarregar, lblStatus
            });

            // O controle Fill deve ser adicionado primeiro
            Controls.Add(painel);
            Controls.Add(grade);
            Controls.Add(topo);
        }

        private void LigarEventos()
        {
            btnGerar.Click += (s, e) => Gerar();
            btnIniciar.Click += (s, e) => AlternarExecucao();
            btnSalvar.Click += (s, e) => Salvar();
            btnCarregar.Click += (s, e) => Carregar();
            _timer.Tick += (s, e) => Passo();
            painel.Paint += Desenhar;
        }

        private static Label Rotulo(string texto)
        {
            return new Label { Text = texto, AutoSize = true, Margin = new Padding(8, 6, 0, 0) };
        }

        // ---------------------------------------------------------------- Ações dos botões

        private void Gerar()
        {
            Pausar();

            var universo = new Universo
            {
                TotalIteracoes = (int)nudIteracoes.Value,
                PassoDeTempo = (double)nudTempo.Value
            };
            universo.GerarAleatoriamente((int)nudCorpos.Value, _rnd);
            _universo = universo;

            // Grava a posição inicial de cada corpo
            string arquivo = Path.Combine(AppContext.BaseDirectory, ArquivoUniversoInicial);
            RepositorioUniverso repo = new RepositorioArquivoTexto(arquivo);
            repo.Gravar(universo);

            Atualizar(universo);
        }

        private void AlternarExecucao()
        {
            if (_universo == null)
            {
                MessageBox.Show("Gere ou carregue um universo primeiro.");
                return;
            }

            if (_timer.Enabled)
            {
                Pausar();
            }
            else
            {
                _timer.Start();
                btnIniciar.Text = "Pausar";
            }
        }

        private void Pausar()
        {
            _timer.Stop();
            btnIniciar.Text = "Iniciar";
        }

        private void Passo()
        {
            if (_universo == null) return;

            _universo.Avancar();
            Atualizar(_universo);
            if (_universo.Concluido) Pausar();
        }

        private void Salvar()
        {
            if (_universo == null) return;

            using (SaveFileDialog dlg = new SaveFileDialog { Filter = FiltroArquivo })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                RepositorioUniverso repo = new RepositorioArquivoTexto(dlg.FileName);
                repo.Gravar(_universo);
            }
        }

        private void Carregar()
        {
            using (OpenFileDialog dlg = new OpenFileDialog { Filter = FiltroArquivo })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;

                Pausar();
                RepositorioUniverso repo = new RepositorioArquivoTexto(dlg.FileName);
                Universo universo = repo.Carregar();
                _universo = universo;

                nudIteracoes.Value = Math.Clamp((decimal)universo.TotalIteracoes, nudIteracoes.Minimum, nudIteracoes.Maximum);
                nudTempo.Value = Math.Clamp((decimal)universo.PassoDeTempo, nudTempo.Minimum, nudTempo.Maximum);
                Atualizar(universo);
            }
        }

        // ---------------------------------------------------------------- Exibição

        // Exibe a situação dos corpos na tela a cada iteração
        private void Atualizar(Universo universo)
        {
            lblStatus.Text = "Iteração " + universo.IteracaoAtual + "/" + universo.TotalIteracoes +
                             "  |  Corpos: " + universo.Corpos.Count;

            grade.DataSource = universo.Corpos.Select(c => new
            {
                c.Nome,
                Massa = c.Massa.ToString("E3"),
                Densidade = c.Densidade.ToString("E3"),
                Raio = c.Raio.ToString("E3"),
                PosX = c.PosicaoX.ToString("E3"),
                PosY = c.PosicaoY.ToString("E3"),
                VelX = c.VelocidadeX.ToString("E3"),
                VelY = c.VelocidadeY.ToString("E3")
            }).ToList();

            painel.Invalidate();
        }

        private void Desenhar(object? sender, PaintEventArgs e)
        {
            _renderizador.Desenhar(e.Graphics, painel.ClientSize, _universo);
        }
    }
}
