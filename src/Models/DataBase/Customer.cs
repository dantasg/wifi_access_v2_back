namespace Models.DataBase
{
    /// <summary>
    /// Cliente de uma empresa, para as campanhas: uma pessoa = um telefone dentro da empresa (D1).
    /// O cadastro do Wi-Fi (<see cref="Lead"/>) é por aparelho em cada loja; aqui a mesma pessoa em
    /// duas lojas, ou com dois celulares, é um cliente só. Atualizado a cada conexão, sem nada
    /// visível para o visitante (D2).
    /// </summary>
    public class Customer
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid IDCompany { get; set; }

        /// <summary>Telefone só com dígitos, como digitado (DDD + número). Chave do cliente na empresa.</summary>
        public string Phone { get; set; } = "";

        /// <summary>Último nome informado.</summary>
        public string Name { get; set; } = "";

        /// <summary>Último Instagram informado ("" = não informou).</summary>
        public string Instagram { get; set; } = "";

        /// <summary>
        /// Nascimento já convertido em data. Nulo quando o texto digitado não é uma data válida ou
        /// está no futuro — o cliente fica fora das campanhas de aniversário (D3).
        /// </summary>
        public DateOnly? BirthDate { get; set; }

        /// <summary>Primeira conexão (UTC) e o dia dela no fuso da empresa (aniversário de cadastro).</summary>
        public DateTime FirstVisitAt { get; set; }
        public DateOnly FirstVisitDate { get; set; }

        /// <summary>Última conexão (UTC) e o dia dela no fuso da empresa.</summary>
        public DateTime LastVisitAt { get; set; }
        public DateOnly LastVisitDate { get; set; }

        /// <summary>Dias diferentes em que o cliente conectou: várias conexões no mesmo dia contam uma visita.</summary>
        public int VisitCount { get; set; } = 1;

        /// <summary>Unidade da última visita (vira o campo {unidade} da mensagem).</summary>
        public Guid? IDLastUnit { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Unidades que o cliente já visitou (filtro "clientes da unidade X" das campanhas).</summary>
    public class CustomerUnit
    {
        public Guid IDCustomer { get; set; }
        public Guid IDUnit { get; set; }
        public DateTime FirstVisitAt { get; set; }
        public DateTime LastVisitAt { get; set; }
    }
}
