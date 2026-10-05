namespace AccessWifi.Ops
{
    /// <summary>
    /// Para cada peça conferida: avisa quando quebra, lembra a cada <c>dRepeatHours</c> enquanto continuar
    /// quebrada e avisa quando volta ao normal. O que está quebrado fica no <see cref="OpsState"/>.
    /// </summary>
    public class CheckTracker
    {
        private readonly OpsState _objState;
        private readonly INotifier _objNotifier;
        private readonly TimeProvider _objTime;

        public CheckTracker(OpsState objState, INotifier objNotifier, TimeProvider objTime)
        {
            _objState = objState;
            _objNotifier = objNotifier;
            _objTime = objTime;
        }

        public async Task CheckAsync(string sKey, string sPiece, bool bOk, string sDetail, double dRepeatHours)
        {
            DateTimeOffset dtNow = Clock.Now(_objTime);
            CheckState objPrevious = _objState.Checks.TryGetValue(sKey, out CheckState? objFound)
                ? objFound
                : new CheckState();

            if (bOk)
            {
                if (objPrevious.Failing)
                {
                    string sSince = objPrevious.Since is null ? "?" : Clock.Format(objPrevious.Since.Value);
                    await _objNotifier.NotifyAsync($"✅ {sPiece}: voltou ao normal",
                        $"Ficou com problema de {sSince} até {Clock.Format(dtNow)}.");
                }
                _objState.Checks[sKey] = new CheckState();
                return;
            }

            if (!objPrevious.Failing)
            {
                await _objNotifier.NotifyAsync($"🔴 {sPiece}: problema", sDetail);
                _objState.Checks[sKey] = new CheckState { Failing = true, Since = dtNow, LastAlert = dtNow };
            }
            else if (objPrevious.LastAlert is null || dtNow - objPrevious.LastAlert.Value >= TimeSpan.FromHours(dRepeatHours))
            {
                string sSince = objPrevious.Since is null ? "?" : Clock.Format(objPrevious.Since.Value);
                await _objNotifier.NotifyAsync($"🔴 {sPiece}: continua com problema", $"{sDetail}\nDesde {sSince}.");
                objPrevious.LastAlert = dtNow;
                _objState.Checks[sKey] = objPrevious;
            }
        }
    }
}
