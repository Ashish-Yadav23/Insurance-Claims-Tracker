using System.Security.Claims;
using System.Text;
using InsuranceClaimsTracker.Application.Common.Interfaces;
using InsuranceClaimsTracker.Infrastructure.Authentication;
using InsuranceClaimsTracker.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace InsuranceClaimsTracker.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    public MockIntegrationLlmClient MockLlmClient { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            var testConfig = new Dictionary<string, string?>
            {
                { "JwtSettings:SigningKey", TestTokenHelper.DefaultSigningKey },
                { "JwtSettings:Issuer", TestTokenHelper.DefaultIssuer },
                { "JwtSettings:Audience", TestTokenHelper.DefaultAudience },
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

            // Configure JWT Bearer options to match TestTokenHelper
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestTokenHelper.DefaultSigningKey)),
                    ValidateIssuer = true,
                    ValidIssuer = TestTokenHelper.DefaultIssuer,
                    ValidateAudience = true,
                    ValidAudience = TestTokenHelper.DefaultAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    RoleClaimType = ClaimTypes.Role,
                    NameClaimType = ClaimTypes.NameIdentifier
                };
            });

            var jwtGenDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IJwtTokenGenerator));
            if (jwtGenDescriptor != null)
            {
                services.Remove(jwtGenDescriptor);
            }

            var testJwtSettings = Options.Create(new JwtSettings
            {
                SigningKey = TestTokenHelper.DefaultSigningKey,
                Issuer = TestTokenHelper.DefaultIssuer,
                Audience = TestTokenHelper.DefaultAudience,
                AccessTokenExpirationMinutes = 20
            });
            services.AddSingleton<IJwtTokenGenerator>(new JwtTokenGenerator(testJwtSettings));

            // Replace ILlmClient with MockIntegrationLlmClient to prevent paid/external calls during automated tests
            var llmDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ILlmClient));
            if (llmDescriptor != null)
            {
                services.Remove(llmDescriptor);
            }
            services.AddSingleton<ILlmClient>(_ => MockLlmClient);
        });
    }
}
