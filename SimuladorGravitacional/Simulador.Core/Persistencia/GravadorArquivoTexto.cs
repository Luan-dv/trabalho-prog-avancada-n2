using System.Globalization;

namespace SimuladorGravitacional.Persistencia
{
    /// <summary>
    /// Gravação em arquivo texto (modo N1), usada como linha de base no benchmark.
    ///
    /// A N1 original gravava só o estado inicial. Para comparar arquivo × banco de forma justa,
    /// este gravador escreve exatamente as mesmas informações que vão para o MySQL, em todas as
    /// interações. Formato (separador ';', cultura invariante):
    ///   1ª linha: corpos;interações;passo de tempo   (mesmo cabeçalho da N1)
    ///   E;interação;id;nome;massa;densidade;raio;posX;posY;velX;velY;acelX;acelY;forçaX;forçaY
    ///   F;interação;id absorvedor;id absorvido
    /// </summary>
    public sealed class GravadorArquivoTexto : IGravadorSimulacao
    {
        private const char Separador = ';';
        private static readonly CultureInfo Cultura = CultureInfo.InvariantCulture;

        private readonly StreamWriter _escritor;

        public GravadorArquivoTexto(string caminho)
        {
            // Buffer de 1 MB: o sistema operacional recebe blocos grandes em vez de uma escrita
            // por linha. É o equivalente, no arquivo, ao INSERT em lote do banco.
            _escritor = new StreamWriter(caminho, append: false, System.Text.Encoding.UTF8, bufferSize: 1 << 20);
        }

        public long LinhasGravadas { get; private set; }

        public void Iniciar(DadosSimulacao dados, IReadOnlyList<Corpo> corpos)
        {
            _escritor.Write(dados.QtdCorpos.ToString(Cultura));
            _escritor.Write(Separador);
            _escritor.Write(dados.QtdInteracoes.ToString(Cultura));
            _escritor.Write(Separador);
            EscreverNumero(dados.PassoDeTempo);
            _escritor.WriteLine();
        }

        public void GravarInteracao(int interacao, IReadOnlyList<Corpo> corpos, IReadOnlyList<Fusao> fusoes)
        {
            foreach (Fusao f in fusoes)
                _escritor.WriteLine(string.Create(Cultura, $"F;{interacao};{f.AbsorvedorId};{f.AbsorvidoId}"));

            for (int i = 0; i < corpos.Count; i++)
            {
                Corpo c = corpos[i];
                _escritor.Write("E;");
                _escritor.Write(interacao.ToString(Cultura));
                _escritor.Write(Separador);
                _escritor.Write(c.Id.ToString(Cultura));
                _escritor.Write(Separador);
                _escritor.Write(c.Nome.Replace(Separador, ','));   // o nome não pode quebrar o formato
                EscreverCampo(c.Massa);
                EscreverCampo(c.Densidade);
                EscreverCampo(c.Raio);
                EscreverCampo(c.PosicaoX);
                EscreverCampo(c.PosicaoY);
                EscreverCampo(c.VelocidadeX);
                EscreverCampo(c.VelocidadeY);

                if (interacao == 0)
                {
                    // Estado inicial: nenhuma aceleração foi aplicada ainda (no banco vira NULL)
                    _escritor.Write(";;;;");
                }
                else
                {
                    EscreverCampo(c.AceleracaoX);
                    EscreverCampo(c.AceleracaoY);
                    EscreverCampo(c.ForcaX);
                    EscreverCampo(c.ForcaY);
                }
                _escritor.WriteLine();
                LinhasGravadas++;
            }
        }

        public void Concluir() => _escritor.Flush();

        public void RegistrarResultado(ResultadoExecucao resultado)
        {
            // O formato da N1 não tem onde guardar tempos; eles ficam no CSV do benchmark.
        }

        public void Dispose() => _escritor.Dispose();

        private void EscreverCampo(double valor)
        {
            _escritor.Write(Separador);
            EscreverNumero(valor);
        }

        // Formata direto num buffer da pilha, sem criar uma string por número (são ~14 por linha)
        private void EscreverNumero(double valor)
        {
            Span<char> buffer = stackalloc char[32];
            valor.TryFormat(buffer, out int escritos, "R", Cultura);
            _escritor.Write(buffer[..escritos]);
        }
    }
}
