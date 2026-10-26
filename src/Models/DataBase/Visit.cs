namespace Models.DataBase
{
    /// <summary>De onde veio a visita.</summary>
    public enum VisitSource
    {
        /// <summary>O cliente passou pelo portal.</summary>
        Portal,

        /// <summary>O aparelho do cliente conectou e a UniFi avisou, sem passar pelo portal (PROPOSTA_VISITAS_UNIFI.md).</summary>
        Unifi,
    }

    /// <summary>
    /// Uma visita: a conexão liberada pelo portal (PROPOSTA_DASHBOARD.md, D3) ou a volta avisada pela UniFi, sem
    /// portal (PROPOSTA_VISITAS_UNIFI.md). É daqui que o dashboard tira as conexões por dia, o horário de pico, o
    /// retorno por loja, as conexões por ponto de acesso e o tempo na loja. Gravada no /authorize depois da
    /// liberação e no webhook da UniFi; o histórico começa no dia em que a tabela entrou no ar.
    /// Sem dado pessoal: o cliente é só o vínculo, que fica nulo quando a retenção apaga o cliente (D8) — os
    /// totais antigos não mudam.
    /// </summary>
    public class Visit
    {
        public long Id { get; set; }
        public Guid IDUnit { get; set; }

        /// <summary>Cliente da empresa (por telefone). Nulo quando o telefone não identificou ninguém ou o cliente foi apagado.</summary>
        public Guid? IDCustomer { get; set; }

        /// <summary>Quando foi liberada (UTC).</summary>
        public DateTime At { get; set; }

        /// <summary>Dia e hora (0–23) no fuso da empresa: o dashboard agrupa por eles.</summary>
        public DateOnly LocalDate { get; set; }
        public int LocalHour { get; set; }

        /// <summary>Primeira visita do cliente na empresa e nesta unidade.</summary>
        public bool NewInCompany { get; set; }
        public bool NewInUnit { get; set; }

        /// <summary>Ponto de acesso, no formato de <see cref="MacAddress.Normalize"/> ("" quando a UniFi não mandou).</summary>
        public string Ap { get; set; } = "";

        /// <summary>
        /// Portal ou aviso da UniFi. Com o cliente conhecido, é uma visita por cliente, unidade e dia, venha de onde
        /// vier (PROPOSTA_VISITAS_UNIFI.md, D3).
        /// </summary>
        public VisitSource Source { get; set; } = VisitSource.Portal;

        /// <summary>
        /// Quanto tempo o cliente ficou na loja nesse dia, em segundos, pelos sinais do aparelho na UniFi (D6).
        /// Nulo = não medido (a unidade não manda os avisos, ou ainda não chegou nenhum aviso de desconexão).
        /// </summary>
        public int? StaySeconds { get; set; }
    }
}
