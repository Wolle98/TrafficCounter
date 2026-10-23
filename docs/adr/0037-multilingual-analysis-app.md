# ADR-0037: Multilingual analysis app with one resource file per language

- **Status:** accepted
- **Date:** 2026-10-07
- **Stage:** 4
- **Supersedes:** the point "the user interface of the analysis app stays German" of [ADR-0036](0036-documentation-in-english.md)

## Context

The analysis app ([ADR-0014](0014-gui-technology.md)) had German texts and German number and date formats written directly into the pages. With the documentation in English, the app should be usable in English as well, with German kept. Further languages should be possible without touching the code: one file per language.

## Options considered

1. **ASP.NET Core localisation** (`Microsoft.Extensions.Localization`, `IStringLocalizer`, request localisation middleware) with `.resx` files: part of .NET (MIT licence), no extra dependency, `.resx` is supported by IDEs and translation tools (e.g. ResX Resource Manager, Weblate, Crowdin).
2. **JSON files** with a community localiser package: easier to edit by hand, but an additional dependency for a small gain.
3. **gettext / PO files** (e.g. via a third-party library): established translation workflow, but unusual in .NET and again an extra dependency.

## Decision

Option 1.

- **One file per language:** `Resources/Strings.resx` holds the English texts (neutral resources, fallback for every missing language); `Resources/Strings.<language>.resx` holds a translation, initially `Strings.de.resx`. Keys are grouped by page (`Nav.*`, `Home.*`, `Period.*`, …). Placeholders `{0}`, `{1}` are used for values.
- **Formats belong to the language:** date formats (`Format.*`) and Excel format codes (`Format.Excel*`) are texts as well, so a translation also decides how dates look. Numbers use the culture of the language.
- **Languages found automatically:** at start-up the app offers English plus every language whose satellite assembly it finds. A new language therefore only needs a new `.resx` file; no code or configuration changes.
- **Choice:** a selection at the top right of every page. The choice is stored in the standard culture cookie for a year; without a cookie the browser's preferred language applies, otherwise English. A change reloads the page, because the Blazor circuit takes its culture when it starts.
- **Exports** (CSV, Excel) use the language of the request: headers, class names, sheet names, notes, file name.
- **Addresses:** the pages get English paths (`/period`, `/week`, `/speed`, `/operations`); the previous German paths remain as aliases so bookmarks keep working.
- **Not translated:** data from the site configuration, such as the direction names, and messages written by the service into the operations log.
- **Tests** check for every translation that all keys exist, that the placeholders match and that the date formats are valid, so an incomplete new language fails the build's tests.

## Consequences

- Positive: English and German now; further languages by adding one file. No new dependency.
- Negative: page code becomes slightly longer (`@L["…"]` instead of plain text), and new texts must be added to every language file; the tests point out missing ones.
