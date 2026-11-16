using System.Data;
using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using SchoolManagement.Data;
using SchoolManagement.Models;

namespace SchoolManagement.Repositories;

public sealed class DapperRoleStore : IRoleClaimStore<ApplicationRole>
{
    private readonly SqlSession _database;
    private readonly IdentityErrorDescriber _errors;
    private readonly List<PendingRoleClaim> _pendingClaims = new();
    private bool _disposed;

    public DapperRoleStore(SqlSession database, IdentityErrorDescriber errors)
    {
        _database = database;
        _errors = errors;
    }

    public void Dispose()
    {
        _disposed = true;
    }

    // Identity uses these methods to read and change role fields.
    public Task<string> GetRoleIdAsync(ApplicationRole role, CancellationToken cancellationToken = default)
    {
        CheckState(cancellationToken);
        return Task.FromResult(role.Id);
    }

    public Task<string?> GetRoleNameAsync(ApplicationRole role, CancellationToken cancellationToken = default)
    {
        CheckState(cancellationToken);
        return Task.FromResult(role.Name);
    }

    public Task SetRoleNameAsync(ApplicationRole role, string? name, CancellationToken cancellationToken = default)
    {
        CheckState(cancellationToken);
        role.Name = name;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedRoleNameAsync(ApplicationRole role, CancellationToken cancellationToken = default)
    {
        CheckState(cancellationToken);
        return Task.FromResult(role.NormalizedName);
    }

    public Task SetNormalizedRoleNameAsync(ApplicationRole role, string? name, CancellationToken cancellationToken = default)
    {
        CheckState(cancellationToken);
        role.NormalizedName = name;
        return Task.CompletedTask;
    }

    public Task<ApplicationRole?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return FindRoleAsync("Id", id, cancellationToken);
    }

    public Task<ApplicationRole?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return FindRoleAsync("Name", name, cancellationToken);
    }

    public async Task<IdentityResult> CreateAsync(ApplicationRole role, CancellationToken cancellationToken = default)
    {
        CheckState(cancellationToken);

        var parameters = CreateRoleParameters(role);
        var command = _database.Command("dbo.usp_IdentityRole_Create", parameters, cancellationToken);

        try
        {
            await _database.Connection.ExecuteAsync(command);
            return IdentityResult.Success;
        }
        catch (SqlException exception) when (exception.Number == 2601 || exception.Number == 2627)
        {
            return IdentityResult.Failed(_errors.DuplicateRoleName(role.Name ?? string.Empty));
        }
    }

    public async Task<IdentityResult> UpdateAsync(ApplicationRole role, CancellationToken cancellationToken = default)
    {
        CheckState(cancellationToken);

        // A caller such as administrator setup may already have a transaction.
        SqlSession.SqlWork? transaction = null;
        if (_database.Transaction is null)
        {
            transaction = await _database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            var newStamp = Guid.NewGuid().ToString();
            var parameters = CreateRoleParameters(role);
            parameters.Add("oldStamp", role.ConcurrencyStamp);
            parameters.Add("ConcurrencyStamp", newStamp);

            var command = _database.Command("dbo.usp_IdentityRole_Update", parameters, cancellationToken);
            int updatedRows = await _database.Connection.QuerySingleAsync<int>(command);

            if (updatedRows != 1)
            {
                ClearPendingClaims(role.Id);
                return IdentityResult.Failed(_errors.ConcurrencyFailure());
            }

            // Save claims only after the role's concurrency check succeeds.
            await SavePendingClaimsAsync(role.Id, cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            role.ConcurrencyStamp = newStamp;
            return IdentityResult.Success;
        }
        catch (SqlException exception) when (exception.Number == 2601 || exception.Number == 2627)
        {
            ClearPendingClaims(role.Id);
            return IdentityResult.Failed(_errors.DuplicateRoleName(role.Name ?? string.Empty));
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    public async Task<IdentityResult> DeleteAsync(ApplicationRole role, CancellationToken cancellationToken = default)
    {
        CheckState(cancellationToken);

        var parameters = new { id = role.Id, stamp = role.ConcurrencyStamp };
        var command = _database.Command("dbo.usp_IdentityRole_Delete", parameters, cancellationToken);
        int deletedRows = await _database.Connection.QuerySingleAsync<int>(command);

        if (deletedRows != 1)
        {
            return IdentityResult.Failed(_errors.ConcurrencyFailure());
        }

        return IdentityResult.Success;
    }

    public async Task<IList<Claim>> GetClaimsAsync(ApplicationRole role, CancellationToken cancellationToken = default)
    {
        CheckState(cancellationToken);

        var parameters = new { roleId = role.Id };
        var command = _database.Command("dbo.usp_IdentityRole_GetClaims", parameters, cancellationToken);
        var rows = await _database.Connection.QueryAsync<IdentityRoleClaim<string>>(command);

        var claims = new List<Claim>();
        foreach (var row in rows)
        {
            claims.Add(row.ToClaim());
        }

        return claims;
    }

    // Identity calls UpdateAsync to save the role and its queued claims together.
    public Task AddClaimAsync(ApplicationRole role, Claim claim, CancellationToken cancellationToken = default)
    {
        CheckState(cancellationToken);
        _pendingClaims.Add(new PendingRoleClaim(role.Id, claim, isAddition: true));
        return Task.CompletedTask;
    }

    public Task RemoveClaimAsync(ApplicationRole role, Claim claim, CancellationToken cancellationToken = default)
    {
        CheckState(cancellationToken);
        _pendingClaims.Add(new PendingRoleClaim(role.Id, claim, isAddition: false));
        return Task.CompletedTask;
    }

    private Task<ApplicationRole?> FindRoleAsync(string kind, string value, CancellationToken cancellationToken)
    {
        CheckState(cancellationToken);

        var parameters = new { kind, value };
        var command = _database.Command("dbo.usp_IdentityRole_Find", parameters, cancellationToken);
        return _database.Connection.QuerySingleOrDefaultAsync<ApplicationRole>(command);
    }

    private async Task SavePendingClaimsAsync(string roleId, CancellationToken cancellationToken)
    {
        foreach (var change in _pendingClaims)
        {
            if (change.RoleId != roleId)
            {
                continue;
            }

            string procedure;
            if (change.IsAddition)
            {
                procedure = "dbo.usp_IdentityRole_AddClaim";
            }
            else
            {
                procedure = "dbo.usp_IdentityRole_RemoveClaim";
            }

            var parameters = new
            {
                roleId,
                type = change.Claim.Type,
                value = change.Claim.Value
            };

            var command = _database.Command(procedure, parameters, cancellationToken);
            await _database.Connection.ExecuteAsync(command);
        }

        ClearPendingClaims(roleId);
    }

    private void ClearPendingClaims(string roleId)
    {
        _pendingClaims.RemoveAll(change => change.RoleId == roleId);
    }

    private static DynamicParameters CreateRoleParameters(ApplicationRole role)
    {
        var parameters = new DynamicParameters(new
        {
            role.Id,
            role.Name,
            role.NormalizedName,
            role.ConcurrencyStamp,
            role.CreatedBy,
            role.ModifiedBy,
            role.DeletedBy
        });

        parameters.Add("CreatedAt", role.CreatedAt, DbType.DateTime2);
        parameters.Add("ModifiedAt", role.ModifiedAt, DbType.DateTime2);
        parameters.Add("DeletedAt", role.DeletedAt, DbType.DateTime2);
        return parameters;
    }

    private void CheckState(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class PendingRoleClaim
    {
        public string RoleId { get; }
        public Claim Claim { get; }
        public bool IsAddition { get; }

        public PendingRoleClaim(string roleId, Claim claim, bool isAddition)
        {
            RoleId = roleId;
            Claim = claim;
            IsAddition = isAddition;
        }
    }
}
