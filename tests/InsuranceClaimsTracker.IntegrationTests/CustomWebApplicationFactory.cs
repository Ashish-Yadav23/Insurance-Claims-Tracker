using InsuranceClaimsTracker.Application.Common.Interfaces;
using InsuranceClaimsTracker.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InsuranceClaimsTracker.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            var testConfig = new Dictionary<string, string?>
            {
                { "JwtSettings:SigningKey", "SuperSecretTestingSigningKeyForJwtValidation1234567890!" },
                { "JwtSettings:Issuer", "InsuranceClaimsTracker-IntegrationTests" },
                { "JwtSettings:Audience", "InsuranceClaimsTracker-IntegrationApp" },
                { "JwtSettings:AccessTokenExpirationMinutes", "20" }
            };
            config.AddInMemoryCollection(testConfig);
        });

        builder.ConfigureServices(services =>
        {
            var descriptorsToRemove = services.Where(d =>
                d.ServiceType == typeof(ApplicationDbContext) ||
                d.ServiceType == typeof(IApplicationDbContext) ||
                d.ServiceType == typeof(DbContextOptions) ||
                d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>) ||
                (d.ServiceType.FullName != null && d.ServiceType.FullName.StartsWith("Microsoft.EntityFrameworkCore")) ||
                (d.ImplementationType != null && d.ImplementationType.FullName != null && d.ImplementationType.FullName.StartsWith("Microsoft.EntityFrameworkCore"))
            ).ToList();

            foreach (var descriptor in descriptorsToRemove)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseInMemoryDatabase(_dbName);
            });

            services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        });
    }
}
