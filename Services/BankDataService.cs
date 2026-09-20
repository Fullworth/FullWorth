namespace FullWorth.Services;

public sealed class BankDataService
{
    private readonly FullWorthApiClient
        _apiClient;

    private readonly AuthenticationService
        _authenticationService;

    public BankDataService(
        FullWorthApiClient apiClient,
        AuthenticationService authenticationService)
    {
        _apiClient =
            apiClient;

        _authenticationService =
            authenticationService;
    }

    public async Task<IReadOnlyList<BankAccountResult>>
        GetAccountsAsync(
            CancellationToken cancellationToken = default)
    {
        var accessToken =
            await GetRequiredAccessTokenAsync(
                cancellationToken);

        return await _apiClient
            .GetBankAccountsAsync(
                accessToken,
                cancellationToken);
    }

    public async Task<IReadOnlyList<BankTransactionResult>>
        GetTransactionsAsync(
            int take = 100,
            CancellationToken cancellationToken = default)
    {
        var accessToken =
            await GetRequiredAccessTokenAsync(
                cancellationToken);

        return await _apiClient
            .GetBankTransactionsAsync(
                accessToken,
                take,
                cancellationToken);
    }

    private async Task<string>
        GetRequiredAccessTokenAsync(
            CancellationToken cancellationToken)
    {
        return await _authenticationService
            .GetValidAccessTokenAsync(
                cancellationToken);
    }
}