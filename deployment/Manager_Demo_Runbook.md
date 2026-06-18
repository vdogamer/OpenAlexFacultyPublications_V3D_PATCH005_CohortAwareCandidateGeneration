# Version 2H Manager Demo Runbook

## Goal

Show that the integrated application now has a real end-to-end workflow:

1. Ingest OpenAlex data.
2. Generate candidates.
3. Review candidates.
4. Export accepted publications.
5. Protect review actions with roles.
6. Show operational/audit visibility.

## Demo Order

1. Login as `admin.demo`.
2. Open Dashboard.
3. Open `/Admin/Jobs`.
4. Explain worker jobs and pipeline.
5. Open Faculty.
6. Open Author Review.
7. Accept/reject one author candidate.
8. Run publication candidate generation if needed.
9. Open Publication Review.
10. Accept/reject one publication candidate.
11. Open Accepted Publications.
12. Download CSV.
13. Open Operations.
14. Open Admin Auth Events.
15. Explain Shibboleth header production mode.

## Core Message

This is no longer a collection of disconnected prototypes. Version 2H packages the integrated app, worker, SQL checks, deployment config, auth, review workflow, and manager demo path.
