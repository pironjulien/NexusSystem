# Contributing

## Environment

- Windows 10 or 11, the .NET SDK pinned by `global.json`, PowerShell 7 and Windows PowerShell 5.1 (the pack dry runs);
- the PSScriptAnalyzer module, saved in a `PSModulePath` folder outside Documents
  (`Save-PSResource PSScriptAnalyzer -Path <folder>`).

## Rules

1. Open an issue or a pull request against `main`. A change is accepted only when `test.ps1` passes: the CI
   (`.github/workflows/test.yml`) runs that same script on every push and pull request.
2. **Every new feature and every bug fix comes with its test**: the catalogs, `get-nexus.ps1` and the repository in
   `tests/NexusSystem.Tests`, the app in `app/tests/Nexus.Core.Tests` (xUnit v3). A fix
   starts with a test that fails without it.
3. PowerShell scripts pass PSScriptAnalyzer with `PSScriptAnalyzerSettings.psd1`; the app builds with warnings as
   errors.
4. The pack dry runs and install tests use throwaway profiles: no test changes the real AI apps or the installed
   products. Never commit a secret, a token, a log or a build output: build and test output go to
   `~\.nexus\build\NexusSystem`, outside the repository.
5. Comments, commit messages and `CHANGELOG.md` entries (Keep a Changelog, under `[Unreleased]`) are written in
   English. Store app changes get their own `## [app a.b.c.d] - YYYY-MM-DD` section, never a subsection of a catalog
   version (`ChangelogTests`).
6. Releasing a product of the catalog follows [RELEASING.md](RELEASING.md).

## Checks

```powershell
pwsh -NoProfile -File test.ps1   # PSScriptAnalyzer, then dotnet test on NexusSystem.slnx: catalogs, pack dry runs, app
git diff --check
```

## Version

A catalog release `vX.Y.Z` has the same version in `.claude-plugin/marketplace.json`, in `get-nexus.ps1`
(`$NexusMaintenanceVersion`) and in its `CHANGELOG.md` section; the `release` workflow checks them on the tag, then
builds, attests and publishes it. The Store app's version is `<Version>` in `app/Directory.Build.props`, with its own
`[app a.b.c.d]` section.
