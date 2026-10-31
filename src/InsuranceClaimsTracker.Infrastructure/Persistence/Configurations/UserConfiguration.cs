using InsuranceClaimsTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InsuranceClaimsTracker.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users", t =>
        {
            t.HasCheckConstraint("CK_Users_Role", "[Role] IN (1, 2)");
        });

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(u => u.Email)
            .HasMaxLength(256)
            .IsRequired();

        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasDatabaseName("UX_Users_Email");

        builder.Property(u => u.PasswordHash)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(u => u.Role)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(u => u.CreatedAt)
            .IsRequired();

        builder.Property(u => u.UpdatedAt)
            .IsRequired(false);

        builder.HasMany(u => u.Policies)
            .WithOne(p => p.Customer)
            .HasForeignKey(p => p.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(u => u.AuthoredHistories)
            .WithOne(h => h.ChangedBy)
            .HasForeignKey(h => h.ChangedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
