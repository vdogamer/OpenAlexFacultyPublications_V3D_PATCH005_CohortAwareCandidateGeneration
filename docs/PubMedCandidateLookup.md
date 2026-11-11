# Read-only PubMed candidate lookup

The page `/Reports/PubMedCandidates` helps a reader review possible publications for a faculty member, including faculty without Academic Online records or stored DOIs. It reads the local faculty directory and NCBI PubMed; it does not insert publications, record acceptance decisions, change DOI totals, or enroll faculty in collection jobs. There is no database migration or bulk import for this feature.

## Run and use

1. Start the existing web application with its usual SQL connection and sign in with Reader, Reviewer, or Admin access.
2. Choose **Publications → PubMed candidates**, or use the PubMed link on a known faculty member's DOI results.
3. Enter a ComputingID or its `@virginia.edu` address and click **Load faculty**. This reads the local directory only. An exact identifier, rather than a name or source-key guess, selects the faculty context.
4. Review the displayed identity and edit the suggested author terms. The suggested surname/initials forms are search hypotheses, especially for compound surnames, suffixes, or aliases. One to five variants, separated by semicolons, are allowed. Quoted author terms are exact PubMed author searches; add byline/initial variants when needed.
5. Alternatively choose **ORCID** and enter an identifier confirmed against a reliable faculty profile, or choose **Known PMIDs / PMCIDs** and supply up to ten identifiers. Stored ORCIDs are hints and are not automatically treated as confirmed identities.
6. Leave affiliation blank to include prior institutions. Optional affiliation and publication-year filters apply to name/ORCID searches. They do not hide citations in known-identifier lookup. Select a maximum of 10, 25, or 50 candidates and click **Search PubMed**.
7. Inspect each candidate's linked record and contributor evidence. Name text, ORCID matches, and affiliations support manual review; none automatically confirms authorship. Affiliations stay beside the contributor who supplied them. Editors, group authors, and investigators/collaborators have separate role labels. The page shows at most 20 contributors per citation, ranking matching evidence first and linking to the complete record.

PMIDs identify PubMed citations; PMCIDs identify PMC full-text records. Neither identifies a faculty member. A PMCID search only returns citations available in PubMed; the supplied PMC link remains available when there is no corresponding PubMed result. Citations without DOIs remain visible. Distinct PMIDs remain separate even if DOI values coincide.

The page distinguishes an unknown local ComputingID, a successful search with no returned citations, and a failed lookup. None establishes that a faculty member has never published. The displayed count is a bounded PubMed query result, not a faculty career-publication total. PubMed coverage and older author/affiliation/ORCID metadata are incomplete. An affiliation filter can match a coauthor; review the specific contributor's affiliation.

## Configuration and operating limits

- The application uses `PubMed:Email` when supplied, otherwise the existing `OpenAlex:MailTo` contact address. NCBI requires an application contact and tool identifier. A missing/invalid address produces a feature-specific configuration message when searching; it does not prevent the rest of the application from starting. A dedicated override can be set through the environment variable `PubMed__Email`.
- Only the fixed HTTPS E-utilities endpoint is queried: ESearch for PMIDs and EFetch for citation XML. A search does not send the local ComputingID; its author/ORCID/identifier query and the application's contact address go to NCBI.
- A singleton gate spaces outgoing dispatches by at least 400 ms within one web process. Multiple application instances or other tools sharing an IP require coordinated limiting to stay within NCBI's aggregate unauthenticated limit of three requests per second. No API key is required by this implementation.
- Each lookup has a 30-second cancellation budget, each HTTP request a 20-second timeout, and each response an 8 MB maximum. A request is not automatically retried. Rate limits, malformed responses, missing citations, and provider failures have explicit messages/notices instead of being silently treated as no publications.
- Results are transient. Refreshing or sharing a search URL performs another lookup. There is no automatic persistence or acceptance action. Forms work with ordinary HTTP GET requests and do not require JavaScript.
- Existing ReaderPolicy access applies. Razor encodes provider text. Citation/identifier URLs use fixed provider hosts; XML external entities are disabled. PubMed HttpClient informational URI logging is suppressed to avoid routinely recording search/contact parameters.

## Implementation and verification

`PubMedCandidates.cshtml.cs` binds GET search inputs, loads the local faculty context, and selects the page state. `PubMedQueryBuilder` validates fields and creates the visible PubMed query. `PubMedClient` performs bounded NCBI calls, `PubMedXmlParser` extracts citation/contributor metadata, and `PubMedCandidateLookupService` annotates evidence for review. The Razor page renders those values conditionally. `IFacultyIdentityLookup` exposes an exact, read-only directory query that works with roster, native, and optional rebuilt-import records.

Synthetic tests cover query/ORCID validation, API response parsing and failures, PMID-only records, contributor affiliations and roles, authorization, HTML encoding, initial/empty/error page states, and exact SQL identity selection. Existing responsive navigation checks cover the new menu entry and the no-JavaScript fallback. SQL integration tests use disposable databases at compatibility level 130.

Live local smoke check: load a known faculty ID, review author variants, and search. Confirm that the query link opens the same PubMed search and that a known PMID/PMCID returns the expected citation. Then check an unlikely query and confirm the explicit empty-result message. The implementation workspace could not make live NCBI API requests because its outbound policy blocks that host; mocked provider tests do not substitute for this live check. No local faculty data is included in tests or committed to the repository.

## Primary references

- [PubMed Help: author searches, author identifiers, affiliations, dates, PMIDs and PMCIDs](https://pubmed.ncbi.nlm.nih.gov/help/)
- [NCBI E-utilities introduction: usage policies, batching, contact and request limits](https://www.ncbi.nlm.nih.gov/books/NBK25497/)
- [E-utilities reference: ESearch and EFetch](https://www.ncbi.nlm.nih.gov/books/NBK25499/)
- [NCBI policies and disclaimers](https://www.ncbi.nlm.nih.gov/home/about/policies/)
