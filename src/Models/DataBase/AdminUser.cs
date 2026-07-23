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
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
