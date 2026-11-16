using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using SchoolManagement.Models;

namespace SchoolManagement.Repositories;

// Identity handles passwords/tokens; UserRepository persists the records through Dapper.
public sealed class DapperUserStore : UserStoreBase<ApplicationUser, string, IdentityUserClaim<string>, IdentityUserLogin<string>, IdentityUserToken<string>>,
    IUserRoleStore<ApplicationUser>
{
    private readonly UserRepository _users;
    private readonly Dictionary<string, List<Func<CancellationToken, Task>>> _pending = new();

    public DapperUserStore(UserRepository users, IdentityErrorDescriber errors) : base(errors) { _users = users; }
    public override IQueryable<ApplicationUser> Users => _users.AllIdentityUsers().AsQueryable();

    public override async Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        Check(user, cancellationToken);
        try
        {
            await _users.SaveIdentityAsync(user, true, cancellationToken);
            return IdentityResult.Success;
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            return IdentityResult.Failed(ErrorDescriber.DuplicateEmail(user.Email ?? user.UserName ?? ""));
        }
    }

    public override async Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        Check(user, cancellationToken);

        var work = _users.Session.Transaction is null ? await _users.Session.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            if (await _users.SaveIdentityAsync(user, false, cancellationToken) is null)
            {
                _pending.Remove(user.Id);
                return IdentityResult.Failed(ErrorDescriber.ConcurrencyFailure());
            }
            if (_pending.TryGetValue(user.Id, out var changes))
            {
                foreach (var change in changes) await change(cancellationToken);
                _pending.Remove(user.Id);
            }
            if (work is not null) await work.CommitAsync(cancellationToken);
            return IdentityResult.Success;
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            _pending.Remove(user.Id);
            return IdentityResult.Failed(ErrorDescriber.DuplicateEmail(user.Email ?? user.UserName ?? ""));
        }
        finally { if (work is not null) await work.DisposeAsync(); }
    }

    public override async Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        Check(user, cancellationToken);
        return await _users.DeleteIdentityAsync(user, cancellationToken) == 1
            ? IdentityResult.Success : IdentityResult.Failed(ErrorDescriber.ConcurrencyFailure());
    }

    private async Task<ApplicationUser?> Find(string kind, string value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        return (await _users.QueryIdentityAsync<ApplicationUser>("dbo.usp_IdentityUser_Find", new { kind, value }, cancellationToken)).SingleOrDefault();
    }
    public override Task<ApplicationUser?> FindByIdAsync(string id, CancellationToken cancellationToken = default) => Find("Id", id, cancellationToken);
    public override Task<ApplicationUser?> FindByNameAsync(string name, CancellationToken cancellationToken = default) => Find("Name", name, cancellationToken);
    public override Task<ApplicationUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default) => Find("Email", email, cancellationToken);
    protected override Task<ApplicationUser?> FindUserAsync(string id, CancellationToken cancellationToken) => Find("Id", id, cancellationToken);

    public async Task AddToRoleAsync(ApplicationUser user, string name, CancellationToken cancellationToken = default)
    {
        Check(user, cancellationToken);
        var roles = await _users.QueryIdentityAsync<ApplicationRole>("dbo.usp_IdentityRole_Find", new { kind = "Name", value = name }, cancellationToken);
        if (roles.Count == 0) throw new InvalidOperationException("The requested role does not exist.");
        Queue(user.Id, "dbo.usp_IdentityUser_AddRole", new { userId = user.Id, name });
    }
    public Task RemoveFromRoleAsync(ApplicationUser user, string name, CancellationToken cancellationToken = default)
    {
        Check(user, cancellationToken); Queue(user.Id, "dbo.usp_IdentityUser_RemoveRole", new { userId = user.Id, name }); return Task.CompletedTask;
    }
    public Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        Check(user, cancellationToken); return _users.QueryIdentityAsync<string>("dbo.usp_IdentityUser_GetRoles", new { userId = user.Id }, cancellationToken);
    }
    public async Task<bool> IsInRoleAsync(ApplicationUser user, string name, CancellationToken cancellationToken = default)
    {
        Check(user, cancellationToken); return (await _users.QueryIdentityAsync<bool>("dbo.usp_IdentityUser_IsInRole", new { userId = user.Id, name }, cancellationToken)).Single();
    }
    public Task<IList<ApplicationUser>> GetUsersInRoleAsync(string name, CancellationToken cancellationToken = default) =>
        _users.QueryIdentityAsync<ApplicationUser>("dbo.usp_IdentityUser_Find", new { kind = "Role", value = name }, cancellationToken);

    public override async Task<IList<Claim>> GetClaimsAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        Check(user, cancellationToken);
        return (await _users.QueryIdentityAsync<IdentityUserClaim<string>>("dbo.usp_IdentityUser_GetClaims", new { userId = user.Id }, cancellationToken)).Select(row => row.ToClaim()).ToList();
    }
    public override Task AddClaimsAsync(ApplicationUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken = default)
    {
        Check(user, cancellationToken);
        foreach (var claim in claims) Queue(user.Id, "dbo.usp_IdentityUser_AddClaim", new { userId = user.Id, type = claim.Type, value = claim.Value });
        return Task.CompletedTask;
    }
    public override Task RemoveClaimsAsync(ApplicationUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken = default)
    {
        Check(user, cancellationToken);
        foreach (var claim in claims) Queue(user.Id, "dbo.usp_IdentityUser_RemoveClaim", new { userId = user.Id, type = claim.Type, value = claim.Value });
        return Task.CompletedTask;
    }
    public override Task ReplaceClaimAsync(ApplicationUser user, Claim claim, Claim newClaim, CancellationToken cancellationToken = default)
    {
        Check(user, cancellationToken);
        Queue(user.Id, "dbo.usp_IdentityUser_ReplaceClaim", new { userId = user.Id, type = claim.Type, value = claim.Value, newType = newClaim.Type, newValue = newClaim.Value });
        return Task.CompletedTask;
    }
    public override Task<IList<ApplicationUser>> GetUsersForClaimAsync(Claim claim, CancellationToken cancellationToken = default) =>
        _users.QueryIdentityAsync<ApplicationUser>("dbo.usp_IdentityUser_Find", new { kind = "Claim", claimType = claim.Type, claimValue = claim.Value }, cancellationToken);

    public override Task AddLoginAsync(ApplicationUser user, UserLoginInfo login, CancellationToken cancellationToken = default)
    {
        Check(user, cancellationToken);
        Queue(user.Id, "dbo.usp_IdentityUser_AddLogin", new { userId = user.Id, loginProvider = login.LoginProvider, providerKey = login.ProviderKey, displayName = login.ProviderDisplayName });
        return Task.CompletedTask;
    }
    public override Task RemoveLoginAsync(ApplicationUser user, string provider, string key, CancellationToken cancellationToken = default)
    {
        Check(user, cancellationToken); Queue(user.Id, "dbo.usp_IdentityUser_RemoveLogin", new { userId = user.Id, loginProvider = provider, providerKey = key }); return Task.CompletedTask;
    }
    public override async Task<IList<UserLoginInfo>> GetLoginsAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        Check(user, cancellationToken);
        return (await _users.QueryIdentityAsync<IdentityUserLogin<string>>("dbo.usp_IdentityUser_GetLogins", new { userId = user.Id }, cancellationToken))
            .Select(row => new UserLoginInfo(row.LoginProvider, row.ProviderKey, row.ProviderDisplayName)).ToList();
    }
    protected override async Task<IdentityUserLogin<string>?> FindUserLoginAsync(string userId, string provider, string key, CancellationToken cancellationToken) =>
        (await _users.QueryIdentityAsync<IdentityUserLogin<string>>("dbo.usp_IdentityUser_GetLogins", new { userId, loginProvider = provider, providerKey = key }, cancellationToken)).SingleOrDefault();
    protected override async Task<IdentityUserLogin<string>?> FindUserLoginAsync(string provider, string key, CancellationToken cancellationToken) =>
        (await _users.QueryIdentityAsync<IdentityUserLogin<string>>("dbo.usp_IdentityUser_GetLogins", new { loginProvider = provider, providerKey = key }, cancellationToken)).SingleOrDefault();

    protected override async Task<IdentityUserToken<string>?> FindTokenAsync(ApplicationUser user, string provider, string name, CancellationToken cancellationToken) =>
        (await _users.QueryIdentityAsync<IdentityUserToken<string>>("dbo.usp_IdentityUser_GetToken", new { userId = user.Id, loginProvider = provider, name }, cancellationToken)).SingleOrDefault();
    public override Task SetTokenAsync(ApplicationUser user, string provider, string name, string? value, CancellationToken cancellationToken = default)
    {
        Check(user, cancellationToken); Queue(user.Id, "dbo.usp_IdentityUser_SetToken", new { userId = user.Id, loginProvider = provider, name, value }); return Task.CompletedTask;
    }
    public override Task RemoveTokenAsync(ApplicationUser user, string provider, string name, CancellationToken cancellationToken = default)
    {
        Check(user, cancellationToken); Queue(user.Id, "dbo.usp_IdentityUser_RemoveToken", new { userId = user.Id, loginProvider = provider, name }); return Task.CompletedTask;
    }
    protected override Task AddUserTokenAsync(IdentityUserToken<string> token)
    {
        Queue(token.UserId, "dbo.usp_IdentityUser_SetToken", new { userId = token.UserId, loginProvider = token.LoginProvider, name = token.Name, value = token.Value }); return Task.CompletedTask;
    }
    protected override Task RemoveUserTokenAsync(IdentityUserToken<string> token)
    {
        Queue(token.UserId, "dbo.usp_IdentityUser_RemoveToken", new { userId = token.UserId, loginProvider = token.LoginProvider, name = token.Name }); return Task.CompletedTask;
    }
    private void Queue(string id, string procedure, object parameters)
    {
        if (!_pending.TryGetValue(id, out var changes)) { changes = new(); _pending[id] = changes; }
        changes.Add(token => _users.ExecuteIdentityAsync(procedure, parameters, token));
    }
    private void Check(ApplicationUser user, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user);
    }
}
