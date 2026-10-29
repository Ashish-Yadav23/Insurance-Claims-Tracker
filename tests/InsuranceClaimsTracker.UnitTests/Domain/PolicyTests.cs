using InsuranceClaimsTracker.Domain.Entities;
using InsuranceClaimsTracker.Domain.Enums;
using Xunit;

namespace InsuranceClaimsTracker.UnitTests.Domain;

public class PolicyTests
{
    [Fact]
    public void Constructor_WithValidParameters_InitializesPolicy()
    {
        // Arrange
        var startDate = DateTimeOffset.UtcNow.AddDays(-10);
        var endDate = DateTimeOffset.UtcNow.AddDays(355);

        // Act
        var policy = new Policy("POL-2026-001", 1, PolicyType.Auto, 25000m, startDate, endDate);

        // Assert
        Assert.Equal("POL-2026-001", policy.PolicyNumber);
        Assert.Equal(1, policy.CustomerId);
        Assert.Equal(PolicyType.Auto, policy.PolicyType);
        Assert.Equal(25000m, policy.CoverageAmount);
        Assert.Equal(startDate, policy.StartDate);
        Assert.Equal(endDate, policy.EndDate);
        Assert.True(policy.IsActive);
    }

    [Fact]
    public void Constructor_WithZeroOrNegativeCoverage_ThrowsArgumentOutOfRangeException()
    {
        var startDate = DateTimeOffset.UtcNow;
        var endDate = DateTimeOffset.UtcNow.AddDays(30);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Policy("POL-001", 1, PolicyType.Health, 0m, startDate, endDate));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Policy("POL-001", 1, PolicyType.Health, -500m, startDate, endDate));
    }

    [Fact]
    public void Constructor_WithEndDateBeforeOrEqualToStartDate_ThrowsArgumentException()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(() =>
            new Policy("POL-001", 1, PolicyType.Home, 10000m, now, now));

        Assert.Throws<ArgumentException>(() =>
            new Policy("POL-001", 1, PolicyType.Home, 10000m, now, now.AddDays(-1)));
    }

    [Fact]
    public void IsActiveOn_WithinCoverageWindow_ReturnsTrue()
    {
        var startDate = DateTimeOffset.UtcNow.AddDays(-10);
        var endDate = DateTimeOffset.UtcNow.AddDays(10);
        var policy = new Policy("POL-001", 1, PolicyType.Life, 100000m, startDate, endDate);

        Assert.True(policy.IsActiveOn(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsActiveOn_OutsideCoverageWindow_ReturnsFalse()
    {
        var startDate = DateTimeOffset.UtcNow.AddDays(-10);
        var endDate = DateTimeOffset.UtcNow.AddDays(10);
        var policy = new Policy("POL-001", 1, PolicyType.Life, 100000m, startDate, endDate);

        Assert.False(policy.IsActiveOn(DateTimeOffset.UtcNow.AddDays(-15)));
        Assert.False(policy.IsActiveOn(DateTimeOffset.UtcNow.AddDays(15)));
    }
}
