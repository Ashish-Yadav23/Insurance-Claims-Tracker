using InsuranceClaimsTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InsuranceClaimsTracker.Infrastructure.Persistence.Configurations;

public class ClaimConfiguration : IEntityTypeConfiguration<Claim>
{
    public void Configure(EntityTypeBuilder<Claim> builder)
    {
        builder.ToTable("Claims", t =>
        {
            t.HasCheckConstraint("CK_Claims_ClaimAmount", "[ClaimAmount] > 0");
            t.HasCheckConstraint("CK_Claims_Status", "[Status] IN (1, 2, 3, 4, 5)");
            t.HasCheckConstraint("CK_Claims_ApprovedAmount", "[ApprovedAmount] IS NULL OR ([ApprovedAmount] > 0 AND [ApprovedAmount] <= [ClaimAmount])");
        });

        builder.HasKey(c => c.Id);

        builder.Property(c => c.PolicyId)
            .IsRequired();

        builder.HasIndex(c => c.PolicyId)
            .HasDatabaseName("IX_Claims_PolicyId");

        builder.Property(c => c.ClaimAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(c => c.IncidentDate)
            .IsRequired();

        builder.Property(c => c.Description)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(c => c.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.HasIndex(c => c.Status)
            .HasDatabaseName("IX_Claims_Status");

        builder.Property(c => c.ApprovedAmount)
            .HasPrecision(18, 2)
            .IsRequired(false);

        builder.Property(c => c.PaymentReference)
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(c => c.PaidAt)
            .IsRequired(false);

        builder.Property(c => c.AiSuggestedCategory)
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(c => c.AiSummary)
            .HasMaxLength(1000)
            .IsRequired(false);

        builder.Property(c => c.RowVersion)
            .IsRowVersion();

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.HasIndex(c => c.CreatedAt)
            .HasDatabaseName("IX_Claims_CreatedAt");

        builder.Property(c => c.UpdatedAt)
            .IsRequired(false);

        builder.HasOne(c => c.Policy)
            .WithMany(p => p.Claims)
            .HasForeignKey(c => c.PolicyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.History)
            .WithOne(h => h.Claim)
            .HasForeignKey(h => h.ClaimId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
