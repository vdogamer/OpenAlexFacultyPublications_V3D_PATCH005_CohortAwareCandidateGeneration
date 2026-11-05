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
