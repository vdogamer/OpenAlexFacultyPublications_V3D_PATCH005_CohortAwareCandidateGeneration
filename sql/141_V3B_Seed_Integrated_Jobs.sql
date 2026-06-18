/* OpenAlex Version 3B — Seed Integrated Job Definitions */
SET NOCOUNT ON;

DECLARE @Jobs TABLE
(
    JobName NVARCHAR(200), JobDisplayName NVARCHAR(300), JobCategory NVARCHAR(100), JobDescription NVARCHAR(MAX),
    IsEnabled BIT, IsWriteJob BIT, RequiresConfirmation BIT, DefaultRunMode NVARCHAR(50), DefaultMaxPages INT NULL,
    DefaultMaxRecords INT NULL, SortOrder INT
);

INSERT INTO @Jobs VALUES
('InstitutionIngestionPreview','Institution Ingestion Preview','Preview','Calls OpenAlex institution endpoint and logs a preview without writing institution rows.',1,0,0,'SmokeTest',1,10,10),
('InstitutionIngestion','Institution Ingestion','Ingestion','Pulls configured institutions from OpenAlex and upserts institution tables.',1,1,1,'TestLoad',1,10,20),
('AuthorIngestion','Author Ingestion','Ingestion','Pulls authors for configured institutions and upserts author tables.',1,1,1,'TestLoad',1,100,30),
('WorkIngestion','Work / Publication Ingestion','Ingestion','Pulls works/publications for configured institutions and upserts work/authorship tables.',1,1,1,'TestLoad',1,100,40),
('FacultyAuthorCandidateGeneration','Faculty-Author Candidate Generation','Matching','Generates faculty-author match candidates from faculty and OpenAlex author data.',1,1,1,'TestLoad',NULL,500,50),
('FacultyPublicationCandidateGeneration','Faculty-Publication Candidate Generation','Matching','Generates publication candidates from accepted author decisions and work authorships.',1,1,1,'TestLoad',NULL,500,60),
('RunPipelineOnce','Run Full Pipeline Once','Orchestration','Runs the configured integrated pipeline sequence once. Use carefully with small limits.',1,1,1,'TestLoad',1,100,70);

MERGE openalex.JobDefinition AS target
USING @Jobs AS source
ON target.JobName = source.JobName
WHEN MATCHED THEN UPDATE SET
    JobDisplayName=source.JobDisplayName, JobCategory=source.JobCategory, JobDescription=source.JobDescription,
    IsEnabled=source.IsEnabled, IsWriteJob=source.IsWriteJob, RequiresConfirmation=source.RequiresConfirmation,
    DefaultRunMode=source.DefaultRunMode, DefaultMaxPages=source.DefaultMaxPages, DefaultMaxRecords=source.DefaultMaxRecords,
    SortOrder=source.SortOrder, UpdatedAtUtc=SYSUTCDATETIME()
WHEN NOT MATCHED BY TARGET THEN INSERT
    (JobName, JobDisplayName, JobCategory, JobDescription, IsEnabled, IsWriteJob, RequiresConfirmation, DefaultRunMode, DefaultMaxPages, DefaultMaxRecords, SortOrder, CreatedAtUtc, UpdatedAtUtc)
VALUES
    (source.JobName, source.JobDisplayName, source.JobCategory, source.JobDescription, source.IsEnabled, source.IsWriteJob, source.RequiresConfirmation, source.DefaultRunMode, source.DefaultMaxPages, source.DefaultMaxRecords, source.SortOrder, SYSUTCDATETIME(), SYSUTCDATETIME());

SELECT * FROM openalex.JobDefinition ORDER BY SortOrder, JobName;
