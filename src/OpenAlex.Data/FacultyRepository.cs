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
    var result =
        await GetFacultyPageAsync(
            search,
            department: null,
            sort: "name",
            direction: "asc",
            pageNumber: 1,
            pageSize: Math.Clamp(top, 1, 500),
            cancellationToken);

    return result.Items;
}


public async Task<IReadOnlyList<FacultyListItem>> GetFacultyAsync(
    string? search,
    string? department,
    int top = 100,
    CancellationToken cancellationToken = default)
{
    var result =
        await GetFacultyPageAsync(
            search,
            department,
            sort: "name",
            direction: "asc",
            pageNumber: 1,
            pageSize: Math.Clamp(top, 1, 500),
            cancellationToken);

    return result.Items;
}


public async Task<FacultySearchPage> GetFacultyPageAsync(
    string? search,
    string? department,
    string? sort,
    string? direction,
    int pageNumber,
    int pageSize,
    CancellationToken cancellationToken = default)
{
    var rows =
        new List<FacultyListItem>();

    var trimmedSearch =
        string.IsNullOrWhiteSpace(search)
            ? null
            : search.Trim();

    var trimmedDepartment =
        string.IsNullOrWhiteSpace(department)
            ? null
            : department.Trim();

    var safePageNumber =
        Math.Max(1, pageNumber);

    var safePageSize =
        Math.Clamp(pageSize, 1, 100);

    var offset =
        (safePageNumber - 1) * safePageSize;

    var normalizedDirection =
        string.Equals(
            direction,
            "desc",
            StringComparison.OrdinalIgnoreCase)
            ? "DESC"
            : "ASC";

    /*
     * IMPORTANT:
     * Never put user-supplied values directly into ORDER BY.
     * Every possible sort clause is explicitly whitelisted here.
     */
    var orderBy =
        (sort ?? string.Empty)
        .Trim()
        .ToLowerInvariant() switch
        {
            "userid" =>
                normalizedDirection == "DESC"
                    ? @"
CASE WHEN UvaUserId IS NULL THEN 1 ELSE 0 END ASC,
UvaUserId DESC,
DisplayName ASC"
                    : @"
CASE WHEN UvaUserId IS NULL THEN 1 ELSE 0 END ASC,
UvaUserId ASC,
DisplayName ASC",

            "department" =>
                $@"
DepartmentName {normalizedDirection},
DisplayName ASC",

            "title" =>
                normalizedDirection == "DESC"
                    ? @"
CASE WHEN Position IS NULL THEN 1 ELSE 0 END ASC,
Position DESC,
DisplayName ASC"
                    : @"
CASE WHEN Position IS NULL THEN 1 ELSE 0 END ASC,
Position ASC,
DisplayName ASC",

            "publications" =>
                $@"
PublicationCount {normalizedDirection},
DisplayName ASC",

            _ =>
                $@"
DisplayName {normalizedDirection},
DepartmentName ASC"
        };

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
        return new FacultySearchPage();
    }

    await using var command =
        connection.CreateCommand();

    command.CommandText = $@"

/* ------------------------------------------------------------
   Result set 1: total rows matching current filters
   ------------------------------------------------------------ */
SELECT COUNT(*)
FROM academiconline.vw_FacultyPublicationSearch f
WHERE
    (
        @Search IS NULL

        OR f.DisplayName LIKE
            N'%' + @Search + N'%'

        OR f.UvaUserId LIKE
            N'%' + @Search + N'%'

        OR f.Email LIKE
            N'%' + @Search + N'%'

        OR f.DepartmentName LIKE
            N'%' + @Search + N'%'

        OR f.DivisionName LIKE
            N'%' + @Search + N'%'
    )

    AND
    (
        @Department IS NULL
        OR f.DepartmentName = @Department
    );


/* ------------------------------------------------------------
   Result set 2: requested page
   ------------------------------------------------------------ */
;WITH FacultyWithCounts AS
(
    SELECT
        f.FacultyKey,
        f.UvaUserId,
        f.DisplayName,
        f.Email,
        f.DepartmentName,
        f.DivisionName,
        f.Position,
        f.DepartmentDataComplete,

        PublicationCount =
            CASE
                WHEN f.FacultyKey LIKE N'ROSTER:%'
                THEN
                (
                    SELECT COUNT(DISTINCT drp.PublicationKey)
                    FROM academiconline.DepartmentRosterPublication drp
                    WHERE drp.DepartmentId =
                          f.DepartmentId

                      AND drp.DepartmentRosterMemberId =
                          TRY_CONVERT
                          (
                              bigint,
                              SUBSTRING(
                                  f.FacultyKey,
                                  LEN(N'ROSTER:') + 1,
                                  50)
                          )

                      AND
                      (
                          (
                              f.DepartmentDataComplete = 1
                              AND drp.IsCurrent = 1
                              AND drp.CurrentSnapshotRunId =
                                  f.DataRunId
                          )

                          OR

                          (
                              f.DepartmentDataComplete = 0
                              AND drp.LastSyncRunId =
                                  f.DataRunId
                          )
                      )
                )

                ELSE
                (
                    SELECT COUNT(DISTINCT dsp.PublicationKey)
                    FROM academiconline.DepartmentScholarPublication dsp
                    WHERE dsp.DepartmentId =
                          f.DepartmentId

                      AND dsp.AcademicOnlineAuthorId =
                          f.SourceAuthorId

                      AND
                      (
                          (
                              f.DepartmentDataComplete = 1
                              AND dsp.IsCurrent = 1
                              AND dsp.CurrentSnapshotRunId =
                                  f.DataRunId
                          )

                          OR

                          (
                              f.DepartmentDataComplete = 0
                              AND dsp.LastSyncRunId =
                                  f.DataRunId
                          )
                      )
                )
            END

    FROM academiconline.vw_FacultyPublicationSearch f

    WHERE
        (
            @Search IS NULL

            OR f.DisplayName LIKE
                N'%' + @Search + N'%'

            OR f.UvaUserId LIKE
                N'%' + @Search + N'%'

            OR f.Email LIKE
                N'%' + @Search + N'%'

            OR f.DepartmentName LIKE
                N'%' + @Search + N'%'

            OR f.DivisionName LIKE
                N'%' + @Search + N'%'
        )

        AND
        (
            @Department IS NULL
            OR f.DepartmentName = @Department
        )
)

SELECT
    FacultyKey,
    UvaUserId,
    DisplayName,
    Email,
    DepartmentName,
    DivisionName,
    Position,
    DepartmentDataComplete,
    PublicationCount

FROM FacultyWithCounts

ORDER BY
    {orderBy},
    FacultyKey ASC

OFFSET @Offset ROWS
FETCH NEXT @PageSize ROWS ONLY;";

    command.Parameters.AddWithValue(
        "@Search",
        trimmedSearch is null
            ? DBNull.Value
            : trimmedSearch);

    command.Parameters.AddWithValue(
        "@Department",
        trimmedDepartment is null
            ? DBNull.Value
            : trimmedDepartment);

    command.Parameters.AddWithValue(
        "@Offset",
        offset);

    command.Parameters.AddWithValue(
        "@PageSize",
        safePageSize);

    await using var reader =
        await command.ExecuteReaderAsync(
            cancellationToken);

    var totalCount = 0;

    if (await reader.ReadAsync(
            cancellationToken))
    {
        totalCount =
            reader.IsDBNull(0)
                ? 0
                : reader.GetInt32(0);
    }

    if (await reader.NextResultAsync(
            cancellationToken))
    {
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

                    PublicationCount =
                        reader.IsDBNull(8)
                            ? 0
                            : reader.GetInt32(8),

                    IsActive = true
                });
        }
    }

    return new FacultySearchPage
    {
        Items = rows,
        TotalCount = totalCount
    };
}

public async Task<int> GetFacultyCountAsync(
    string? search,
    string? department,
    CancellationToken cancellationToken = default)
{
    var trimmedSearch =
        string.IsNullOrWhiteSpace(search)
            ? null
            : search.Trim();

    var trimmedDepartment =
        string.IsNullOrWhiteSpace(department)
            ? null
            : department.Trim();

    await using var connection =
        _connectionFactory.CreateConnection();

    await connection.OpenAsync(cancellationToken);

    await using var command =
        connection.CreateCommand();

    command.CommandText = @"
SELECT COUNT(*)
FROM academiconline.vw_FacultyPublicationSearch f
WHERE
    (
        @Search IS NULL
        OR f.DisplayName LIKE N'%' + @Search + N'%'
        OR f.UvaUserId LIKE N'%' + @Search + N'%'
        OR f.Email LIKE N'%' + @Search + N'%'
        OR f.DepartmentName LIKE N'%' + @Search + N'%'
        OR f.DivisionName LIKE N'%' + @Search + N'%'
    )
    AND
    (
        @Department IS NULL
        OR f.DepartmentName = @Department
    );";

    command.Parameters.AddWithValue(
        "@Search",
        trimmedSearch is null
            ? DBNull.Value
            : trimmedSearch);

    command.Parameters.AddWithValue(
        "@Department",
        trimmedDepartment is null
            ? DBNull.Value
            : trimmedDepartment);

    var result =
        await command.ExecuteScalarAsync(
            cancellationToken);

    return result is null || result == DBNull.Value
        ? 0
        : Convert.ToInt32(result);
}

    public async Task<DateTime?> GetFacultyRefreshTimeAsync(
    string? department,
    CancellationToken cancellationToken = default)
{
    var trimmedDepartment =
        string.IsNullOrWhiteSpace(department)
            ? null
            : department.Trim();

    await using var connection =
        _connectionFactory.CreateConnection();

    await connection.OpenAsync(cancellationToken);

    await using var command =
        connection.CreateCommand();

    /*
     * When one department is selected, show that department's
     * exact current snapshot time.
     *
     * For All Departments, use MIN rather than MAX. This means
     * "every active department is refreshed at least through
     * this time" rather than misleadingly showing only the
     * newest department refresh.
     */
    command.CommandText = @"
SELECT
    CASE
        WHEN @Department IS NULL
        THEN MIN(d.CurrentSnapshotAtUtc)
        ELSE MAX(d.CurrentSnapshotAtUtc)
    END
FROM academiconline.Department d
WHERE d.IsActive = 1
  AND d.CurrentSnapshotRunId IS NOT NULL
  AND
  (
      @Department IS NULL
      OR d.CanonicalName = @Department
  );";

    command.Parameters.AddWithValue(
        "@Department",
        trimmedDepartment is null
            ? DBNull.Value
            : trimmedDepartment);

    var result =
        await command.ExecuteScalarAsync(
            cancellationToken);

    if (result is null || result == DBNull.Value)
    {
        return null;
    }

    return Convert.ToDateTime(result);
}

    public async Task<IReadOnlyList<string>>
    GetFacultyDepartmentsAsync(
        CancellationToken cancellationToken = default)
{
    var departments =
        new List<string>();

    await using var connection =
        _connectionFactory.CreateConnection();

    await connection.OpenAsync(
        cancellationToken);

    await using var command =
        connection.CreateCommand();

    command.CommandText = @"
SELECT DISTINCT
    DepartmentName
FROM academiconline.vw_FacultyPublicationSearch
WHERE DepartmentName IS NOT NULL
  AND LTRIM(RTRIM(DepartmentName)) <> N''
ORDER BY DepartmentName;";

    await using var reader =
        await command.ExecuteReaderAsync(
            cancellationToken);

    while (await reader.ReadAsync(
        cancellationToken))
    {
        departments.Add(
            reader.GetString(0));
    }

    return departments;
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
