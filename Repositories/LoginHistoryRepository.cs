using System.Data;
using Dapper;
using SchoolManagement.Data;
using SchoolManagement.Interfaces.RepositoryInterfaces;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;
using SchoolManagement.Services;

namespace SchoolManagement.Repositories;

public sealed class LoginHistoryRepository : ILoginHistoryRepository
{
    private readonly SqlConnectionFactory _connections;

    public LoginHistoryRepository(SqlConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task CreateAsync(UserLoginHistory session)
    {
        const string procedure = "dbo.usp_LoginHistory_Create";

        var parameters = new DynamicParameters(new
        {
            session.SessionId,
            session.UserId,
            session.EndReason,
            session.IpAddress,
            session.UserAgent,
            session.DeviceId, 
            session.DeviceName, 
            session.OperatingSystem, 
            session.Browser,
            session.DeviceType,
            session.TimeZone,
            session.LocationSource, 
            session.LocationStatus,
            session.Latitude,
            session.Longitude,
            session.LocationAccuracyMeters,
            session.AreaName,
            session.AreaRegion,
            session.AreaCountry,
            session.AreaSource,
            session.AreaGeoNamesId,
            session.AreaDistanceKm,
            session.GeoLocation,
            session.DeviceFormFactor,
            session.LocationResponse,
            session.GeocodingProvider,
            session.GeocodingStatus
        });
        parameters.Add("LoginAtUtc", session.LoginAtUtc, DbType.DateTime2);
        parameters.Add("LastSeenAtUtc", session.LastSeenAtUtc, DbType.DateTime2);
        parameters.Add("ExpiresAtUtc", session.ExpiresAtUtc, DbType.DateTime2);
        parameters.Add("LogoutAtUtc", session.LogoutAtUtc, DbType.DateTime2);
        parameters.Add("EndedAtUtc", session.EndedAtUtc, DbType.DateTime2);
        parameters.Add("LocationCapturedAtUtc", session.LocationCapturedAtUtc, DbType.DateTime2);

        await using var connection = _connections.CreateConnection();
        session.Id = await connection.QuerySingleAsync<int>(procedure, parameters, commandType: CommandType.StoredProcedure);
    }

    public async Task<bool> TouchAsync(Guid sessionId, string userId, DateTime seenAtUtc, DateTime expiresAtUtc)
    {
        const string procedure = "dbo.usp_LoginHistory_Touch";

        var parameters = new DynamicParameters(new { sessionId, userId });
        parameters.Add("seenAtUtc", seenAtUtc, DbType.DateTime2);
        parameters.Add("expiresAtUtc", expiresAtUtc, DbType.DateTime2);
        await using var connection = _connections.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(procedure, parameters, commandType: CommandType.StoredProcedure) == 1;
    }

    public async Task EndSessionAsync(Guid sessionId, string userId, DateTime endedAtUtc, string reason)
    {
        await CloseExpiredSessionsAsync(endedAtUtc);

        DateTime? logoutAtUtc = null;
        if (reason == LoginHistoryConstants.Logout)
        {
            logoutAtUtc = endedAtUtc;
        }

        const string procedure = "dbo.usp_LoginHistory_EndSession";
        var parameters = new DynamicParameters(new { sessionId, userId, reason });
        parameters.Add("endedAtUtc", endedAtUtc, DbType.DateTime2);
        parameters.Add("logoutAtUtc", logoutAtUtc, DbType.DateTime2);
        await using var connection = _connections.CreateConnection();
        await connection.ExecuteAsync(procedure, parameters, commandType: CommandType.StoredProcedure);
    }

    public async Task EndUserSessionsAsync(string userId, DateTime endedAtUtc, string reason)
    {
        await CloseExpiredSessionsAsync(endedAtUtc);

        const string procedure = "dbo.usp_LoginHistory_EndUserSessions";
        var parameters = new DynamicParameters(new { userId, reason });
        parameters.Add("endedAtUtc", endedAtUtc, DbType.DateTime2);
        await using var connection = _connections.CreateConnection();
        await connection.ExecuteAsync(procedure, parameters, commandType: CommandType.StoredProcedure);
    }

    public async Task CloseExpiredSessionsAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        const string procedure = "dbo.usp_LoginHistory_CloseExpired";
        var parameters = new DynamicParameters(new { reason = LoginHistoryConstants.Expired });
        parameters.Add("nowUtc", nowUtc, DbType.DateTime2);
        var command = new CommandDefinition(procedure, parameters, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken);
        await using var connection = _connections.CreateConnection();
        await connection.ExecuteAsync(command);
    }

    public async Task<PagedResult<LoginHistorySummary>> ListAsync(
        string? search, int page, CancellationToken cancellationToken = default)
    {
        const int pageSize = 50;
        page = Math.Clamp(page, 1, 100000);
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        const string procedure = "dbo.usp_LoginHistory_List";
        var parameters = new { search, offset = (page - 1) * pageSize };
        var command = new CommandDefinition(procedure, parameters, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken);
        await using var connection = _connections.CreateConnection();
        var rows = (await connection.QueryAsync<LoginHistorySummary>(command)).ToList();
        return new PagedResult<LoginHistorySummary>(rows.Take(pageSize).ToList(), page, rows.Count > pageSize, search);
    }
}
