# V3E PATCH016A - Academic Online Comparison Diagnostics Fix

## Purpose

PATCH016A improves the Academic Online comparison diagnostics created in PATCH016.

The main fix is that `LocalOpenAlexOnly` rows are now restricted to faculty included in the selected Academic Online run. This prevents unrelated faculty, such as David Schiff, from appearing when the Academic Online run only pulled one different faculty member.

## Branch

```text
v3e-patch016a-academic-online-comparison-diagnostics
```

## Added file

```text
sql/352_V3E_PATCH016A_AcademicOnlineComparison_Diagnostics_SQL2016.sql
```

## What changed

The new diagnostic script adds clearer comparison categories:

```text
AcademicOnlineOnly_NoLocalWork
AcademicOnlineWorkExists_NoCandidate
AcademicOnlineCandidateExists_NotAccepted
AcademicOnlineAcceptedByUs
LocalOpenAlexOnly
```

It also keeps the match method separate:

```text
ExactOpenAlexIdMatch
ExactDoiMatch
LikelyTitleYearMatch
NoLocalMatch
```

## Why this matters

PATCH016 could tell us that Academic Online returned publications for a faculty member.

PATCH016A tells us what our system does or does not have for those same publication records:

```text
Does the OpenAlex work exist locally?
Did our app create a faculty-publication candidate?
Did our app accept that publication?
Is Academic Online showing publications our local OpenAlex ingest does not have?
Is our app showing local candidates Academic Online did not return?
```

## How to run

After running the PATCH016 AcademicOnlineComparison worker job, run:

```sql
sql/352_V3E_PATCH016A_AcademicOnlineComparison_Diagnostics_SQL2016.sql
```

## Expected output sections

```text
1) Selected Academic Online comparison run
2) Faculty included in this Academic Online run
3) Per-faculty Academic Online comparison summary
4) Academic Online publications compared to local Work, Candidate, and Accepted Decision
5) Comparison status counts
6) Local candidates not seen in Academic Online snapshot, restricted to this run faculty only
7) LocalOpenAlexOnly counts by faculty, restricted to this run faculty only
```
