using InsuranceClaimsTracker.Application.Common.Interfaces;
using InsuranceClaimsTracker.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace InsuranceClaimsTracker.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IPolicyService, PolicyService>();
        services.AddScoped<IClaimService, ClaimService>();
        services.AddScoped<IAdminClaimService, AdminClaimService>();
        services.AddScoped<IClaimAdvisoryService, ClaimAdvisoryService>();
        return services;
    }
}
