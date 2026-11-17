namespace SimuladorGravitacional
{
    internal static class Program
    {
        /// <summary>Arquivo onde ficam registrados os erros não tratados, com a pilha completa.</summary>
        public static readonly string ArquivoDeErros = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SimuladorGravitacional", "erros.log");

        [STAThread]
        static void Main()
        {
            // Sem estes tratadores, uma exceção não prevista fecha o programa sem explicação.
            // Com eles, o erro aparece na tela e fica registrado em arquivo para diagnóstico.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => RegistrarErro(e.Exception, encerrando: false);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => RegistrarErro((Exception)e.ExceptionObject, encerrando: true);

            try
            {
                ApplicationConfiguration.Initialize();
                Application.Run(new FormSimulador());
            }
            catch (Exception ex)
            {
                RegistrarErro(ex, encerrando: true);
            }
        }

        private static void RegistrarErro(Exception ex, bool encerrando)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ArquivoDeErros)!);
                File.AppendAllText(ArquivoDeErros, $"===== {DateTime.Now:dd/MM/yyyy HH:mm:ss} =====\n{ex}\n\n");
            }
            catch
            {
                // Se nem o log puder ser gravado, ao menos a mensagem abaixo é exibida
            }

            MessageBox.Show(
                $"Ocorreu um erro inesperado{(encerrando ? " e o programa será fechado" : "")}:\n\n{ex.Message}\n\n" +
                $"Detalhes registrados em:\n{ArquivoDeErros}",
                "Simulador gravitacional", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
