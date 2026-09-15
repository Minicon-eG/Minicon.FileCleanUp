# Publishing releases to NuGet.org

The release workflow is `.github/workflows/release.yml`. It runs automatically when a GitHub release is published (including prereleases). Draft releases and ordinary commits do not publish packages. Manual dispatch on a matching version tag remains available for recovery. The workflow requires the tag to match the project version and runs the package and Windows acceptance checks before publishing. A GitHub release or local `.nupkg` does not mean NuGet.org has accepted/indexed the package.

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
| Scope | Allow publishing new versions of the existing package |
| Policy owner | `nitr0n` (current package owner) |

Set GitHub repository **Actions variable** `NUGET_USER` to the NuGet.org **user profile name**, not an email address or an assumed GitHub username. The user must have the intended package-owner permissions. Never paste passwords or API keys into an issue or chat.

The repository variable is currently configured as `NUGET_USER=nitr0n`. Creating the policy in NuGet.org is still required; the variable alone does not grant publishing access.

## Publish

1. Update `<Version>` in `src/Minicon.FileCleanUp/Minicon.FileCleanUp.csproj` to a new version, for example `1.0.1`, and update the changelog.
2. Commit and push the changes, including the release workflow, then wait for CI to pass.
3. Create a GitHub release with tag `v1.0.1` pointing to that exact commit and click **Publish release**. For prereleases use a matching package version and tag such as `1.1.0-preview.1` / `v1.1.0-preview.1`.
4. The `Publish NuGet release` workflow tests, packs, authenticates through Trusted Publishing and uploads the package automatically. A failed check prevents the upload; inspect the Actions run if it fails.
5. Confirm the publish step succeeds, then check NuGet.org validation/indexing and download the package into a fresh consumer to verify version, dependencies and offline HTML. GitHub release publication happens before NuGet upload and is not evidence that NuGet publication succeeded.

Version `1.0.0` was already published manually. Do not republish it to test automation. The release tag must include this updated workflow; changing `main` does not update older tags. To retry a failed release, rerun its failed Actions job or manually dispatch the workflow on that version tag after resolving the cause.

NuGet package versions cannot be overwritten. If a retry reports an existing version, inspect the existing package before deciding whether any action is required; the workflow deliberately does not hide conflicts with `--skip-duplicate`.

## Test publishing access without an upload

Run `Publish NuGet release` manually on `main` with `dry_run=true` (the default). This runs the build and acceptance checks and performs a real NuGet OIDC login, but skips the package upload. It verifies the Trusted Publishing policy without consuming a new package version. It does not test the release event or NuGet upload/indexing.

For a manual production retry, select the matching version tag and explicitly set `dry_run=false`. Published GitHub releases always take the production path.

For a workflow-only repair, dispatch the corrected workflow on `main`, set `release_tag` to the existing release tag, and set `dry_run=false`. Checkout and version validation use that tag; the tag and package source remain immutable. Version lookup selects the Version XML element explicitly even when the project has multiple PropertyGroup elements.
