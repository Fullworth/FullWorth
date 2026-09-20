using FullWorth.API.Authorization;

namespace FullWorth.Tests.Security;

public sealed class FullWorthRoleHierarchyTests
{
    [Theory]
    [InlineData(FullWorthRoles.Owner, 300)]
    [InlineData(FullWorthRoles.Admin, 200)]
    [InlineData(FullWorthRoles.Moderator, 100)]
    [InlineData("User", 0)]
    [InlineData(null, 0)]
    public void GetRank_ReturnsExpectedHierarchy(
        string? roleName,
        int expectedRank)
    {
        Assert.Equal(
            expectedRank,
            FullWorthRoleHierarchy.GetRank(
                roleName));
    }

    [Theory]
    [InlineData(FullWorthRoles.Owner, FullWorthRoles.Admin, true)]
    [InlineData(FullWorthRoles.Owner, FullWorthRoles.Moderator, true)]
    [InlineData(FullWorthRoles.Admin, FullWorthRoles.Moderator, true)]
    [InlineData(FullWorthRoles.Admin, FullWorthRoles.Admin, false)]
    [InlineData(FullWorthRoles.Moderator, FullWorthRoles.Admin, false)]
    [InlineData(FullWorthRoles.Moderator, FullWorthRoles.Moderator, false)]
    [InlineData(FullWorthRoles.Owner, FullWorthRoles.Owner, false)]
    [InlineData(FullWorthRoles.Admin, FullWorthRoles.Owner, false)]
    [InlineData("User", FullWorthRoles.Moderator, false)]
    public void CanAssignRole_EnforcesStaffHierarchy(
        string actorRole,
        string targetRole,
        bool expected)
    {
        Assert.Equal(
            expected,
            FullWorthRoleHierarchy.CanAssignRole(
                actorRole,
                targetRole));
    }

    [Theory]
    [InlineData(FullWorthRoles.Owner, FullWorthRoles.Admin, true)]
    [InlineData(FullWorthRoles.Owner, FullWorthRoles.Moderator, true)]
    [InlineData(FullWorthRoles.Owner, null, true)]
    [InlineData(FullWorthRoles.Admin, FullWorthRoles.Moderator, true)]
    [InlineData(FullWorthRoles.Admin, null, true)]
    [InlineData(FullWorthRoles.Admin, FullWorthRoles.Owner, false)]
    [InlineData(FullWorthRoles.Admin, FullWorthRoles.Admin, false)]
    [InlineData(FullWorthRoles.Moderator, null, true)]
    [InlineData(FullWorthRoles.Moderator, FullWorthRoles.Moderator, false)]
    [InlineData("User", null, false)]
    public void CanManageUser_EnforcesStaffHierarchy(
        string actorRole,
        string? targetHighestRole,
        bool expected)
    {
        Assert.Equal(
            expected,
            FullWorthRoleHierarchy.CanManageUser(
                actorRole,
                targetHighestRole));
    }
}
