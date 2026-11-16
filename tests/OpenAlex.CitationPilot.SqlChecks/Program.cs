using Microsoft.Data.SqlClient;
using OpenAlexIngestion.CitationPilot;

namespace OpenAlexIngestion.CitationPilot.SqlChecks;

// CI-only fixture writer. This project is never included in the user distribution.
internal static class Program
{
    private const string DatabaseName = "OpenAlexIngestion_Test";
    private const string ReaderName = "OpenAlexCitationSyntheticReader";
    private const string ReaderPassword = "Synthetic_Citation_Reader_65432!";
    private static string _admin = string.Empty;
    private static string _reader = string.Empty;
    private static int _passed;

    public static async Task<int> Main()
    {
        try
        {
            await InitializeFreshServerAsync();
            await SeedImportAsync();
            await CheckAsync("import-only source and faculty-scoped DOI associations", async () =>
            {
                var read = await ReadAsync("demo01"); AssertComplete(read);
                Equal(1, read.PublicationRows.Length); Equal("10.5555/imported-good", read.PublicationRows[0].RawDoi);
                Equal("RebuildImport", read.PublicationRows[0].Source);
                Equal("StoredAssociationNotIndependentlyReviewed", read.PublicationRows[0].ReviewDisposition);
                True(read.Snapshot.SourceChecks.Where(check => check.Source is not ("RebuildImport" or "RebuildIdentity")).All(check => check.Status == "NotPresent"), "Absent optional sources must be explicitly NotPresent.");
                AssertSourceTotals(read);
            });
            await CheckAsync("known faculty without DOI rows is not a missing or invalid publication", async () =>
            {
                var read = await ReadAsync("zero01"); AssertComplete(read);
                Equal("zero01", read.Faculty.UserId); Equal(0, read.PublicationRows.Length);
                True(read.Rows.Length > 0 && read.Rows.All(row => !row.HasPublication), "Identity-only rows must not become publication associations.");
                Equal(0, DoiRules.Plan(read, []).Length); AssertSourceTotals(read);
            });
            await CheckAsync("unknown ComputingID does not become an empty bibliography", async () =>
            {
                await ThrowsAsync<FormatException>(() => ReadAsync("unknown99"));
            });
            await CheckAsync("wrong database is rejected before publication-source queries", async () =>
            {
                var connection = new SqlConnectionStringBuilder(_reader) { InitialCatalog = "master" };
                await ThrowsAsync<FormatException>(() => DatabaseReader.ReadAsync(connection.ConnectionString, "demo01", CancellationToken.None));
            });
            await SeedScholarAnchorOnlyAsync();
            await CheckAsync("partially installed source is recorded as failed while good rows survive", async () =>
            {
                var read = await ReadAsync("demo01");
                True(!read.Snapshot.Complete, "A partly installed current-publication source cannot claim completeness.");
                Equal("PartialOneFacultyRead", read.Snapshot.EndMarker);
                Equal("Failed", Source(read, "AcademicOnlineCurrent").Status);
                True(!string.IsNullOrWhiteSpace(Source(read, "AcademicOnlineCurrent").Error), "The failed source needs an explanatory message.");
                True(read.PublicationRows.Any(row => row.RawDoi == "10.5555/imported-good"), "Successful imported rows must remain available.");
                AssertSourceTotals(read);
            });
            await SeedNativeAsync();
            await CheckAsync("only current accepted native publications follow exact identities", async () =>
            {
                var read = await ReadAsync("demo01");
                Equal("Succeeded", Source(read, "NativeIdentity").Status);
                Equal("Succeeded", Source(read, "AcceptedOpenAlex").Status);
                var accepted = read.PublicationRows.Where(row => row.Source == "AcceptedOpenAlex").ToArray();
                Equal(2, accepted.Length);
                True(accepted.All(row => row.NativeFacultyId == 101 && row.ReviewDisposition == "AcceptedOpenAlex"), "Accepted provenance must stay with the selected native identity.");
                True(accepted.Any(row => row.RawDoi == "10.5555/accepted-current"), "The current accepted DOI was lost.");
                var missing = accepted.Single(row => row.RawDoi is null);
                Equal("Accepted title without a DOI", missing.StoredTitle); True(missing.HasPublication, "A real DOI-less accepted publication is not an identity placeholder.");
                var forbidden = new[] { "10.5555/accepted-old", "10.5555/rejected", "10.5555/source-key-only", "10.5555/contradictory-id", "10.5555/other-native-only" };
                True(!read.PublicationRows.Any(row => forbidden.Contains(row.RawDoi)), "A rejected, stale, source-key-only, contradictory, or unrelated faculty record leaked into the export.");
                AssertSourceTotals(read);
            });
            await CompleteScholarSourceAsync();
            await CheckAsync("synchronized snapshots exclude stale and inactive associations", async () =>
            {
                var read = await ReadAsync("demo01"); AssertComplete(read);
                Equal("NotPresent", Source(read, "RosterCurrent").Status);
                Equal("Succeeded", Source(read, "AcademicOnlineCurrent").Status);
                var sync = read.PublicationRows.Where(row => row.Source == "AcademicOnlineCurrent").ToArray();
                Equal(2, sync.Length); True(sync.Any(row => row.RawDoi == "10.5555/sync-current"), "The current scholar DOI was lost.");
                var missing = sync.Single(row => row.RawDoi is null);
                Equal("Current synchronized title without a DOI", missing.StoredTitle);
                var forbidden = new[] { "10.5555/sync-stale", "10.5555/sync-inactive", "10.5555/scholar-identity-conflict" };
                True(!read.PublicationRows.Any(row => forbidden.Contains(row.RawDoi)), "Stale, inactive or contradictory scholar records leaked into the export.");
                AssertSourceTotals(read);
            });
            await SeedRosterCurrentAsync();
            await CheckAsync("roster-current source retains its independent current-snapshot scope", async () =>
            {
                var read = await ReadAsync("demo01"); AssertComplete(read);
                var rows = read.PublicationRows.Where(row => row.Source == "RosterCurrent").ToArray();
                Equal(1, rows.Length); Equal("10.5555/roster-current", rows[0].RawDoi);
                Equal("StoredAssociationNotIndependentlyReviewed", rows[0].ReviewDisposition);
                True(!read.PublicationRows.Any(row => row.RawDoi is "10.5555/roster-stale" or "10.5555/roster-inactive"), "A stale or inactive roster association leaked.");
                AssertSourceTotals(read);
            });
            await ExecuteAsync("DENY SELECT ON OBJECT::openalex.FacultyPublicationCandidate TO [OpenAlexCitationSyntheticReader];");
            await CheckAsync("actual SELECT failure produces a partial source read, not a zero count", async () =>
            {
                var read = await ReadAsync("demo01");
                True(!read.Snapshot.Complete, "A denied source query cannot claim a complete read.");
                Equal("PartialOneFacultyRead", read.Snapshot.EndMarker);
                var failures = read.Snapshot.SourceChecks.Where(check => check.Status == "Failed").ToArray();
                Equal(1, failures.Length); Equal("AcceptedOpenAlex", failures[0].Source);
                True(read.PublicationRows.Any(row => row.Source == "RebuildImport"), "The failed source erased successful imported rows.");
                True(read.PublicationRows.Any(row => row.Source == "AcademicOnlineCurrent"), "The failed source erased successful synchronized rows.");
                AssertSourceTotals(read);
            });
            await ExecuteAsync("REVOKE SELECT ON OBJECT::openalex.FacultyPublicationCandidate FROM [OpenAlexCitationSyntheticReader];");
            await CheckAsync("reader has no mutation rights and leaves source data unchanged", async () =>
            {
                await using (var connection = new SqlConnection(_reader))
                {
                    await connection.OpenAsync();
                    using var command = new SqlCommand("SELECT HAS_PERMS_BY_NAME(N'openalex.Faculty',N'OBJECT',N'INSERT'), HAS_PERMS_BY_NAME(N'openalex.Faculty',N'OBJECT',N'UPDATE'), HAS_PERMS_BY_NAME(N'openalex.Faculty',N'OBJECT',N'DELETE');", connection);
                    await using var permissions = await command.ExecuteReaderAsync(); await permissions.ReadAsync();
                    Equal(0, permissions.GetInt32(0)); Equal(0, permissions.GetInt32(1)); Equal(0, permissions.GetInt32(2));
                }
                var before = await FingerprintAsync();
                var read = await ReadAsync("demo01"); AssertComplete(read); AssertSourceTotals(read);
                Equal(before, await FingerprintAsync());
                True(read.PublicationRows.All(row => !string.IsNullOrWhiteSpace(row.SourceRecordKey)), "Source publication records need their provenance keys.");
                var planned = DoiRules.Plan(read, []);
                Equal(2, planned.Count(row => row.Status == "MissingDoi"));
            });
            Console.WriteLine($"OpenAlex SQL integration checks passed: {_passed}. Reader permissions: SELECT only; live metadata requests: 0.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL OpenAlex SQL integration after " + _passed + " checks: " + ex.Message);
            return 1;
        }
    }

    private static async Task InitializeFreshServerAsync()
    {
        if (Environment.GetEnvironmentVariable("OPENALEX_CITATION_DISPOSABLE_SQL") != "1")
            throw new InvalidOperationException("This CI-only fixture harness requires explicit disposable-SQL authorization.");
        var supplied = Environment.GetEnvironmentVariable("OPENALEX_CITATION_SQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(supplied)) throw new InvalidOperationException("The disposable SQL service connection is missing.");
        var builder = new SqlConnectionStringBuilder(supplied) { InitialCatalog = "master", ConnectTimeout = 3 };
        if (builder.DataSource is not ("127.0.0.1,1433" or "localhost,1433"))
            throw new InvalidOperationException("This fixture harness accepts only its isolated loopback SQL service.");
        var ready = false;
        for (var attempt = 0; attempt < 45; attempt++)
        {
            try { await using var probe = new SqlConnection(builder.ConnectionString); await probe.OpenAsync(); ready = true; break; }
            catch (SqlException) { await Task.Delay(2000); }
        }
        if (!ready) throw new InvalidOperationException("The disposable SQL service did not become ready.");
        await using (var connection = new SqlConnection(builder.ConnectionString))
        {
            await connection.OpenAsync();
            using var preflight = new SqlCommand("SELECT (SELECT COUNT(*) FROM sys.databases WHERE name NOT IN(N'master',N'model',N'msdb',N'tempdb')), CASE WHEN SUSER_ID(N'OpenAlexCitationSyntheticReader') IS NULL THEN 0 ELSE 1 END;", connection);
            await using (var reader = await preflight.ExecuteReaderAsync())
            {
                await reader.ReadAsync();
                if (reader.GetInt32(0) != 0 || reader.GetInt32(1) != 0)
                    throw new InvalidOperationException("The SQL service is not fresh. No existing database or login will be replaced or dropped.");
            }
            using var create = new SqlCommand("CREATE DATABASE [OpenAlexIngestion_Test];", connection); await create.ExecuteNonQueryAsync();
            using var compatibility = new SqlCommand("ALTER DATABASE [OpenAlexIngestion_Test] SET COMPATIBILITY_LEVEL=130;", connection); await compatibility.ExecuteNonQueryAsync();
            using var login = new SqlCommand($"CREATE LOGIN [{ReaderName}] WITH PASSWORD=N'{ReaderPassword}', CHECK_POLICY=OFF;", connection); await login.ExecuteNonQueryAsync();
        }
        builder.InitialCatalog = DatabaseName; builder.ConnectTimeout = 10; _admin = builder.ConnectionString;
        await ExecuteAsync($"CREATE USER [{ReaderName}] FOR LOGIN [{ReaderName}]; ALTER ROLE db_datareader ADD MEMBER [{ReaderName}]; GRANT VIEW DEFINITION TO [{ReaderName}];");
        await ExecuteAsync("CREATE SCHEMA academiconline;"); await ExecuteAsync("CREATE SCHEMA openalex;");
        builder.IntegratedSecurity = false; builder.UserID = ReaderName; builder.Password = ReaderPassword; _reader = builder.ConnectionString;
    }

    private static Task SeedImportAsync() => ExecuteAsync("""
        CREATE TABLE academiconline.RebuildImportedFaculty(UserId nvarchar(32) COLLATE Latin1_General_100_CI_AS PRIMARY KEY,FacultyName nvarchar(200),PrimaryDepartment nvarchar(200));
        CREATE TABLE academiconline.RebuildImportedFacultyDoi(UserId nvarchar(32) COLLATE Latin1_General_100_CI_AS,Doi nvarchar(400) COLLATE Latin1_General_100_BIN2);
        INSERT academiconline.RebuildImportedFaculty VALUES(N'demo01',N'Synthetic Faculty',N'Synthetic department'),(N'zero01',N'Synthetic Zero',N'Synthetic department'),(N'other01',N'Synthetic Coauthor',N'Synthetic department');
        INSERT academiconline.RebuildImportedFacultyDoi VALUES(N'demo01',N'10.5555/imported-good'),(N'other01',N'10.5555/imported-good'),(N'other01',N'10.5555/other-imported-only');
        """);

    private static Task SeedScholarAnchorOnlyAsync() => ExecuteAsync("""
        CREATE TABLE academiconline.Scholar(AcademicOnlineAuthorId nvarchar(64),DisplayName nvarchar(512),UvaUserId nvarchar(32),Email nvarchar(320));
        INSERT academiconline.Scholar VALUES(N'synthetic-scholar',N'Synthetic Scholar',N'demo01',N'demo01@virginia.edu'),(N'conflicting-scholar',N'Other Scholar',N'other01',N'demo01@virginia.edu');
        """);

    private static Task SeedNativeAsync() => ExecuteAsync("""
        CREATE TABLE openalex.Faculty(FacultyId bigint PRIMARY KEY,DisplayName nvarchar(512),PrimaryEmail nvarchar(512),SourceFacultyKey nvarchar(512),ComputingId nvarchar(32));
        INSERT openalex.Faculty VALUES
        (101,N'Synthetic Native',N'demo01@virginia.edu',N'profile-local-101',N'demo01'),
        (102,N'Synthetic Other',N'other01@virginia.edu',N'demo01',N'other01'),
        (103,N'Synthetic Contradiction',N'demo01@virginia.edu',N'profile-local-103',N'other02'),
        (104,N'Synthetic Zero Native',N'zero01@virginia.edu',N'profile-local-104',N'zero01');
        CREATE TABLE openalex.FacultyPublicationCandidate(FacultyPublicationCandidateId bigint PRIMARY KEY,WorkDoi nvarchar(512),WorkTitle nvarchar(512),PublicationYear int);
        INSERT openalex.FacultyPublicationCandidate VALUES(1,N'10.5555/accepted-current',N'Current accepted title',2025),(2,NULL,N'Accepted title without a DOI',2025);
        CREATE TABLE openalex.FacultyPublicationDecision(FacultyPublicationDecisionId bigint PRIMARY KEY,FacultyId bigint,FacultyPublicationCandidateId bigint,WorkDoi nvarchar(512),WorkTitle nvarchar(512),PublicationYear int,IsCurrent bit,DecisionStatus nvarchar(32));
        INSERT openalex.FacultyPublicationDecision VALUES
        (1,101,1,NULL,NULL,NULL,1,N'Accepted'),(2,101,2,NULL,NULL,NULL,1,N'Accepted'),
        (3,101,NULL,N'10.5555/accepted-old',N'Old accepted title',2024,0,N'Accepted'),
        (4,101,NULL,N'10.5555/rejected',N'Rejected title',2025,1,N'Rejected'),
        (5,102,NULL,N'10.5555/source-key-only',N'Other faculty title',2025,1,N'Accepted'),
        (6,103,NULL,N'10.5555/contradictory-id',N'Contradictory faculty title',2025,1,N'Accepted');
        """);

    private static async Task CompleteScholarSourceAsync()
    {
        await ExecuteAsync("""
            CREATE TABLE academiconline.Department(DepartmentId int PRIMARY KEY,CurrentSnapshotRunId uniqueidentifier,SourceMode nvarchar(32));
            INSERT academiconline.Department VALUES(1,'11111111-1111-1111-1111-111111111111',N'AcademicOnline'),(2,'11111111-1111-1111-1111-111111111111',N'FacultyRoster');
            CREATE TABLE academiconline.DepartmentScholarPublication(DepartmentId int,AcademicOnlineAuthorId nvarchar(64),PublicationKey nvarchar(64),IsCurrent bit,CurrentSnapshotRunId uniqueidentifier);
            INSERT academiconline.DepartmentScholarPublication VALUES
            (1,N'synthetic-scholar',N'sync-current',1,'11111111-1111-1111-1111-111111111111'),
            (1,N'synthetic-scholar',N'sync-missing-doi',1,'11111111-1111-1111-1111-111111111111'),
            (1,N'synthetic-scholar',N'sync-stale',1,'22222222-2222-2222-2222-222222222222'),
            (1,N'synthetic-scholar',N'sync-inactive',0,'11111111-1111-1111-1111-111111111111'),
            (1,N'conflicting-scholar',N'sync-conflict',1,'11111111-1111-1111-1111-111111111111');
            CREATE TABLE academiconline.TestPublication(DepartmentId int,PublicationKey nvarchar(64),Doi nvarchar(512),Title nvarchar(512),PublicationYear int);
            INSERT academiconline.TestPublication VALUES
            (1,N'sync-current',N'10.5555/sync-current',N'Current synchronized title',2025),
            (1,N'sync-missing-doi',NULL,N'Current synchronized title without a DOI',2025),
            (1,N'sync-stale',N'10.5555/sync-stale',N'Stale synchronized title',2024),
            (1,N'sync-inactive',N'10.5555/sync-inactive',N'Inactive synchronized title',2025),
            (1,N'sync-conflict',N'10.5555/scholar-identity-conflict',N'Conflicting scholar title',2025);
            """);
        await ExecuteAsync("CREATE VIEW academiconline.vw_DepartmentNewestPublication AS SELECT * FROM academiconline.TestPublication;");
    }

    private static Task SeedRosterCurrentAsync() => ExecuteAsync("""
        CREATE TABLE academiconline.DepartmentRosterMember(DepartmentRosterMemberId int PRIMARY KEY,DisplayName nvarchar(512),ExpectedEmail nvarchar(320),IsActive bit);
        INSERT academiconline.DepartmentRosterMember VALUES(1,N'Synthetic Roster Member',N'demo01@virginia.edu',1),(2,N'Inactive Roster Member',N'demo01@virginia.edu',0);
        CREATE TABLE academiconline.DepartmentRosterPublication(DepartmentId int,DepartmentRosterMemberId int,PublicationKey nvarchar(64),IsCurrent bit,CurrentSnapshotRunId uniqueidentifier);
        INSERT academiconline.DepartmentRosterPublication VALUES
        (2,1,N'roster-current',1,'11111111-1111-1111-1111-111111111111'),
        (2,1,N'roster-stale',1,'22222222-2222-2222-2222-222222222222'),
        (2,2,N'roster-inactive',1,'11111111-1111-1111-1111-111111111111');
        INSERT academiconline.TestPublication VALUES
        (2,N'roster-current',N'10.5555/roster-current',N'Current roster title',2025),
        (2,N'roster-stale',N'10.5555/roster-stale',N'Stale roster title',2024),
        (2,N'roster-inactive',N'10.5555/roster-inactive',N'Inactive roster title',2025);
        """);

    private static async Task<string> FingerprintAsync()
    {
        var tables = new[] { "academiconline.RebuildImportedFaculty", "academiconline.RebuildImportedFacultyDoi", "openalex.Faculty", "openalex.FacultyPublicationCandidate", "openalex.FacultyPublicationDecision", "academiconline.Scholar", "academiconline.Department", "academiconline.DepartmentScholarPublication", "academiconline.DepartmentRosterMember", "academiconline.DepartmentRosterPublication", "academiconline.TestPublication" };
        var fingerprints = new List<string>();
        await using var connection = new SqlConnection(_admin); await connection.OpenAsync();
        foreach (var table in tables)
        {
            using var command = new SqlCommand("SELECT COUNT_BIG(*),CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM " + table + ";", connection);
            await using var reader = await command.ExecuteReaderAsync(); await reader.ReadAsync();
            fingerprints.Add(table + ":" + reader.GetInt64(0) + ":" + (reader.IsDBNull(1) ? "NULL" : reader.GetInt32(1).ToString()));
        }
        return string.Join("|", fingerprints);
    }

    private static async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(_admin); await connection.OpenAsync();
        using var command = new SqlCommand(sql, connection) { CommandTimeout = 30 }; await command.ExecuteNonQueryAsync();
    }
    private static Task<DatabaseReadResult> ReadAsync(string id) => DatabaseReader.ReadAsync(_reader, id, CancellationToken.None);
    private static SourceCheck Source(DatabaseReadResult read, string source) => read.Snapshot.SourceChecks.Single(check => check.Source == source);
    private static void AssertComplete(DatabaseReadResult read)
    {
        True(read.Snapshot.Complete, "Expected complete available-source read; failures: " + string.Join(" | ", read.Snapshot.SourceChecks.Where(check => check.Status == "Failed").Select(check => check.Source + ": " + check.Error)));
        Equal(DatabaseName, read.Snapshot.DatabaseName); Equal("CompleteOneFacultyRead", read.Snapshot.EndMarker);
    }
    private static void AssertSourceTotals(DatabaseReadResult read)
    {
        Equal(read.PublicationRows.Length, read.Snapshot.DoiRows);
        Equal(read.Rows.Length, read.Snapshot.SourceChecks.Sum(check => check.RowsRead));
        Equal(read.PublicationRows.Length, read.Snapshot.SourceChecks.Sum(check => check.PublicationRows));
        True(read.Rows.All(row => row.UserId == read.Faculty.UserId), "A returned source row belongs to another ComputingID.");
    }
    private static async Task CheckAsync(string name, Func<Task> action)
    { try { await action(); _passed++; Console.WriteLine("PASS SQL " + name); } catch (Exception ex) { throw new InvalidOperationException(name + ": " + ex.Message, ex); } }
    private static void True(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException("Expected " + expected + ", got " + actual + "."); }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name + "."); }
}
