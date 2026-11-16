using System.Data;
using Dapper;
using Microsoft.AspNetCore.Identity;
using SchoolManagement.Data;
using SchoolManagement.Interfaces.RepositoryInterfaces;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Repositories;

public sealed class AccountRepository : IAccountRepository
{
    private readonly SqlSession _database;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;

    public AccountRepository(
        SqlSession database,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager)
    {
        _database = database;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<OperationResult> CreateRoleAsync(string name, string actorId)
    {
        var role = new ApplicationRole
        {
            Name = name,
            CreatedBy = actorId
        };

        var result = await _roleManager.CreateAsync(role);
        if (!result.Succeeded)
        {
            return Failure(result);
        }

        return OperationResult.Success();
    }

    public async Task<OperationResult> CreateAdministratorAsync(string email, string password)
    {
        // Identity writes credentials; Dapper handles the application tables in the same transaction.
        await using var transaction = await _database.BeginTransactionAsync();
        var connection = _database.Connection;
        var sqlTransaction = _database.Transaction;

        const string administratorProcedure = "dbo.usp_Administration_Exists";
        bool administratorExists = await connection.ExecuteScalarAsync<bool>(administratorProcedure,
            new { name = _roleManager.NormalizeKey(AppRoles.Administrator) }, sqlTransaction, commandType: CommandType.StoredProcedure);
        if (administratorExists)
        {
            return OperationResult.Failure("An administrator already exists. Use the Users page for further user creation.");
        }

        if (await _userManager.FindByEmailAsync(email.Trim()) is not null)
        {
            return OperationResult.Failure("This email already exists. Bootstrap requires a new administrator email.");
        }

        foreach (var roleName in new[] { AppRoles.Administrator, "Staff" })
        {
            if (!await _roleManager.RoleExistsAsync(roleName))
            {
                var roleResult = await _roleManager.CreateAsync(new ApplicationRole { Name = roleName });
                if (!roleResult.Succeeded)
                {
                    return Failure(roleResult);
                }
            }
        }

        var administratorRole = (await _roleManager.FindByNameAsync(AppRoles.Administrator))!;
        const string findDesignationProcedure = "dbo.usp_Administration_FindDesignation";
        var designation = await connection.QuerySingleOrDefaultAsync<Designation>(findDesignationProcedure,
            new { name = "Administrator" }, sqlTransaction, commandType: CommandType.StoredProcedure);
        if (designation is null)
        {
            const string insertDesignationProcedure = "dbo.usp_Administration_CreateDesignation";
            int designationId = await connection.QuerySingleAsync<int>(insertDesignationProcedure,
                new { name = "Administrator", roleId = administratorRole.Id }, sqlTransaction, commandType: CommandType.StoredProcedure);
            designation = new Designation { DesignationId = designationId, RoleId = administratorRole.Id };
        }

        if (designation.RoleId != administratorRole.Id || !designation.IsActive || designation.DeletedAt is not null)
        {
            return OperationResult.Failure("The Administrator designation is inactive or belongs to a different role.");
        }

        var user = new ApplicationUser
        {
            UserName = email.Trim(),
            Email = email.Trim(),
            FullName = "School Administrator",
            DesignationId = designation.DesignationId,
            MustChangePassword = true,
            TemporaryPasswordExpiresAt = DateTime.UtcNow.AddHours(24)
        };
        var createResult = await _userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            return Failure(createResult);
        }

        var membershipResult = await _userManager.AddToRoleAsync(user, AppRoles.Administrator);
        if (!membershipResult.Succeeded)
        {
            return Failure(membershipResult);
        }

        await transaction.CommitAsync();
        return OperationResult.Success(user.Id);
    }

    internal static OperationResult Failure(IdentityResult result)
    {
        var errors = result.Errors.Select(error => error.Description).ToArray();
        return OperationResult.Failure(errors);
    }
}
