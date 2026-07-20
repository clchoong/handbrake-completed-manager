# Contributing

Thank you for helping improve HandBrake Completed Manager.

This is an independent third-party project. Contributions must not include HandBrake binaries, source code, documentation, logos, or other project assets unless their licence and inclusion have been reviewed first.

## Before opening a change

- Search existing issues to avoid duplicate work.
- Open a feature request before beginning a large behavioral or architectural change.
- Keep file-management changes conservative: destructive actions require explicit confirmation, exact-path validation, recoverable Windows Recycle Bin behavior where applicable, and automated failure-path tests.
- Never include personal media, local databases, credentials, logs containing private paths, or generated build artifacts.

## Build and test

Install the .NET 10 SDK on Windows, then run:

```powershell
dotnet restore .\desktop\HandBrakeCompletedManager.sln
dotnet build .\desktop\HandBrakeCompletedManager.sln --configuration Release
dotnet test .\desktop\HandBrakeCompletedManager.sln --configuration Release --no-build
```

Submit focused changes with tests and update the relevant documentation when behavior changes. By contributing, you agree that your contribution is licensed under the repository's [MIT License](LICENSE).
