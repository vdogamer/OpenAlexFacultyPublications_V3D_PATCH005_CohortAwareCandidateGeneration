# UVA theme and navigation

The shared layout groups the existing application destinations into Faculty, Publications, Review, Operations, and Administration. Dashboard and Faculty DOI Lookup remain directly accessible. Native disclosure menus support keyboard use even without JavaScript; the script adds mobile collapse, one-open-menu behavior, Escape, outside-click closing, and viewport resets.

| Menu | Pages |
| --- | --- |
| Faculty | Directory, My publications when signed in, Add faculty |
| Publications | UVA lookup, accepted, shared, department publications, data provenance |
| Review | Identity, author, publication review |
| Operations | Overview, run history, execution windows, scheduler, jobs |
| Administration | Department catalog, preflight, deployment, roles, users |

The palette uses [UVA institutional colors](https://brand.virginia.edu/design-assets/colors): navy `#232D4B` and orange `#E57200`, with the official pale tints. White text on navy and dark text on orange follow the [accessible combinations](https://brand.virginia.edu/design-assets/colors/accessible-type-color-combinations). Existing success and error status colors retain their meaning.

This branch builds on the imported-roster page branch. It changes the shared layout, CSS, navigation JavaScript, dashboard wording, and frontend checks. Database schema, faculty matching, publication queries, collection years, page handlers, and authorization policies are unchanged.

## Verification

The .NET CI job builds the solution, compiles Razor, and runs the existing service and HTTP tests. The separate browser job uses Playwright 1.62.1 to check widths from 320px to 1920px, preservation of the original menu routes, native keyboard controls, outside-click closing, signed-out navigation, and operation with JavaScript disabled. It renders a fixture from the actual layout and frontend assets; it does not connect to SQL or replace live application testing. CI artifacts contain preview screenshots and a result JSON.

For local browser checks, install the pinned development dependency without creating a lock file (`npm install --no-save --package-lock=false playwright@1.62.1`), install Chromium (`npx playwright install chromium`), and run `node tests/ui/check-navigation.mjs` from the repository root. Set `UI_CHECK_OUTPUT` to choose the output folder.

Review this branch after the preceding roster changes. Rebuild and restart the local web app using its existing launch configuration to see the new header and colors. Stylesheet and navigation-script URLs include a content version so browsers refresh the updated assets.
