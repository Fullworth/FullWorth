namespace FullWorth.API.Services.Plaid;

// This lifecycle conflict is safe to communicate to an authenticated caller.
// Do not include account details, Item IDs or provider credentials in errors.
public sealed class PlaidDisconnectedConnectionException : InvalidOperationException
{
    public PlaidDisconnectedConnectionException()
        : base("Disconnected bank connection cannot be reactivated by token exchange.")
    {
    }
}
