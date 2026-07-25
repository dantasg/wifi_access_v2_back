using System.Security.Cryptography;
using System.Text;

namespace Models.Security
{
    /// <summary>Cifra/decifra strings sensíveis para guardar no banco (senhas de UniFi/SMTP).</summary>
    public interface IEncryptor
    {
        /// <summary>Cifra o texto. Null/vazio passa direto; texto já cifrado não é cifrado de novo.</summary>
        string? Encrypt(string? sPlainText);

        /// <summary>Decifra. Valor sem o prefixo de cifra é devolvido como está (tolera texto legado).</summary>
        string? Decrypt(string? sValue);
    }

    /// <summary>
    /// AES-256-GCM com chave em "Encryption:Key" (base64 de 32 bytes, via env/user-secrets).
    /// Formato guardado: "enc:v1:" + base64(nonce[12] + tag[16] + ciphertext). Valores sem o
    /// prefixo são tratados como texto puro (permite migração gradual de dados já existentes).
    /// </summary>
    public class AesGcmEncryptor : IEncryptor
    {
        private const string Prefix = "enc:v1:";
        private const int NonceSize = 12; // AesGcm.NonceByteSizes.MaxSize
        private const int TagSize = 16;   // AesGcm.TagByteSizes.MaxSize

        private readonly byte[] _arrKey;

        public AesGcmEncryptor(string? sBase64Key)
        {
            if (string.IsNullOrWhiteSpace(sBase64Key))
            {
                throw new InvalidOperationException(
                    "Encryption:Key não configurada. Gere com 'openssl rand -base64 32'.");
            }

            byte[] arrKey;
            try
            {
                arrKey = Convert.FromBase64String(sBase64Key);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException("Encryption:Key deve ser um base64 válido.");
            }

            if (arrKey.Length != 32)
            {
                throw new InvalidOperationException(
                    "Encryption:Key deve ter 32 bytes (256 bits) — use 'openssl rand -base64 32'.");
            }

            _arrKey = arrKey;
        }

        public string? Encrypt(string? sPlainText)
        {
            if (string.IsNullOrEmpty(sPlainText) || sPlainText.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return sPlainText;
            }

            byte[] arrPlain = Encoding.UTF8.GetBytes(sPlainText);
            byte[] arrNonce = RandomNumberGenerator.GetBytes(NonceSize);
            byte[] arrCipher = new byte[arrPlain.Length];
            byte[] arrTag = new byte[TagSize];

            using (AesGcm objAes = new AesGcm(_arrKey, TagSize))
            {
                objAes.Encrypt(arrNonce, arrPlain, arrCipher, arrTag);
            }

            byte[] arrCombined = new byte[NonceSize + TagSize + arrCipher.Length];
            Buffer.BlockCopy(arrNonce, 0, arrCombined, 0, NonceSize);
            Buffer.BlockCopy(arrTag, 0, arrCombined, NonceSize, TagSize);
            Buffer.BlockCopy(arrCipher, 0, arrCombined, NonceSize + TagSize, arrCipher.Length);

            return Prefix + Convert.ToBase64String(arrCombined);
        }

        public string? Decrypt(string? sValue)
        {
            if (string.IsNullOrEmpty(sValue) || !sValue.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return sValue; // texto puro (legado) — devolve como está.
            }

            byte[] arrCombined = Convert.FromBase64String(sValue.Substring(Prefix.Length));
            byte[] arrNonce = new byte[NonceSize];
            byte[] arrTag = new byte[TagSize];
            byte[] arrCipher = new byte[arrCombined.Length - NonceSize - TagSize];
            Buffer.BlockCopy(arrCombined, 0, arrNonce, 0, NonceSize);
            Buffer.BlockCopy(arrCombined, NonceSize, arrTag, 0, TagSize);
            Buffer.BlockCopy(arrCombined, NonceSize + TagSize, arrCipher, 0, arrCipher.Length);

            byte[] arrPlain = new byte[arrCipher.Length];
            using (AesGcm objAes = new AesGcm(_arrKey, TagSize))
            {
                objAes.Decrypt(arrNonce, arrCipher, arrTag, arrPlain);
            }

            return Encoding.UTF8.GetString(arrPlain);
        }
    }
}
