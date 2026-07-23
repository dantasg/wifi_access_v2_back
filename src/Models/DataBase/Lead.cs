using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Models.DataBase
{
    public class Lead
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid IDCompany { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Nome { get; set; } = "";
        public string Instagram { get; set; } = "";
        public string Telefone { get; set; } = "";
        public string Nascimento { get; set; } = "";
        public string? Mac { get; set; }
        public string? Ap { get; set; }
        public string? Ssid { get; set; }
    }
}
