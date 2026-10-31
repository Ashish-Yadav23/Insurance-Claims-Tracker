using InsuranceClaimsTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InsuranceClaimsTracker.Infrastructure.Persistence.Configurations;

public class ClaimHistoryConfiguration : IEntityTypeConfiguration<ClaimHistory>
{
    public void Configure(EntityTypeBuilder<ClaimHistory> builder)
    {
        builder.ToTable("ClaimHistory", t =>
        {
            t.HasCheckConstraint("CK_ClaimHistory_PreviousStatus", "[PreviousStatus] IS NULL OR [PreviousStatus] IN (1, 2, 3, 4, 5)");
            t.HasCheckConstraint("CK_ClaimHistory_NewStatus", "[NewStatus] IN (1, 2, 3, 4, 5)");
        });

        builder.HasKey(h => h.Id);

        builder.Property(h => h.ClaimId)
            .IsRequired();

        builder.HasIndex(h => h.ClaimId)
            .HasDatabaseName("IX_ClaimHistory_ClaimId");

        builder.Property(h => h.PreviousStatus)
            .HasConversion<int>()
            .IsRequired(false);

        builder.Property(h => h.NewStatus)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(h => h.ChangedById)
            .IsRequired();

        builder.HasIndex(h => h.ChangedById)
            .HasDatabaseName("IX_ClaimHistory_ChangedById");

        builder.Property(h => h.ChangedAt)
            .IsRequired();

        builder.Property(h => h.ReviewNote)
            .HasMaxLength(1000)
            .IsRequired(false);

        builder.HasOne(h => h.Claim)
            .WithMany(c => c.History)
            .HasForeignKey(h => h.ClaimId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(h => h.ChangedBy)
            .WithMany(u => u.AuthoredHistories)
            .HasForeignKey(h => h.ChangedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
