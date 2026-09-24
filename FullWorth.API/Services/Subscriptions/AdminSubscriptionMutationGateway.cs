using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Subscriptions;

public sealed class AdminSubscriptionMutationGateway(
    FullWorthDbContext dbContext)
    : IAdminSubscriptionMutationGateway
{
    public Guid StageGrantEntitlement(
        Guid targetUserId,
        string tier,
        int? durationDays,
        bool grantsLifetimeAccess,
        Guid actorUserId,
        DateTimeOffset nowUtc)
    {
        if (targetUserId == Guid.Empty ||
            actorUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "User IDs are required.");
        }

        if (grantsLifetimeAccess == durationDays.HasValue ||
            durationDays is <= 0 or > 3650)
        {
            throw new ArgumentException(
                "Specify a valid lifetime or bounded-duration grant.");
        }

        if (!Enum.TryParse<FullWorthSubscriptionTier>(
                tier,
                ignoreCase: true,
                out var parsedTier) ||
            !Enum.IsDefined(parsedTier))
        {
            throw new ArgumentException(
                "Subscription tier is invalid.",
                nameof(tier));
        }

        var entitlement =
            new SubscriptionEntitlementEntity
            {
                UserId = targetUserId,
                Tier = parsedTier,
                Source = SubscriptionEntitlementSource.Complimentary,
                StartsAtUtc = nowUtc,
                EndsAtUtc = grantsLifetimeAccess
                    ? null
                    : nowUtc.AddDays(durationDays!.Value),
                GrantedByUserId = actorUserId,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            };

        dbContext.SubscriptionEntitlements.Add(
            entitlement);

        return entitlement.Id;
    }

    public async Task<AdminSubscriptionMutationResult>
        StageRevokeEntitlementAsync(
            Guid targetUserId,
            Guid entitlementId,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken = default)
    {
        if (targetUserId == Guid.Empty ||
            entitlementId == Guid.Empty)
        {
            return new AdminSubscriptionMutationResult(
                Found: false,
                Changed: false,
                ResourceId: null);
        }

        var entitlement =
            await dbContext.SubscriptionEntitlements
                .SingleOrDefaultAsync(
                    candidate =>
                        candidate.Id == entitlementId &&
                        candidate.UserId == targetUserId,
                    cancellationToken);

        if (entitlement is null)
        {
            return new AdminSubscriptionMutationResult(
                Found: false,
                Changed: false,
                ResourceId: null);
        }

        if (entitlement.IsRevoked)
        {
            return new AdminSubscriptionMutationResult(
                Found: true,
                Changed: false,
                ResourceId: entitlement.Id);
        }

        entitlement.IsRevoked = true;
        entitlement.RevokedAtUtc = nowUtc;
        entitlement.UpdatedAtUtc = nowUtc;

        return new AdminSubscriptionMutationResult(
            Found: true,
            Changed: true,
            ResourceId: entitlement.Id);
    }

    public async Task<Guid> StageSetProgramMembershipAsync(
        Guid targetUserId,
        string program,
        bool isActive,
        DateTimeOffset? endsAtUtc,
        Guid actorUserId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (targetUserId == Guid.Empty ||
            actorUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "User IDs are required.");
        }

        if (endsAtUtc <= nowUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endsAtUtc),
                "Program membership expiration must be in the future.");
        }

        if (!Enum.TryParse<UserProgramType>(
                program,
                ignoreCase: true,
                out var parsedProgram) ||
            !Enum.IsDefined(parsedProgram))
        {
            throw new ArgumentException(
                "Program is invalid.",
                nameof(program));
        }

        var membership =
            await dbContext.UserProgramMemberships
                .SingleOrDefaultAsync(
                    candidate =>
                        candidate.UserId == targetUserId &&
                        candidate.Program == parsedProgram,
                    cancellationToken);

        if (membership is null)
        {
            membership =
                new UserProgramMembershipEntity
                {
                    UserId = targetUserId,
                    Program = parsedProgram,
                    StartsAtUtc = nowUtc,
                    EndsAtUtc = endsAtUtc,
                    IsActive = isActive,
                    GrantedByUserId = actorUserId,
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc
                };

            dbContext.UserProgramMemberships.Add(
                membership);
        }
        else
        {
            membership.IsActive = isActive;
            membership.EndsAtUtc = endsAtUtc;
            membership.GrantedByUserId = actorUserId;
            membership.UpdatedAtUtc = nowUtc;
        }

        return membership.Id;
    }
}
