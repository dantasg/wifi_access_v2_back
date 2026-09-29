using Models.DataBase;

namespace AccessWifiService.Campaigns
{
    /// <summary>Resultado de um envio: o status final do destinatário e, se falhou, o motivo.</summary>
    public sealed record MessageSendResult(string Status, string? Reason = null);

    /// <summary>
    /// A "porta" do envio. Cada canal (WhatsApp, Instagram) implementa a sua numa fase seguinte; a
    /// execução não muda quando isso acontecer.
    /// </summary>
    public interface IMessageChannel
    {
        Task<MessageSendResult> SendAsync(
            CampaignRecipient objRecipient, string sChannel, CancellationToken objCancellationToken = default);
    }

    /// <summary>
    /// Modo simulação (D14): percorre tudo como se enviasse, sem mandar nada. Serve para testar a
    /// campanha e ver quem receberia antes de o WhatsApp existir.
    /// </summary>
    public sealed class SimulatedMessageChannel : IMessageChannel
    {
        public Task<MessageSendResult> SendAsync(
            CampaignRecipient objRecipient, string sChannel, CancellationToken objCancellationToken = default)
        {
            return Task.FromResult(new MessageSendResult(CampaignRecipientStatus.Simulated));
        }
    }
}
