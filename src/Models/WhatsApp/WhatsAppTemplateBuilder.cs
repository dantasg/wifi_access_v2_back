using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Models.Campaigns;
using Models.Persistence;

namespace Models.WhatsApp
{
    /// <summary>Os campos que viram variável no modelo da Meta. Os nomes ficam gravados no modelo (ParametersJson).</summary>
    public static class WhatsAppTemplateField
    {
        public const string FirstName = "primeiro_nome";
        public const string FullName = "nome";
        public const string Unit = "unidade";
        public const string Age = "idade";
        public const string YearsSinceSignup = "anos_de_cadastro";

        /// <summary>O WhatsApp de atendimento da loja do cliente, no fim de toda mensagem (D10).</summary>
        public const string StorePhone = "telefone_loja";
    }

    /// <summary>
    /// O modelo montado. <paramref name="Invalid"/> preenchido = o texto não segue as regras do WhatsApp e não vai
    /// para a Meta (o motivo, em português, aparece na tela).
    /// </summary>
    public sealed record WhatsAppTemplateContent(
        string BodyText, string FooterText, IReadOnlyList<string> Parameters, string? Invalid);

    /// <summary>Os dados de um cliente para os campos do modelo. StorePhone já formatado ("(93) 99123-4567").</summary>
    public sealed record WhatsAppMessageValues(
        string Name, string UnitName, int? Age, int? YearsSinceSignup, string StorePhone);

    /// <summary>Dados reais da empresa para os exemplos que a Meta pede junto do modelo.</summary>
    public sealed record WhatsAppTemplateSamples(string UnitName, string StorePhone)
    {
        /// <summary>O nome e o WhatsApp de uma unidade ativa da empresa (de preferência uma com o WhatsApp cadastrado).</summary>
        public static async Task<WhatsAppTemplateSamples> LoadAsync(
            AppDbContext objDbContext, Guid objCompanyId, CancellationToken objCancellationToken)
        {
            var objUnit = await objDbContext.Units.AsNoTracking()
                .Where(unit => unit.IDCompany == objCompanyId && unit.Active)
                .OrderBy(unit => unit.StorePhone == "" ? 1 : 0)
                .ThenBy(unit => unit.Name)
                .Select(unit => new { unit.Name, unit.StorePhone })
                .FirstOrDefaultAsync(objCancellationToken);
            return objUnit is null
                ? new WhatsAppTemplateSamples("", "")
                : new WhatsAppTemplateSamples(
                    objUnit.Name, objUnit.StorePhone.Length > 0 ? CampaignContact.FormatPhone(objUnit.StorePhone) : "");
        }
    }

    /// <summary>
    /// Transforma o texto de uma campanha num modelo da Meta (PROPOSTA_WHATSAPP.md, D8 e D10):
    /// <list type="bullet">
    /// <item>{primeiro_nome}, {nome}, {unidade}, {idade} e {anos_de_cadastro} viram {{1}}, {{2}}… na ordem em que
    /// aparecem;</item>
    /// <item>{empresa} entra já escrito: é o mesmo para todos os clientes da empresa;</item>
    /// <item>no fim vai o WhatsApp de atendimento da loja e, no rodapé, "Para não receber mais, responda SAIR.".</item>
    /// </list>
    /// Regras da Meta (template-review): o modelo não pode começar nem terminar com variável, e o corpo tem no
    /// máximo 1024 caracteres. Texto que começa com um campo ganha um "Olá, " na frente.
    /// </summary>
    public static class WhatsAppTemplateBuilder
    {
        public const string Footer = "Para não receber mais, responda SAIR.";
        public const string Greeting = "Olá, ";
        public const int MaxBodyChars = 1024;
        public const string Language = "pt_BR";
        public const string Category = "MARKETING";

        private const string StoreLine = "Fale com a loja pelo WhatsApp {0} sempre que precisar.";

        private static readonly JsonSerializerOptions s_objJsonOptions = new(JsonSerializerDefaults.Web);

        /// <summary>Campo do texto da campanha → nome da variável no modelo.</summary>
        private static readonly IReadOnlyList<(string sToken, string sField)> s_objFields =
        [
            (CampaignMessage.FirstName, WhatsAppTemplateField.FirstName),
            (CampaignMessage.FullName, WhatsAppTemplateField.FullName),
            (CampaignMessage.UnitField, WhatsAppTemplateField.Unit),
            (CampaignMessage.Age, WhatsAppTemplateField.Age),
            (CampaignMessage.YearsSinceSignup, WhatsAppTemplateField.YearsSinceSignup),
        ];

        public static WhatsAppTemplateContent Build(string? sMessage, string sCompanyName)
        {
            string sText = (sMessage ?? "").Replace("\r\n", "\n").Trim()
                .Replace(CampaignMessage.Company, sCompanyName.Trim());

            // A Meta não aceita modelo que começa com variável ("dangling parameters").
            if (FieldAt(sText, 0) is not null)
            {
                sText = Greeting + sText;
            }

            List<string> objParameters = [];
            StringBuilder objBody = new StringBuilder();
            int iIndex = 0;
            while (iIndex < sText.Length)
            {
                (string sToken, string sField)? objField = FieldAt(sText, iIndex);
                if (objField is (string sToken, string sField))
                {
                    objParameters.Add(sField);
                    objBody.Append("{{").Append(objParameters.Count).Append("}}");
                    iIndex += sToken.Length;
                    continue;
                }
                objBody.Append(sText[iIndex]);
                iIndex++;
            }

            objParameters.Add(WhatsAppTemplateField.StorePhone);
            objBody.Append("\n\n").Append(string.Format(StoreLine, "{{" + objParameters.Count + "}}"));
            string sBody = objBody.ToString();

            string? sInvalid = null;
            if (sText.Contains("{{", StringComparison.Ordinal) || sText.Contains("}}", StringComparison.Ordinal))
            {
                sInvalid = "Tire as chaves duplas ({{ ou }}) do texto: no WhatsApp elas marcam os campos.";
            }
            else if (sBody.Length > MaxBodyChars)
            {
                sInvalid = $"Com o final automático (o WhatsApp da loja), o texto ficou com {sBody.Length} caracteres, e o "
                    + $"WhatsApp aceita até {MaxBodyChars}. Tire pelo menos {sBody.Length - MaxBodyChars} caracteres.";
            }

            return new WhatsAppTemplateContent(sBody, Footer, objParameters, sInvalid);
        }

        /// <summary>Um exemplo para cada variável, como a Meta pede na criação do modelo.</summary>
        public static List<string> Examples(IReadOnlyList<string> objParameters, WhatsAppTemplateSamples objSamples) =>
            objParameters.Select(sField => sField switch
            {
                WhatsAppTemplateField.FirstName => "Maria",
                WhatsAppTemplateField.FullName => "Maria Souza",
                WhatsAppTemplateField.Unit => objSamples.UnitName.Length > 0 ? objSamples.UnitName : "Centro",
                WhatsAppTemplateField.Age => "30",
                WhatsAppTemplateField.YearsSinceSignup => "2",
                WhatsAppTemplateField.StorePhone => objSamples.StorePhone.Length > 0 ? objSamples.StorePhone : "(93) 99123-4567",
                _ => "exemplo",
            }).ToList();

        /// <summary>
        /// Os valores de um cliente, na ordem das variáveis do modelo. Campo sem dado (ex.: idade de quem não informou
        /// o nascimento) devolve nulo e o nome do campo: a Meta não aceita variável vazia.
        /// </summary>
        public static (List<string>? objValues, string sMissing) Values(
            IReadOnlyList<string> objParameters, WhatsAppMessageValues objData)
        {
            string sName = Clean(objData.Name);
            List<string> objValues = [];
            foreach (string sField in objParameters)
            {
                (string sValue, string sLabel) = sField switch
                {
                    WhatsAppTemplateField.FirstName =>
                        (sName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "", "primeiro nome"),
                    WhatsAppTemplateField.FullName => (sName, "nome"),
                    WhatsAppTemplateField.Unit => (Clean(objData.UnitName), "unidade"),
                    WhatsAppTemplateField.Age => (objData.Age?.ToString() ?? "", "idade"),
                    WhatsAppTemplateField.YearsSinceSignup => (objData.YearsSinceSignup?.ToString() ?? "", "anos de cadastro"),
                    WhatsAppTemplateField.StorePhone => (Clean(objData.StorePhone), "WhatsApp da unidade"),
                    _ => ("", sField),
                };
                if (sValue.Length == 0)
                {
                    return (null, sLabel);
                }
                objValues.Add(sValue);
            }
            return (objValues, "");
        }

        /// <summary>O corpo com os valores no lugar de {{1}}, {{2}}…: o texto como o cliente lê.</summary>
        public static string Render(string sBodyText, IReadOnlyList<string> objValues)
        {
            string sText = sBodyText;
            // Do maior para o menor: {{12}} antes de {{1}}.
            for (int iPosition = objValues.Count; iPosition >= 1; iPosition--)
            {
                sText = sText.Replace("{{" + iPosition + "}}", objValues[iPosition - 1], StringComparison.Ordinal);
            }
            return sText;
        }

        /// <summary>O modelo com os exemplos no lugar dos campos, mais o rodapé: como o cliente vê no WhatsApp.</summary>
        public static string Preview(
            string sBodyText, string sFooterText, IReadOnlyList<string> objParameters, WhatsAppTemplateSamples objSamples)
        {
            string sBody = Render(sBodyText, Examples(objParameters, objSamples));
            return sFooterText.Length > 0 ? sBody + "\n\n" + sFooterText : sBody;
        }

        /// <summary>
        /// Nome do modelo na Meta: só minúsculas, números e "_" (template-management). Ex.: "aw_birthday_3f2a1b9c_v4".
        /// </summary>
        public static string NameFor(string sKind, Guid objCampaignId, int iVersion) =>
            $"aw_{new string(sKind.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray())}_"
            + $"{objCampaignId.ToString("N")[..8]}_v{iVersion}";

        public static string ParametersToJson(IReadOnlyList<string> objParameters) =>
            JsonSerializer.Serialize(objParameters, s_objJsonOptions);

        public static List<string> ParametersFromJson(string? sJson)
        {
            if (string.IsNullOrWhiteSpace(sJson))
            {
                return [];
            }
            return JsonSerializer.Deserialize<List<string>>(sJson, s_objJsonOptions) ?? [];
        }

        /// <summary>Valor de variável numa linha só, sem espaços repetidos (quebra de linha e tabulação viram espaço).</summary>
        private static string Clean(string? sValue) =>
            string.Join(' ', (sValue ?? "").Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));

        private static (string sToken, string sField)? FieldAt(string sText, int iIndex)
        {
            foreach ((string sToken, string sField) in s_objFields)
            {
                if (string.CompareOrdinal(sText, iIndex, sToken, 0, sToken.Length) == 0)
                {
                    return (sToken, sField);
                }
            }
            return null;
        }
    }
}
