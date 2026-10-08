namespace Models.DataBase
{
    /// <summary>
    /// Aparelho UniFi de uma unidade (ponto de acesso, switch ou controladora), lido da nuvem da Ubiquiti.
    /// É por ele que o portal acha a loja: em toda visita a UniFi manda o MAC do ponto de acesso (<c>ap</c>),
    /// e todas as lojas podem usar o mesmo endereço do portal.
    /// </summary>
    public class UnitDevice
    {
        public Guid IDUnit { get; set; }

        /// <summary>No formato de <see cref="MacAddress.Normalize"/>: "aa:bb:cc:dd:ee:ff".</summary>
        public string Mac { get; set; } = "";

        public string Name { get; set; } = "";
        public string Model { get; set; } = "";

        /// <summary>Última sincronização em que o aparelho apareceu no console da unidade (UTC).</summary>
        public DateTime SyncedAt { get; set; }
    }

    public static class MacAddress
    {
        /// <summary>
        /// Aceita "AA:BB:...", "aa-bb-..." ou "aabb..." e devolve "aa:bb:cc:dd:ee:ff"; vazio quando não é um MAC.
        /// </summary>
        public static string Normalize(string? sMac)
        {
            string sValue = (sMac ?? "").Trim();
            string sHex = new string(sValue.Where(Uri.IsHexDigit).ToArray()).ToLowerInvariant();
            if (sHex.Length != 12 || sValue.Length > 17)
            {
                return "";
            }
            return string.Join(':', Enumerable.Range(0, 6).Select(i => sHex.Substring(i * 2, 2)));
        }
    }
}
