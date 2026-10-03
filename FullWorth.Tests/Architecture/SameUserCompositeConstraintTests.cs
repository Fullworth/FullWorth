using FullWorth.API.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace FullWorth.Tests.Architecture;

public sealed class SameUserCompositeConstraintTests
{
    [Fact]
    public void UserOwnedForeignKeys_PreserveSameUserAtDatabaseBoundary()
    {
        using var dbContext =
            new FullWorthDbContext(
                new DbContextOptionsBuilder<FullWorthDbContext>()
                    .UseInMemoryDatabase(
                        $"same-user-composite-{Guid.NewGuid():N}")
                    .Options);

        var violations =
            dbContext.Model
                .GetEntityTypes()
                .SelectMany(
                    dependentEntity =>
                        dependentEntity
                            .GetForeignKeys()
                            .Select(
                                foreignKey =>
                                    new
                                    {
                                        DependentEntity =
                                            dependentEntity,
                                        ForeignKey =
                                            foreignKey
                                    }))
                .Where(
                    relationship =>
                        relationship.DependentEntity.FindProperty(
                            "UserId") is not null &&
                        relationship.ForeignKey.PrincipalEntityType
                            .FindProperty(
                                "UserId") is not null)
                .Select(
                    relationship =>
                    {
                        var foreignKey =
                            relationship.ForeignKey;

                        var dependentUserIndex =
                            IndexOfUserId(
                                foreignKey.Properties);

                        var principalUserIndex =
                            IndexOfUserId(
                                foreignKey.PrincipalKey.Properties);

                        return new
                        {
                            relationship.DependentEntity,
                            ForeignKey =
                                foreignKey,
                            DependentUserIndex =
                                dependentUserIndex,
                            PrincipalUserIndex =
                                principalUserIndex
                        };
                    })
                .Where(
                    relationship =>
                        relationship.DependentUserIndex < 0 ||
                        relationship.PrincipalUserIndex < 0 ||
                        relationship.DependentUserIndex !=
                            relationship.PrincipalUserIndex)
                .Select(
                    relationship =>
                        $"{relationship.DependentEntity.ClrType.Name} -> " +
                        $"{relationship.ForeignKey.PrincipalEntityType.ClrType.Name}: " +
                        $"dependent FK [{FormatProperties(relationship.ForeignKey.Properties)}], " +
                        $"principal key [{FormatProperties(relationship.ForeignKey.PrincipalKey.Properties)}]")
                .OrderBy(
                    violation =>
                        violation,
                    StringComparer.Ordinal)
                .ToArray();

        Assert.True(
            violations.Length == 0,
            "Every database foreign key between two UserId-scoped entities " +
            "must pair the dependent UserId with the principal UserId. " +
            "Violations: " +
            string.Join(
                "; ",
                violations));
    }

    private static int IndexOfUserId(
        IReadOnlyList<IProperty> properties)
    {
        for (var index = 0;
             index < properties.Count;
             index++)
        {
            if (string.Equals(
                    properties[index].Name,
                    "UserId",
                    StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static string FormatProperties(
        IReadOnlyList<IProperty> properties)
    {
        return string.Join(
            ", ",
            properties.Select(
                property =>
                    property.Name));
    }
}
