using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Models.DataBase;
using Models.Persistence;

namespace Models.Campaigns
{
    /// <summary>
    /// Mantém a tabela de clientes (D2) a partir das conexões do Wi-Fi: um cliente por telefone dentro
    /// da empresa (D1). Nada disso aparece para o visitante.
    /// </summary>
    public static class CustomerDirectory
    {
        /// <summary>DDD + número; menos que isso não é um telefone que dê para usar.</summary>
        public const int MinPhoneDigits = 10;
        private const int MaxPhoneDigits = 20;

        public static string NormalizePhone(string? sPhone) =>
            new string((sPhone ?? "").Where(char.IsDigit).ToArray());

        /// <summary>
        /// "dd/mm/aaaa" → data. Nulo quando a data não existe (31/02), é no futuro ou antes de 1900:
        /// o cliente fica fora das campanhas de aniversário, sem adivinhar (D3).
        /// </summary>
        public static DateOnly? ParseBirthDate(string? sBirth, DateOnly dtToday)
        {
            bool bOk = DateOnly.TryParseExact(
                (sBirth ?? "").Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out DateOnly dtBirth);
            if (!bOk || dtBirth > dtToday || dtBirth.Year < 1900)
            {
                return null;
            }
            return dtBirth;
        }

        /// <summary>
        /// Registra uma conexão no cliente da empresa, criando-o na primeira. Várias conexões no mesmo
        /// dia (fuso da empresa) contam uma visita. Não salva: quem chama salva.
        /// Devolve null quando o telefone não serve para identificar o cliente.
        /// </summary>
        public static async Task<Customer?> RegisterVisitAsync(
            AppDbContext objDbContext,
            Guid objCompanyId,
            TimeZoneInfo objZone,
            Guid objUnitId,
            string? sName,
            string? sInstagram,
            string? sPhone,
            string? sBirth,
            DateTime dtNowUtc,
            CancellationToken objCancellationToken = default)
        {
            string sDigits = NormalizePhone(sPhone);
            if (sDigits.Length < MinPhoneDigits || sDigits.Length > MaxPhoneDigits)
            {
                return null;
            }

            DateOnly dtToday = CompanyTimeZone.Today(objZone, dtNowUtc);
            Customer? objCustomer = await objDbContext.Customers.FirstOrDefaultAsync(
                customer => customer.IDCompany == objCompanyId && customer.Phone == sDigits,
                objCancellationToken);

            if (objCustomer is null)
            {
                objCustomer = new Customer
                {
                    IDCompany = objCompanyId,
                    Phone = sDigits,
                    FirstVisitAt = dtNowUtc,
                    FirstVisitDate = dtToday,
                    LastVisitAt = dtNowUtc,
                    LastVisitDate = dtToday,
                    VisitCount = 1,
                    CreatedAt = dtNowUtc,
                };
                objDbContext.Customers.Add(objCustomer);
            }
            else
            {
                if (dtToday > objCustomer.LastVisitDate)
                {
                    objCustomer.VisitCount++;
                }
                objCustomer.LastVisitAt = dtNowUtc;
                objCustomer.LastVisitDate = dtToday;
            }

            // Vale o que foi digitado por último; em branco não apaga o que já se sabia.
            if (!string.IsNullOrWhiteSpace(sName))
            {
                objCustomer.Name = Truncate(sName.Trim(), 200);
            }
            if (!string.IsNullOrWhiteSpace(sInstagram))
            {
                objCustomer.Instagram = Truncate(sInstagram.Trim(), 100);
            }
            DateOnly? dtBirth = ParseBirthDate(sBirth, dtToday);
            if (dtBirth is not null)
            {
                objCustomer.BirthDate = dtBirth;
            }
            objCustomer.IDLastUnit = objUnitId;
            objCustomer.UpdatedAt = dtNowUtc;

            CustomerUnit? objLink = await objDbContext.CustomerUnits.FindAsync(
                [objCustomer.Id, objUnitId], objCancellationToken);
            if (objLink is null)
            {
                objDbContext.CustomerUnits.Add(new CustomerUnit
                {
                    IDCustomer = objCustomer.Id,
                    IDUnit = objUnitId,
                    FirstVisitAt = dtNowUtc,
                    LastVisitAt = dtNowUtc,
                });
            }
            else
            {
                objLink.LastVisitAt = dtNowUtc;
            }

            return objCustomer;
        }

        /// <summary>
        /// Carga inicial: monta os clientes a partir dos cadastros que já existiam antes da tabela. Só
        /// roda se a tabela de clientes estiver vazia. O histórico de visitas de antes não existe: conta
        /// como visita cada dia conhecido (o do cadastro e o da última conexão de cada aparelho).
        /// </summary>
        public static async Task<int> BackfillIfEmptyAsync(
            AppDbContext objDbContext, DateTime dtNowUtc, CancellationToken objCancellationToken = default)
        {
            if (await objDbContext.Customers.AnyAsync(objCancellationToken))
            {
                return 0;
            }

            var objLeads = await (
                from lead in objDbContext.Leads.AsNoTracking()
                join unit in objDbContext.Units.AsNoTracking() on lead.IDUnit equals unit.Id
                join company in objDbContext.Companies.AsNoTracking() on unit.IDCompany equals company.Id
                select new
                {
                    Lead = lead,
                    IDCompany = company.Id,
                    company.TimeZone,
                }).ToListAsync(objCancellationToken);

            int iCriados = 0;
            foreach (var objGroup in objLeads
                .Select(item => new { item.Lead, item.IDCompany, item.TimeZone, Phone = NormalizePhone(item.Lead.Telefone) })
                .Where(item => item.Phone.Length is >= MinPhoneDigits and <= MaxPhoneDigits)
                .GroupBy(item => new { item.IDCompany, item.Phone }))
            {
                TimeZoneInfo objZone = CompanyTimeZone.Resolve(objGroup.First().TimeZone);
                List<Lead> objDoCliente = objGroup.Select(item => item.Lead).ToList();
                Lead objMaisRecente = objDoCliente.OrderByDescending(lead => lead.Timestamp).First();
                DateTime dtFirst = objDoCliente.Min(lead => lead.CreatedAt);
                DateOnly dtToday = CompanyTimeZone.Today(objZone, dtNowUtc);

                int iDias = objDoCliente
                    .SelectMany(lead => new[] { lead.CreatedAt, lead.Timestamp })
                    .Select(dtVisit => CompanyTimeZone.Today(objZone, dtVisit))
                    .Distinct()
                    .Count();

                Customer objCustomer = new Customer
                {
                    IDCompany = objGroup.Key.IDCompany,
                    Phone = objGroup.Key.Phone,
                    Name = Truncate(objMaisRecente.Nome.Trim(), 200),
                    Instagram = Truncate(
                        objDoCliente.OrderByDescending(lead => lead.Timestamp)
                            .Select(lead => lead.Instagram.Trim())
                            .FirstOrDefault(sValue => sValue.Length > 0) ?? "",
                        100),
                    BirthDate = objDoCliente.OrderByDescending(lead => lead.Timestamp)
                        .Select(lead => ParseBirthDate(lead.Nascimento, dtToday))
                        .FirstOrDefault(dtBirth => dtBirth is not null),
                    FirstVisitAt = dtFirst,
                    FirstVisitDate = CompanyTimeZone.Today(objZone, dtFirst),
                    LastVisitAt = objMaisRecente.Timestamp,
                    LastVisitDate = CompanyTimeZone.Today(objZone, objMaisRecente.Timestamp),
                    VisitCount = Math.Max(1, iDias),
                    IDLastUnit = objMaisRecente.IDUnit,
                    CreatedAt = dtNowUtc,
                    UpdatedAt = dtNowUtc,
                };
                objDbContext.Customers.Add(objCustomer);

                foreach (IGrouping<Guid, Lead> objPorUnidade in objDoCliente.GroupBy(lead => lead.IDUnit))
                {
                    objDbContext.CustomerUnits.Add(new CustomerUnit
                    {
                        IDCustomer = objCustomer.Id,
                        IDUnit = objPorUnidade.Key,
                        FirstVisitAt = objPorUnidade.Min(lead => lead.CreatedAt),
                        LastVisitAt = objPorUnidade.Max(lead => lead.Timestamp),
                    });
                }
                iCriados++;
            }

            await objDbContext.SaveChangesAsync(objCancellationToken);
            return iCriados;
        }

        private static string Truncate(string sValue, int iMax) => sValue.Length <= iMax ? sValue : sValue[..iMax];
    }
}
