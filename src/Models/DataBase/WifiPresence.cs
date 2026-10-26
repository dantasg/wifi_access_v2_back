namespace Models.DataBase
{
    /// <summary>
    /// Os sinais de um aparelho na rede de clientes de uma unidade, num dia (fuso da empresa), montados com os avisos
    /// da UniFi (PROPOSTA_VISITAS_UNIFI.md, D6). É daqui que sai o tempo que o cliente ficou na loja.
    ///
    /// A estadia junta as quedas curtas: um sinal até <c>UnifiVisitTracker.StayGap</c> depois do anterior continua a
    /// mesma estadia (o celular desliga o Wi-Fi para economizar bateria e volta). Um intervalo maior fecha a estadia
    /// (vai para <see cref="ClosedSeconds"/>) e começa outra.
    ///
    /// Guardado por 90 dias (D9); o tempo do dia fica também na visita (<see cref="Visit.StaySeconds"/>).
    /// </summary>
    public class WifiPresence
    {
        public long Id { get; set; }
        public Guid IDUnit { get; set; }

        /// <summary>Endereço do aparelho, no formato de <see cref="MacAddress.Normalize"/>.</summary>
        public string Mac { get; set; } = "";

        public DateOnly LocalDate { get; set; }

        /// <summary>Primeiro e último sinal do dia (UTC), pela hora do aviso.</summary>
        public DateTime FirstSeenAt { get; set; }
        public DateTime LastSeenAt { get; set; }

        /// <summary>Início da estadia em andamento (UTC).</summary>
        public DateTime StayStartedAt { get; set; }

        /// <summary>Soma das estadias do dia que já fecharam, em segundos.</summary>
        public int ClosedSeconds { get; set; }

        /// <summary>Quantos avisos de "conectou" e de "desconectou" chegaram no dia.</summary>
        public int Connects { get; set; }
        public int Disconnects { get; set; }

        /// <summary>Último ponto de acesso informado ("" = nenhum).</summary>
        public string Ap { get; set; } = "";

        /// <summary>Nome do aparelho na rede (ex.: "Galaxy-A25-5G"), para reconhecer os aparelhos da loja na tela.</summary>
        public string Hostname { get; set; } = "";

        /// <summary>Cliente do aparelho, achado pelo cadastro. Nulo = aparelho sem cadastro (ou cliente apagado).</summary>
        public Guid? IDCustomer { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Tempo na loja no dia: as estadias fechadas mais a que está em andamento.</summary>
        public int StaySeconds => ClosedSeconds + (int)Math.Max(0, (LastSeenAt - StayStartedAt).TotalSeconds);
    }

    /// <summary>
    /// Aparelho da própria loja na rede de clientes (a TV, o celular de um funcionário): os avisos dele não viram visita
    /// nem entram no tempo na loja (PROPOSTA_VISITAS_UNIFI.md, D8).
    /// </summary>
    public class UnitIgnoredDevice
    {
        public Guid IDUnit { get; set; }

        /// <summary>No formato de <see cref="MacAddress.Normalize"/>.</summary>
        public string Mac { get; set; } = "";

        /// <summary>Como o aparelho aparece na tela (ex.: "TV da loja").</summary>
        public string Label { get; set; } = "";

        /// <summary>Quem marcou e quando (UTC).</summary>
        public string Username { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
