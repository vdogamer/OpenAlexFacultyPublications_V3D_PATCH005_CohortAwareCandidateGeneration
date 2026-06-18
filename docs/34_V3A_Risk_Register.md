# Version 3A Production Risk Register

| Risk | Impact | Mitigation |
|---|---:|---|
| Name-only matching accepts wrong author | High | Keep human review required; prioritize ORCID/OpenAlex ID |
| Shibboleth headers spoofed | High | IIS/Shibboleth must strip inbound spoofed headers |
| Worker runs too broad a load | High | Keep TestLoad limits; require approved schedule |
| Schema drift between V1 scripts and V3 code | Medium | Use preflight SQL and schema-safe operational readers |
| Missing faculty source integration | Medium | Treat `openalex.Faculty` as staging/mirror, not source of truth |
| Operational logs missing | Medium | Run V1O schema or disable DB logging only for local debugging |
| API rate/polite usage concerns | Medium | Configure real OpenAlex mailto and small load limits |
