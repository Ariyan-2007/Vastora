using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Webhooks;
using Vastora.Infrastructure.Configuration;
using Vastora.Infrastructure.Identity;
using Vastora.Infrastructure.Notifications;
using Vastora.Infrastructure.Persistence;
using Vastora.Infrastructure.Storage;
using Vastora.Infrastructure.Webhooks;

namespace Vastora.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MongoDbSettings>(configuration.GetSection(MongoDbSettings.SectionName));
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.Configure<SmtpSettings>(configuration.GetSection(SmtpSettings.SectionName));

        services.AddSingleton<MongoDbContext>();
        services.AddSingleton(typeof(IMongoRepository<>), typeof(MongoRepository<>));
        services.AddSingleton<IProductStockStore, ProductStockStore>();

        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IFileStorageService, LocalFileStorageService>();

        // Real delivery once Smtp:Host is configured; falls back to logging-only otherwise so a
        // machine with no SMTP set up (a fresh clone, CI) still starts instead of crashing on send.
        if (string.IsNullOrWhiteSpace(configuration["Smtp:Host"]))
        {
            services.AddSingleton<INotificationService, LoggingNotificationService>();
        }
        else
        {
            services.AddSingleton<INotificationService, SmtpNotificationService>();
        }

        services.AddSingleton<IPlatformSettings, PlatformSettings>();

        // A short timeout on purpose: a tenant's slow endpoint must not hold an order request
        // open, and delivery is best-effort by contract (§9.39).
        services.AddHttpClient("webhooks", client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<IWebhookPublisher, HttpWebhookPublisher>();

        services.AddScoped<DatabaseInitializer>();

        return services;
    }
}
