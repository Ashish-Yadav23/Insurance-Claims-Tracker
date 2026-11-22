using System.Text;
using InsuranceClaimsTracker.Application.Common.Interfaces;
using InsuranceClaimsTracker.Infrastructure.Authentication;
using InsuranceClaimsTracker.Infrastructure.Persistence;
using InsuranceClaimsTracker.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InsuranceClaimsTracker.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");
        }

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        // Configure and validate JwtSettings
        var jwtSection = configuration.GetSection(JwtSettings.SectionName);
        var jwtSettings = jwtSection.Get<JwtSettings>() ?? new JwtSettings();

        // Validate JWT configuration
        if (string.IsNullOrWhiteSpace(jwtSettings.Issuer))
        {
            throw new InvalidOperationException("JwtSettings:Issuer must be configured.");
        }

        if (string.IsNullOrWhiteSpace(jwtSettings.Audience))
        {
            throw new InvalidOperationException("JwtSettings:Audience must be configured.");
        }

        if (string.IsNullOrWhiteSpace(jwtSettings.SigningKey) || Encoding.UTF8.GetByteCount(jwtSettings.SigningKey) < 32)
        {
            throw new InvalidOperationException("JwtSettings:SigningKey must be configured and contain at least 256 bits (32 bytes).");
        }

        if (jwtSettings.AccessTokenExpirationMinutes <= 0)
        {
            throw new InvalidOperationException("JwtSettings:AccessTokenExpirationMinutes must be greater than zero.");
        }

        services.Configure<JwtSettings>(jwtSection);
        services.AddSingleton(Options.Create(jwtSettings));

        services.AddSingleton<IPasswordHasherService, PasswordHasherService>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        return services;
    }
}
