# UVA frontend verification

Verified November 5, 2026 for PR #6.

| Check | Result |
| --- | --- |
| .NET Release build and Razor compilation | Passed |
| Existing .NET tests | Passed |
| Chromium navigation checks | Passed |
| Original menu destinations | All 24 retained |
| Viewport widths | 320, 390, 640, 768, 840, 841, 1024, 1366, 1920 pixels |
| Keyboard controls | Enter, Tab, and Escape passed |
| Menu dismissal | Outside click and one-open-menu behavior passed |
| Responsive menu | Open/close state and resize reset passed |
| Authentication display | Signed-out navigation passed |
| JavaScript disabled | Native disclosure navigation passed |
| Browser JavaScript errors | None |

[Build, tests, browser checks, and preview artifacts](https://github.com/vdogamer/OpenAlexFacultyPublications_V3D_PATCH005_CohortAwareCandidateGeneration/actions/runs/42065207946)

[Independent browser diagnostic result](https://github.com/vdogamer/OpenAlexFacultyPublications_V3D_PATCH005_CohortAwareCandidateGeneration/actions/runs/42065208074)

The `uva-navigation-previews` artifact contains desktop, expanded-menu, and mobile screenshots plus the browser-check summary. These screenshots use a static fixture generated from the actual layout, stylesheet, navigation script, and initial DOI lookup view. They do not show a live database query.

CI ran on the isolated validation branch. Git blob comparisons confirmed that the shared Razor layout, dashboard, CSS, JavaScript, and browser test in app commit `d0e8a41e63a1291654b58e9eaaefe0bb47fa7500` exactly match the tested files in validation commit `183e26d304776ca5838c81a45a77c136bddd3f85`. The validation branch has an additional push trigger and diagnostic workflow; those are not part of the app changes.

Primary text color contrast checks: white on UVA navy 13.56:1, dark text on UVA orange 6.06:1, UVA navy on pale orange 10.34:1, and muted text on the page background 5.81:1.

The app changes remain in the frontend branch, stacked on PR #5. The application on localhost:5000 has not been rebuilt or restarted by this change.
