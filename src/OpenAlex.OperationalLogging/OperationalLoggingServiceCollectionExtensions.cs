using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OpenAlex.OperationalLogging;

public static class OperationalLoggingServiceCollectionExtensions
{
    public static IServiceCollection AddOpenAlexOperationalLogging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OperationalLoggingOptions>(configuration.GetSection("OperationalLogging"));
        services.AddScoped<IOperationalLogger, SqlOperationalLogger>();
        return services;
    }
}
