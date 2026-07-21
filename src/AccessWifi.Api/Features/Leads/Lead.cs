namespace AccessWifi.Api.Features.Leads;

/// <summary>Cadastro feito pelo visitante no portal (uma linha do relatório do admin).</summary>
public class Lead
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Sempre em UTC — serializa em ISO 8601 para o front.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string Nome { get; set; } = "";
    public string Instagram { get; set; } = "";
    public string Telefone { get; set; } = "";

    /// <summary>Guardado como veio do front (dd/mm/aaaa).</summary>
    public string Nascimento { get; set; } = "";

    public string? Mac { get; set; }
    public string? Ap { get; set; }
    public string? Ssid { get; set; }
}
