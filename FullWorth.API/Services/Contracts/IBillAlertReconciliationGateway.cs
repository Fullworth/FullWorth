namespace FullWorth.API.Services.Contracts;

public enum BillAlertContractType
{
    BillIncrease = 1,
    BillDecrease = 2,
    NewFee = 3,
    RemovedDiscount = 4,
    PaymentDue = 5
}

public enum BillAlertContractSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public enum BillAlertReconciliationMode
{
    ReplaceManagedSet = 0,
    SingleManagedSlot = 1,
    UpsertDesiredIdentities = 2
}

public sealed record BillAlertDesiredState(
    BillAlertContractType AlertType,
    BillAlertContractSeverity Severity,
    string Title,
    string Message);

public sealed record BillAlertReconciliationScope(
    Guid? BillChangeId,
    IReadOnlyCollection<BillAlertContractType> ManagedAlertTypes,
    IReadOnlyCollection<BillAlertDesiredState> DesiredAlerts,
    BillAlertReconciliationMode Mode);

public interface IBillAlertReconciliationGateway
{
    Task StageReconciliationAsync(
        Guid userId,
        Guid billStreamId,
        IReadOnlyCollection<BillAlertReconciliationScope> scopes,
        IReadOnlyCollection<Guid> removeBillChangeIds,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
