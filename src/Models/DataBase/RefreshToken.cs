namespace Models.DataBase
{
    /// <summary>
    /// Refresh token (durável) emitido no login. Guardamos só o HASH do token — o valor bruto
    /// vive no cliente. Usado para renovar o access token (curto) sem novo login e para revogar.
    /// </summary>
    public class RefreshToken
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Usuário admin dono do token.</summary>
        public Guid IDUser { get; set; }

        /// <summary>SHA-256 (base64) do token bruto — nunca guardamos o valor em claro.</summary>
        public string TokenHash { get; set; } = "";

        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Quando foi revogado (logout ou rotação). Nulo = ativo.</summary>
        public DateTime? RevokedAt { get; set; }
    }
}
