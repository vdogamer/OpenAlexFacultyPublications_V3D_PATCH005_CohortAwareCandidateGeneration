# V3E PATCH017A - Provenance Coverage Gaps and Status Consistency

## Purpose

PATCH017A expands the read-only provenance report created in PATCH017.

It answers two additional questions:

```text
Which Academic Online publications have not reached the same stage in the local UVA/OpenAlex workflow?
Do local publication candidate statuses agree with the current UVA publication decisions?
```

## Branch

```text
v3e-patch017a-provenance-coverage-gaps-status-consistency
```

## Safety boundary

PATCH017A does not modify:

```text
OpenAlex metadata
Academic Online snapshots
faculty-author identities
publication candidates
match scores
publication decisions
candidate statuses
```

The report and SQL diagnostic only read and classify existing records.

## Coverage statuses

```text
AcademicOnlineOnly_NoLocalWork
```

Academic Online contains the publication, but the corresponding OpenAlex work is not present locally.

```text
AcademicOnlineWorkExists_NoCandidate
```

The OpenAlex work exists locally, but there is no faculty-publication candidate for the same faculty member.

```text
AcademicOnlineCandidateExists_NotAccepted
```

A local faculty-publication candidate exists, but UVA does not have a current accepted publication decision.

```text
AcademicOnlineAcceptedByUs
```

Academic Online contains the publication and UVA has a current accepted decision for the same faculty and work.

## Status consistency rule

A candidate/decision mismatch is reported when:

```text
Current decision = Accepted
Candidate status is not AutoAcceptedCandidate, AcceptedCandidate, or Accepted
```

or:

```text
Current decision = Rejected
Candidate status is not AutoRejectedCandidate, RejectedCandidate, or Rejected
```

The current publication decision remains authoritative. PATCH017A only reports the mismatch.

## Added files

```text
src/OpenAlex.Data/Models/PublicationProvenanceCoverageRow.cs
src/OpenAlex.Data/PublicationProvenanceCoverageRepository.cs
sql/361_V3E_PATCH017A_ProvenanceCoverageGaps_StatusConsistency_Diagnostics_SQL2016.sql
```

## Modified files

```text
src/OpenAlex.Data/OpenAlexDataServiceCollectionExtensions.cs
src/OpenAlex.Web/Pages/Reports/DataProvenance.cshtml.cs
src/OpenAlex.Web/Pages/Reports/DataProvenance.cshtml
```

## Local validation

```powershell
git fetch
git checkout v3e-patch017a-provenance-coverage-gaps-status-consistency
git pull
dotnet clean OpenAlexFacultyPublications.sln
dotnet build OpenAlexFacultyPublications.sln
```

Run the diagnostic:

```sql
sql/361_V3E_PATCH017A_ProvenanceCoverageGaps_StatusConsistency_Diagnostics_SQL2016.sql
```

Start the web application:

```powershell
dotnet run --project src/OpenAlex.Web/OpenAlex.Web.csproj
```

Open:

```text
/Reports/DataProvenance
```

## Expected page additions

```text
Academic Online coverage rows
Academic Online coverage gaps
Academic Online only—no local work
Accepted by both
Candidate/decision mismatches
Coverage status filter
Status consistency filter
Academic Online coverage comparison table
Candidate/decision mismatch warning badges
```
