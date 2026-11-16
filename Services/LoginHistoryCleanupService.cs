using SchoolManagement.Interfaces.RepositoryInterfaces;

namespace SchoolManagement.Services;

public sealed class LoginHistoryCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<LoginHistoryCleanupService> _logger;

    public LoginHistoryCleanupService(
        IServiceScopeFactory scopeFactory, TimeProvider clock, ILogger<LoginHistoryCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var repository = scope.ServiceProvider.GetRequiredService<ILoginHistoryRepository>();
                    await repository.CloseExpiredSessionsAsync(_clock.GetUtcNow().UtcDateTime, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Could not mark expired login history sessions.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
    }
}
