using System.Reflection;
using InsuranceClaimsTracker.Application.Common.Interfaces;
using InsuranceClaimsTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InsuranceClaimsTracker.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<ClaimHistory> ClaimHistories => Set<ClaimHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
