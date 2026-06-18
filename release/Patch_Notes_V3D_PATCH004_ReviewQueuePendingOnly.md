# Patch Notes — V3D PATCH004 Review Queue Pending-Only Fix

## Patched Files

```text
src/OpenAlex.Data/ReviewQueueRepository.cs
src/OpenAlex.Web/Pages/Review/Authors.cshtml
src/OpenAlex.Web/Pages/Review/Publications.cshtml
sql/198_ReviewQueue_AfterAccept_Diagnostic.sql
docs/48_V3D_PATCH004_ReviewQueuePendingOnly.md
```

## Result

Accepted/rejected author and publication candidates no longer remain visible in the pending review queues.
