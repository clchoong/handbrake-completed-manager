# Publishing and independence

> **Review status:** The source is prepared for publication under the MIT License. Binary publication requires the package verifier to confirm that all licence and third-party-notice files are present. This is a technical licence review, not legal advice.

HandBrake Completed Manager is an independent companion application. It is not an official HandBrake product and is not affiliated with, endorsed by, sponsored by, or maintained by the HandBrake project.

## HandBrake relationship

The application interoperates with a separately installed or portable HandBrake copy through documented completion-action values and normal Windows process and file-system behavior. This repository does not include, modify, link against, or redistribute HandBrake source code, compiled binaries, documentation, or graphic assets.

HandBrake identifies its compiled application as GPLv2 software. That licence continues to govern HandBrake itself. This companion has its own codebase and distribution, so any future change that copies HandBrake code, links its components, bundles its binaries, or incorporates its assets requires a fresh licence review.

On that current boundary, distributing this independent companion does not by itself redistribute or create a derivative build of HandBrake. The companion detects a separately installed executable and receives completion values through HandBrake's user-configured external-action facility. This conclusion must be revisited if the integration boundary changes.

## Name and visual identity

The HandBrake name is used only to describe compatibility. Public repository and release pages should keep the independence statement prominent and must not imply that downloads are official HandBrake releases.

The companion uses an original coral, navy, and cream media/check icon. It must not use or imitate HandBrake's official pineapple-and-cocktail artwork, website presentation, or other project graphics.

Using “HandBrake” descriptively to identify compatibility is different from presenting the companion as an official HandBrake release. The name **HandBrake Completed Manager** is retained for product clarity, together with a prominent independence notice and original visual identity. Avoid HandBrake artwork, logos, release-page styling, or an `official` claim. Obtain qualified legal advice before commercial distribution or if certainty about naming rights is required.

## Repository licence decision

The project's original source code is published under the MIT License. This choice is independent of HandBrake's GPLv2 because the companion does not incorporate or link HandBrake code. Third-party components remain governed by their respective licences.

## Bundled dependency review

The current Windows application resolves these distributable dependency families:

- Microsoft .NET 10 runtime and `Microsoft.Data.Sqlite` 10.0.10 — MIT licence and Microsoft/runtime third-party notices
- `SQLitePCLRaw` 2.1.12 — Apache License 2.0
- SQLite native library — public domain

The self-contained single-file package includes the .NET runtime and SQLite components even though their individual DLLs are not visible beside the executable. Public packages must include the project MIT licence, applicable MIT and Apache 2.0 texts, copyright notices, and .NET third-party notices, then pass archive-content verification.

## Sources reviewed

- [HandBrake licence](https://github.com/HandBrake/HandBrake/blob/master/LICENSE)
- [HandBrake application and project information](https://github.com/HandBrake/HandBrake/wiki/Application-%26-Project-Information)
- [.NET runtime licence](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) and [third-party notices](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT)
- [`Microsoft.Data.Sqlite` 10.0.10 package metadata](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.10)
- [`SQLitePCLRaw.bundle_e_sqlite3` 2.1.12 package metadata](https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlite3/2.1.12)
- [SQLite public-domain statement](https://www.sqlite.org/copyright.html)

## Public release checklist

- Keep the independent-project notice near the top of the README and release descriptions.
- Link users to `https://handbrake.fr/` for the official HandBrake application.
- Do not bundle HandBrake installers, binaries, source code, documentation, or artwork.
- Include and verify all required third-party licence and notice files in the binary package.
- Repeat the independent-project notice prominently on the repository and release page.
- Publish checksums and clearly identify unsigned binaries so users understand Windows reputation warnings.
- Review the relationship and licences again before adding deeper integration or third-party components.
