namespace AiLight.App;

// Compatibility name for the Windows shell; both shells use the same watcher.
public sealed class CodexApprovalWatcher(string? sessionsRoot = null)
    : AiLight.Core.CodexApprovalWatcher(sessionsRoot);
