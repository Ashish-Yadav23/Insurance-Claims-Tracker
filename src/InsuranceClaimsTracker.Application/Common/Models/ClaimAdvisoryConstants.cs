namespace InsuranceClaimsTracker.Application.Common.Models;

public static class ClaimAdvisoryConstants
{
    public const int MaxSummaryLength = 1000;

    public static readonly HashSet<string> AllowedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Medical",
        "AutoAccident",
        "PropertyDamage",
        "WaterDamage",
        "Theft",
        "Life",
        "Other"
    };

    public static readonly HashSet<string> AllowedPriorities = new(StringComparer.OrdinalIgnoreCase)
    {
        "Low",
        "Medium",
        "High"
    };

    public static bool TryNormalizeCategory(string? rawCategory, out string normalizedCategory)
    {
        normalizedCategory = string.Empty;
        if (string.IsNullOrWhiteSpace(rawCategory))
            return false;

        var trimmed = rawCategory.Trim();
        foreach (var allowed in AllowedCategories)
        {
            if (string.Equals(allowed, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                normalizedCategory = allowed;
                return true;
            }
        }

        return false;
    }

    public static bool TryNormalizePriority(string? rawPriority, out string normalizedPriority)
    {
        normalizedPriority = string.Empty;
        if (string.IsNullOrWhiteSpace(rawPriority))
            return false;

        var trimmed = rawPriority.Trim();
        foreach (var allowed in AllowedPriorities)
        {
            if (string.Equals(allowed, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                normalizedPriority = allowed;
                return true;
            }
        }

        return false;
    }
}
