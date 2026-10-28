import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createServer } from "node:net";
import { randomUUID } from "node:crypto";
import { resolve } from "node:path";
import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";
const [exe, sid] = process.argv.slice(2);
assert.ok(exe && sid);
const suffix = randomUUID();
process.env.AI_LIGHT_TEST_PIPE = suffix;
process.env.AI_LIGHT_DATA_DIR = resolve(".test-data", suffix);
const pipe = "\\\\.\\pipe\\AiLight.v1." + sid + ".test." + suffix;
const events = [];
const server = createServer(socket => {
  let wire = "";
  socket.on("data", data => wire += data);
  socket.on("end", () => { events.push(JSON.parse(wire)); });
});
await new Promise((ok, fail) => { server.once("error", fail); server.listen(pipe, ok); });
try {
  const source = readFileSync(new URL("../App/opencode-plugin.js", import.meta.url), "utf8").replace("__AI_LIGHT_EXECUTABLE__", JSON.stringify(exe));
  const { default: plugin } = await import("data:text/javascript;base64," + Buffer.from(source).toString("base64"));
  const hooks = await plugin({ directory: "C:/Demo" });
  const input = [
    ["session.created", { info: { id: "s", title: "Deneme" } }, "SessionStart"],
    ["session.status", { sessionID: "s", status: { type: "busy" } }, "OpenCodeWorking"],
    ["permission.asked", { sessionID: "s", id: "p", metadata: { command: "SECRET" } }, "OpenCodeAsked"],
    ["question.asked", { sessionID: "s", id: "q", questions: ["SECRET"] }, "OpenCodeAsked"],
    ["permission.replied", { sessionID: "s", requestID: "p", reply: "once" }, "OpenCodeReplied"],
    ["question.replied", { sessionID: "s", requestID: "q", answers: ["SECRET"] }, "OpenCodeReplied"],
    ["session.status", { sessionID: "s", status: { type: "retry", message: "SECRET" } }, "OpenCodeWorking"],
    ["session.idle", { sessionID: "s" }, "OpenCodeIdle"],
    ["session.updated", { info: { id: "s", title: "Yeni başlık" } }, "OpenCodeMetadata"],
    ["session.error", { sessionID: "s", error: { name: "APIError", data: "SECRET" } }, "OpenCodeError"],
    ["question.v2.asked", { sessionID: "s", id: "v2" }, "OpenCodeAsked"],
    ["question.v2.rejected", { sessionID: "s", requestID: "v2" }, "OpenCodeReplied"],
    ["session.deleted", { info: { id: "s" } }, "SessionEnd"],
  ];
  await hooks.event({ event: { type: "message.updated", properties: { sessionID: "s", text: "SECRET" } } });
  await hooks.event({ event: { type: "permission.asked", properties: { sessionID: "s" } } });
  for (const [type, properties] of input) await hooks.event({ event: { type, properties } });
  const deadline = Date.now() + 25000;
  while (events.length < input.length && Date.now() < deadline) await new Promise(r => setTimeout(r, 25));
  assert.deepEqual(events.map(e => e.EventName), input.map(e => e[2]));
  assert.ok(events.every(e => e.Harness === "opencode" && e.Project === "Demo"));
  assert.equal(events[0].Title, "Deneme");
  assert.equal(events[8].Title, "Yeni başlık");
  assert.equal(events[2].ToolId, events[4].ToolId);
  assert.equal(events[3].ToolId, events[5].ToolId);
  assert.ok(!JSON.stringify(events).includes("SECRET"));
  assert.ok(events.every(e => e.ProducerId === events[0].ProducerId && e.Sequence > 0));
  assert.ok(events.every((e, i) => i === 0 || e.Sequence > events[i - 1].Sequence));
  const before = events.length;
  for (let i = 0; i < 1000; i++) await hooks.event({ event: { type: "session.status", properties: { session_id: "burst", status: { type: "busy" } } } });
  await hooks.event({ event: { type: "session.idle", properties: { session: { id: "burst" } } } });
  const until = Date.now() + 10000;
  while (events.at(-1)?.EventName !== "OpenCodeIdle" && Date.now() < until) await new Promise(r => setTimeout(r, 25));
  assert.equal(events.at(-1).EventName, "OpenCodeIdle");
  assert.ok(events.length - before <= 3, "redundant busy burst coalesces before final idle");
  const child = spawn(process.execPath, [fileURLToPath(new URL("./opencode-exit-child.mjs", import.meta.url)), exe], {
    windowsHide: true, stdio: ["ignore", "ignore", "pipe", "ipc"], env: process.env
  });
  let stderr = ""; child.stderr.on("data", data => stderr += data);
  const exited = new Promise((ok, fail) => { child.once("error", fail); child.once("exit", code => code === 0 ? ok() : fail(new Error(stderr || `exit ${code}`))); });
  const exitDeadline = Date.now() + 10000;
  while (!events.some(e => e.SessionId === "exit-session") && Date.now() < exitDeadline) await new Promise(r => setTimeout(r, 25));
  assert.ok(events.some(e => e.SessionId === "exit-session"), "child's first hook arrived");
  child.send("exit");
  await exited;
  const shutdownDeadline = Date.now() + 3000;
  while (!events.some(e => e.EventName === "OpenCodeDisconnected") && Date.now() < shutdownDeadline) await new Promise(r => setTimeout(r, 25));
  const shutdown = events.find(e => e.EventName === "OpenCodeDisconnected");
  assert.ok(shutdown);
  assert.equal(shutdown.ProducerId, events.find(e => e.SessionId === "exit-session").ProducerId);
  assert.notEqual(shutdown.ProducerId, events[0].ProducerId);
  console.log("PASS: packaged OpenCode transport: ordered lifecycle, privacy, 1000-event burst, alternate IDs, producer identity and real process-exit notification.");
} finally { server.close(); }
