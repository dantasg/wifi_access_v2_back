using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Models.DataBase
{
    public class AdminUser
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid? IDCompany { get; set; }
        public Company? Company { get; set; }
        public string Username { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        /// <summary>Usuário desativado não entra nem renova a sessão (o registro é mantido).</summary>
        public bool Active { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        /// <summary>
        /// true = usuário de unidade: só enxerga as unidades de <see cref="Units"/> (sem nenhuma, não
        /// vê nada). false = a empresa inteira. A marca é explícita para que apagar a última unidade
        /// de alguém nunca o transforme em admin da empresa.
        /// </summary>
        public bool RestrictToUnits { get; set; }
        /// <summary>Unidades que o usuário de unidade enxerga (cadastros, campanhas e PDFs).</summary>
        public List<AdminUserUnit> Units { get; set; } = [];
    }

    /// <summary>Liga um usuário de empresa a uma unidade que ele pode ver.</summary>
    public class AdminUserUnit
    {
        public Guid IDUser { get; set; }
        public Guid IDUnit { get; set; }
    }
}
