using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vastora.Application.Common.Interfaces;
using Vastora.Infrastructure.Identity;
using Vastora.Infrastructure.Persistence;

namespace Vastora.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MongoDbSettings>(configuration.GetSection(MongoDbSettings.SectionName));
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));

        services.AddSingleton<MongoDbContext>();
        services.AddSingleton(typeof(IMongoRepository<>), typeof(MongoRepository<>));

        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        services.AddScoped<DatabaseInitializer>();

        return services;
    }
}
