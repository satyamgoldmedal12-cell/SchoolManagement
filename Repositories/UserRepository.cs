using System.Data;
using Dapper;
using SchoolManagement.Data;
using SchoolManagement.Interfaces.RepositoryInterfaces;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Repositories;

public sealed class UserRepository : IUserRepository
{
    private const int PageSize = 50;
    private readonly SqlSession _database;

    public UserRepository(SqlSession database)
    {
        _database = database;
    }

    public async Task<PagedResult<UserSummary>> ListUsersAsync(
        string? search,
        int page,
        CancellationToken cancellationToken = default)
    {
        page = Math.Clamp(page, 1, 100000);

        if (string.IsNullOrWhiteSpace(search))
        {
            search = null;
        }
        else
        {
            search = search.Trim();
        }

        // Fetch one extra row to check whether another page is available.
        const string procedure = "dbo.usp_Users_List";

        var parameters = new
        {
            Search = search,
            Offset = (page - 1) * PageSize
        };
        var command = CreateCommand(procedure, parameters, cancellationToken);

        var connection = _database.Connection;
        var results = await connection.QueryAsync<UserSummary>(command);
        var users = results.ToList();

        bool hasNextPage = users.Count > PageSize;
        var currentPageUsers = users.Take(PageSize).ToList();
        return new PagedResult<UserSummary>(currentPageUsers, page, hasNextPage, search);
    }

    public async Task<UserAccess?> FindUserAsync(string id)
    {
        const string procedure = "dbo.usp_Users_Find";

        var parameters = new { id };

        var connection = _database.Connection;
        var user = await connection.QuerySingleOrDefaultAsync<UserAccess>(CreateCommand(procedure, parameters));
        return user;
    }

    public async Task MarkEmailSentAsync(string id, string? actorId)
    {
        const string procedure = "dbo.usp_Users_MarkEmailSent";

        var parameters = new { id, actorId };

        var connection = _database.Connection;
        await connection.ExecuteAsync(CreateCommand(procedure, parameters));
    }

    public async Task<bool> SetUserActiveAsync(string id, bool active, byte[] rowVersion, string actorId)
    {
        const string procedure = "dbo.usp_Users_SetActive";

        var parameters = new
        {
            id,
            active,
            rowVersion,
            actorId,
            stamp = Guid.NewGuid().ToString(),
            concurrency = Guid.NewGuid().ToString()
        };

        var connection = _database.Connection;
        int updatedRows = await connection.ExecuteScalarAsync<int>(CreateCommand(procedure, parameters));
        return updatedRows == 1;
    }



    public async Task UpdateLastLoginAsync(string id, DateTime loginAtUtc)
    {
        const string procedure = "dbo.usp_Users_UpdateLastLogin";
        var parameters = new DynamicParameters(new { id });
        parameters.Add("loginAtUtc", loginAtUtc, DbType.DateTime2);
        var connection = _database.Connection;
        await connection.ExecuteAsync(CreateCommand(procedure, parameters));
    }


    internal SqlSession Session => _database;
    public async Task<bool> IsActiveDesignationAsync(int id, string roleId)
    {
        return await _database.Connection.QuerySingleAsync<bool>(CreateCommand("dbo.usp_Designations_IsActive", new { id, roleId }));
    }

    internal async Task<IList<T>> QueryIdentityAsync<T>(string procedure, object? parameters = null, CancellationToken cancellationToken = default)
    {
        return (await _database.Connection.QueryAsync<T>(CreateCommand(procedure, parameters, cancellationToken))).ToList();
    }

    internal IEnumerable<ApplicationUser> AllIdentityUsers()
    {
        return _database.Connection.Query<ApplicationUser>(CreateCommand("dbo.usp_IdentityUser_Find", new { kind = "All" }));
    }

    internal Task ExecuteIdentityAsync(string procedure, object parameters, CancellationToken cancellationToken = default)
    {
        return _database.Connection.ExecuteAsync(CreateCommand(procedure, parameters, cancellationToken));
    }

    internal async Task<byte[]?> SaveIdentityAsync(ApplicationUser user, bool create, CancellationToken cancellationToken)
    {
        var oldStamp = user.ConcurrencyStamp;
        var nextStamp = create ? user.ConcurrencyStamp ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();

        var parameters = new DynamicParameters();
        parameters.Add("Id", user.Id);
        parameters.Add("UserName", user.UserName);
        parameters.Add("NormalizedUserName", user.NormalizedUserName);
        parameters.Add("Email", user.Email);
        parameters.Add("NormalizedEmail", user.NormalizedEmail);
        parameters.Add("EmailConfirmed", user.EmailConfirmed);
        parameters.Add("PasswordHash", user.PasswordHash);
        parameters.Add("SecurityStamp", user.SecurityStamp);
        parameters.Add("ConcurrencyStamp", nextStamp);
        parameters.Add("PhoneNumber", user.PhoneNumber);
        parameters.Add("PhoneNumberConfirmed", user.PhoneNumberConfirmed);
        parameters.Add("TwoFactorEnabled", user.TwoFactorEnabled);
        parameters.Add("LockoutEnd", user.LockoutEnd);
        parameters.Add("LockoutEnabled", user.LockoutEnabled);
        parameters.Add("AccessFailedCount", user.AccessFailedCount);
        parameters.Add("FullName", user.FullName);
        parameters.Add("DesignationId", user.DesignationId);
        parameters.Add("IsActive", user.IsActive);
        parameters.Add("slno", user.slno);
        parameters.Add("ModifiedBy", user.ModifiedBy);
        parameters.Add("DeletedBy", user.DeletedBy);
        parameters.Add("MustChangePassword", user.MustChangePassword);
        parameters.Add("CreatedAt", user.CreatedAt, DbType.DateTime2);
        parameters.Add("ModifiedAt", user.ModifiedAt, DbType.DateTime2);
        parameters.Add("DeletedAt", user.DeletedAt, DbType.DateTime2);
        parameters.Add("LastLoginAt", user.LastLoginAt, DbType.DateTime2);
        parameters.Add("InvitationSentAt", user.InvitationSentAt, DbType.DateTime2);
        parameters.Add("PasswordSetAt", user.PasswordSetAt, DbType.DateTime2);
        parameters.Add("TemporaryPasswordExpiresAt", user.TemporaryPasswordExpiresAt, DbType.DateTime2);

        if (!create)
        {
            parameters.Add("oldStamp", oldStamp);
            parameters.Add("rowVersion", user.RowVersion);
        }
        var procedure = create ? "dbo.usp_IdentityUser_Create" : "dbo.usp_IdentityUser_Update";
        var version = await _database.Connection.QuerySingleOrDefaultAsync<byte[]>(CreateCommand(procedure, parameters, cancellationToken));
        if (version is not null)
        {
            user.RowVersion = version;
            user.ConcurrencyStamp = nextStamp;
        }
        return version;
    }

    internal Task<int> DeleteIdentityAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        return _database.Connection.QuerySingleAsync<int>(CreateCommand("dbo.usp_IdentityUser_Delete",
            new { id = user.Id, stamp = user.ConcurrencyStamp, rowVersion = user.RowVersion }, cancellationToken));
    }

    private CommandDefinition CreateCommand(string procedure, object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        // The scoped SQL session owns the connection and transaction.
        // Identity and application operations share the same transaction.
        var transaction = _database.Transaction;
        return new CommandDefinition(procedure, parameters, transaction,
            commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken);
    }

}
