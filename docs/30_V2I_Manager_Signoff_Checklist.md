# Version 2I Manager Signoff Checklist

## Demo Acceptance

- [ ] The app shows faculty.
- [ ] The app ingests OpenAlex institutions.
- [ ] The app ingests OpenAlex authors.
- [ ] The app ingests OpenAlex works/publications.
- [ ] The app generates faculty-author candidates.
- [ ] The app lets reviewers accept/reject author candidates.
- [ ] The app generates faculty-publication candidates.
- [ ] The app lets reviewers accept/reject publication candidates.
- [ ] The accepted publications report works.
- [ ] CSV export works.
- [ ] Operational dashboard works.
- [ ] Auth/roles work for Reader, Reviewer, Admin.
- [ ] Deployment checklist is clear.

## Production Caveats

- [ ] Internal faculty system must remain source of truth.
- [ ] Shibboleth headers must be trusted only behind IIS/Shibboleth.
- [ ] Worker jobs should run with approved limits/schedules.
- [ ] Name-only matching must remain human-reviewed.
- [ ] Database backups/rollback plan must exist before production loads.
