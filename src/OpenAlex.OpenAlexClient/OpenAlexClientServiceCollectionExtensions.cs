using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OpenAlex.OpenAlexClient;

public static class OpenAlexClientServiceCollectionExtensions
{
    public static IServiceCollection AddOpenAlexClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OpenAlexOptions>(configuration.GetSection("OpenAlex"));
        services.AddHttpClient<OpenAlexApiClient>();
        return services;
    }
}
