using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace OpenAlexIngestion.CitationPilot;

internal sealed record SourceRead(SourceCheck Check, SourceRow[] Rows);

internal static class DatabaseReader
{
    private sealed record Query(string Source, string Label, string[] Owned, string[] Required,
        Func<Schema, string> Build);
    private sealed class Schema
    {
        public Dictionary<string, HashSet<string>> Objects { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool Has(string name) => Objects.ContainsKey(name);
        public HashSet<string> Columns(string name) => Objects.GetValueOrDefault(name) ?? new(StringComparer.OrdinalIgnoreCase);
        public void Require(string name, params string[] columns)
        {
            if (!Has(name) || columns.Any(column => !Columns(name).Contains(column)))
                throw new FormatException("A required object/column in " + name + " is not available in the supported schema.");
        }
    }

    public static async Task<DatabaseReadResult> ReadAsync(string connectionString, string userId, CancellationToken ct)
    {
        userId = DoiRules.UserId(userId);
        var started = DateTimeOffset.UtcNow;
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        RequireTargetDatabase(connection.Database);
        var checks = new List<SourceCheck>(); var rows = new List<SourceRow>();
        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        string server;
        bool visible;
        using (var header = new SqlCommand("""
            SELECT CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName')),
              CASE WHEN IS_ROLEMEMBER(N'db_datareader')=1 OR IS_ROLEMEMBER(N'db_owner')=1
                 OR IS_SRVROLEMEMBER(N'sysadmin')=1
                 OR HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'VIEW DEFINITION')=1 THEN 1 ELSE 0 END;
            """, connection, transaction) { CommandTimeout = 30 })
        await using (var headerReader = await header.ExecuteReaderAsync(ct))
        {
            if (!await headerReader.ReadAsync(ct)) throw new FormatException("The SQL source header was not returned.");
            server = headerReader.IsDBNull(0) ? connection.DataSource : headerReader.GetString(0);
            visible = headerReader.GetInt32(1) == 1;
        }
        if (!visible) checks.Add(new("SourceVisibility", "Failed", 0, 0,
            "The login's metadata visibility could not establish that unlisted sources are absent. Visible source results are partial."));
        var schema = await ReadSchemaAsync(connection, transaction, ct);
        foreach (var query in Queries())
        {
            ct.ThrowIfCancellationRequested();
            var ownedPresent = query.Owned.Count(schema.Has);
            if (ownedPresent == 0)
            { checks.Add(new(query.Source, "NotPresent", 0, 0, "This optional source's own objects are not installed or visible.")); continue; }
            if (ownedPresent != query.Owned.Length || query.Required.Any(name => !schema.Has(name)))
            { checks.Add(new(query.Source, "Failed", 0, 0, "The source is partially installed or a required supporting object is unavailable.")); continue; }
            try
            {
                var sql = query.Build(schema);
                using var command = new SqlCommand("SELECT COUNT_BIG(*) FROM (" + sql + ") AS SourceRows;\n"
                    + sql + ";\nSELECT N'CompleteSourceRead';", connection, transaction) { CommandTimeout = 30 };
                command.Parameters.Add("@UserId", SqlDbType.NVarChar, 32).Value = userId;
                command.Parameters.Add("@Email", SqlDbType.NVarChar, 512).Value = userId + "@virginia.edu";
                await using var reader = await command.ExecuteReaderAsync(ct);
                var source = await ParseSourceAsync(reader, query.Source, query.Label, userId, ct);
                checks.Add(source.Check); rows.AddRange(source.Rows);
            }
            catch (SqlException ex)
            { checks.Add(new(query.Source, "Failed", 0, 0, "The source query failed (SQL error " + ex.Number + "). No zero-publication conclusion was made.")); }
            catch (FormatException ex)
            { checks.Add(new(query.Source, "Failed", 0, 0, ex.Message)); }
            catch (InvalidOperationException)
            { checks.Add(new(query.Source, "Failed", 0, 0, "The source transaction/connection could not be used. No zero-publication conclusion was made.")); }
        }
        try { await transaction.CommitAsync(ct); }
        catch (SqlException ex) { checks.Add(new("SnapshotTransaction", "Failed", 0, 0, "The source read transaction could not complete (SQL error " + ex.Number + ").")); }
        catch (InvalidOperationException) { checks.Add(new("SnapshotTransaction", "Failed", 0, 0, "The source read transaction did not remain active through completion.")); }
        var nativeIds = rows.Where(row => row.NativeFacultyId is not null).Select(row => row.NativeFacultyId).Distinct().ToArray();
        if (nativeIds.Length > 1)
        {
            for (var index = 0; index < rows.Count; index++)
                if (rows[index].Source is "NativeIdentity" or "AcceptedOpenAlex") rows[index] = rows[index] with { IdentityConflict = true };
            checks.RemoveAll(check => check.Source is "NativeIdentity" or "AcceptedOpenAlex");
            checks.Add(new("NativeIdentity", "Failed", rows.Count(row => row.Source == "NativeIdentity"), 0,
                "Several native faculty IDs match this ComputingID. Native/accepted results were retained under an identity hold; other exact-identifier sources remain separate."));
            checks.Add(new("AcceptedOpenAlex", "Failed", rows.Count(row => row.Source == "AcceptedOpenAlex"), rows.Count(row => row.Source == "AcceptedOpenAlex" && row.HasPublication),
                "Accepted rows were retained under an identity hold rather than selecting between conflicting native identities."));
        }
        return Build(rows.ToArray(), checks.ToArray(), userId, server, started);
    }

    internal static void RequireTargetDatabase(string database)
    {
        if (!database.Equals("OpenAlexIngestion_Test", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("This citation pilot only reads OpenAlexIngestion_Test. Correct citation-settings.json; no other database was queried for publications.");
    }

    internal static DatabaseReadResult Build(SourceRow[] rows, SourceCheck[] checks, string userId,
        string server, DateTimeOffset started)
    {
        var failed = checks.Any(check => check.Status == "Failed");
        if (rows.Length == 0)
            throw new FormatException(failed
                ? "The faculty identity could not be established because one or more source reads failed. No absence or empty publication list was inferred."
                : "No supported exact-identifier faculty record matches this ComputingID. No empty publication list was inferred.");
        var ordered = rows.OrderBy(row => row.Source == "RosterIdentity" ? 0 : row.Source == "NativeIdentity" ? 1 : 2).ToArray();
        var names = ordered.Select(row => row.FacultyName).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var departments = ordered.Select(row => row.PrimaryDepartment).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var faculty = new FacultyIdentity(userId, names.FirstOrDefault() ?? "(Name not supplied)", departments.FirstOrDefault() ?? string.Empty, names, departments);
        return new(new(Guid.NewGuid(), server, "OpenAlexIngestion_Test", started, DateTimeOffset.UtcNow,
            1, rows.Count(row => row.HasPublication), !failed, failed ? "PartialOneFacultyRead" : "CompleteOneFacultyRead", checks), faculty, rows);
    }

    internal static async Task<SourceRead> ParseSourceAsync(DbDataReader reader, string source, string label,
        string userId, CancellationToken ct)
    {
        if (!await reader.ReadAsync(ct)) throw new FormatException("The source count header was not returned.");
        var expected = reader.GetInt64(0);
        if (expected is < 0 or > 50000) throw new FormatException("This source exceeds the 50,000-row one-faculty pilot bound. No truncated source read was accepted.");
        if (await reader.ReadAsync(ct) || !await reader.NextResultAsync(ct)) throw new FormatException("The source count header is inconsistent.");
        var rows = new List<SourceRow>();
        while (await reader.ReadAsync(ct))
        {
            string? Text(int field) => reader.IsDBNull(field) ? null : reader.GetString(field);
            if (!string.Equals(Text(0), userId, StringComparison.OrdinalIgnoreCase)) throw new FormatException("A source row does not belong to the requested ComputingID.");
            rows.Add(new(userId, Text(1) ?? string.Empty, Text(2), Text(3), Text(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5), source, label, Text(6) ?? string.Empty,
                reader.IsDBNull(7) ? null : reader.GetInt64(7), reader.GetBoolean(8),
                Text(9) ?? "StoredAssociationNotIndependentlyReviewed", Text(10) ?? string.Empty));
        }
        if (rows.Count != expected || !await reader.NextResultAsync(ct) || !await reader.ReadAsync(ct)
            || reader.GetString(0) != "CompleteSourceRead" || await reader.ReadAsync(ct) || await reader.NextResultAsync(ct))
            throw new FormatException("The source rows/count/completion marker are incomplete or inconsistent.");
        return new(new(source, "Succeeded", rows.Count, rows.Count(row => row.HasPublication)), rows.ToArray());
    }

    private static async Task<Schema> ReadSchemaAsync(SqlConnection connection, SqlTransaction transaction, CancellationToken ct)
    {
        const string sql = """
            SELECT s.name+N'.'+o.name,c.name FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id
            LEFT JOIN sys.columns c ON c.object_id=o.object_id
            WHERE s.name IN (N'openalex',N'academiconline') AND o.type IN (N'U',N'V');
            """;
        using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        await using var reader = await command.ExecuteReaderAsync(ct);
        var schema = new Schema();
        while (await reader.ReadAsync(ct))
        {
            var name = reader.GetString(0);
            if (!schema.Objects.TryGetValue(name, out var columns)) schema.Objects[name] = columns = new(StringComparer.OrdinalIgnoreCase);
            if (!reader.IsDBNull(1)) columns.Add(reader.GetString(1));
        }
        return schema;
    }

    private static string Prefix(string name, string department, string doi, string title, string year,
        string key, string facultyId, string present, string disposition, string basis) =>
        $"SELECT @UserId AS MatchedUserId, CONVERT(nvarchar(512),{name}) AS FacultyName, CONVERT(nvarchar(512),{department}) AS DepartmentName, "
        + $"CONVERT(nvarchar(max),{doi}) AS Doi, CONVERT(nvarchar(max),{title}) AS Title, CONVERT(int,{year}) AS PublicationYear, "
        + $"CONVERT(nvarchar(2048),{key}) AS SourceRecordKey, CONVERT(bigint,{facultyId}) AS NativeFacultyId, CONVERT(bit,{present}) AS HasPublication, "
        + $"N'{disposition}' AS ReviewDisposition, N'{basis}' AS IdentityBasis ";

    private static Query[] Queries() =>
    [
        new("RosterIdentity", "Imported faculty roster", ["openalex.FacultyRoster"], ["openalex.FacultyRoster"], s =>
        {
            s.Require("openalex.FacultyRoster", "ComputingId", "DisplayName", "NativeFacultyId");
            var columns = s.Columns("openalex.FacultyRoster");
            var department = columns.Contains("OrganizationName") ? "r.OrganizationName" : columns.Contains("DepartmentCode") ? "r.DepartmentCode" : "NULL";
            return Prefix("r.DisplayName", department, "NULL", "NULL", "NULL", "r.ComputingId", "r.NativeFacultyId", "0", "StoredAssociationNotIndependentlyReviewed", "Exact roster ComputingID")
                + "FROM openalex.FacultyRoster r WHERE LOWER(LTRIM(RTRIM(r.ComputingId)))=@UserId";
        }),
        new("NativeIdentity", "Native faculty identity", ["openalex.Faculty"], ["openalex.Faculty"], s =>
        {
            var (name, department, predicate) = Native(s);
            return Prefix(name, department, "NULL", "NULL", "NULL", "CONVERT(nvarchar(32),f.FacultyId)", "f.FacultyId", "0", "StoredAssociationNotIndependentlyReviewed", "Native ComputingID, canonical email, or confirmed roster link")
                + "FROM openalex.Faculty f WHERE " + predicate;
        }),
        new("RebuildIdentity", "Academic Online imported faculty identity", ["academiconline.RebuildImportedFaculty"], ["academiconline.RebuildImportedFaculty"], s =>
        {
            s.Require("academiconline.RebuildImportedFaculty", "UserId", "FacultyName", "PrimaryDepartment");
            return Prefix("f.FacultyName", "f.PrimaryDepartment", "NULL", "NULL", "NULL", "f.UserId", "NULL", "0", "StoredAssociationNotIndependentlyReviewed", "Exact imported ComputingID")
                + "FROM academiconline.RebuildImportedFaculty f WHERE f.UserId=@UserId";
        }),
        new("RebuildImport", "Academic Online import", ["academiconline.RebuildImportedFaculty", "academiconline.RebuildImportedFacultyDoi"], ["academiconline.RebuildImportedFaculty", "academiconline.RebuildImportedFacultyDoi"], s =>
        {
            s.Require("academiconline.RebuildImportedFaculty", "UserId", "FacultyName", "PrimaryDepartment");
            s.Require("academiconline.RebuildImportedFacultyDoi", "UserId", "Doi");
            return Prefix("f.FacultyName", "f.PrimaryDepartment", "d.Doi", "NULL", "NULL", "f.UserId COLLATE DATABASE_DEFAULT+N':'+COALESCE(d.Doi,N'(no DOI)') COLLATE DATABASE_DEFAULT", "NULL", "CASE WHEN d.UserId IS NULL THEN 0 ELSE 1 END", "StoredAssociationNotIndependentlyReviewed", "Exact imported ComputingID")
                + "FROM academiconline.RebuildImportedFaculty f LEFT JOIN academiconline.RebuildImportedFacultyDoi d ON d.UserId=f.UserId WHERE f.UserId=@UserId";
        }),
        new("AcademicOnlineCurrent", "Synchronized publications", ["academiconline.Scholar", "academiconline.DepartmentScholarPublication"], ["academiconline.Scholar", "academiconline.DepartmentScholarPublication", "academiconline.Department", "academiconline.vw_DepartmentNewestPublication"], s =>
        {
            s.Require("academiconline.Scholar", "DisplayName", "AcademicOnlineAuthorId", "UvaUserId", "Email");
            s.Require("academiconline.DepartmentScholarPublication", "AcademicOnlineAuthorId", "DepartmentId", "PublicationKey", "IsCurrent", "CurrentSnapshotRunId");
            Shared(s);
            return Prefix("s.DisplayName", "NULL", "p.Doi", "p.Title", "p.PublicationYear", "s.AcademicOnlineAuthorId COLLATE DATABASE_DEFAULT+N':'+COALESCE(CONVERT(nvarchar(32),p.DepartmentId),N'')+N':'+COALESCE(p.PublicationKey,N'') COLLATE DATABASE_DEFAULT", "NULL", "CASE WHEN p.PublicationKey IS NULL THEN 0 ELSE 1 END", "StoredAssociationNotIndependentlyReviewed", "Exact Academic Online userid, or canonical email when userid is absent")
                + """
                FROM academiconline.Scholar s LEFT JOIN (
                  SELECT sp.AcademicOnlineAuthorId,sp.DepartmentId,v.PublicationKey,v.Doi,v.Title,v.PublicationYear
                  FROM academiconline.DepartmentScholarPublication sp JOIN academiconline.Department d ON d.DepartmentId=sp.DepartmentId
                  JOIN academiconline.vw_DepartmentNewestPublication v ON v.DepartmentId=sp.DepartmentId AND v.PublicationKey=sp.PublicationKey
                  WHERE sp.IsCurrent=1 AND sp.CurrentSnapshotRunId=d.CurrentSnapshotRunId AND d.SourceMode<>N'FacultyRoster'
                ) p ON p.AcademicOnlineAuthorId=s.AcademicOnlineAuthorId
                WHERE LOWER(LTRIM(RTRIM(s.UvaUserId)))=@UserId OR
                  (NULLIF(LTRIM(RTRIM(s.UvaUserId)),N'') IS NULL AND LOWER(LTRIM(RTRIM(s.Email)))=@Email)
                """;
        }),
        new("RosterCurrent", "Synchronized faculty-roster publications", ["academiconline.DepartmentRosterMember", "academiconline.DepartmentRosterPublication"], ["academiconline.DepartmentRosterMember", "academiconline.DepartmentRosterPublication", "academiconline.Department", "academiconline.vw_DepartmentNewestPublication"], s =>
        {
            s.Require("academiconline.DepartmentRosterMember", "DisplayName", "DepartmentRosterMemberId", "IsActive", "ExpectedEmail");
            s.Require("academiconline.DepartmentRosterPublication", "DepartmentRosterMemberId", "DepartmentId", "PublicationKey", "IsCurrent", "CurrentSnapshotRunId");
            Shared(s);
            return Prefix("rm.DisplayName", "NULL", "p.Doi", "p.Title", "p.PublicationYear", "CONVERT(nvarchar(32),rm.DepartmentRosterMemberId)+N':'+COALESCE(CONVERT(nvarchar(32),p.DepartmentId),N'')+N':'+COALESCE(p.PublicationKey,N'') COLLATE DATABASE_DEFAULT", "NULL", "CASE WHEN p.PublicationKey IS NULL THEN 0 ELSE 1 END", "StoredAssociationNotIndependentlyReviewed", "Active roster member with exact canonical email")
                + """
                FROM academiconline.DepartmentRosterMember rm LEFT JOIN (
                  SELECT rp.DepartmentRosterMemberId,rp.DepartmentId,v.PublicationKey,v.Doi,v.Title,v.PublicationYear
                  FROM academiconline.DepartmentRosterPublication rp JOIN academiconline.Department d ON d.DepartmentId=rp.DepartmentId
                  JOIN academiconline.vw_DepartmentNewestPublication v ON v.DepartmentId=rp.DepartmentId AND v.PublicationKey=rp.PublicationKey
                  WHERE rp.IsCurrent=1 AND rp.CurrentSnapshotRunId=d.CurrentSnapshotRunId AND d.SourceMode=N'FacultyRoster'
                ) p ON p.DepartmentRosterMemberId=rm.DepartmentRosterMemberId
                WHERE rm.IsActive=1 AND LOWER(LTRIM(RTRIM(rm.ExpectedEmail)))=@Email
                """;
        }),
        new("AcceptedOpenAlex", "Accepted OpenAlex publications", ["openalex.FacultyPublicationDecision", "openalex.FacultyPublicationCandidate"], ["openalex.Faculty", "openalex.FacultyPublicationDecision", "openalex.FacultyPublicationCandidate"], s =>
        {
            var (name, department, predicate) = Native(s);
            s.Require("openalex.FacultyPublicationDecision", "FacultyId", "FacultyPublicationCandidateId", "WorkDoi", "WorkTitle", "PublicationYear", "IsCurrent", "DecisionStatus");
            s.Require("openalex.FacultyPublicationCandidate", "FacultyPublicationCandidateId", "WorkDoi", "WorkTitle", "PublicationYear");
            return Prefix(name, department, "p.Doi", "p.Title", "p.PublicationYear", "CONVERT(nvarchar(32),f.FacultyId)+N':'+COALESCE(CONVERT(nvarchar(32),p.FacultyPublicationCandidateId),N'decision')", "f.FacultyId", "CASE WHEN p.FacultyId IS NULL THEN 0 ELSE 1 END", "AcceptedOpenAlex", "Native exact identifier or confirmed roster link; current Accepted decision")
                + """
                FROM openalex.Faculty f LEFT JOIN (
                  SELECT d.FacultyId,d.FacultyPublicationCandidateId,
                    COALESCE(NULLIF(LTRIM(RTRIM(d.WorkDoi)),N''),pc.WorkDoi) AS Doi,
                    COALESCE(d.WorkTitle,pc.WorkTitle) AS Title,COALESCE(d.PublicationYear,pc.PublicationYear) AS PublicationYear
                  FROM openalex.FacultyPublicationDecision d LEFT JOIN openalex.FacultyPublicationCandidate pc
                    ON pc.FacultyPublicationCandidateId=d.FacultyPublicationCandidateId
                  WHERE d.IsCurrent=1 AND d.DecisionStatus=N'Accepted'
                ) p ON p.FacultyId=f.FacultyId WHERE
                """ + predicate;
        })
    ];

    private static void Shared(Schema s)
    {
        s.Require("academiconline.Department", "DepartmentId", "CurrentSnapshotRunId", "SourceMode");
        s.Require("academiconline.vw_DepartmentNewestPublication", "DepartmentId", "PublicationKey", "Doi", "Title", "PublicationYear");
    }

    private static (string Name, string Department, string Predicate) Native(Schema s)
    {
        s.Require("openalex.Faculty", "FacultyId");
        var columns = s.Columns("openalex.Faculty");
        string? Pick(params string[] candidates) => candidates.FirstOrDefault(columns.Contains);
        var name = Pick("DisplayName", "FacultyDisplayName", "FullName", "Name")
            ?? throw new FormatException("The native faculty name column is not supported.");
        var department = Pick("DepartmentName", "Department"); var email = Pick("PrimaryEmail", "Email");
        var hasId = columns.Contains("ComputingId"); var matches = new List<string>();
        if (hasId) matches.Add("LOWER(LTRIM(RTRIM(f.ComputingId)))=@UserId");
        var idGuard = hasId ? " AND (NULLIF(LTRIM(RTRIM(f.ComputingId)),N'') IS NULL OR LOWER(LTRIM(RTRIM(f.ComputingId)))=@UserId)" : string.Empty;
        if (email is not null) matches.Add("(LOWER(LTRIM(RTRIM(f.[" + email + "])))=@Email" + idGuard + ")");
        if (s.Has("openalex.FacultyRoster") && s.Columns("openalex.FacultyRoster").Contains("ComputingId") && s.Columns("openalex.FacultyRoster").Contains("NativeFacultyId"))
            matches.Add("(EXISTS(SELECT 1 FROM openalex.FacultyRoster r WHERE LOWER(LTRIM(RTRIM(r.ComputingId)))=@UserId AND r.NativeFacultyId=f.FacultyId)" + idGuard + ")");
        return ("f.[" + name + "]", department is null ? "NULL" : "f.[" + department + "]", matches.Count == 0 ? "1=0" : "(" + string.Join(" OR ", matches) + ")");
    }
}
