namespace AccessWifi.Ops
{
    /// <summary>Rede instável: tenta de novo algumas vezes antes de desistir.</summary>
    public static class Retry
    {
        public static async Task RunAsync(Func<Task> objAction, int iAttempts = 3, int iWaitSeconds = 5)
        {
            for (int iAttempt = 1; ; iAttempt++)
            {
                try
                {
                    await objAction();
                    return;
                }
                catch (Exception) when (iAttempt < iAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(iWaitSeconds));
                }
            }
        }
    }
}
