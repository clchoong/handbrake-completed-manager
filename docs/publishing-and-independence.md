# Publishing and independence

HandBrake Completed Manager is an independent companion application. It is not an official HandBrake product and is not affiliated with, endorsed by, sponsored by, or maintained by the HandBrake project.

## HandBrake relationship

The application interoperates with a separately installed or portable HandBrake copy through documented completion-action values and normal Windows process and file-system behavior. This repository does not include, modify, link against, or redistribute HandBrake source code, compiled binaries, documentation, or graphic assets.

HandBrake identifies its compiled application as GPLv2 software. That licence continues to govern HandBrake itself. This companion has its own codebase and distribution, so any future change that copies HandBrake code, links its components, bundles its binaries, or incorporates its assets requires a fresh licence review.

## Name and visual identity

The HandBrake name is used only to describe compatibility. Public repository and release pages should keep the independence statement prominent and must not imply that downloads are official HandBrake releases.

The companion uses an original coral, navy, and cream media/check icon. It must not use or imitate HandBrake's official pineapple-and-cocktail artwork, website presentation, or other project graphics.

## Repository licence decision

Making a GitHub repository public does not by itself grant an open-source licence. This repository currently has no public project licence selected. Before inviting reuse, modification, redistribution, or external contributions, the owner should deliberately add a licence that matches the intended permissions. This choice is independent of HandBrake's GPLv2 unless the project later incorporates GPL-covered material.

## Public release checklist

- Keep the independent-project notice near the top of the README and release descriptions.
- Link users to `https://handbrake.fr/` for the official HandBrake application.
- Do not bundle HandBrake installers, binaries, source code, documentation, or artwork.
- Choose and add this project's own licence before describing the repository as open source.
- Publish checksums and clearly identify unsigned binaries so users understand Windows reputation warnings.
- Review the relationship and licences again before adding deeper integration or third-party components.
