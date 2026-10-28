# Contribution policy

This is a public source publication maintained by the repository owner. External code contributions are not accepted, and GitHub pull requests are disabled. Reading, downloading, modifying and redistributing the source remain governed by [Apache-2.0](LICENSE); this policy only controls contributions to this repository.

The following guidelines apply to maintenance by the owner:

- Use English for commit messages and public maintenance documentation. Keep changes focused; use imperative subjects such as `Fix card alignment on narrow windows`.
- Build instructions are in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md). Run the tests appropriate to your change and describe exactly what was checked.
- Preserve user-owned hook/plugin settings, backups, consent and assistant trust decisions. Never enable an integration as a side effect of a test.
- Check Codex CLI/Desktop, Claude Code CLI/Desktop Code and OpenCode applicability for changes to session tracking, lifecycle events or transport. Explain exclusions instead of assuming parity.
- Distinguish working, waiting and idle states. Preserve red waiting priority, mixed yellow state and blue idle breathing. Do not infer live activity from old history.
- Treat hooks, transcripts and tool output as untrusted input. Do not log prompt bodies, answers, command arguments, credentials or raw configurations.
- UI changes should include synthetic screenshots at narrow and wide sizes and checks of display scaling. Do not attach screenshots containing private sessions.
- No firmware requirement should be added for a desktop-only fix without an explicit compatibility decision.
- Update third-party notices and license materials when dependencies change. Do not commit binaries, personal settings, private keys or signing credentials.

For private vulnerability reports, follow [SECURITY.md](SECURITY.md). Do not submit sensitive data in public issues.
