using Models.Security;

namespace AccessWifi.Api.Tests;

public class AesGcmEncryptorTests
{
    // Chave AES de 32 bytes (base64) para os testes.
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    private static AesGcmEncryptor Create() => new AesGcmEncryptor(Key);

    [Fact]
    public void EncryptDecrypt_RoundTrip_RecoversOriginalText()
    {
        AesGcmEncryptor objEncryptor = Create();

        string? sEncrypted = objEncryptor.Encrypt("senha-super-secreta");

        Assert.NotNull(sEncrypted);
        Assert.StartsWith("enc:v1:", sEncrypted);
        Assert.NotEqual("senha-super-secreta", sEncrypted);
        Assert.Equal("senha-super-secreta", objEncryptor.Decrypt(sEncrypted));
    }

    [Fact]
    public void Encrypt_SameText_ProducesDifferentCiphertexts_ButDecryptsEqual()
    {
        AesGcmEncryptor objEncryptor = Create();

        string? sA = objEncryptor.Encrypt("igual");
        string? sB = objEncryptor.Encrypt("igual");

        // Nonce aleatório: ciphertexts distintos, mas ambos decifram para o mesmo valor.
        Assert.NotEqual(sA, sB);
        Assert.Equal("igual", objEncryptor.Decrypt(sA));
        Assert.Equal("igual", objEncryptor.Decrypt(sB));
    }

    [Fact]
    public void Decrypt_PlainTextWithoutPrefix_ReturnsAsIs()
    {
        // Tolerância a dados legados (gravados antes da cifragem).
        Assert.Equal("texto-puro-legado", Create().Decrypt("texto-puro-legado"));
    }

    [Fact]
    public void Encrypt_NullOrEmpty_PassesThrough()
    {
        AesGcmEncryptor objEncryptor = Create();
        Assert.Null(objEncryptor.Encrypt(null));
        Assert.Equal("", objEncryptor.Encrypt(""));
    }

    [Fact]
    public void Ctor_InvalidKey_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new AesGcmEncryptor(""));
        Assert.Throws<InvalidOperationException>(() => new AesGcmEncryptor("chave-curta"));
    }

    [Fact]
    public void Decrypt_DifferentKey_Fails()
    {
        string? sEncrypted = Create().Encrypt("segredo");
        // Outra chave de 32 bytes.
        AesGcmEncryptor objOther = new AesGcmEncryptor("YWJjZGVmZ2hpamtsbW5vcHFyc3R1dnd4eXowMTIzNDU=");

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(
            () => objOther.Decrypt(sEncrypted));
    }
}
