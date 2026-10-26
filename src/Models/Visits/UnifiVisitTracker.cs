using Microsoft.EntityFrameworkCore;
using Models.Campaigns;
using Models.DataBase;
using Models.Persistence;

namespace Models.Visits
{
    /// <summary>
    /// Transforma os avisos da UniFi em sinais do aparelho, visitas e tempo na loja (PROPOSTA_VISITAS_UNIFI.md):
    /// <list type="number">
    ///   <item>só a rede de clientes conta: a rede do portal da empresa (D2). Aviso sem o nome da rede (ex.: o de
    ///   desconexão) vale quando o aparelho já foi visto na rede de clientes no dia;</item>
    ///   <item>aparelho da loja (<see cref="UnitIgnoredDevice"/>) para aí (D8);</item>
    ///   <item>atualiza os sinais do aparelho no dia (<see cref="WifiPresence"/>, D6);</item>
    ///   <item>procura o cliente pelo endereço do aparelho nos cadastros das lojas da empresa (D4);</item>
    ///   <item>"conectou" de um cliente conhecido sem visita no dia nesta unidade: registra a visita e atualiza o
    ///   cliente como o portal faz (D3, D5);</item>
    ///   <item>com pelo menos um aviso de desconexão no dia, o tempo da estadia vai para a visita (D6).</item>
    /// </list>
    /// Não salva o que é de fora: quem chama salva a unidade. Salva o que gravou aqui.
    /// </summary>
    public static class UnifiVisitTracker
    {
        /// <summary>Até este intervalo entre dois sinais, é a mesma estadia (D6).</summary>
        public static readonly TimeSpan StayGap = TimeSpan.FromMinutes(30);

        /// <summary>A hora do aviso vale se estiver a menos disto de agora; fora disso, vale a hora em que chegou.</summary>
        private static readonly TimeSpan s_tsMaxClockSkew = TimeSpan.FromHours(12);

        public enum Outcome
        {
            /// <summary>Não é a rede de clientes (outra rede, cabo, ou desconexão de um aparelho não visto).</summary>
            NotGuestNetwork,

            /// <summary>Aparelho da loja.</summary>
            IgnoredDevice,

            /// <summary>Sinal guardado; sem visita nova.</summary>
            Seen,

            /// <summary>Sinal guardado e visita nova registrada.</summary>
            VisitRegistered,
        }

        public static async Task<Outcome> HandleAsync(
            AppDbContext objDbContext, Unit objUnit, UnifiNotice objNotice, DateTime dtNowUtc,
            CancellationToken objCancellationToken = default)
        {
            var objCompany = await objDbContext.Companies.AsNoTracking()
                .Where(company => company.Id == objUnit.IDCompany)
                .Select(company => new
                {
                    company.TimeZone,
                    Ssid = objDbContext.PortalSettings
                        .Where(settings => settings.IDCompany == company.Id)
                        .Select(settings => settings.Ssid)
                        .FirstOrDefault(),
                })
                .FirstAsync(objCancellationToken);
            TimeZoneInfo objZone = CompanyTimeZone.Resolve(objCompany.TimeZone);

            DateTime dtAt = objNotice.EventAtUtc is DateTime dtEvent && (dtEvent - dtNowUtc).Duration() <= s_tsMaxClockSkew
                ? dtEvent
                : dtNowUtc;
            DateOnly dtDay = CompanyTimeZone.Today(objZone, dtAt);

            if (await objDbContext.UnitIgnoredDevices.AnyAsync(
                    ignored => ignored.IDUnit == objUnit.Id && ignored.Mac == objNotice.Mac, objCancellationToken))
            {
                return Outcome.IgnoredDevice;
            }

            WifiPresence? objPresence = await objDbContext.WifiPresences.FirstOrDefaultAsync(
                presence => presence.IDUnit == objUnit.Id && presence.Mac == objNotice.Mac && presence.LocalDate == dtDay,
                objCancellationToken);

            bool bGuest = objNotice.WifiName.Length > 0
                ? string.Equals(objNotice.WifiName, (objCompany.Ssid ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
                : objNotice.Kind != UnifiNoticeKind.Connected && objPresence is not null;
            if (!bGuest)
            {
                return Outcome.NotGuestNetwork;
            }

            if (objPresence is null)
            {
                objPresence = new WifiPresence
                {
                    IDUnit = objUnit.Id,
                    Mac = objNotice.Mac,
                    LocalDate = dtDay,
                    FirstSeenAt = dtAt,
                    LastSeenAt = dtAt,
                    StayStartedAt = dtAt,
                };
                objDbContext.WifiPresences.Add(objPresence);
            }
            else
            {
                AddSignal(objPresence, dtAt);
            }

            if (objNotice.Kind == UnifiNoticeKind.Connected)
            {
                objPresence.Connects++;
            }
            else if (objNotice.Kind == UnifiNoticeKind.Disconnected)
            {
                objPresence.Disconnects++;
            }
            if (objNotice.Ap.Length > 0)
            {
                objPresence.Ap = objNotice.Ap;
            }
            if (objNotice.Hostname.Length > 0)
            {
                objPresence.Hostname = objNotice.Hostname;
            }
            objPresence.UpdatedAt = dtNowUtc;
            objPresence.IDCustomer ??= await FindCustomerAsync(objDbContext, objUnit.IDCompany, objNotice.Mac, objCancellationToken);
            await objDbContext.SaveChangesAsync(objCancellationToken);

            if (objPresence.IDCustomer is not Guid objCustomerId)
            {
                return Outcome.Seen;
            }

            Visit? objVisit = await FindDayVisitAsync(objDbContext, objUnit.Id, objCustomerId, dtDay, objCancellationToken);
            bool bRegistered = false;
            if (objVisit is null && objNotice.Kind == UnifiNoticeKind.Connected)
            {
                objVisit = await RegisterVisitAsync(
                    objDbContext, objUnit, objZone, objCustomerId, objNotice, dtAt, dtDay, objCancellationToken);
                bRegistered = objVisit is not null;
            }

            if (objVisit is not null)
            {
                ApplyStay(objVisit, objPresence);
                await objDbContext.SaveChangesAsync(objCancellationToken);
            }
            return bRegistered ? Outcome.VisitRegistered : Outcome.Seen;
        }

        /// <summary>
        /// Um sinal novo na estadia: até <see cref="StayGap"/> depois do último, continua a mesma; depois disso, a
        /// estadia anterior fecha e começa outra. Sinal fora de ordem (mais antigo que o último) só puxa o início.
        /// </summary>
        public static void AddSignal(WifiPresence objPresence, DateTime dtAt)
        {
            if (dtAt < objPresence.FirstSeenAt)
            {
                objPresence.FirstSeenAt = dtAt;
            }
            if (dtAt < objPresence.StayStartedAt)
            {
                objPresence.StayStartedAt = dtAt;
            }
            if (dtAt <= objPresence.LastSeenAt)
            {
                return;
            }

            if (dtAt - objPresence.LastSeenAt > StayGap)
            {
                objPresence.ClosedSeconds += (int)(objPresence.LastSeenAt - objPresence.StayStartedAt).TotalSeconds;
                objPresence.StayStartedAt = dtAt;
            }
            objPresence.LastSeenAt = dtAt;
        }

        /// <summary>O tempo do dia vai para a visita só quando já chegou algum aviso de desconexão (D6).</summary>
        public static void ApplyStay(Visit objVisit, WifiPresence objPresence)
        {
            if (objPresence.Disconnects > 0)
            {
                objVisit.StaySeconds = objPresence.StaySeconds;
            }
        }

        /// <summary>A visita do cliente nesta unidade no dia (uma por cliente, unidade e dia, D3).</summary>
        public static Task<Visit?> FindDayVisitAsync(
            AppDbContext objDbContext, Guid objUnitId, Guid objCustomerId, DateOnly dtDay,
            CancellationToken objCancellationToken = default) =>
            objDbContext.Visits
                .Where(visit => visit.IDUnit == objUnitId && visit.IDCustomer == objCustomerId && visit.LocalDate == dtDay)
                .OrderBy(visit => visit.Id)
                .FirstOrDefaultAsync(objCancellationToken);

        /// <summary>
        /// O cliente do aparelho: o cadastro mais recente com este endereço nas lojas da empresa (a rede tem o mesmo
        /// nome nas lojas, então o celular usa o mesmo endereço), e o cliente pelo telefone dele (D4).
        /// </summary>
        private static async Task<Guid?> FindCustomerAsync(
            AppDbContext objDbContext, Guid objCompanyId, string sMac, CancellationToken objCancellationToken)
        {
            List<string> objPhones = await (
                    from lead in objDbContext.Leads.AsNoTracking()
                    join unit in objDbContext.Units.AsNoTracking() on lead.IDUnit equals unit.Id
                    where unit.IDCompany == objCompanyId && lead.Mac == sMac
                    orderby lead.Timestamp descending
                    select lead.Phone)
                .ToListAsync(objCancellationToken);

            foreach (string sPhone in objPhones.Select(CustomerDirectory.NormalizePhone).Distinct())
            {
                Guid? objCustomerId = await objDbContext.Customers.AsNoTracking()
                    .Where(customer => customer.IDCompany == objCompanyId && customer.Phone == sPhone)
                    .Select(customer => (Guid?)customer.Id)
                    .FirstOrDefaultAsync(objCancellationToken);
                if (objCustomerId is not null)
                {
                    return objCustomerId;
                }
            }
            return null;
        }

        /// <summary>
        /// A visita pela UniFi atualiza o mesmo que a do portal (D5): o cliente (última visita, número de visitas,
        /// unidade), o vínculo com a unidade e a "última conexão" do cadastro desta loja. Não cria cliente.
        /// </summary>
        private static async Task<Visit?> RegisterVisitAsync(
            AppDbContext objDbContext, Unit objUnit, TimeZoneInfo objZone, Guid objCustomerId, UnifiNotice objNotice,
            DateTime dtAt, DateOnly dtDay, CancellationToken objCancellationToken)
        {
            string? sPhone = await objDbContext.Customers.AsNoTracking()
                .Where(customer => customer.Id == objCustomerId)
                .Select(customer => customer.Phone)
                .FirstOrDefaultAsync(objCancellationToken);
            if (sPhone is null)
            {
                return null;
            }

            Customer? objCustomer = await CustomerDirectory.RegisterVisitAsync(
                objDbContext, objUnit.IDCompany, objZone, objUnit.Id, null, null, sPhone, null, dtAt, objCancellationToken);
            if (objCustomer is null)
            {
                return null;
            }
            bool bNewInUnit = objDbContext.ChangeTracker.Entries<CustomerUnit>().Any(entry =>
                entry.State == EntityState.Added
                && entry.Entity.IDCustomer == objCustomer.Id
                && entry.Entity.IDUnit == objUnit.Id);

            Lead? objLead = await objDbContext.Leads.FirstOrDefaultAsync(
                lead => lead.IDUnit == objUnit.Id && lead.Mac == objNotice.Mac, objCancellationToken);
            if (objLead is not null && dtAt > objLead.Timestamp)
            {
                objLead.Timestamp = dtAt;
            }

            DateTime dtLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(dtAt, DateTimeKind.Utc), objZone);
            Visit objVisit = new Visit
            {
                IDUnit = objUnit.Id,
                IDCustomer = objCustomer.Id,
                At = dtAt,
                LocalDate = dtDay,
                LocalHour = dtLocal.Hour,
                NewInCompany = false,
                NewInUnit = bNewInUnit,
                Ap = objNotice.Ap,
                Source = VisitSource.Unifi,
            };
            objDbContext.Visits.Add(objVisit);
            return objVisit;
        }
    }
}
