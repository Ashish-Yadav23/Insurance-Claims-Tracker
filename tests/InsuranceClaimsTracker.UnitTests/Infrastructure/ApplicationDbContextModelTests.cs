using InsuranceClaimsTracker.Domain.Entities;
using InsuranceClaimsTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace InsuranceClaimsTracker.UnitTests.Infrastructure;

public class ApplicationDbContextModelTests
{
    private readonly IModel _model;

    public ApplicationDbContextModelTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=InsuranceClaimsTrackerDb;Trusted_Connection=True;TrustServerCertificate=True;")
            .Options;

        using var context = new ApplicationDbContext(options);
        _model = context.GetService<IDesignTimeModel>().Model;
    }

    [Fact]
    public void UserModel_HasExpectedConfigurations()
    {
        var entity = _model.FindEntityType(typeof(User));
        Assert.NotNull(entity);

        // Primary Key
        var primaryKey = entity.FindPrimaryKey();
        Assert.NotNull(primaryKey);
        Assert.Equal("Id", primaryKey.Properties.Single().Name);

        // Email constraints & index
        var emailProp = entity.FindProperty(nameof(User.Email));
        Assert.NotNull(emailProp);
        Assert.False(emailProp.IsNullable);
        Assert.Equal(256, emailProp.GetMaxLength());

        var emailIndex = entity.GetIndexes().FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(User.Email)));
        Assert.NotNull(emailIndex);
        Assert.True(emailIndex.IsUnique);
        Assert.Equal("UX_Users_Email", emailIndex.GetDatabaseName());

        // Name and PasswordHash length
        var nameProp = entity.FindProperty(nameof(User.Name));
        Assert.NotNull(nameProp);
        Assert.False(nameProp.IsNullable);
        Assert.Equal(100, nameProp.GetMaxLength());

        var pwdProp = entity.FindProperty(nameof(User.PasswordHash));
        Assert.NotNull(pwdProp);
        Assert.False(pwdProp.IsNullable);
        Assert.Equal(500, pwdProp.GetMaxLength());

        // Check constraint
        var checkConstraint = entity.GetCheckConstraints().FirstOrDefault(c => c.Name == "CK_Users_Role");
        Assert.NotNull(checkConstraint);
        Assert.Equal("[Role] IN (1, 2)", checkConstraint.Sql);

        // Delete behavior on relationships
        var policiesFk = entity.GetReferencingForeignKeys()
            .FirstOrDefault(fk => fk.DeclaringEntityType.ClrType == typeof(Policy));
        Assert.NotNull(policiesFk);
        Assert.Equal(DeleteBehavior.Restrict, policiesFk.DeleteBehavior);

        var historyFk = entity.GetReferencingForeignKeys()
            .FirstOrDefault(fk => fk.DeclaringEntityType.ClrType == typeof(ClaimHistory));
        Assert.NotNull(historyFk);
        Assert.Equal(DeleteBehavior.Restrict, historyFk.DeleteBehavior);
    }

    [Fact]
    public void PolicyModel_HasExpectedConfigurations()
    {
        var entity = _model.FindEntityType(typeof(Policy));
        Assert.NotNull(entity);

        // Primary key
        var primaryKey = entity.FindPrimaryKey();
        Assert.NotNull(primaryKey);
        Assert.Equal("Id", primaryKey.Properties.Single().Name);

        // PolicyNumber index
        var policyNumberProp = entity.FindProperty(nameof(Policy.PolicyNumber));
        Assert.NotNull(policyNumberProp);
        Assert.Equal(50, policyNumberProp.GetMaxLength());

        var policyNumberIndex = entity.GetIndexes().FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(Policy.PolicyNumber)));
        Assert.NotNull(policyNumberIndex);
        Assert.True(policyNumberIndex.IsUnique);
        Assert.Equal("UX_Policies_PolicyNumber", policyNumberIndex.GetDatabaseName());

        // CoverageAmount precision
        var coverageProp = entity.FindProperty(nameof(Policy.CoverageAmount));
        Assert.NotNull(coverageProp);
        Assert.Equal(18, coverageProp.GetPrecision());
        Assert.Equal(2, coverageProp.GetScale());

        // Check constraints
        var constraints = entity.GetCheckConstraints().ToList();
        Assert.Contains(constraints, c => c.Name == "CK_Policies_CoverageAmount" && c.Sql == "[CoverageAmount] > 0");
        Assert.Contains(constraints, c => c.Name == "CK_Policies_EndDate" && c.Sql == "[EndDate] > [StartDate]");
        Assert.Contains(constraints, c => c.Name == "CK_Policies_PolicyType" && c.Sql == "[PolicyType] IN (1, 2, 3, 4)");

        // Foreign keys & delete behaviors
        var customerFk = entity.GetForeignKeys().FirstOrDefault(fk => fk.PrincipalEntityType.ClrType == typeof(User));
        Assert.NotNull(customerFk);
        Assert.Equal(DeleteBehavior.Restrict, customerFk.DeleteBehavior);

        var claimsFk = entity.GetReferencingForeignKeys().FirstOrDefault(fk => fk.DeclaringEntityType.ClrType == typeof(Claim));
        Assert.NotNull(claimsFk);
        Assert.Equal(DeleteBehavior.Restrict, claimsFk.DeleteBehavior);
    }

    [Fact]
    public void ClaimModel_HasExpectedConfigurations()
    {
        var entity = _model.FindEntityType(typeof(Claim));
        Assert.NotNull(entity);

        // ClaimAmount precision
        var amountProp = entity.FindProperty(nameof(Claim.ClaimAmount));
        Assert.NotNull(amountProp);
        Assert.Equal(18, amountProp.GetPrecision());
        Assert.Equal(2, amountProp.GetScale());

        // Description max length
        var descProp = entity.FindProperty(nameof(Claim.Description));
        Assert.NotNull(descProp);
        Assert.Equal(2000, descProp.GetMaxLength());

        // RowVersion concurrency token
        var rowVersionProp = entity.FindProperty(nameof(Claim.RowVersion));
        Assert.NotNull(rowVersionProp);
        Assert.True(rowVersionProp.IsConcurrencyToken);

        // Check constraints
        var constraints = entity.GetCheckConstraints().ToList();
        Assert.Contains(constraints, c => c.Name == "CK_Claims_ClaimAmount" && c.Sql == "[ClaimAmount] > 0");
        Assert.Contains(constraints, c => c.Name == "CK_Claims_Status" && c.Sql == "[Status] IN (1, 2, 3, 4, 5)");

        // Foreign keys & delete behaviors
        var policyFk = entity.GetForeignKeys().FirstOrDefault(fk => fk.PrincipalEntityType.ClrType == typeof(Policy));
        Assert.NotNull(policyFk);
        Assert.Equal(DeleteBehavior.Restrict, policyFk.DeleteBehavior);

        var historyFk = entity.GetReferencingForeignKeys().FirstOrDefault(fk => fk.DeclaringEntityType.ClrType == typeof(ClaimHistory));
        Assert.NotNull(historyFk);
        Assert.Equal(DeleteBehavior.Restrict, historyFk.DeleteBehavior);
    }

    [Fact]
    public void ClaimHistoryModel_HasExpectedConfigurations()
    {
        var entity = _model.FindEntityType(typeof(ClaimHistory));
        Assert.NotNull(entity);

        // PreviousStatus nullable
        var prevStatusProp = entity.FindProperty(nameof(ClaimHistory.PreviousStatus));
        Assert.NotNull(prevStatusProp);
        Assert.True(prevStatusProp.IsNullable);

        // NewStatus not nullable
        var newStatusProp = entity.FindProperty(nameof(ClaimHistory.NewStatus));
        Assert.NotNull(newStatusProp);
        Assert.False(newStatusProp.IsNullable);

        // Check constraints
        var constraints = entity.GetCheckConstraints().ToList();
        Assert.Contains(constraints, c => c.Name == "CK_ClaimHistory_NewStatus" && c.Sql == "[NewStatus] IN (1, 2, 3, 4, 5)");
        Assert.Contains(constraints, c => c.Name == "CK_ClaimHistory_PreviousStatus" && c.Sql == "[PreviousStatus] IS NULL OR [PreviousStatus] IN (1, 2, 3, 4, 5)");

        // Foreign keys & delete behaviors
        var claimFk = entity.GetForeignKeys().FirstOrDefault(fk => fk.PrincipalEntityType.ClrType == typeof(Claim));
        Assert.NotNull(claimFk);
        Assert.Equal(DeleteBehavior.Restrict, claimFk.DeleteBehavior);

        var userFk = entity.GetForeignKeys().FirstOrDefault(fk => fk.PrincipalEntityType.ClrType == typeof(User));
        Assert.NotNull(userFk);
        Assert.Equal(DeleteBehavior.Restrict, userFk.DeleteBehavior);
    }
}
