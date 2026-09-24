namespace FullWorth.API.Services.Contracts;

public sealed record AccountStatementQuarantineEntry(
    Guid UserId,
    string StorageKey,
    bool WasPresent);

public sealed class AccountBankRevocationException
    : Exception
{
    public AccountBankRevocationException(
        string exceptionType,
        Exception innerException)
        : base(
            "A bank connection could not be revoked safely.",
            innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            exceptionType);

        ExceptionType =
            exceptionType;
    }

    public string ExceptionType { get; }
}

public interface IAccountBankDeletionGateway
{
    Task RevokeExternalAccessAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task ApplyOwnedDataDeletionAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

public interface IAccountBillDeletionGateway
{
    Task ApplyDependentDataDeletionAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task ApplyRootDataDeletionAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

public interface IAccountStatementDeletionGateway
{
    Task<IReadOnlyList<AccountStatementQuarantineEntry>>
        QuarantineOwnedFilesAsync(
            Guid userId,
            CancellationToken cancellationToken = default);

    void RestoreQuarantine(
        AccountStatementQuarantineEntry entry);

    void CommitQuarantine(
        AccountStatementQuarantineEntry entry);

    Task ApplyOwnedDataDeletionAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

public interface IAccountSubscriptionDeletionGateway
{
    Task ApplyOwnedDataDeletionAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
