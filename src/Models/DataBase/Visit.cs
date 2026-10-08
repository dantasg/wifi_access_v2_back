namespace Models.DataBase
{
    /// <summary>
    /// Uma conexão liberada pelo portal (PROPOSTA_DASHBOARD.md, D3). É daqui que o dashboard tira as conexões
    /// por dia, o horário de pico, o retorno por loja e as conexões por ponto de acesso. Gravada no /authorize
    /// depois da liberação; o histórico começa no dia em que a tabela entrou no ar.
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
    }
}
