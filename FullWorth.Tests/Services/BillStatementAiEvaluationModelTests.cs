using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace FullWorth.Tests.Services;

public sealed class BillStatementAiEvaluationModelTests
{
    [Fact]
    public void EvaluationModel_IsOwnershipScopedAndUniquelyCostKeyed()
    {
        var options =
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(
                    $"ai-evaluation-model-{Guid.NewGuid():N}")
                .Options;

        using var dbContext =
            new FullWorthDbContext(
                options);

        var entityType =
            dbContext.Model.FindEntityType(
                typeof(
                    BillStatementAiEvaluationEntity));

        Assert.NotNull(
            entityType);

        var uploadForeignKey =
            Assert.Single(
                entityType.GetForeignKeys(),
                foreignKey =>
                    foreignKey.PrincipalEntityType.ClrType ==
                    typeof(
                        BillStatementUploadEntity));

        Assert.Equal(
            [
                nameof(
                    BillStatementAiEvaluationEntity.BillStatementUploadId),

                nameof(
                    BillStatementAiEvaluationEntity.UserId)
            ],
            uploadForeignKey.Properties
                .Select(
                    property => property.Name));

        Assert.Equal(
            [
                nameof(
                    BillStatementUploadEntity.Id),

                nameof(
                    BillStatementUploadEntity.UserId)
            ],
            uploadForeignKey.PrincipalKey.Properties
                .Select(
                    property => property.Name));

        var uniqueCostKey =
            Assert.Single(
                entityType.GetIndexes(),
                index =>
                    index.IsUnique &&
                    index.Properties.Count ==
                        5);

        Assert.Equal(
            [
                nameof(
                    BillStatementAiEvaluationEntity.UserId),

                nameof(
                    BillStatementAiEvaluationEntity.BillStatementUploadId),

                nameof(
                    BillStatementAiEvaluationEntity.Provider),

                nameof(
                    BillStatementAiEvaluationEntity.Model),

                nameof(
                    BillStatementAiEvaluationEntity.PromptVersion)
            ],
            uniqueCostKey.Properties
                .Select(
                    property => property.Name));

        var attemptConstraint =
            Assert.Single(
                dbContext.GetService<IDesignTimeModel>()
                    .Model
                    .FindEntityType(
                        typeof(
                            BillStatementAiEvaluationEntity))!
                    .GetCheckConstraints(),
                constraint =>
                    constraint.Name ==
                    "CK_BillStatementAiEvaluations_AttemptCount");

        Assert.Contains(
            "\"AttemptCount\" <= 1",
            attemptConstraint.Sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluationEntity_DoesNotExposeSensitivePayloadFields()
    {
        var propertyNames =
            typeof(
                    BillStatementAiEvaluationEntity)
                .GetProperties()
                .Select(
                    property => property.Name)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "DocumentText",
            propertyNames);

        Assert.DoesNotContain(
            "Prompt",
            propertyNames);

        Assert.DoesNotContain(
            "ModelResponse",
            propertyNames);

        Assert.DoesNotContain(
            "Evidence",
            propertyNames);

        Assert.DoesNotContain(
            "ProviderError",
            propertyNames);

        Assert.DoesNotContain(
            "AccountIdentifier",
            propertyNames);
    }
}
