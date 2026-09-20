namespace FullWorth.API.Authorization;

public static class FullWorthRoleHierarchy
{
    public static int GetRank(
        string? roleName)
    {
        return roleName switch
        {
            FullWorthRoles.Owner => 300,
            FullWorthRoles.Admin => 200,
            FullWorthRoles.Moderator => 100,
            _ => 0
        };
    }

    public static bool CanAssignRole(
        string actorRole,
        string targetRole)
    {
        if (!FullWorthRoles.IsStaffRole(
                actorRole) ||
            !FullWorthRoles.IsStaffRole(
                targetRole))
        {
            return false;
        }

        /*
         * Owner is a bootstrap/recovery role, not a role that the normal
         * admin console may mint. This prevents a compromised staff account
         * from creating another top-level principal through ordinary UI/API
         * flows. Owner changes require the explicit owner-recovery path.
         */
        if (string.Equals(
                targetRole,
                FullWorthRoles.Owner,
                StringComparison.Ordinal))
        {
            return false;
        }

        return GetRank(actorRole) >
            GetRank(targetRole);
    }

    public static bool CanManageUser(
        string actorRole,
        string? targetHighestRole)
    {
        if (!FullWorthRoles.IsStaffRole(
                actorRole))
        {
            return false;
        }

        return GetRank(actorRole) >
            GetRank(targetHighestRole);
    }
}
