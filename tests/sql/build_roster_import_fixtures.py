"""Synthetic-only SQL Server integration fixtures. Never reads user exports."""
from pathlib import Path
import csv
import subprocess
import sys

repo=Path(__file__).resolve().parents[2]
out=Path(sys.argv[1]).resolve()
out.mkdir(parents=True,exist_ok=True)
run='11111111-1111-1111-1111-111111111111'
stamp='2026-01-01 00:00:00.000'
cache=[]
profiles=[]
for uid,name,email in [
    ('f001','Alex Example','f001@virginia.edu'),
    ('f002','Blair Example','alias.blair@virginia.edu'),
    ('f003','Casey Example',''),
    ('f004','Dana Example',''),
    ('f005',"Émery O'Neil",'f005'),
    ('service_test','User Test','')
]:
    pid=str(len(profiles)+1)
    first,last=name.split(' ',1)
    cache.append(dict(ExportRunId=run,CapturedAtUtc=stamp,CacheUserName=uid,NormalizedUserName=uid,
        FullName=name,FirstName=first,LastName=last,MBU='MD',FacultyType='Faculty',DeptCode='MD-TEST',
        OrgName='Synthetic department',AcademicRank='Professor',AcademicTitle='Professor',Title='Professor',
        RFD_ID=pid,DirectoryAccountStatus='Active'))
    profiles.append(dict(ExportRunId=run,CapturedAtUtc=stamp,PersonID=pid,NormalizedUserName=uid,Email=email))

def save(path,rows):
    with path.open('w',encoding='utf-8-sig',newline='') as f:
        writer=csv.DictWriter(f,fieldnames=list(rows[0]));writer.writeheader();writer.writerows(rows)

def build(name,rows):
    source=out/(name+'.csv');save(source,rows)
    target=out/name
    subprocess.run([sys.executable,str(repo/'scripts/build-som-roster-import.py'),
        '--roster',str(source),'--profiles',str(out/'profiles.csv'),'--output-dir',str(target),
        '--expected-candidates',str(len(rows)),'--expected-quarantined','1'],check=True,stdout=subprocess.DEVNULL)
    return (target/'Load_SOM_Faculty_Roster.sql').read_text(encoding='utf-8-sig')

save(out/'profiles.csv',profiles)
apply=build('valid',cache)
(out/'apply.sql').write_text(apply,encoding='utf-8-sig')
assert apply.count('INSERT #SomRosterContext VALUES (0,')==1
(out/'preview.sql').write_text(apply.replace('INSERT #SomRosterContext VALUES (0,','INSERT #SomRosterContext VALUES (1,',1),encoding='utf-8-sig')
(out/'bad-fingerprint.sql').write_text(apply.replace("N'Alex Example'","N'Tampered Example'",1),encoding='utf-8-sig')
changed=[dict(r) for r in cache]
changed[0]['FullName']='Changed Example'
(out/'conflicting-source.sql').write_text(build('conflict',changed),encoding='utf-8-sig')
additional=dict(cache[4],CacheUserName='f006',NormalizedUserName='f006',FullName='Finley Example',FirstName='Finley',RFD_ID='')
(out/'constraint-failure.sql').write_text(build('constraint',cache+[additional]),encoding='utf-8-sig')

(out/'setup.sql').write_text('''USE master;
GO
CREATE DATABASE OpenAlexIngestion_Test;
GO
ALTER DATABASE OpenAlexIngestion_Test SET COMPATIBILITY_LEVEL=130;
GO
USE OpenAlexIngestion_Test;
GO
CREATE SCHEMA openalex;
GO
CREATE TABLE openalex.Faculty(FacultyId bigint PRIMARY KEY,DisplayName nvarchar(512),PrimaryEmail nvarchar(512),SourceFacultyKey nvarchar(512));
INSERT openalex.Faculty VALUES
(101,N'Alex Example',N'f001@virginia.edu',N'f001'),
(102,N'Blair Example',N'alias.blair@virginia.edu',N'manual-blair'),
(103,N'Casey Example',NULL,N'manual-casey'),
(104,N'Dana Example',N'f004@virginia.edu',N'manual-dana1'),
(105,N'Dana Other',N'f004@virginia.edu',N'manual-dana2');
CREATE TABLE openalex.FacultyPublicationDecision(FacultyId bigint,WorkDoi nvarchar(128),DecisionStatus nvarchar(32));
INSERT openalex.FacultyPublicationDecision VALUES(101,N'10.9999/synthetic',N'Accepted');
CREATE TABLE dbo.RosterTestProof(ProofHash int);
''',encoding='utf-8')
(out/'assert-preview.sql').write_text('''USE OpenAlexIngestion_Test;
IF OBJECT_ID(N'openalex.FacultyRoster') IS NOT NULL THROW 51501,'Preview created a permanent roster.',1;
IF OBJECT_ID(N'openalex.FacultyRosterImportRun') IS NOT NULL THROW 51502,'Preview created an audit table.',1;
IF (SELECT COUNT(*) FROM openalex.Faculty)<>5 THROW 51503,'Preview changed native faculty.',1;
PRINT 'Preview passed';
''',encoding='utf-8')
(out/'assert-first.sql').write_text('''USE OpenAlexIngestion_Test;
IF (SELECT COUNT(*) FROM openalex.FacultyRoster)<>5 THROW 51504,'Wrong roster count.',1;
IF (SELECT COUNT(*) FROM openalex.FacultyRoster WHERE NativeLinkStatus=N'ExactIdentifier')<>2 THROW 51505,'Exact link count incorrect.',1;
IF (SELECT COUNT(*) FROM openalex.FacultyRoster WHERE NativeLinkStatus=N'NeedsReview')<>2 THROW 51506,'Review count incorrect.',1;
IF (SELECT COUNT(*) FROM openalex.FacultyRoster WHERE NativeLinkStatus=N'NotLinked')<>1 THROW 51507,'Unlinked count incorrect.',1;
IF EXISTS(SELECT 1 FROM openalex.FacultyRoster WHERE ComputingId=N'service_test') THROW 51508,'Test account became faculty.',1;
IF (SELECT COUNT(*) FROM openalex.FacultyRosterImportRow WHERE Disposition=N'Quarantine')<>1 THROW 51509,'Quarantine missing.',1;
IF EXISTS(SELECT 1 FROM openalex.FacultyRoster WHERE ComputingId=N'f005' AND DirectoryEmail IS NOT NULL) THROW 51510,'An email was invented.',1;
IF EXISTS(SELECT 1 FROM openalex.FacultyRoster WHERE ComputingId=N'f003' AND NativeFacultyId IS NOT NULL) THROW 51511,'Name-only link accepted.',1;
IF EXISTS(SELECT 1 FROM openalex.FacultyRoster WHERE ComputingId=N'f004' AND NativeFacultyId IS NOT NULL) THROW 51512,'Ambiguous link accepted.',1;
INSERT dbo.RosterTestProof SELECT CHECKSUM_AGG(BINARY_CHECKSUM(ComputingId,DisplayName,SourceRecordHash,CreatedAtUtc)) FROM openalex.FacultyRoster;
PRINT 'First import passed';
''',encoding='utf-8')
(out/'assert-final.sql').write_text('''USE OpenAlexIngestion_Test;
IF (SELECT COUNT(*) FROM openalex.FacultyRoster)<>5 THROW 51513,'Roster changed after rerun/failure.',1;
IF (SELECT COUNT(*) FROM openalex.FacultyRosterImportRun)<>2 THROW 51514,'Unexpected committed audit runs.',1;
IF (SELECT COUNT(*) FROM openalex.FacultyRosterImportRow)<>12 THROW 51515,'Audit membership is incorrect.',1;
IF (SELECT CHECKSUM_AGG(BINARY_CHECKSUM(ComputingId,DisplayName,SourceRecordHash,CreatedAtUtc)) FROM openalex.FacultyRoster)<>(SELECT ProofHash FROM dbo.RosterTestProof) THROW 51516,'Existing roster records changed.',1;
IF (SELECT COUNT(*) FROM openalex.Faculty)<>5 THROW 51517,'Native faculty changed.',1;
IF (SELECT COUNT(*) FROM openalex.FacultyPublicationDecision WHERE FacultyId=101 AND WorkDoi=N'10.9999/synthetic' AND DecisionStatus=N'Accepted')<>1 THROW 51518,'Publication decisions changed.',1;
IF NOT EXISTS(SELECT 1 FROM openalex.FacultyRosterImportRun WHERE InsertedRows=0 AND ReusedRows=5) THROW 51519,'Rerun was not idempotent.',1;
PRINT 'All roster import integration assertions passed';
''',encoding='utf-8')
(out/'add-failing-constraint.sql').write_text("USE OpenAlexIngestion_Test; ALTER TABLE openalex.FacultyRoster ADD CONSTRAINT CK_RosterSyntheticReject CHECK(ComputingId<>N'f006');",encoding='utf-8')
print('Built synthetic fixtures in '+str(out))
