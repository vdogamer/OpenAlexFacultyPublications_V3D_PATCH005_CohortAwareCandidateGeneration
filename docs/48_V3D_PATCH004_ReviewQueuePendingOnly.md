# V3D PATCH004 — Review Queue Pending-Only Fix

## Problem

On `/Review/Authors`, clicking **Accept** refreshed the page but the same candidates stayed visible.

## Cause

The Accept action was writing a decision and updating candidate status, but the author review queue query returned all candidates. It did not filter out already accepted/rejected candidates.

## Fix

PATCH004 changes the review queue behavior:

- `/Review/Authors` now shows only candidates that are still pending review.
- Accepted/rejected candidates are hidden from the queue.
- `/Review/Publications` gets the same pending-only behavior.
- Empty-state messages were added to both pages.
- Diagnostic SQL was added.

## Diagnostic

Run:

```sql
:r sql/198_ReviewQueue_AfterAccept_Diagnostic.sql
```

## Expected behavior

After clicking **Accept** or **Reject**, the candidate should disappear from `/Review/Authors`.
