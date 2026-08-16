using Vastora.Application.Notifications;

namespace Vastora.API.BackgroundJobs;

/// <summary>
/// §9.36. Periodically runs the lifecycle sweeps — abandoned carts, back-in-stock, low-stock and
/// review requests.
///
/// An in-process <see cref="BackgroundService"/> rather than a job framework, deliberately: the
/// work is idempotent (every sweep guards on an "already notified" marker), so running it twice on
/// two instances sends nothing twice, and introducing Hangfire/Quartz to schedule four queries
/// would be a dependency bought for very little. Set <c>Notifications:SweepIntervalMinutes</c> to
/// 0 to switch it off entirely.
/// </summary>
public class LifecycleNotificationWorker(
    IServiceProvider services,
    IConfiguration configuration,
    ILogger<LifecycleNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = configuration.GetValue("Notifications:SweepIntervalMinutes", 30);
        if (intervalMinutes <= 0)
        {
            logger.LogInformation("Lifecycle notification sweeps are disabled (Notifications:SweepIntervalMinutes = 0).");
            return;
        }

        var interval = TimeSpan.FromMinutes(intervalMinutes);

        // Delayed first run so startup — including DatabaseInitializer's index creation — isn't
        // competing with a full sweep for the connection pool.
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        using var timer = new PeriodicTimer(interval);

        do
        {
            try
            {
                // Its own scope per tick: the service graph is scoped and a BackgroundService is a
                // singleton, so resolving scoped dependencies directly would capture them for the
                // lifetime of the process.
                using var scope = services.CreateScope();
                var lifecycle = scope.ServiceProvider.GetRequiredService<ILifecycleNotificationService>();

                var result = await lifecycle.RunSweepAsync(stoppingToken);

                if (result != new LifecycleSweepResult(0, 0, 0, 0))
                {
                    logger.LogInformation(
                        "Lifecycle sweep sent {Abandoned} cart nudge(s), {BackInStock} back-in-stock alert(s), {LowStock} low-stock alert(s), {Reviews} review request(s).",
                        result.AbandonedCartNudges, result.BackInStockAlerts, result.LowStockAlerts, result.ReviewRequests);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // One bad sweep must not kill the worker for the rest of the process's life.
                logger.LogError(ex, "Lifecycle notification sweep failed; will retry next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
