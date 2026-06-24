# V3E PATCH008 - Identity Review Constraints for Ambiguous Faculty Records

## Purpose

PATCH008 adds infrastructure for stricter identity review when a specific faculty record is known to be ambiguous.

Example situations:

```text
Two active faculty records have the same full name.
Two active faculty records have the same first initial and last name.
Two similar records are in the same department.
A reviewer knows a specific FacultyId needs stronger evidence before identity acceptance.
```

## Design decision

PATCH008 uses explicit reviewer/admin constraints instead of automatically inferring risk from a person's name.

That means the system does not classify a faculty member based on ethnicity, nationality, or cultural background. It only applies stricter behavior when a reviewer/admin marks a specific FacultyId.

## New table

```text
openalex.FacultyIdentityReviewConstraint
```

Important columns:

```text
FacultyId
RequiresVerifiedIdentifierForLikelyIdentity
RequiresManualReviewForInitialOnlyName
RequiresAuthorSpecificAffiliationEvidence
RequiresKnownCoauthorOrTopicEvidence
ConstraintReason
IsActive
```

## Setup

Run:

```sql
sql/270_V3E_PATCH008_FacultyIdentityReviewConstraint_SQL2016.sql
```

## Diagnostics

Run:

```sql
sql/271_V3E_PATCH008_FacultyIdentityReviewConstraint_Diagnostics_SQL2016.sql
```

## How to mark a faculty record for stricter review

Example:

```sql
INSERT INTO openalex.FacultyIdentityReviewConstraint
(
    FacultyId,
    RequiresVerifiedIdentifierForLikelyIdentity,
    RequiresManualReviewForInitialOnlyName,
    RequiresAuthorSpecificAffiliationEvidence,
    RequiresKnownCoauthorOrTopicEvidence,
    ConstraintReason,
    IsActive,
    CreatedAtUtc,
    UpdatedAtUtc
)
VALUES
(
    12345,
    1,
    1,
    1,
    1,
    N'Known ambiguous faculty record. Require stronger evidence before likely identity classification.',
    1,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);
```

Replace `12345` with the real `FacultyId`.

## What PATCH008 currently does

PATCH008 currently adds the database infrastructure and diagnostics.

It does not yet enforce these constraints inside the worker. That enforcement should be a smaller follow-up patch once the table is validated.

## Recommended follow-up patch

```text
V3E PATCH008A - Enforce Identity Review Constraints in Summary Triage
```

PATCH008A should:

```text
read active FacultyIdentityReviewConstraint rows
include constraint values in FacultyIdentityCandidateSummarySource
prevent LikelyIdentity when a constraint requires verified evidence and the candidate lacks it
keep initial-only candidates at NeedsManualReview when that constraint is active
include the constraint reason in SummaryText and EvidenceJson
```

## Why split this patch?

The safer path is:

```text
1. Add the table and diagnostics.
2. Let you mark known ambiguous FacultyId values.
3. Verify the rows look correct.
4. Then enforce the constraints in worker scoring.
```
