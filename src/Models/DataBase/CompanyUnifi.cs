using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Models.DataBase
{
    public class CompanyUnifi
    {
        public string Host { get; set; } = "";
        public string Site { get; set; } = "default";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public bool UnifiOs { get; set; } = true;
        public bool VerifySsl { get; set; }
    }
}
