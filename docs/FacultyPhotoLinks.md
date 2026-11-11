# Faculty photo links

The Faculty directory, known-faculty DOI results, and loaded PubMed faculty context now include **View UVA photo**. Opening the link reads the exact local faculty identity and checks an official UVA image URL. An available image opens in a new tab. Missing links and unavailable photo sources have separate messages; neither claims that the person has no photo.

No database script is needed. This feature reads the existing optional `openalex.FacultyRoster.DirectoryPersonId` column and makes no database writes, image uploads, or publication changes. Directory and publication pages do not make photo requests themselves. The external check occurs only when a photo link is opened.

## Link sources and coverage

- `FacultyPhotos:VerifiedLinks` contains public photo/profile pairs checked against an official UVA profile, keyed by ComputingID. This supports departments outside the School of Medicine as well as imported-only faculty. The initial public entries are James Casanova (`jec9e`), Aakrosh Ratan (`ar7jq`), and Aaron Evans (`ae3f`), checked on November 11, 2026. Only public source links are included, not private roster exports.
- Otherwise, a roster member with an existing positive directory person ID is checked at the observed Research Faculty Directory image format: `https://apps.med.virginia.edu/ResearchFacultyDirectory/Exchange/Photos/Small/{ComputingID}_{DirectoryPersonId}.jpg`. This is a candidate URL until the server returns a successful image response. Filename conventions and availability can change; the feature does not claim that every directory ID has a photo.
- A name, an Academic Online association, and a native source key do not establish a photo link. Faculty without either a checked public mapping or usable roster directory ID receive the unavailable-link message. The existing imported roster need not be reloaded.
- Multiple conflicting native faculty IDs or directory person IDs stop photo selection. The service does not select between people by appearance.

Add another checked mapping to `FacultyPhotos:VerifiedLinks` in local configuration, using the normalized ComputingID as its key and both `PhotoUrl` and `ProfileUrl`. Verify that the official profile associates the photo with that ComputingID or its institutional email. Both URLs must use HTTPS on `virginia.edu` or its subdomains. Do not invent an image filename from the faculty name.

## Request handling

The authorized `/Faculty/Photo?UserId=...` Razor Page resolves the exact directory identity. The service checks HTTP HEAD first and falls back to GET response headers when HEAD is unsupported. It does not read or store the image body. A successful image content type produces a browser redirect; HTML responses and HTTP 404/410 do not. Authentication refusals, redirects, timeouts, and other provider failures produce a connection/check message rather than claiming a missing photo. Automatic server-side redirects are disabled. Requests have a ten-second cancellation budget. The existing ReaderPolicy applies, and browser navigation works without JavaScript.

Configured profile links remain available as a fallback when a provider check fails. Faculty without a configured profile link can return to the faculty directory. The source may require access from the user's local network; a successful browser view does not guarantee that a deployed application server has the same access.

## Local check

1. Start the updated application and sign in.
2. Load ComputingID `jec9e` on the Faculty, DOI Lookup, or PubMed Candidate Lookup page.
3. Click **View UVA photo**. The new tab should open the image linked by James Casanova's official UVA profile.
4. Repeat with `ar7jq` and `ae3f` to check a Medicine photo and a separate departmental photo source.
5. For another roster member, check the returned image against that person's official faculty profile. Some records will have no available link. A connection error should remain distinct from an unavailable link.

Synthetic service and production-Razor HTTP tests cover identity selection, authorized redirects, URL restrictions, missing-image and provider-error states, HEAD fallback, and cancellation. SQL integration tests cover the optional directory ID, its roster provenance, and existing schemas without the column at compatibility level 130. Live requests to UVA hosts are blocked by this implementation workspace's outbound policy; public page/image references were inspected through web retrieval, and application-server access must be checked locally.

## Public profile sources

- [James E. Casanova](https://med.virginia.edu/researchers/James-Casanova-P0000039)
- [Aakrosh Ratan](https://med.virginia.edu/researchers/Aakrosh-Ratan-P0066349)
- [Aaron Evans](https://astronomy.as.virginia.edu/aaron-evans)
