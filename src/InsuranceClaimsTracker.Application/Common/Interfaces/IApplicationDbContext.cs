using InsuranceClaimsTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InsuranceClaimsTracker.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Policy> Policies { get; }
    DbSet<Claim> Claims { get; }
    DbSet<ClaimHistory> ClaimHistories { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
