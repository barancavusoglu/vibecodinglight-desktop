# Repository instructions

- All commit messages must be in English. Use English for new public documentation and pull request metadata; the existing application UI is Turkish.
- This repository contains desktop sources only. Keep website, commerce, customer/operations data and physical design files outside it.
- Never change installed applications, real hook/trust settings, autostart or devices merely to run source tests. Use the documented isolated smoke tests and offline previews.
- Preserve the shared core used by Windows and macOS. Check Codex CLI/Desktop, Claude Code CLI/Desktop Code and OpenCode applicability for lifecycle changes. Never claim Mac or physical-device validation from Windows-only tests.
- Keep credentials and private signing keys out of the tree and logs. Do not publish package outputs automatically without the relevant release process.
- Source tests: `dotnet run --project windows/Tests/AiLight.Tests.csproj -c Release`. Windows packaging and isolated transport checks: `pwsh -File windows/build.ps1`.
- macOS service checks: `dotnet run --project macos/AiLight.Mac.csproj -c Release -- --self-test`. Final bundle/Swift/signing checks require macOS.
