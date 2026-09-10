using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class FacultyRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public FacultyRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<FacultyListItem>> GetFacultyAsync(
    string? search = null,
    int top = 100,
    CancellationToken cancellationToken = default)
{
    var rows =
        new List<FacultyListItem>();

    await using var connection =
        _connectionFactory.CreateConnection();

    await connection.OpenAsync(
        cancellationToken);

    if (!await ObjectExistsAsync(
            connection,
            "academiconline.vw_FacultyPublicationSearch",
            "V",
            cancellationToken))
    {
        return rows;
    }

    var trimmedSearch =
        string.IsNullOrWhiteSpace(search)
            ? null
            : search.Trim();

    await using var command =
        connection.CreateCommand();

    command.CommandText = $@"
SELECT TOP ({Math.Clamp(top, 1, 500)})
    FacultyKey,
    UvaUserId,
    DisplayName,
    Email,
    DepartmentName,
    DivisionName,
    Position,
    DepartmentDataComplete
FROM academiconline.vw_FacultyPublicationSearch
WHERE
    @Search IS NULL

    OR DisplayName LIKE
        N'%' + @Search + N'%'

    OR UvaUserId LIKE
        N'%' + @Search + N'%'

    OR Email LIKE
        N'%' + @Search + N'%'

    OR DepartmentName LIKE
        N'%' + @Search + N'%'

    OR DivisionName LIKE
        N'%' + @Search + N'%'

ORDER BY
    DisplayName,
    DepartmentName,
    DivisionName;";

    command.Parameters.AddWithValue(
        "@Search",
        trimmedSearch is null
            ? DBNull.Value
            : trimmedSearch);

    await using var reader =
        await command.ExecuteReaderAsync(
            cancellationToken);

    while (await reader.ReadAsync(
        cancellationToken))
    {
        rows.Add(
            new FacultyListItem
            {
                FacultyKey =
                    reader.IsDBNull(0)
                        ? string.Empty
                        : reader.GetString(0),

                UvaUserId =
                    reader.IsDBNull(1)
                        ? null
                        : reader.GetString(1),

                DisplayName =
                    reader.IsDBNull(2)
                        ? string.Empty
                        : reader.GetString(2),

                PrimaryEmail =
                    reader.IsDBNull(3)
                        ? null
                        : reader.GetString(3),

                DepartmentName =
                    reader.IsDBNull(4)
                        ? null
                        : reader.GetString(4),

                DivisionName =
                    reader.IsDBNull(5)
                        ? null
                        : reader.GetString(5),

                AppointmentTitle =
                    reader.IsDBNull(6)
                        ? null
                        : reader.GetString(6),

                DepartmentDataComplete =
                    !reader.IsDBNull(7) &&
                    reader.GetBoolean(7),

                IsActive = true
            });
    }

    return rows;
}

    public async Task<long> AddFacultyAsync(ManualFacultyInput input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input.DisplayName))
        {
            throw new InvalidOperationException("Display name is required.");
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.Faculty", "U", cancellationToken))
        {
            throw new InvalidOperationException("openalex.Faculty does not exist.");
        }

        var columns = await GetColumnsAsync(connection, cancellationToken);
        var displayColumn = PickFirstExisting(columns, "DisplayName", "FacultyDisplayName", "FullName", "Name");
        var emailColumn = PickFirstExisting(columns, "PrimaryEmail", "Email");
        var departmentColumn = PickFirstExisting(columns, "DepartmentName", "Department");
        var divisionColumn = PickFirstExisting(columns, "DivisionName", "Division");
        var titleColumn = PickFirstExisting(columns, "AppointmentTitle", "Title");
        var orcidColumn = PickFirstExisting(columns, "Orcid", "ORCID");
        var openAlexColumn = PickFirstExisting(columns, "ClaimedOpenAlexAuthorId", "OpenAlexAuthorId", "AuthorOpenAlexId");
        var activeColumn = PickFirstExisting(columns, "IsActive", "Active");

        if (displayColumn is null)
        {
            throw new InvalidOperationException("openalex.Faculty does not have a recognizable display-name column. Expected one of: DisplayName, FacultyDisplayName, FullName, Name.");
        }

        await EnsureNoDuplicateAsync(connection, displayColumn, emailColumn, input, cancellationToken);

        var insertColumns = new List<string>();
        var insertValues = new List<string>();
        var parameters = new List<SqlParameter>();

        AddParameter(insertColumns, insertValues, parameters, displayColumn, "@DisplayName", input.DisplayName.Trim());

        if (emailColumn is not null)
        {
            AddParameter(insertColumns, insertValues, parameters, emailColumn, "@PrimaryEmail", NullIfBlank(input.PrimaryEmail));
        }

        if (departmentColumn is not null)
        {
            AddParameter(insertColumns, insertValues, parameters, departmentColumn, "@DepartmentName", NullIfBlank(input.DepartmentName));
        }

        if (divisionColumn is not null)
        {
            AddParameter(insertColumns, insertValues, parameters, divisionColumn, "@DivisionName", NullIfBlank(input.DivisionName));
        }

        if (titleColumn is not null)
        {
            AddParameter(insertColumns, insertValues, parameters, titleColumn, "@AppointmentTitle", NullIfBlank(input.AppointmentTitle));
        }

        if (orcidColumn is not null)
        {
            AddParameter(insertColumns, insertValues, parameters, orcidColumn, "@Orcid", NullIfBlank(input.Orcid));
        }

        if (openAlexColumn is not null)
        {
            AddParameter(insertColumns, insertValues, parameters, openAlexColumn, "@ClaimedOpenAlexAuthorId", NullIfBlank(input.ClaimedOpenAlexAuthorId));
        }

        if (activeColumn is not null)
        {
            AddParameter(insertColumns, insertValues, parameters, activeColumn, "@IsActive", input.IsActive);
        }

        if (columns.Contains("SourceSystem"))
        {
            AddParameter(insertColumns, insertValues, parameters, "SourceSystem", "@SourceSystem", "ManualFacultyIntake");
        }

        if (columns.Contains("SourceFacultyKey"))
        {
            var sourceKey = !string.IsNullOrWhiteSpace(input.PrimaryEmail)
                ? input.PrimaryEmail.Trim().ToLowerInvariant()
                : "manual-" + Guid.NewGuid().ToString("N");

            AddParameter(insertColumns, insertValues, parameters, "SourceFacultyKey", "@SourceFacultyKey", sourceKey);
        }

        if (columns.Contains("CreatedAtUtc"))
        {
            insertColumns.Add(Bracket("CreatedAtUtc"));
            insertValues.Add("SYSUTCDATETIME()");
        }

        if (columns.Contains("UpdatedAtUtc"))
        {
            insertColumns.Add(Bracket("UpdatedAtUtc"));
            insertValues.Add("SYSUTCDATETIME()");
        }

        if (columns.Contains("LastSeenAtUtc"))
        {
            insertColumns.Add(Bracket("LastSeenAtUtc"));
            insertValues.Add("SYSUTCDATETIME()");
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
INSERT INTO openalex.Faculty
(
    {string.Join(",\n    ", insertColumns)}
)
OUTPUT INSERTED.FacultyId
VALUES
(
    {string.Join(",\n    ", insertValues)}
);";

        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result);
    }

    public async Task<IReadOnlyList<string>> GetFacultyColumnsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return (await GetColumnsAsync(connection, cancellationToken)).OrderBy(c => c).ToList();
    }

    private static async Task EnsureNoDuplicateAsync(
        SqlConnection connection,
        string displayColumn,
        string? emailColumn,
        ManualFacultyInput input,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();

        if (emailColumn is not null && !string.IsNullOrWhiteSpace(input.PrimaryEmail))
        {
            command.CommandText = $@"
SELECT COUNT_BIG(*)
FROM openalex.Faculty
WHERE {Bracket(displayColumn)} = @DisplayName
   OR {Bracket(emailColumn)} = @PrimaryEmail;";
            command.Parameters.AddWithValue("@DisplayName", input.DisplayName.Trim());
            command.Parameters.AddWithValue("@PrimaryEmail", input.PrimaryEmail.Trim());
        }
        else
        {
            command.CommandText = $@"
SELECT COUNT_BIG(*)
FROM openalex.Faculty
WHERE {Bracket(displayColumn)} = @DisplayName;";
            command.Parameters.AddWithValue("@DisplayName", input.DisplayName.Trim());
        }

        var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        if (count > 0)
        {
            throw new InvalidOperationException("A faculty row with the same display name or email already exists.");
        }
    }

    private static void AddParameter(
        List<string> insertColumns,
        List<string> insertValues,
        List<SqlParameter> parameters,
        string columnName,
        string parameterName,
        object? value)
    {
        insertColumns.Add(Bracket(columnName));
        insertValues.Add(parameterName);
        parameters.Add(new SqlParameter(parameterName, value ?? DBNull.Value));
    }

    private static object? NullIfBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? PickFirstExisting(HashSet<string> columns, params string[] names)
    {
        return names.FirstOrDefault(columns.Contains);
    }

    private static string Bracket(string identifier)
    {
        return "[" + identifier.Replace("]", "]]") + "]";
    }

    private static async Task<HashSet<string>> GetColumnsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT c.name
FROM sys.columns c
WHERE c.object_id = OBJECT_ID(N'openalex.Faculty');";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static async Task<bool> ObjectExistsAsync(SqlConnection connection, string objectName, string objectType, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@ObjectName", objectName);
        command.Parameters.AddWithValue("@ObjectType", objectType);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }
}
