using Models.DataBase;

namespace Models.Campaigns
{
    /// <summary>
    /// Quando uma campanha dispara. Sempre no horário configurado, no fuso da empresa (D8), respeitando
    /// a repetição (D10/D12). As de sistema disparam todo dia; o que muda é quem recebe.
    /// </summary>
    public static class CampaignCalendar
    {
        // Anual com início em 29/02 pode levar até 4 anos para achar o próximo dia; folga acima disso.
        private const int MaxDaysAhead = 366 * 5;

        /// <summary>Próximo disparo depois de <paramref name="dtAfterUtc"/> (exclusivo), em UTC; null = acabou.</summary>
        public static DateTime? NextOccurrence(
            string sKind, CampaignConfig objConfig, TimeZoneInfo objZone, DateTime dtAfterUtc)
        {
            TimeOnly objSendTime = objConfig.SendTimeValue();
            DateOnly dtFrom = CompanyTimeZone.Today(objZone, dtAfterUtc);
            CampaignScheduleConfig? objSchedule = CampaignKind.IsSystem(sKind) ? null : objConfig.Schedule;

            if (objSchedule is not null && objSchedule.StartDate > dtFrom)
            {
                dtFrom = objSchedule.StartDate;
            }

            for (int iDay = 0; iDay <= MaxDaysAhead; iDay++)
            {
                DateOnly dtCandidate = dtFrom.AddDays(iDay);
                if (objSchedule?.EndDate is DateOnly dtEnd && dtCandidate > dtEnd)
                {
                    return null;
                }
                if (!CampaignKind.RunsOn(sKind, dtCandidate))
                {
                    continue;
                }
                if (objSchedule is not null && !OccursOn(objSchedule, dtCandidate))
                {
                    if (objSchedule.Recurrence == CampaignRecurrence.Once && dtCandidate > objSchedule.StartDate)
                    {
                        return null;
                    }
                    continue;
                }

                DateTime dtInstantUtc = ToUtc(dtCandidate, objSendTime, objZone);
                if (dtInstantUtc > dtAfterUtc)
                {
                    return dtInstantUtc;
                }
            }

            return null;
        }

        /// <summary>A agenda de uma campanha filtrada tem disparo neste dia?</summary>
        public static bool OccursOn(CampaignScheduleConfig objSchedule, DateOnly dtDate)
        {
            if (dtDate < objSchedule.StartDate || (objSchedule.EndDate is DateOnly dtEnd && dtDate > dtEnd))
            {
                return false;
            }

            DateOnly dtStart = objSchedule.StartDate;
            switch (objSchedule.Recurrence)
            {
                case CampaignRecurrence.Once:
                    return dtDate == dtStart;
                case CampaignRecurrence.Daily:
                    return true;
                case CampaignRecurrence.Weekly:
                    IReadOnlyList<int> objDays = objSchedule.DaysOfWeek is { Count: > 0 } objChosen
                        ? objChosen
                        : [(int)dtStart.DayOfWeek];
                    return objDays.Contains((int)dtDate.DayOfWeek);
                case CampaignRecurrence.Monthly:
                    // Dia 31 em mês de 30 dias vira dia 30; em fevereiro, 28 ou 29.
                    int iDayOfMonth = Math.Min(dtStart.Day, DateTime.DaysInMonth(dtDate.Year, dtDate.Month));
                    return dtDate.Day == iDayOfMonth;
                case CampaignRecurrence.Yearly:
                    return IsAnniversary(dtStart.Month, dtStart.Day, dtDate);
                default:
                    return false;
            }
        }

        /// <summary>
        /// É o "aniversário" de (mês, dia) nesta data? Quem é de 29/02 comemora em 28/02 nos anos não
        /// bissextos (D3).
        /// </summary>
        public static bool IsAnniversary(int iMonth, int iDay, DateOnly dtDate)
        {
            if (iMonth == 2 && iDay == 29 && !DateTime.IsLeapYear(dtDate.Year))
            {
                return dtDate.Month == 2 && dtDate.Day == 28;
            }
            return dtDate.Month == iMonth && dtDate.Day == iDay;
        }

        /// <summary>
        /// Os dias de aniversário que a lista deste dia cobre (D21): de hoje até sábado. Segunda e
        /// domingo cobrem a semana inteira, de domingo a sábado — no domingo a campanha não dispara,
        /// então quem faz anos no domingo aparece na segunda.
        /// </summary>
        public static (DateOnly Start, DateOnly End) BirthdayRange(DateOnly dtDate)
        {
            DateOnly dtSabado = dtDate.AddDays(DayOfWeek.Saturday - dtDate.DayOfWeek);
            DateOnly dtInicio = dtDate.DayOfWeek == DayOfWeek.Monday ? dtDate.AddDays(-1) : dtDate;
            return (dtInicio, dtSabado);
        }

        /// <summary>
        /// Os dias de <see cref="BirthdayRange"/> como "mês × 100 + dia" (12/10 = 1012), para a consulta.
        /// Em ano não bissexto, o 28/02 leva junto quem é de 29/02 (D3).
        /// </summary>
        public static int[] BirthdayKeys(DateOnly dtDate)
        {
            (DateOnly dtStart, DateOnly dtEnd) = BirthdayRange(dtDate);
            List<int> objKeys = [];
            for (DateOnly dtDia = dtStart; dtDia <= dtEnd; dtDia = dtDia.AddDays(1))
            {
                objKeys.Add(dtDia.Month * 100 + dtDia.Day);
                if (dtDia.Month == 2 && dtDia.Day == 28 && !DateTime.IsLeapYear(dtDia.Year))
                {
                    objKeys.Add(229);
                }
            }
            return [.. objKeys];
        }

        /// <summary>Em que dia da lista deste dia o cliente faz anos. Nulo = fora dela.</summary>
        public static DateOnly? BirthdayInRange(DateOnly dtBirth, DateOnly dtDate)
        {
            (DateOnly dtStart, DateOnly dtEnd) = BirthdayRange(dtDate);
            for (DateOnly dtDia = dtStart; dtDia <= dtEnd; dtDia = dtDia.AddDays(1))
            {
                if (IsAnniversary(dtBirth.Month, dtBirth.Day, dtDia))
                {
                    return dtDia;
                }
            }
            return null;
        }

        /// <summary>Dia + horário local da empresa → instante UTC.</summary>
        public static DateTime ToUtc(DateOnly dtDate, TimeOnly objTime, TimeZoneInfo objZone)
        {
            DateTime dtLocal = DateTime.SpecifyKind(dtDate.ToDateTime(objTime), DateTimeKind.Unspecified);
            // Horário que não existe (início de horário de verão, se o fuso tiver): anda uma hora.
            if (objZone.IsInvalidTime(dtLocal))
            {
                dtLocal = dtLocal.AddHours(1);
            }
            return TimeZoneInfo.ConvertTimeToUtc(dtLocal, objZone);
        }
    }
}
