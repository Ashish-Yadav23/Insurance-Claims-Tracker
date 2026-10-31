using InsuranceClaimsTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InsuranceClaimsTracker.Infrastructure.Persistence.Configurations;

public class PolicyConfiguration : IEntityTypeConfiguration<Policy>
{
    public void Configure(EntityTypeBuilder<Policy> builder)
    {
        builder.ToTable("Policies", t =>
        {
            t.HasCheckConstraint("CK_Policies_CoverageAmount", "[CoverageAmount] > 0");
            t.HasCheckConstraint("CK_Policies_EndDate", "[EndDate] > [StartDate]");
            t.HasCheckConstraint("CK_Policies_PolicyType", "[PolicyType] IN (1, 2, 3, 4)");
        });

        builder.HasKey(p => p.Id);

        builder.Property(p => p.PolicyNumber)
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(p => p.PolicyNumber)
            .IsUnique()
            .HasDatabaseName("UX_Policies_PolicyNumber");

        builder.Property(p => p.CustomerId)
            .IsRequired();

        builder.HasIndex(p => p.CustomerId)
            .HasDatabaseName("IX_Policies_CustomerId");

        builder.Property(p => p.PolicyType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(p => p.CoverageAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.StartDate)
            .IsRequired();

        builder.Property(p => p.EndDate)
            .IsRequired();

        builder.Property(p => p.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.HasOne(p => p.Customer)
            .WithMany(u => u.Policies)
            .HasForeignKey(p => p.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Claims)
            .WithOne(c => c.Policy)
            .HasForeignKey(c => c.PolicyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
