import { readFileSync } from "node:fs";
const source = readFileSync(new URL("../App/opencode-plugin.js", import.meta.url), "utf8").replace("__AI_LIGHT_EXECUTABLE__", JSON.stringify(process.argv[2]));
const { default: plugin } = await import("data:text/javascript;base64," + Buffer.from(source).toString("base64"));
const before = [process.listenerCount("SIGINT"), process.listenerCount("SIGTERM")];
const hooks = await plugin({ directory: "C:/ExitTest" });
if (before[0] !== process.listenerCount("SIGINT") || before[1] !== process.listenerCount("SIGTERM")) throw new Error("Plugin installed signal handlers");
await hooks.event({ event: { type: "session.status", properties: { sessionID: "exit-session", status: { type: "busy" } } } });
process.on("message", message => { if (message === "exit") process.exit(0); });
setTimeout(() => process.exit(2), 12000).unref();
