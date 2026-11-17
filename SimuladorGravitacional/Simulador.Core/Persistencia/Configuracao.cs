using System.Text.Json;
using MySqlConnector;

namespace SimuladorGravitacional.Persistencia
{
    /// <summary>
    /// Configurações lidas do arquivo appsettings.json.
    ///
    /// Os dados de conexão ficam fora do código porque (1) mudam de máquina para máquina
    /// (Docker local, MySQL do laboratório...) sem precisar recompilar e (2) a senha não deve
    /// ir para o repositório: o appsettings.json está no .gitignore e apenas o
    /// appsettings.example.json (sem senha real) é versionado.
    /// </summary>
    public sealed class Configuracao
    {
        public const string NomeArquivo = "appsettings.json";
        public const string VariavelAmbiente = "SIMULADOR_CONFIG";

        public ConfiguracaoMySql MySql { get; set; } = new ConfiguracaoMySql();
        public ConfiguracaoGravacao Gravacao { get; set; } = new ConfiguracaoGravacao();

        /// <summary>
        /// Procura o arquivo na variável de ambiente SIMULADOR_CONFIG ou, se ela não existir,
        /// subindo pelas pastas a partir da pasta atual e da pasta do executável. Assim um único
        /// appsettings.json na raiz do repositório serve para a interface, o benchmark e os
        /// testes, sem precisar copiá-lo para cada bin/.
        /// </summary>
        public static Configuracao Carregar()
        {
            string caminho = LocalizarArquivo()
                ?? throw new FileNotFoundException(
                    $"Arquivo {NomeArquivo} não encontrado. Copie appsettings.example.json para " +
                    $"{NomeArquivo} na raiz do repositório e ajuste os dados de conexão " +
                    $"(ou aponte a variável de ambiente {VariavelAmbiente} para o arquivo).");

            return CarregarDe(caminho);
        }

        public static Configuracao CarregarDe(string caminho)
        {
            var opcoes = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            string json = File.ReadAllText(caminho);
            return JsonSerializer.Deserialize<Configuracao>(json, opcoes) ?? new Configuracao();
        }

        private static string? LocalizarArquivo()
        {
            string? doAmbiente = Environment.GetEnvironmentVariable(VariavelAmbiente);
            if (!string.IsNullOrWhiteSpace(doAmbiente))
                return File.Exists(doAmbiente) ? doAmbiente : null;

            foreach (string inicio in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                for (DirectoryInfo? pasta = new DirectoryInfo(inicio); pasta != null; pasta = pasta.Parent)
                {
                    string candidato = Path.Combine(pasta.FullName, NomeArquivo);
                    if (File.Exists(candidato)) return candidato;
                }
            }

            return null;
        }
    }

    public sealed class ConfiguracaoMySql
    {
        public string Host { get; set; } = "localhost";
        public uint Porta { get; set; } = 3306;
        public string Usuario { get; set; } = "";
        public string Senha { get; set; } = "";
        public string Banco { get; set; } = "simulador";

        public string MontarStringConexao()
        {
            var construtor = new MySqlConnectionStringBuilder
            {
                Server = Host,
                Port = Porta,
                UserID = Usuario,
                Password = Senha,
                Database = Banco,
                // Exclusões em cascata de simulações grandes podem passar dos 30 s padrão
                DefaultCommandTimeout = 300
            };
            return construtor.ConnectionString;
        }
    }

    public sealed class ConfiguracaoGravacao
    {
        /// <summary>Linhas por comando INSERT (ver comentários em GravadorMySql).</summary>
        public int LinhasPorInsert { get; set; } = 1000;

        /// <summary>Linhas por transação antes de um COMMIT.</summary>
        public int LinhasPorTransacao { get; set; } = 50000;
    }
}
