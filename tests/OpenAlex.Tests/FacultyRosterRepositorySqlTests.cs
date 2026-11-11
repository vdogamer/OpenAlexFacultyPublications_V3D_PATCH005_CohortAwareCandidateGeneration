using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OpenAlex.Data;
using OpenAlex.Web.Services;

namespace OpenAlex.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENALEX_ROSTER_SQL_TEST_CONNECTION")))
            Skip = "Requires the disposable SQL Server configured by faculty-roster-sql CI.";
    }
}

public sealed class FacultyRosterRepositorySqlTests
{
    [SqlServerFact]
    public async Task DirectoryIncludesUnlinkedRosterDeduplicatesConfirmedLinksAndSearchesIdentifiers()
    {
        await using var db = await TestDatabase.CreateAsync();
        var directory = new FacultyDirectoryRepository(db.Factory);
        var all = await directory.GetPageAsync();
        Assert.Equal(3, all.RosterCount);
        Assert.Equal(2, all.AdditionalNativeCount);
        Assert.Equal(5, all.TotalCount);
        Assert.Single(all.Items.Where(r => r.FacultyId == 101));
        var rosterOnly = Assert.Single((await directory.GetPageAsync("R002")).Items);
        Assert.Equal("r002", rosterOnly.ComputingId);
        Assert.Null(rosterOnly.FacultyId);
        Assert.Null(rosterOnly.IsActive);
        Assert.Single((await directory.GetPageAsync("alias.one@uvahealth.org")).Items);
        Assert.Equal(3, (await directory.GetPageAsync("MD-TEST")).TotalCount);
        Assert.Empty((await directory.GetPageAsync("%_[]' OR 1=1--")).Items);
        Assert.Equal(0, (await directory.GetPageAsync("%", int.MaxValue)).TotalCount);
        Assert.Equal(1, (await directory.GetPageAsync("nobody", int.MaxValue)).PageNumber);
    }

    [SqlServerFact]
    public async Task DirectoryPaginationReachesFacultyBeyondTheFormerTwoHundredRowLimit()
    {
        await using var db = await TestDatabase.CreateAsync();
        var values = Enumerable.Range(1, 205).Select(i =>
            $"(N'p{i:0000}',N'Pagination faculty {i:0000}',N'MD-PAGE',N'Pagination department',N'No local match found')");
        await db.ExecuteAsync("INSERT openalex.FacultyRoster(ComputingId,DisplayName,DepartmentCode,OrganizationName,BaselineIdentityStatus) VALUES " + string.Join(",",values));
        var directory = new FacultyDirectoryRepository(db.Factory);
        var first = await directory.GetPageAsync("Pagination",1,100);
        var second = await directory.GetPageAsync("Pagination",2,100);
        var last = await directory.GetPageAsync("Pagination",3,100);
        Assert.Equal(205, first.TotalCount);
        Assert.Equal(3, first.PageCount);
        Assert.Equal(100, first.Items.Count);
        Assert.Equal(100, second.Items.Count);
        Assert.Equal(5, last.Items.Count);
        Assert.Equal(205, first.Items.Concat(second.Items).Concat(last.Items).Select(r => r.ComputingId).Distinct().Count());
        Assert.Contains(last.Items,r => r.ComputingId=="p0205");
        Assert.Equal(3, (await directory.GetPageAsync("Pagination",int.MaxValue,100)).PageNumber);
        Assert.Equal(1, (await directory.GetPageAsync("Pagination",-1,100)).PageNumber);
    }

    [SqlServerFact]
    public async Task DoiLookupUsesConfirmedNativeLinkAndRetainsOnlyCurrentPublicationResults()
    {
        await using var db = await TestDatabase.CreateAsync(publications:true);
        var service = new FacultyDoiLookupService(new FacultyDoiRepository(db.Factory));
        var result = await service.LookupAsync("R001@Virginia.edu");
        Assert.True(result.InImportedRoster);
        Assert.Equal(new[] { "Alex Roster" }, result.FacultyNames);
        Assert.Equal(new[] { "10.1234/roster", "10.1234/shared" }, result.Dois.Select(r => r.Doi));
        Assert.Equal(2, result.Dois.Single(r => r.Doi=="10.1234/shared").Sources.Count);
        var noPublications = await service.LookupAsync("r002");
        Assert.True(noPublications.FacultyFound);
        Assert.True(noPublications.InImportedRoster);
        Assert.Empty(noPublications.Dois); // A name-only native candidate owns a different accepted DOI.
        Assert.False((await service.LookupAsync("unknown99")).FacultyFound);
    }

    [SqlServerFact]
    public async Task RosterAloneWorksWhenNativeAndPublicationTablesAreAbsent()
    {
        await using var db = await TestDatabase.CreateAsync(native:false);
        var directory = await new FacultyDirectoryRepository(db.Factory).GetPageAsync("r003");
        Assert.Equal(3, directory.RosterCount);
        Assert.Equal(0, directory.AdditionalNativeCount);
        Assert.Single(directory.Items);
        var result = await new FacultyDoiLookupService(new FacultyDoiRepository(db.Factory)).LookupAsync("r003");
        Assert.True(result.FacultyFound);
        Assert.True(result.InImportedRoster);
        Assert.Empty(result.Dois);
    }

    [SqlServerFact]
    public async Task PreImportDatabaseKeepsNativeDirectoryAndExistingDoiLookup()
    {
        await using var db = await TestDatabase.CreateAsync(roster:false,publications:true);
        var directory = await new FacultyDirectoryRepository(db.Factory).GetPageAsync("r009");
        Assert.Equal(0, directory.RosterCount);
        Assert.Equal(3, directory.AdditionalNativeCount);
        Assert.Equal("r009", Assert.Single(directory.Items).ComputingId);
        var result = await new FacultyDoiLookupService(new FacultyDoiRepository(db.Factory)).LookupAsync("r009");
        Assert.False(result.InImportedRoster);
        Assert.Equal("10.1234/legacy", Assert.Single(result.Dois).Doi);
    }

    [SqlServerFact]
    public async Task DirectorySupportsNativeSchemaAliasesAndAnEmptyDatabase()
    {
        await using var db = await TestDatabase.CreateAsync(roster:false,native:false);
        var repository = new FacultyDirectoryRepository(db.Factory);
        Assert.Equal(0, (await repository.GetPageAsync()).TotalCount);
        await db.ExecuteAsync(@"CREATE TABLE openalex.Faculty(FacultyId bigint,FacultyDisplayName nvarchar(512),Email nvarchar(320),Department nvarchar(100),Title nvarchar(100));
            INSERT openalex.Faculty VALUES(1,N'Alias Faculty',N'alias7@virginia.edu',N'Alias department',N'Professor');");
        var row = Assert.Single((await repository.GetPageAsync("ALIAS7")).Items);
        Assert.Equal("Alias Faculty",row.DisplayName);
        Assert.Equal("alias7",row.ComputingId);
        Assert.Equal("Professor",row.AppointmentTitle);
    }

    [SqlServerFact]
    public async Task RebuildImportAddsOnlyUnrepresentedRowsAndRetainsAlternateNamesForSearch()
    {
        await using var db = await TestDatabase.CreateAsync(publications:true);
        await db.AddRebuildImportAsync();
        var repository = new FacultyDirectoryRepository(db.Factory);
        var all = await repository.GetPageAsync();
        Assert.Equal(7, all.TotalCount);
        Assert.Equal(3, all.RosterCount);
        Assert.Equal(2, all.AdditionalNativeCount);
        Assert.Equal(2, all.AdditionalImportedCount);
        var roster = Assert.Single(all.Items.Where(row => row.ComputingId == "r001"));
        Assert.Equal("Alex Roster", roster.DisplayName);
        Assert.True(roster.InRebuildImport);
        Assert.Equal("Alternate source Alex", roster.ImportedDisplayName);
        Assert.Equal("r001", Assert.Single((await repository.GetPageAsync("Alternate source Alex")).Items).ComputingId);
        Assert.Equal(3, (await repository.GetPageAsync("Alternate source department")).TotalCount);
        var legacy = Assert.Single(all.Items.Where(row => row.ComputingId == "r009"));
        Assert.Equal(103L, legacy.FacultyId);
        Assert.True(legacy.InRebuildImport);
        var imported = Assert.Single((await repository.GetPageAsync("i001")).Items);
        Assert.True(imported.InRebuildImport);
        Assert.False(imported.IsRosterMember);
        Assert.Null(imported.FacultyId);
        Assert.Null(imported.PrimaryEmail);
        Assert.Null(imported.IsActive);
        Assert.Equal("Imported source department", imported.DepartmentName);
        Assert.Equal(0, (await repository.GetPageAsync("%_[]' OR 1=1--")).TotalCount);
    }

    [SqlServerFact]
    public async Task RebuildImportSupportsImportOnlySchemaAndSearchBeforePagination()
    {
        await using var db = await TestDatabase.CreateAsync(roster:false,native:false);
        await db.AddRebuildImportAsync(seed:false);
        var values = Enumerable.Range(1,205).Select(i =>
            $"(N'i{i:0000}',N'Imported pagination {i:0000}',N'Imported department')");
        await db.ExecuteAsync("INSERT academiconline.RebuildImportedFaculty VALUES " + string.Join(",",values));
        var repository = new FacultyDirectoryRepository(db.Factory);
        var first = await repository.GetPageAsync("Imported pagination",1,100);
        var second = await repository.GetPageAsync("Imported pagination",2,100);
        var last = await repository.GetPageAsync("Imported pagination",3,100);
        Assert.Equal(205, first.TotalCount);
        Assert.Equal(205, first.AdditionalImportedCount);
        Assert.Equal(0, first.RosterCount);
        Assert.Equal(0, first.AdditionalNativeCount);
        Assert.Equal(100, first.Items.Count);
        Assert.Equal(100, second.Items.Count);
        Assert.Equal(5, last.Items.Count);
        Assert.Equal(205, first.Items.Concat(second.Items).Concat(last.Items).Select(row => row.ComputingId).Distinct().Count());
        Assert.Equal("i0205", Assert.Single((await repository.GetPageAsync("i0205")).Items).ComputingId);
        Assert.Equal(3, (await repository.GetPageAsync("Imported pagination",int.MaxValue)).PageNumber);
        await db.ExecuteAsync("INSERT academiconline.RebuildImportedFaculty VALUES(N'literal01',N'Literal %_[] marker',N'Literal department');");
        Assert.Equal("literal01", Assert.Single((await repository.GetPageAsync("%_[]")).Items).ComputingId);
    }

    [SqlServerFact]
    public async Task RebuildDoisMergeWithEligibleSourcesAndKeepUnknownMetadataAndZeroStates()
    {
        await using var db = await TestDatabase.CreateAsync(publications:true);
        await db.AddRebuildImportAsync();
        var service = new FacultyDoiLookupService(new FacultyDoiRepository(db.Factory));
        var merged = await service.LookupAsync("r001");
        Assert.True(merged.InImportedRoster);
        Assert.True(merged.InRebuildImport);
        Assert.Equal(new[] { "Alex Roster" }, merged.FacultyNames);
        Assert.Equal(new[] { "10.1234/roster", "10.1234/shared", "10.1234/imported" }, merged.Dois.Select(row => row.Doi));
        Assert.Equal(3, merged.Dois.Single(row => row.Doi == "10.1234/shared").Sources.Count);
        Assert.DoesNotContain(merged.Dois, row => row.Doi is "10.1234/stale" or "10.1234/inactive" or "10.1234/rejected");
        var imported = await service.LookupAsync("i001");
        Assert.True(imported.FacultyFound);
        Assert.True(imported.InRebuildImport);
        Assert.False(imported.InImportedRoster);
        Assert.Equal(2, imported.Dois.Count); // The shared DOI is still associated with each coauthor.
        Assert.All(imported.Dois, row =>
        {
            Assert.Null(row.Year);
            Assert.Equal("(Title not supplied)",row.Title);
            Assert.Equal("Academic Online import",Assert.Single(row.Sources));
        });
        var zero = await service.LookupAsync("i002");
        Assert.True(zero.FacultyFound);
        Assert.True(zero.InRebuildImport);
        Assert.Empty(zero.Dois);
        var rosterZero = await service.LookupAsync("r003");
        Assert.True(rosterZero.InImportedRoster);
        Assert.True(rosterZero.InRebuildImport);
        Assert.Empty(rosterZero.Dois);
        Assert.False((await service.LookupAsync("unknown99")).FacultyFound);
        var legacy = await service.LookupAsync("r009");
        var legacyDoi = Assert.Single(legacy.Dois);
        Assert.Equal("Legacy",legacyDoi.Title);
        Assert.Equal(2026,legacyDoi.Year);
        Assert.Equal(2,legacyDoi.Sources.Count);
    }

    [SqlServerFact]
    public async Task RebuildImportDoesNotMergeByNameOrAnUnconfirmedNativeSourceKey()
    {
        await using var db = await TestDatabase.CreateAsync(publications:true);
        await db.AddRebuildImportAsync();
        await db.ExecuteAsync(@"UPDATE openalex.Faculty SET SourceFacultyKey=N'i001' WHERE FacultyId=102;
            INSERT academiconline.RebuildImportedFaculty VALUES(N'i003',N'Casey Roster',N'Imported source department');");
        var result = await new FacultyDirectoryRepository(db.Factory).GetPageAsync();
        Assert.Equal(3,result.AdditionalImportedCount);
        Assert.Equal(3,result.Items.Count(row => row.DisplayName == "Casey Roster"));
        Assert.False(Assert.Single(result.Items.Where(row => row.FacultyId == 102)).InRebuildImport);
        Assert.Null(Assert.Single(result.Items.Where(row => row.FacultyId == 102)).ComputingId);
        Assert.Single(result.Items.Where(row => row.ComputingId == "i001"));
        Assert.Single(result.Items.Where(row => row.ComputingId == "i001" && row.FacultyId is null));
        var dois = await new FacultyDoiLookupService(new FacultyDoiRepository(db.Factory)).LookupAsync("i001");
        Assert.DoesNotContain(dois.Dois,row => row.Doi == "10.1234/name-only");
        Assert.All(dois.Dois,row => Assert.Equal("Academic Online import",Assert.Single(row.Sources)));
    }

    [SqlServerFact]
    public async Task RebuildImportDoesNotAttachCanonicalEmailToAContradictoryNativeComputingId()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.AddRebuildImportAsync();
        await db.ExecuteAsync("ALTER TABLE openalex.Faculty ADD ComputingId nvarchar(100);");
        await db.ExecuteAsync("UPDATE openalex.Faculty SET ComputingId=N'r777' WHERE FacultyId=103;");
        var result = await new FacultyDirectoryRepository(db.Factory).GetPageAsync();
        var native = Assert.Single(result.Items.Where(row => row.FacultyId == 103));
        Assert.Equal("r777",native.ComputingId);
        Assert.False(native.InRebuildImport);
        Assert.Null(Assert.Single(result.Items.Where(row => row.ComputingId == "r009")).FacultyId);
        Assert.Equal(3,result.AdditionalImportedCount);
    }

    [SqlServerFact]
    public async Task PartialRebuildSourceIsAnErrorInsteadOfAnUnknownFacultyResult()
    {
        await using var db = await TestDatabase.CreateAsync(roster:false,native:false);
        await db.AddRebuildImportAsync();
        await db.ExecuteAsync("DROP TABLE academiconline.RebuildImportedFacultyDoi;");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new FacultyDoiLookupService(new FacultyDoiRepository(db.Factory)).LookupAsync("i001"));
    }

    [SqlServerFact]
    public async Task ExactFacultyIdentityLookupIsNotALimitedSubstringSearchOrASourceKeyGuess()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.AddRebuildImportAsync(seed:false);
        var values = Enumerable.Range(1,205).Select(i =>
            $"(N'z{i:0000}',N'Name mentioning target01 {i:0000}',N'Fixture department')");
        await db.ExecuteAsync("INSERT academiconline.RebuildImportedFaculty VALUES " + string.Join(",",values)
            + ",(N'target01',N'Actual Target Faculty',N'Fixture department');");
        await db.ExecuteAsync("UPDATE openalex.Faculty SET SourceFacultyKey=N'target02' WHERE FacultyId=102;");
        var repository = new FacultyDirectoryRepository(db.Factory);
        var exact = Assert.Single(await repository.GetByComputingIdAsync("TARGET01@Virginia.edu"));
        Assert.Equal("Actual Target Faculty",exact.DisplayName);
        Assert.Equal("target01",exact.ComputingId);
        Assert.Empty(await repository.GetByComputingIdAsync("target02"));
        Assert.Equal(103L,Assert.Single(await repository.GetByComputingIdAsync("r009")).FacultyId);
        Assert.Equal("Alex Roster",Assert.Single(await repository.GetByComputingIdAsync("r001")).DisplayName);
        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetByComputingIdAsync("bad input!"));
    }

    [SqlServerFact]
    public async Task DirectoryPhotoPersonIdIsOptionalAndStaysWithItsRosterIdentity()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repository = new FacultyDirectoryRepository(db.Factory);
        Assert.Null(Assert.Single(await repository.GetByComputingIdAsync("r001")).DirectoryPersonId);
        await db.ExecuteAsync("ALTER TABLE openalex.FacultyRoster ADD DirectoryPersonId int NULL;");
        await db.ExecuteAsync("UPDATE openalex.FacultyRoster SET DirectoryPersonId=123 WHERE ComputingId=N'r001';");
        Assert.Equal(123,Assert.Single(await repository.GetByComputingIdAsync("r001")).DirectoryPersonId);
        Assert.Equal(123,Assert.Single((await repository.GetPageAsync("Alex Roster")).Items).DirectoryPersonId);
        Assert.Null(Assert.Single(await repository.GetByComputingIdAsync("r009")).DirectoryPersonId);
        await db.AddRebuildImportAsync();
        Assert.Null(Assert.Single(await repository.GetByComputingIdAsync("i001")).DirectoryPersonId);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string _master;
        private readonly string _name = "RosterUi_" + Guid.NewGuid().ToString("N");
        private string ConnectionString => new SqlConnectionStringBuilder(_master) { InitialCatalog=_name }.ConnectionString;
        public SqlConnectionFactory Factory => new(new ConfigurationBuilder().Build(),
            Options.Create(new DatabaseOptions { ConnectionString=ConnectionString }));

        private TestDatabase()
        {
            _master = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("OPENALEX_ROSTER_SQL_TEST_CONNECTION")!)
                { InitialCatalog="master" }.ConnectionString;
        }

        public static async Task<TestDatabase> CreateAsync(bool roster=true,bool native=true,bool publications=false)
        {
            var db = new TestDatabase();
            await using var connection = new SqlConnection(db._master);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText=$"CREATE DATABASE [{db._name}]; ALTER DATABASE [{db._name}] SET COMPATIBILITY_LEVEL=130;";
            await command.ExecuteNonQueryAsync();
            try
            {
                await db.ExecuteAsync("CREATE SCHEMA openalex;");
                if (native) await db.ExecuteAsync(@"
CREATE TABLE openalex.Faculty(FacultyId bigint PRIMARY KEY,DisplayName nvarchar(512),PrimaryEmail nvarchar(512),SourceFacultyKey nvarchar(512));
INSERT openalex.Faculty VALUES
(101,N'Alex Native',N'alias.one@uvahealth.org',N'manual-alex'),
(102,N'Casey Roster',NULL,N'manual-casey'),
(103,N'Legacy Faculty',N'r009@virginia.edu',N'r009');");
                if (roster) await db.ExecuteAsync(@"
CREATE TABLE openalex.FacultyRoster(ComputingId nvarchar(32) PRIMARY KEY,DisplayName nvarchar(512),
    DirectoryEmail nvarchar(320),DepartmentCode nvarchar(100),OrganizationName nvarchar(512),
    AppointmentTitle nvarchar(512),AcademicRank nvarchar(255),NativeFacultyId bigint,
    BaselineIdentityStatus nvarchar(40));
INSERT openalex.FacultyRoster VALUES
(N'r001',N'Alex Roster',N'alias.one@uvahealth.org',N'MD-TEST',N'Test department',N'Professor',N'Professor',101,N'Matched by identifier'),
(N'r002',N'Casey Roster',NULL,N'MD-TEST',N'Test department',NULL,N'Professor',NULL,N'Review name candidate'),
(N'r003',N'Unlinked Faculty',NULL,N'MD-TEST',N'Test department',NULL,NULL,NULL,N'No local match found');");
                if (publications) await db.AddPublicationsAsync();
                return db;
            }
            catch { await db.DisposeAsync(); throw; }
        }

        private async Task AddPublicationsAsync()
        {
            await ExecuteAsync(@"
CREATE TABLE openalex.FacultyPublicationCandidate(FacultyPublicationCandidateId bigint,WorkDoi nvarchar(512),WorkTitle nvarchar(512),PublicationYear int);
CREATE TABLE openalex.FacultyPublicationDecision(FacultyId bigint,FacultyPublicationCandidateId bigint,WorkDoi nvarchar(512),WorkTitle nvarchar(512),PublicationYear int,IsCurrent bit,DecisionStatus nvarchar(32));
INSERT openalex.FacultyPublicationCandidate VALUES(1,N'10.1234/shared',N'Current work',2025);
INSERT openalex.FacultyPublicationDecision VALUES
(101,1,NULL,NULL,NULL,1,N'Accepted'),(101,NULL,N'10.1234/old',N'Old',2023,0,N'Accepted'),
(101,NULL,N'10.1234/rejected',N'Rejected',2026,1,N'Rejected'),
(102,NULL,N'10.1234/name-only',N'Not linked by name',2026,1,N'Accepted'),
(103,NULL,N'10.1234/legacy',N'Legacy',2026,1,N'Accepted');");
            await ExecuteAsync("CREATE SCHEMA academiconline;");
            await ExecuteAsync(@"
CREATE TABLE academiconline.Scholar(AcademicOnlineAuthorId nvarchar(64),DisplayName nvarchar(512),UvaUserId nvarchar(32),Email nvarchar(320));
CREATE TABLE academiconline.Department(DepartmentId int,CurrentSnapshotRunId uniqueidentifier,SourceMode nvarchar(32));
CREATE TABLE academiconline.DepartmentScholarPublication(DepartmentId int,AcademicOnlineAuthorId nvarchar(64),PublicationKey nvarchar(64),IsCurrent bit,CurrentSnapshotRunId uniqueidentifier);
CREATE TABLE academiconline.DepartmentRosterMember(DepartmentRosterMemberId int,DisplayName nvarchar(512),ExpectedEmail nvarchar(320),IsActive bit);
CREATE TABLE academiconline.DepartmentRosterPublication(DepartmentId int,DepartmentRosterMemberId int,PublicationKey nvarchar(64),IsCurrent bit,CurrentSnapshotRunId uniqueidentifier);
CREATE TABLE academiconline.TestPublication(DepartmentId int,PublicationKey nvarchar(64),Doi nvarchar(512),Title nvarchar(512),PublicationYear int);
INSERT academiconline.Scholar VALUES(N's1',N'Alex Provider',N'r001',N'r001@virginia.edu');
INSERT academiconline.Department VALUES(1,'11111111-1111-1111-1111-111111111111',N'AcademicOnline'),(2,'11111111-1111-1111-1111-111111111111',N'FacultyRoster');
INSERT academiconline.DepartmentScholarPublication VALUES
(1,N's1',N'good',1,'11111111-1111-1111-1111-111111111111'),
(1,N's1',N'stale',1,'22222222-2222-2222-2222-222222222222'),
(1,N's1',N'inactive',0,'11111111-1111-1111-1111-111111111111');
INSERT academiconline.DepartmentRosterMember VALUES(1,N'Alex Roster Provider',N'r001@virginia.edu',1);
INSERT academiconline.DepartmentRosterPublication VALUES(2,1,N'roster',1,'11111111-1111-1111-1111-111111111111');
INSERT academiconline.TestPublication VALUES
(1,N'good',N'https://doi.org/10.1234/SHARED',N'Current work',2025),
(1,N'stale',N'10.1234/stale',N'Stale work',2026),
(1,N'inactive',N'10.1234/inactive',N'Inactive work',2026),
(2,N'roster',N'10.1234/roster',N'Roster publication',2026);");
            await ExecuteAsync("CREATE VIEW academiconline.vw_DepartmentNewestPublication AS SELECT * FROM academiconline.TestPublication;");
        }

        public async Task AddRebuildImportAsync(bool seed=true)
        {
            await ExecuteAsync("IF SCHEMA_ID(N'academiconline') IS NULL EXEC(N'CREATE SCHEMA academiconline;');");
            await ExecuteAsync(@"
CREATE TABLE academiconline.RebuildImportedFaculty(
    UserId nvarchar(32) COLLATE Latin1_General_100_CI_AS PRIMARY KEY,
    FacultyName nvarchar(200) NOT NULL,PrimaryDepartment nvarchar(200) NOT NULL);
CREATE TABLE academiconline.RebuildImportedFacultyDoi(
    UserId nvarchar(32) COLLATE Latin1_General_100_CI_AS NOT NULL,
    Doi nvarchar(400) COLLATE Latin1_General_100_BIN2 NOT NULL,
    PRIMARY KEY(UserId,Doi));");
            if (seed) await ExecuteAsync(@"
INSERT academiconline.RebuildImportedFaculty VALUES
(N'r001',N'Alternate source Alex',N'Alternate source department'),
(N'r003',N'Alternate source Unlinked',N'Alternate source department'),
(N'r009',N'Alternate source Legacy',N'Alternate source department'),
(N'i001',N'Imported Faculty',N'Imported source department'),
(N'i002',N'Imported Zero Faculty',N'Imported source department');
INSERT academiconline.RebuildImportedFacultyDoi VALUES
(N'r001',N'10.1234/shared'),(N'r001',N'10.1234/imported'),
(N'r009',N'10.1234/legacy'),(N'i001',N'10.1234/import-only'),(N'i001',N'10.1234/shared');");
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText=sql;
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(_master);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText=$"ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_name}];";
            await command.ExecuteNonQueryAsync();
        }
    }
}

