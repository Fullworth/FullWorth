using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Contracts;
using FullWorth.API.Services.Statements;

namespace FullWorth.Tests.Services;

public sealed class BillStatementEvidenceAlertServiceTests
{
    [Fact]
    public void ForeignChange_IsRejected()
    {
        var userId =
            Guid.NewGuid();

        var billStreamId =
            Guid.NewGuid();

        var change =
            new BillChangeEntity
            {
                UserId =
                    Guid.NewGuid(),

                BillStreamId =
                    billStreamId,

                CurrentStatementId =
                    Guid.NewGuid(),

                ChangeType =
                    BillChangeType.TotalIncrease
            };

        var service =
            new BillStatementEvidenceAlertService();

        Assert.Throws<InvalidOperationException>(
            () =>
                service.BuildDesiredAlerts(
                    userId,
                    billStreamId,
                    "Example Provider",
                    change,
                    [],
                    []));
    }

    [Fact]
    public void OwnedChange_WithoutEvidence_ReturnsNoAlerts()
    {
        var userId =
            Guid.NewGuid();

        var billStreamId =
            Guid.NewGuid();

        var change =
            new BillChangeEntity
            {
                UserId =
                    userId,

                BillStreamId =
                    billStreamId,

                CurrentStatementId =
                    Guid.NewGuid(),

                ChangeType =
                    BillChangeType.TotalIncrease
            };

        var service =
            new BillStatementEvidenceAlertService();

        var results =
            service.BuildDesiredAlerts(
                userId,
                billStreamId,
                "Example Provider",
                change,
                [],
                []);

        Assert.Empty(
            results);
    }

    [Fact]
    public void NewFee_ReturnsWarningDesiredAlert()
    {
        var userId =
            Guid.NewGuid();

        var billStreamId =
            Guid.NewGuid();

        var previousStatementId =
            Guid.NewGuid();

        var currentStatementId =
            Guid.NewGuid();

        var change =
            new BillChangeEntity
            {
                UserId =
                    userId,

                BillStreamId =
                    billStreamId,

                PreviousStatementId =
                    previousStatementId,

                CurrentStatementId =
                    currentStatementId,

                ChangeType =
                    BillChangeType.TotalIncrease
            };

        var currentFee =
            new BillLineItemEntity
            {
                UserId =
                    userId,

                BillStatementId =
                    currentStatementId,

                Description =
                    "Late fee",

                Amount =
                    7.50m,

                Category =
                    "Fee"
            };

        var service =
            new BillStatementEvidenceAlertService();

        var alert =
            Assert.Single(
                service.BuildDesiredAlerts(
                    userId,
                    billStreamId,
                    "Example Provider",
                    change,
                    [],
                    [currentFee]));

        Assert.Equal(
            BillAlertContractType.NewFee,
            alert.AlertType);

        Assert.Equal(
            BillAlertContractSeverity.Warning,
            alert.Severity);

        Assert.Contains(
            "new fee",
            alert.Title,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "Late fee",
            alert.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ForeignLineItem_IsRejected()
    {
        var userId =
            Guid.NewGuid();

        var billStreamId =
            Guid.NewGuid();

        var currentStatementId =
            Guid.NewGuid();

        var change =
            new BillChangeEntity
            {
                UserId =
                    userId,

                BillStreamId =
                    billStreamId,

                CurrentStatementId =
                    currentStatementId,

                ChangeType =
                    BillChangeType.TotalIncrease
            };

        var foreignLineItem =
            new BillLineItemEntity
            {
                UserId =
                    Guid.NewGuid(),

                BillStatementId =
                    currentStatementId,

                Description =
                    "Fee",

                Amount =
                    5m,

                Category =
                    "Fee"
            };

        var service =
            new BillStatementEvidenceAlertService();

        Assert.Throws<InvalidOperationException>(
            () =>
                service.BuildDesiredAlerts(
                    userId,
                    billStreamId,
                    "Example Provider",
                    change,
                    [],
                    [foreignLineItem]));
    }
}
