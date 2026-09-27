using FullWorth.API.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FullWorth.API.Data.Configurations;

internal sealed class PlanningPayScheduleEntityConfiguration
    : IEntityTypeConfiguration<PlanningPayScheduleEntity>
{
    public void Configure(
        EntityTypeBuilder<PlanningPayScheduleEntity> entity)
    {
        entity.ToTable(
            "PlanningPaySchedules",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_PlanningPaySchedules_Frequency",
                    "\"Frequency\" IN ('Weekly', 'Biweekly', 'SemiMonthly', 'Monthly')");

                table.HasCheckConstraint(
                    "CK_PlanningPaySchedules_DefaultPaychecksAhead",
                    "\"DefaultPaychecksAhead\" >= 1 AND \"DefaultPaychecksAhead\" <= 26");

                table.HasCheckConstraint(
                    "CK_PlanningPaySchedules_SecondaryDay",
                    "(\"Frequency\" = 'SemiMonthly' AND \"SecondaryDayOfMonth\" IS NOT NULL AND \"SecondaryDayOfMonth\" >= 1 AND \"SecondaryDayOfMonth\" <= 31 AND \"SecondaryDayOfMonth\" <> EXTRACT(DAY FROM \"AnchorPayDate\")::integer) OR (\"Frequency\" <> 'SemiMonthly' AND \"SecondaryDayOfMonth\" IS NULL)");
            });

        entity.HasKey(
            schedule => schedule.Id);

        entity.HasAlternateKey(
            schedule => new
            {
                schedule.Id,
                schedule.UserId
            });

        entity.Property(
                schedule => schedule.Frequency)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        entity.Property(
                schedule => schedule.AnchorPayDate)
            .IsRequired();

        entity.Property(
                schedule => schedule.DefaultPaychecksAhead)
            .IsRequired();

        entity.Property(
                schedule => schedule.CreatedAtUtc)
            .IsRequired();

        entity.Property(
                schedule => schedule.UpdatedAtUtc)
            .IsRequired();

        entity.HasIndex(
                schedule => schedule.UserId)
            .IsUnique();

        entity.HasOne(
                schedule => schedule.User)
            .WithMany()
            .HasForeignKey(
                schedule => schedule.UserId)
            .OnDelete(
                DeleteBehavior.Cascade);
    }
}

internal sealed class PlanningBillFundingPreferenceEntityConfiguration
    : IEntityTypeConfiguration<PlanningBillFundingPreferenceEntity>
{
    public void Configure(
        EntityTypeBuilder<PlanningBillFundingPreferenceEntity> entity)
    {
        entity.ToTable(
            "PlanningBillFundingPreferences",
            table =>
                table.HasCheckConstraint(
                    "CK_PlanningBillFundingPreferences_PaychecksAheadOverride",
                    "\"PaychecksAheadOverride\" IS NULL OR (\"PaychecksAheadOverride\" >= 1 AND \"PaychecksAheadOverride\" <= 26)"));

        entity.HasKey(
            preference => preference.Id);

        entity.HasAlternateKey(
            preference => new
            {
                preference.Id,
                preference.UserId
            });

        entity.Property(
                preference => preference.BillStreamId)
            .IsRequired();

        entity.Property(
                preference => preference.CreatedAtUtc)
            .IsRequired();

        entity.Property(
                preference => preference.UpdatedAtUtc)
            .IsRequired();

        entity.HasIndex(
                preference => new
                {
                    preference.UserId,
                    preference.BillStreamId
                })
            .IsUnique();

        /*
         * Deliberately no Planning -> Bills foreign key.
         *
         * Bill ownership is checked by the Bills owner contract at the
         * application boundary. This keeps Planning persistence independent
         * while the UserId column scopes every stored preference.
         */
        entity.HasOne(
                preference => preference.User)
            .WithMany()
            .HasForeignKey(
                preference => preference.UserId)
            .OnDelete(
                DeleteBehavior.Cascade);
    }
}
