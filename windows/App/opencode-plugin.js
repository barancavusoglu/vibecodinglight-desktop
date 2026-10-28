// VibeCodingLight managed plugin v1
import { spawn, spawnSync } from "node:child_process";
import { randomUUID } from "node:crypto";
const executable = __AI_LIGHT_EXECUTABLE__;
export default async function VibeCodingLight({ directory, worktree, project }) {
  const producer = randomUUID();
  let sequence = 0, sending = false, closed = false;
  const queue = [];
  const clean = (value, limit = 240) => typeof value === "string" ? value.replace(/[\x00-\x1f\x7f]/g, "").slice(0, limit) : "";
  const cwd = clean(worktree || directory || project?.worktree || project?.path, 2048);
  const envelope = payload => ({ ...payload, producer_id: producer, sequence: ++sequence });
  const progress = p => ["OpenCodeWorking", "OpenCodeIdle", "OpenCodeMetadata"].includes(p.hook_event_name);
  function send(payload) {
    if (closed) return;
    // Collapse only adjacent redundant progress; never cross a permission or stop.
    const tail = queue.at(-1);
    if (tail && progress(payload) && tail.session_id === payload.session_id && tail.hook_event_name === payload.hook_event_name)
      queue[queue.length - 1] = envelope(payload);
    else {
      if (queue.length >= 256) {
        const replaceable = queue.findIndex(progress);
        if (replaceable >= 0) queue.splice(replaceable, 1);
        else return; // Bound memory even if a malfunctioning host floods requests.
      }
      queue.push(envelope(payload));
    }
    void drain();
  }
  async function drain() {
    if (sending || closed) return;
    sending = true;
    while (queue.length && !closed) {
      const payload = queue.shift();
      await new Promise(resolve => {
      let child, timer;
      const done = () => { clearTimeout(timer); resolve(); };
      try {
        child = spawn(executable, ["--hook", "opencode"], { windowsHide: true, shell: false, stdio: ["pipe", "ignore", "ignore"] });
        timer = setTimeout(() => { child.kill(); done(); }, 2000);
        child.once("error", done); child.once("close", done);
        child.stdin.on("error", done);
        child.stdin.end(JSON.stringify(payload));
      } catch { done(); }
      });
    }
    sending = false;
  }
  // One bounded shutdown message closes this producer only. A delayed child
  // cannot resurrect it; the receiver retains a producer tombstone.
  process.once("exit", () => {
    closed = true; queue.length = 0;
    try { spawnSync(executable, ["--hook", "opencode"], {
      windowsHide: true, shell: false, timeout: 1000,
      stdio: ["pipe", "ignore", "ignore"],
      input: JSON.stringify(envelope({ hook_event_name: "OpenCodeDisconnected", session_id: "", cwd }))
    }); } catch { }
  });
  return { event: async ({ event }) => {
    try {
      const p = event?.properties;
      if (!p) return;
      const session = clean(p.sessionID || p.session_id || p.info?.id || p.session?.id);
      if (!session) return;
      let name = "", token = "", title = "";
      switch (event.type) {
        case "session.created": name = "SessionStart"; title = clean(p.info?.title); break;
        case "session.updated": name = "OpenCodeMetadata"; title = clean(p.info?.title); break;
        case "session.deleted": name = "SessionEnd"; break;
        case "session.status":
          name = p.status?.type === "idle" ? "OpenCodeIdle" : ["busy", "retry"].includes(p.status?.type) ? "OpenCodeWorking" : ""; break;
        case "session.idle": name = "OpenCodeIdle"; break;
        case "session.error": name = p.error?.name === "MessageAbortedError" ? "Interrupt" : "OpenCodeError"; break;
        case "permission.asked": case "permission.v2.asked":
          name = "OpenCodeAsked"; token = "permission:" + clean(p.id); break;
        case "question.asked": case "question.v2.asked":
          name = "OpenCodeAsked"; token = "question:" + clean(p.id); break;
        case "permission.replied": case "permission.v2.replied":
          name = "OpenCodeReplied"; token = "permission:" + clean(p.requestID); break;
        case "question.replied": case "question.rejected": case "question.v2.replied": case "question.v2.rejected":
          name = "OpenCodeReplied"; token = "question:" + clean(p.requestID); break;
      }
      if (!name || (token && token.endsWith(":"))) return;
      send({ session_id: session, hook_event_name: name, tool_use_id: token, session_title: title, cwd });
    } catch { /* Observe only; never change or reject an OpenCode operation. */ }
  }};
}
