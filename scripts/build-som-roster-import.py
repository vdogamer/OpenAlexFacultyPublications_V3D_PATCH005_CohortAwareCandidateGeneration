"""Build a private, self-contained roster import from the supplied SQL exports.

Only the builder and generic SQL belong in git. Generated output contains private
directory data and must be kept outside the repository.
"""
import argparse
import csv
import hashlib
import json
from pathlib import Path
import re
import zipfile

FIELDS = [
    'SourceUserName','ComputingId','DisplayName','FirstName','LastName',
    'PrimarySchoolCode','FacultyClassification','DepartmentCode','OrganizationName',
    'AcademicRank','AppointmentTitle','RawDirectoryEmail','DirectoryEmail',
    'DirectoryPersonId','DirectoryAccountStatus','BaselineIdentityStatus',
    'Disposition','ValidationNote'
]

def clean(value):
    return '' if value is None or value.strip() in ('','NULL') else value.strip()

def read_csv(path):
    with path.open(encoding='utf-8-sig',newline='') as stream:
        rows=list(csv.DictReader(stream))
    if not rows or any(None in row for row in rows):
        raise ValueError('Empty or malformed CSV: '+str(path))
    return [{k:clean(v) for k,v in row.items()} for row in rows]

def digest(record):
    value='\x1f'.join('' if record[k] is None else str(record[k]) for k in FIELDS)
    return hashlib.sha256(value.encode('utf-16le')).hexdigest().upper()

def sql_literal(value):
    if value is None or value=='':return 'NULL'
    if isinstance(value,int):return str(value)
    return "N'"+str(value).replace("'","''")+"'"

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--roster',type=Path,required=True)
    parser.add_argument('--profiles',type=Path,required=True)
    parser.add_argument('--baseline',type=Path)
    parser.add_argument('--output-dir',type=Path,required=True)
    parser.add_argument('--expected-candidates',type=int,default=1570)
    parser.add_argument('--expected-quarantined',type=int,default=1)
    parser.add_argument('--source-server',default='Not recorded in CSV')
    args=parser.parse_args()
    repo=Path(__file__).resolve().parents[1]
    out=args.output_dir.resolve()
    if out==repo or repo in out.parents:
        raise ValueError('Generated directory data must be outside the git repository.')
    roster=read_csv(args.roster)
    profiles=read_csv(args.profiles)
    runs={r['ExportRunId'] for r in roster+profiles}
    if len(runs)!=1:raise ValueError('Roster and profile exports are not from the same run.')
    source_run=next(iter(runs))
    captured={r['CapturedAtUtc'] for r in roster+profiles}
    if len(captured)!=1:raise ValueError('Inconsistent export timestamps.')
    by_person={r['PersonID']:r for r in profiles}
    if len(by_person)!=len(profiles):raise ValueError('Duplicate directory PersonIDs.')
    usernames=[r['NormalizedUserName'].casefold() for r in roster]
    if len(set(usernames))!=len(usernames):raise ValueError('Duplicate management-cache usernames.')
    selected=[r for r in roster if r['MBU']=='MD' and r['FacultyType']=='Faculty']
    if len(selected)!=args.expected_candidates:raise ValueError('Unexpected MD/Faculty scope count.')
    baseline={}
    evidence_run=None
    if args.baseline:
        analysis=json.loads(args.baseline.read_text(encoding='utf-8'))
        if analysis['stats']['rfd_run']!=source_run:raise ValueError('The baseline uses a different roster export.')
        baseline={r['ComputingID']:r['ComparisonStatus'] for r in analysis['comparison']}
        if set(baseline)!={r['NormalizedUserName'].casefold() for r in selected}:
            raise ValueError('The baseline and selected roster do not contain the same usernames.')
        evidence_run=analysis['stats']['local_run']
    records=[]
    email_issues=[]
    for row in selected:
        uid=row['NormalizedUserName'].casefold()
        if uid!=row['CacheUserName'].strip().casefold():raise ValueError('Stored username normalization disagrees.')
        if not row['FullName']:raise ValueError('Missing faculty display name.')
        if any(ord(ch)<32 for value in row.values() for ch in value):raise ValueError('Unexpected control character in source data.')
        profile=by_person.get(row['RFD_ID']) if row['RFD_ID'] else None
        if row['RFD_ID'] and profile is None:raise ValueError('A directory PersonID could not be found.')
        if profile and profile['NormalizedUserName'].casefold()!=uid:raise ValueError('Directory person and cache usernames disagree.')
        raw_email=profile['Email'] if profile else ''
        email=raw_email.casefold()
        notes=[]
        if email and not re.fullmatch(r'[^\s@]+@[^\s@]+\.[^\s@]+',email):
            found=re.findall(r'[A-Za-z0-9.!#$%&\x27*+/=?^_`{|}~-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+',raw_email)
            if len(found)==1:
                email=found[0].casefold()
                notes.append('One email address extracted from annotated directory value; original retained.')
            else:
                email=''
                notes.append('Directory email is not a full address; original retained, cleaned email left null.')
            email_issues.append({'ComputingId':uid,'Name':row['FullName'],'Issue':notes[-1]})
        valid=bool(re.fullmatch(r'[a-z0-9]{2,32}',uid))
        explicit_test=(row['FirstName'].casefold()=='user' and row['LastName'].casefold()=='test')
        disposition='Import' if valid and not explicit_test else 'Quarantine'
        if disposition=='Quarantine':
            notes.append('Service/test account or nonstandard username; excluded from faculty master pending review.')
        record=dict(zip(FIELDS,[
            row['CacheUserName'],uid if disposition=='Import' else None,row['FullName'],
            row['FirstName'] or None,row['LastName'] or None,row['MBU'],row['FacultyType'],
            row['DeptCode'] or None,row['OrgName'] or None,row['AcademicRank'] or None,
            row['AcademicTitle'] or row['Title'] or None,raw_email or None,email or None,
            int(row['RFD_ID']) if row['RFD_ID'] else None,row['DirectoryAccountStatus'] or None,
            baseline.get(uid,'Not assessed'),disposition,' '.join(notes) or None
        ]))
        limits=[255,32,512,255,255,20,50,100,512,255,512,512,320,None,50,40,16,512]
        for key,limit in zip(FIELDS,limits):
            if limit and record[key] and len(str(record[key]))>limit:raise ValueError('Target field too short: '+key)
        record['SourceRecordHash']=digest(record)
        records.append(record)
    records.sort(key=lambda r:r['SourceUserName'].casefold())
    accepted=[r for r in records if r['Disposition']=='Import']
    quarantined=[r for r in records if r['Disposition']=='Quarantine']
    if len(quarantined)!=args.expected_quarantined:raise ValueError('Unexpected quarantine count; inspect source before building.')
    if len({r['ComputingId'] for r in accepted})!=len(accepted):raise ValueError('Duplicate accepted ComputingIDs.')
    payload=''.join(r['SourceUserName'].strip().casefold()+'|'+r['SourceRecordHash']+';' for r in records)
    payload_hash=hashlib.sha256(payload.encode('utf-16le')).hexdigest().upper()
    stage=(repo/'sql/453_SomFacultyRoster_Stage_SQL2016.sql').read_text()
    engine=(repo/'sql/454_ImportSomFacultyRoster_SQL2016.sql').read_text()
    context=[0,len(records),len(accepted),len(quarantined),args.source_server,'ResearchFacultyDirectory',source_run,next(iter(captured)),evidence_run]
    preamble='''/* PRIVATE ROSTER PAYLOAD - do not commit or publish this file.
Target: local OpenAlexIngestion_Test. Apply is authorized for this load.
All candidate source records are accounted for. Service/test records are retained
in the import audit, and are not inserted as faculty. Existing native faculty,
provider identities, publication data, cohorts and jobs are unchanged.
To preview only, change the first context value (PreviewOnly) from 0 to 1.
*/
USE [OpenAlexIngestion_Test];
GO
'''
    statements=[preamble,stage,'INSERT #SomRosterContext VALUES ('+', '.join(sql_literal(v) for v in context)+', 0x'+payload_hash+');\n']
    for start in range(0,len(records),500):
        rows=records[start:start+500]
        values=['('+', '.join(sql_literal(r[f]) for f in FIELDS)+', 0x'+r['SourceRecordHash']+')' for r in rows]
        statements.append('INSERT #SomRosterInput ('+', '.join(FIELDS)+', SourceRecordHash) VALUES\n'+',\n'.join(values)+';\n')
    statements.append(engine)
    out.mkdir(parents=True,exist_ok=True)
    sql_path=out/'Load_SOM_Faculty_Roster.sql'
    sql_path.write_text('\n'.join(statements),encoding='utf-8-sig')
    with (out/'Validated_Roster_Candidates.csv').open('w',encoding='utf-8-sig',newline='') as stream:
        writer=csv.DictWriter(stream,fieldnames=FIELDS+['SourceRecordHash'])
        writer.writeheader();writer.writerows(records)
    summary={
        'SourceExportRunId':source_run,'SourceCapturedAtUtc':next(iter(captured)),
        'CandidateRecords':len(records),'FacultyRecordsToLoad':len(accepted),'QuarantinedSourceRecords':len(quarantined),
        'Quarantine':[{'SourceUserName':r['SourceUserName'],'DisplayName':r['DisplayName'],'Reason':r['ValidationNote']} for r in quarantined],
        'EmailCleanups':email_issues,'CleanedDirectoryEmails':sum(bool(r['DirectoryEmail']) for r in accepted),
        'BaselineIdentityCounts':{status:sum(r['BaselineIdentityStatus']==status for r in accepted) for status in sorted({r['BaselineIdentityStatus'] for r in accepted})},
        'PayloadSHA256':payload_hash,
        'InputFileSHA256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (args.roster,args.profiles)},
        'Scope':'Primary-appointment cache group MBU=MD and FacultyType=Faculty; joint coverage and feed freshness unverified.'
    }
    (out/'Validation_Summary.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
    (out/'Verify_SOM_Faculty_Roster.sql').write_text((repo/'sql/455_VerifySomFacultyRoster_SQL2016.sql').read_text(),encoding='utf-8-sig')
    (out/'README.txt').write_text('''SOM FACULTY ROSTER LOAD

1. In SSMS, connect to your local SQL Server and select OpenAlexIngestion_Test.
2. Open Load_SOM_Faculty_Roster.sql and run the entire file.
   Its default mode validates and applies the load in one transaction.
   For an optional preview, change the first #SomRosterContext value from 0 to 1.
3. Confirm the final Outcome is Committed, with 1,569 verified roster rows and
   one quarantined source record. Retain the final result grid and Messages.
4. Run Verify_SOM_Faculty_Roster.sql to inspect the stored roster and exceptions.

The permanent roster is openalex.FacultyRoster. The service/test account is kept
in openalex.FacultyRosterImportRow, with Disposition=Quarantine.
The load is insert-only. Identical roster keys are reused on a rerun. A different
source fingerprint for an existing key stops the whole transaction for review.

This establishes the local roster, not publication-source coverage. Provider
matching remains separate. Existing openalex.Faculty records, provider data,
publication decisions, worker jobs and cohorts are not modified. The existing
Faculty app page continues to read its native faculty table; this import does
not deploy new application views or add every roster person to matching jobs.

The 1,570-row comparison population was provisional: validation found one
non-person User Test / service account, leaving 1,569 accepted faculty records.
Two annotated or incomplete profile email values are handled without inventing
an address. Their original values and cleanup notes are preserved.

Scope: primary-appointment cache group MBU=MD and FacultyType=Faculty. The source
export time is recorded. Feed freshness and full joint-appointment coverage have
not been certified. Directory account status is not employment verification.

These files contain private directory data. Keep them out of public repositories.
''',encoding='utf-8')
    archive=out.parent/'SOM_Faculty_Roster_Load.zip'
    with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as z:
        for p in sorted(out.iterdir()):
            if p.is_file():z.write(p,p.name)
    print(json.dumps(summary,indent=2))
    print('Private load package: '+str(archive))

if __name__=='__main__':main()
