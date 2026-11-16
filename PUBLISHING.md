# Publishing to NuGet

`Transynq.Sdk` is published to [nuget.org](https://www.nuget.org/packages/Transynq.Sdk) by
[`.github/workflows/release.yml`](.github/workflows/release.yml) using **NuGet trusted publishing**: GitHub Actions gets a
short-lived OIDC token, `NuGet/login@v1` exchanges it for a temporary API key, and no long-lived key is stored anywhere.

## One-time setup (repository owner)

1. **nuget.org account.** Sign in at <https://www.nuget.org> with the Microsoft account that will own the package. Note the
   account's **user name** (profile name, not the e-mail address).
2. **Reserve the package id.** The id `Transynq.Sdk` is claimed by the first successful push. Optionally request an
   [ID prefix reservation](https://learn.microsoft.com/nuget/nuget-org/id-prefix-reservation) for `Transynq.*` (gives the
   "verified" check mark and stops others from using the prefix).
3. **Trusted publishing policy.** nuget.org > your user name > **Trusted Publishing** > *Add policy*, with exactly:

   | Field | Value |
   |---|---|
   | Policy name | `transynq-sdk-dotnet release` |
   | Package owner | your nuget.org user (or organisation) |
   | Repository owner | `qadir0108` |
   | Repository | `transynq-sdk-dotnet` |
   | Workflow file | `release.yml` |
   | Environment | `release` |

4. **GitHub environment.** Repository *Settings > Environments > New environment* `release`. Optionally add **required
   reviewers** (manual approval before every publish) and restrict it to tags `v*`.
5. **GitHub variable.** *Settings > Secrets and variables > Actions > Variables*: add `NUGET_USER` = your nuget.org user name
   (from step 1). No secret is needed.

## Release procedure

1. Update `<Version>` in `src/Transynq.Sdk/Transynq.Sdk.csproj` and `SdkInfo.Version` in `src/Transynq.Sdk/TransynqClient.cs`
   (semantic versioning; the SDK version is independent of the API version).
2. Move the "Unreleased" entries of [CHANGELOG.md](CHANGELOG.md) under a new `## [X.Y.Z] - YYYY-MM-DD` heading.
3. Open a pull request, let CI pass, merge to `main`.
4. Create a GitHub release with a **new tag `vX.Y.Z`** (exactly the package version with a `v` prefix) on `main`, paste the
   changelog section as notes, and publish it.
5. The `release` workflow checks the tag against the package version (it fails on a mismatch), builds, tests, packs and pushes
   the `.nupkg` and `.snupkg` (after the `release` environment approval, if configured). The package appears on nuget.org after
   validation (usually a few minutes).

**Dry run:** *Actions > release > Run workflow* with `dry-run` checked (the default) builds, tests and packs without pushing;
the packages are attached to the run as the `nuget-packages` artifact. Unchecking `dry-run` on a manual run publishes the current
`main` (only do that deliberately; releases are the normal path).

A version that is already on nuget.org is skipped (`--skip-duplicate`); nuget.org versions cannot be overwritten, only
unlisted.
