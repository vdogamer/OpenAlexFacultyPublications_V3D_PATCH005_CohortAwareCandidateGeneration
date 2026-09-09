# V3E PATCH021L — Publication Data Quality Hardening and Repository Hygiene

## Starting point

PATCH021K produced the first complete Department of Neurosurgery snapshot:

- ScholarsDiscovered = 15
- ScholarsProcessed = 15
- IsCacheComplete = 1
- mixed Academic Online/OpenAlex roster publication retrieval validated

Two cleanup items remained visible:

1. some Academic Online publication rows use invalid numeric sentinel years such as -1;
2. the repository still tracks generated .NET bin/obj output and has no historical ignore policy, which has caused noisy Git status and Windows CI checkout failures on long generated paths.

## Publication data hardening

PATCH021L applies the same validity rule at three layers.

### Academic Online client

Numeric publication years are accepted only when they are between 1000 and 2100.

Invalid numeric sentinels such as -1 become PublicationYear = NULL.

YearText is preserved exactly as supplied by Academic Online so raw provenance is not lost.

Negative citation-count sentinels become NULL.

### Reporting-store repository

UpsertPublicationAsync applies the same defensive normalization before SQL persistence.

This protects the database even if a future provider or caller bypasses the Academic Online parser.

### Database boundary

sql/445 normalizes already-stored invalid values and adds trusted CHECK constraints:

- PublicationYear IS NULL OR PublicationYear BETWEEN 1000 AND 2100
- CitationCount IS NULL OR CitationCount >= 0

The migration does not overwrite YearText or RawJson.

## Validation

sql/446 verifies:

- zero remaining invalid numeric publication years;
- zero negative citation counts;
- both CHECK constraints exist, are enabled, and are trusted;
- the promoted Neurosurgery snapshot remains intact;
- the current reporting view contains no invalid numeric years;
- current provider attribution still reflects the promoted mixed-source snapshot;
- NULL publication-year rows are surfaced for review rather than hidden.

## Repository hygiene

PATCH021L adds a root .gitignore for generated .NET build output and common local artifacts.

The repository currently contains hundreds of already-tracked bin/obj files. .gitignore prevents new generated files from being added, but Git does not automatically untrack files already committed.

scripts/Prepare-RepositoryHygieneCleanup.ps1 provides a safe two-step cleanup:

Preview:

    .\scripts\Prepare-RepositoryHygieneCleanup.ps1

Apply to Git index:

    .\scripts\Prepare-RepositoryHygieneCleanup.ps1 -Apply

The apply mode uses git rm --cached, so local binaries remain on disk.

Review git status before committing the staged removals.

Removing these generated files from the repository should also eliminate the Windows CI checkout failure caused by long tracked obj paths.

## Deployment order

1. Pull PATCH021L.
2. Build the solution.
3. Stop the durable refresh service.
4. Run sql/445.
5. Run sql/446.
6. Deploy the updated worker/client binaries.
7. Restart the service.
8. Run one normal Neurosurgery refresh and rerun sql/446.
9. Preview repository hygiene cleanup.
10. Apply and commit the generated-file untracking only after reviewing the staged changes.

Repository cleanup is intentionally separate from the database migration transaction so a Git hygiene mistake cannot affect the validated reporting snapshot.
