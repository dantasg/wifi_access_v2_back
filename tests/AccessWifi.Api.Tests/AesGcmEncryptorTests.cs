using Models.Security;

namespace AccessWifi.Api.Tests;

public class AesGcmEncryptorTests
{
    // Chave AES de 32 bytes (base64) para os testes.
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    private static AesGcmEncryptor Create() => new AesGcmEncryptor(Key);

    [Fact]
    public void EncryptDecrypt_IdaEVolta_RecuperaOTextoOriginal()
    {
        AesGcmEncryptor objEncryptor = Create();

        string? sCifrado = objEncryptor.Encrypt("senha-super-secreta");

        Assert.NotNull(sCifrado);
        Assert.StartsWith("enc:v1:", sCifrado);
        Assert.NotEqual("senha-super-secreta", sCifrado);
        Assert.Equal("senha-super-secreta", objEncryptor.Decrypt(sCifrado));
    }

    [Fact]
    public void Encrypt_MesmoTexto_GeraCifradosDiferentes_MasDecifraIgual()
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
    public void Decrypt_TextoPuroSemPrefixo_DevolveComoEsta()
    {
        // Tolerância a dados legados (gravados antes da cifragem).
        Assert.Equal("texto-puro-legado", Create().Decrypt("texto-puro-legado"));
    }

    [Fact]
    public void Encrypt_NuloOuVazio_PassaDireto()
    {
        AesGcmEncryptor objEncryptor = Create();
        Assert.Null(objEncryptor.Encrypt(null));
        Assert.Equal("", objEncryptor.Encrypt(""));
    }

    [Fact]
    public void Ctor_ChaveInvalida_Lanca()
    {
        Assert.Throws<InvalidOperationException>(() => new AesGcmEncryptor(""));
        Assert.Throws<InvalidOperationException>(() => new AesGcmEncryptor("chave-curta"));
    }

    [Fact]
    public void Decrypt_ChaveDiferente_Falha()
    {
        string? sCifrado = Create().Encrypt("segredo");
        // Outra chave de 32 bytes.
        AesGcmEncryptor objOutro = new AesGcmEncryptor("YWJjZGVmZ2hpamtsbW5vcHFyc3R1dnd4eXowMTIzNDU=");

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(
            () => objOutro.Decrypt(sCifrado));
    }
}
