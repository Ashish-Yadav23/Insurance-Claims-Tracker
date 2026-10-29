using Microsoft.Extensions.DependencyInjection;

namespace InsuranceClaimsTracker.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Application layer services and validators will be registered here in subsequent phases.
        return services;
    }
}
