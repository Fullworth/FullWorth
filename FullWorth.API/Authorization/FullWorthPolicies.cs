namespace FullWorth.API.Authorization;

public static class FullWorthPolicies
{
    public const string OwnerOnly =
        "FullWorth.OwnerOnly";

    public const string AdminOrOwner =
        "FullWorth.AdminOrOwner";

    public const string ModeratorOrAbove =
        "FullWorth.ModeratorOrAbove";

    public const string ActiveSubscription =
        "FullWorth.ActiveSubscription";
}
