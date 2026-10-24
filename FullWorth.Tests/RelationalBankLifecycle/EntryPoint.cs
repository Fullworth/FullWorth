namespace FullWorth.Tests.RelationalBankLifecycle;

internal static class EntryPoint
{
    public static Task<int> Main() => BankLifecycleRaceChecks.RunAsync();
}
