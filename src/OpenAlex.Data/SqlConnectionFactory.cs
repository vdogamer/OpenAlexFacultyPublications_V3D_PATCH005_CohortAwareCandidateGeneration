using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace OpenAlex.Data;

public sealed class SqlConnectionFactory
{
    private readonly IConfiguration _configuration;
    private readonly DatabaseOptions _options;

    public SqlConnectionFactory(IConfiguration configuration, IOptions<DatabaseOptions> options)
    {
        _configuration = configuration;
        _options = options.Value;
    }

    public SqlConnection CreateConnection()
    {
        var connectionString = _options.ConnectionString;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = _configuration.GetConnectionString(_options.DefaultConnectionName);
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"A database connection string is required. Set either Database:ConnectionString or ConnectionStrings:{_options.DefaultConnectionName}.");
        }

        return new SqlConnection(connectionString);
    }
}
