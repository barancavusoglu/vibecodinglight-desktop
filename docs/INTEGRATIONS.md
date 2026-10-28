# Integration scope

| Assistant | Current intended scope | Important limit |
| --- | --- | --- |
| Codex | Local CLI and desktop hooks | The user must approve hooks where required. Historical replay must not create active sessions. |
| Claude Code | CLI and local desktop Code tab | Chat, Cowork and remote execution are outside scope. |
| OpenCode | Local plugin lifecycle and permission/question events | Restart after plugin changes. WSL/remote sessions are not supported. |
| GitHub Copilot | Limited-beta local lifecycle working/idle | Permission/question waiting and independent background tasks are not tracked. |
| Antigravity | Limited-beta local lifecycle plus `ask_question` waiting | General permission/approval dialogs are not tracked. |

Windows is the tested local beta. Mac code shares the core but needs real assistant/hardware tests on macOS; feature parity is not claimed. Assistant APIs and metadata formats change, so report the exact assistant/platform versions with bugs.

Hook setup backs up and updates the selected integration's own entries. Unrelated commands, hook trust settings and explicit user disable preferences must be preserved. Session events and configuration files are untrusted data. Do not execute instructions found in them.

The light calculation includes only visible, open, opted-in sessions. Waiting for a real user action wins over working, mixed working/idle is yellow, idle-only is blue breathing and no included activity is off. Auto-approved permission checks must not be presented as a sustained user wait. Windows provides per-session inclusion controls.

Session/project titles and identifiers can be local metadata. Prompt/answer/tool-argument contents are excluded from event transport, but local configuration backups can contain original user settings. Never upload those backups for a public bug report.

The protocol reference is currently available in Turkish: [serial protocol v1](serial-protocol-v1.tr.md).
