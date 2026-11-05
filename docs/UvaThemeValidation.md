# UVA frontend validation

This validation branch runs the same Razor layout, dashboard, CSS, JavaScript and browser fixture as PR #6. Its extra workflow push trigger allows the checks to run independently of the stacked pull request's base-branch filters. No application code differs from PR #6 at commit 151f5e05008498ca0ad12129b08801938b92b64c.

The workflow builds the .NET solution, runs existing tests, and exercises the frontend at desktop and mobile widths. Browser preview screenshots and the result summary are uploaded as workflow artifacts.
