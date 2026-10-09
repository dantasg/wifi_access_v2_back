using System.Text.Json;
using System.Text.Json.Serialization;

namespace AccessWifi.Api.Features.Authorize
{
    /// <summary>
    /// Lê o pedido do portal aceitando também os nomes antigos dos campos ("nome", "telefone",
    /// "nascimento", "consentimento"). Um celular que abriu o portal antes de uma publicação ainda manda
    /// o formato antigo e precisa conseguir se cadastrar. Pode sair depois que a versão com os nomes em
    /// inglês estiver no ar há alguns dias.
    /// </summary>
    public sealed class AuthorizeRequestJsonConverter : JsonConverter<AuthorizeRequest>
    {
        public override AuthorizeRequest? Read(ref Utf8JsonReader objReader, Type objTypeToConvert, JsonSerializerOptions objOptions)
        {
            using JsonDocument objDocument = JsonDocument.ParseValue(ref objReader);
            JsonElement objRoot = objDocument.RootElement;
            if (objRoot.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("O pedido do portal precisa ser um objeto JSON.");
            }

            // Campo ausente fica nulo: a validação do modelo devolve 400, como antes.
            return new AuthorizeRequest(
                ReadString(objRoot, "name", "nome")!,
                ReadString(objRoot, "instagram")!,
                ReadString(objRoot, "phone", "telefone")!,
                ReadString(objRoot, "birthDate", "nascimento")!,
                ReadBool(objRoot, "consent", "consentimento"),
                ReadString(objRoot, "unit"),
                ReadString(objRoot, "mac"),
                ReadString(objRoot, "ap"),
                ReadString(objRoot, "ssid"),
                ReadString(objRoot, "url"),
                ReadString(objRoot, "host"));
        }

        public override void Write(Utf8JsonWriter objWriter, AuthorizeRequest objValue, JsonSerializerOptions objOptions)
        {
            // O pedido só chega, nunca sai pela API.
            throw new NotSupportedException();
        }

        /// <summary>Procura o campo pelo nome (sem diferenciar maiúsculas), depois pelos nomes antigos.</summary>
        private static JsonElement? Find(JsonElement objRoot, string[] arrNames)
        {
            foreach (string sName in arrNames)
            {
                foreach (JsonProperty objProperty in objRoot.EnumerateObject())
                {
                    if (string.Equals(objProperty.Name, sName, StringComparison.OrdinalIgnoreCase)
                        && objProperty.Value.ValueKind != JsonValueKind.Null)
                    {
                        return objProperty.Value;
                    }
                }
            }
            return null;
        }

        private static string? ReadString(JsonElement objRoot, params string[] arrNames)
        {
            JsonElement? objValue = Find(objRoot, arrNames);
            if (objValue is null)
            {
                return null;
            }
            return objValue.Value.ValueKind == JsonValueKind.String
                ? objValue.Value.GetString()
                : throw new JsonException($"O campo \"{arrNames[0]}\" precisa ser texto.");
        }

        private static bool ReadBool(JsonElement objRoot, params string[] arrNames)
        {
            JsonElement? objValue = Find(objRoot, arrNames);
            return objValue?.ValueKind switch
            {
                null => false,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new JsonException($"O campo \"{arrNames[0]}\" precisa ser verdadeiro ou falso."),
            };
        }
    }
}
