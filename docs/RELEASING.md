# Publishing 1.0.0 to NuGet.org

The release workflow is `.github/workflows/release.yml`. It is manual, requires the matching version tag and runs the package and Windows acceptance checks before publishing. A GitHub release or local `.nupkg` does not mean NuGet.org has accepted/indexed the package.

## One-time NuGet account setup

Use [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing), which exchanges the GitHub workflow identity for a short-lived publishing credential. No long-lived API key is stored in the repository.

In the intended NuGet.org account, create a GitHub Trusted Publishing policy:

| Field | Value |
| --- | --- |
| Repository owner | `Minicon-eG` |
| Repository | `Minicon.FileCleanUp` |
| Workflow file | `release.yml` |
| Environment | Leave empty; this workflow does not declare an environment |
| Package pattern | `Minicon.FileCleanUp` |
| Scope | Allow publishing new packages and new versions |
| Policy owner | The intended NuGet user or organization |

Set GitHub repository **Actions variable** `NUGET_USER` to the NuGet.org **user profile name**, not an email address or an assumed GitHub username. The user must have the intended package-owner permissions. Never paste passwords or API keys into an issue or chat.

## Publish

1. Confirm the version commit has passed CI and `v1.0.0` points to that exact commit.
2. Run `Publish NuGet release` on tag `v1.0.0`.
3. Confirm the publish step succeeds, then check the NuGet.org validation/indexing result and package ownership. Do not claim a public release solely because the upload was accepted.
4. Download the published package into a fresh consumer and verify version, dependencies and offline HTML.
5. Publish the corresponding GitHub release notes after NuGet publication is confirmed.

NuGet package versions cannot be overwritten. If a retry reports an existing version, inspect the existing package before deciding whether any action is required; the workflow deliberately does not hide conflicts with `--skip-duplicate`.
