namespace FullWorth.API.Services.Contracts;

public sealed record AdminSubscriptionMutationResult(
    bool Found,
    bool Changed,
    Guid? ResourceId);

public interface IAdminSubscriptionMutationGateway
{
    Guid StageGrantEntitlement(
        Guid targetUserId,
        string tier,
        int? durationDays,
        bool grantsLifetimeAccess,
        Guid actorUserId,
        DateTimeOffset nowUtc);

    Task<AdminSubscriptionMutationResult> StageRevokeEntitlementAsync(
        Guid targetUserId,
        Guid entitlementId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<Guid> StageSetProgramMembershipAsync(
        Guid targetUserId,
        string program,
        bool isActive,
        DateTimeOffset? endsAtUtc,
        Guid actorUserId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
}
