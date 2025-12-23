using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InternetShopService_back.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InternetShopService_back.Infrastructure.Events;

public sealed class EventCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EventCleanupService> _logger;

    public EventCleanupService(IServiceScopeFactory scopeFactory, ILogger<EventCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var threshold = DateTime.UtcNow.AddDays(-7);

                var deleted = await db.OrderEvents
                    .Where(e => e.CreatedAt < threshold)
                    .ExecuteDeleteAsync(stoppingToken);

                if (deleted > 0)
                    _logger.LogInformation("Event cleanup removed {Count} events older than {Threshold}", deleted, threshold);
            }
            catch (TaskCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Event cleanup failed");
            }
        }
    }
}
