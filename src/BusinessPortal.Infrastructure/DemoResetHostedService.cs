using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BusinessPortal.Infrastructure;

public sealed partial class DemoResetHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<DemoResetHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("DemoReset:Enabled"))
        {
            return;
        }

        if (!configuration.GetValue<bool>("SeedDemoData"))
        {
            throw new InvalidOperationException("DemoReset:Enabled requires SeedDemoData=true.");
        }

        var resetHourUtc = configuration.GetValue("DemoReset:HourUtc", 3);
        if (resetHourUtc is < 0 or > 23)
        {
            throw new InvalidOperationException("DemoReset:HourUtc must be between 0 and 23.");
        }

        LogScheduleEnabled(logger, resetHourUtc);
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = DelayUntilNextReset(timeProvider.GetUtcNow(), resetHourUtc);
            await Task.Delay(delay, timeProvider, stoppingToken);

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().ResetAndSeedAsync(stoppingToken);
                LogResetCompleted(logger);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogResetFailed(logger, exception);
            }
        }
    }

    internal static TimeSpan DelayUntilNextReset(DateTimeOffset nowUtc, int resetHourUtc)
    {
        var nextReset = new DateTimeOffset(nowUtc.Year, nowUtc.Month, nowUtc.Day, resetHourUtc, 0, 0, TimeSpan.Zero);
        if (nextReset <= nowUtc)
        {
            nextReset = nextReset.AddDays(1);
        }

        return nextReset - nowUtc;
    }

    [LoggerMessage(EventId = 101, Level = LogLevel.Information, Message = "Nightly demo reset is enabled for {ResetHour}:00 UTC.")]
    private static partial void LogScheduleEnabled(ILogger logger, int resetHour);

    [LoggerMessage(EventId = 102, Level = LogLevel.Information, Message = "The Vela demo database was restored to its nightly baseline.")]
    private static partial void LogResetCompleted(ILogger logger);

    [LoggerMessage(EventId = 103, Level = LogLevel.Error, Message = "The nightly Vela demo database reset failed. It will retry at the next scheduled time.")]
    private static partial void LogResetFailed(ILogger logger, Exception exception);
}
