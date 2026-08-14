#!/usr/bin/env node
// Sends an utterance to a running OpenJibo cloud exactly the way the robot's
// listen socket does, and prints what comes back. No microphone, no robot, and
// no dependencies - Node's built-in WebSocket only.
//
// This is the tool to reach for when a phrase "does not answer": it separates a
// cloud that never replied from a robot that did not ask, and it shows the
// intent the cloud picked, so a misheard phrase looks different from a broken one.
//
// Usage:
//   node scripts/cloud/say-to-jibo.mjs "verify me"
//   node scripts/cloud/say-to-jibo.mjs "how many people do you know" --server ws://192.168.7.10:8765
//   node scripts/cloud/say-to-jibo.mjs "spell umbrella" --json
//
// Text mode hands the cloud a finished transcript, so it tests intent routing and
// the reply, skipping speech recognition. To exercise recognition too - the part
// that turns audio into that transcript - synthesize the utterance and stream it
// as the robot would, no microphone needed:
//
//   espeak-ng -w /tmp/say.wav "verify me"
//   ffmpeg -y -i /tmp/say.wav -ar 16000 -ac 1 -c:a libopus -f ogg /tmp/say.ogg
//   node scripts/cloud/say-to-jibo.mjs --audio /tmp/say.ogg
//
// Both modes print how long the cloud took, which is what separates "never
// answered" from "answered after the robot stopped waiting".
//
// Options:
//   --server <url>   Cloud listen endpoint. Default ws://127.0.0.1:8765
//                    Use the machine's IP rather than "localhost": the cloud
//                    routes sockets by Host, and "localhost" is not a hub host.
//   --robot <id>     robotID sent in CONTEXT. Default $OPENJIBO_ROBOT_ID or
//                    Ghost-Instance-Onion-Silk.
//   --audio <file>   Stream an Ogg/Opus file as robot microphone audio instead of
//                    sending a transcript. The cloud transcribes it itself.
//   --timeout <ms>   How long to wait for the reply batch. Default 15000.
//   --insecure       Accept a self-signed certificate (wss:// only).
//   --json           Print raw reply frames instead of the readable summary.
//
// Exit code is 0 when the cloud produced something for Jibo to say, 1 otherwise,
// so it can gate a smoke test.

import { readFileSync } from "node:fs";

const args = process.argv.slice(2);
const words = [];
const options = {
  server: "ws://127.0.0.1:8765",
  robot: process.env.OPENJIBO_ROBOT_ID || "Ghost-Instance-Onion-Silk",
  audio: null,
  timeout: 15000,
  insecure: false,
  json: false
};

for (let i = 0; i < args.length; i++) {
  const arg = args[i];
  switch (arg) {
    case "--server":
    case "--robot":
    case "--audio":
      options[arg.slice(2)] = args[++i];
      break;
    case "--timeout":
      options.timeout = Number(args[++i]);
      break;
    case "--insecure":
      options.insecure = true;
      break;
    case "--json":
      options.json = true;
      break;
    case "-h":
    case "--help":
      printUsage();
      process.exit(0);
      break;
    default:
      if (arg.startsWith("--")) fail(`unknown option: ${arg}`);
      words.push(arg);
  }
}

const transcript = words.join(" ").trim();
if (!transcript && !options.audio) {
  printUsage();
  fail("nothing to say - pass the utterance as the first argument, or --audio <file.ogg>");
}

let audioBytes = null;
if (options.audio) {
  try {
    audioBytes = readFileSync(options.audio);
  } catch (error) {
    fail(`cannot read --audio ${options.audio}: ${error.message}`);
  }
  if (audioBytes.subarray(0, 4).toString("latin1") !== "OggS") {
    fail(`--audio expects Ogg/Opus (the container the robot streams). Convert first:\n` +
      `       ffmpeg -y -i ${options.audio} -ar 16000 -ac 1 -c:a libopus -f ogg out.ogg`);
  }
}
if (!Number.isFinite(options.timeout) || options.timeout <= 0) fail("--timeout must be a positive number");
if (options.insecure) process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";

const endpoint = new URL(options.server);
if (!/^wss?:$/.test(endpoint.protocol)) fail(`--server must be ws:// or wss:// (got ${options.server})`);
if (endpoint.pathname === "/" || endpoint.pathname === "") endpoint.pathname = "/v1/listen";
if (endpoint.hostname === "localhost") {
  warn('"localhost" is not routed as a hub host - use 127.0.0.1 or the LAN IP if the cloud does not answer.');
}

const transId = `say-${Date.now().toString(36)}`;
const replies = [];
let spoken = false;
let opened = false;
let askedAt = Date.now();

const socket = new WebSocket(endpoint.toString());
const deadline = setTimeout(() => {
  finish(`no reply within ${options.timeout}ms - the cloud never answered this turn`);
}, options.timeout);

socket.addEventListener("open", async () => {
  opened = true;
  console.log(`connected  ${endpoint}`);
  send({
    type: "CONTEXT",
    transID: transId,
    data: { general: { accountID: "acct-say-to-jibo", robotID: options.robot } }
  });

  if (audioBytes) {
    console.log(`streaming  ${options.audio}  ${audioBytes.length} bytes  (robotID ${options.robot})`);
    send({
      type: "LISTEN",
      transID: transId,
      data: { hotphrase: true, rules: ["launch", "globals/global_commands_launch"] }
    });
    await streamAudio(audioBytes);
    console.log("           audio sent, waiting for the cloud to transcribe...");
  } else {
    console.log(`saying     "${transcript}"  (robotID ${options.robot})`);
    send({ type: "CLIENT_ASR", transID: transId, data: { text: transcript } });
  }

  askedAt = Date.now();
});

// Stream in real time, the way a microphone produces audio. Dumping the whole
// file at once makes the cloud's silence detection see one instantaneous burst,
// which is not the shape it tunes its finalization windows for.
async function streamAudio(bytes) {
  const chunkSize = 4096;
  const chunks = Math.ceil(bytes.length / chunkSize);
  const durationMs = opusDurationMs(bytes);
  const interval = Math.max(5, Math.round(durationMs / chunks));
  console.log(`           ~${(durationMs / 1000).toFixed(1)}s of audio in ${chunks} chunk(s), ${interval}ms apart`);

  for (let offset = 0; offset < bytes.length; offset += chunkSize) {
    socket.send(bytes.subarray(offset, offset + chunkSize));
    await new Promise((resolve) => setTimeout(resolve, interval));
  }
}

// Opus granule positions count samples at 48kHz regardless of input rate, so the
// last Ogg page's granule is the exact playback length.
function opusDurationMs(bytes) {
  for (let offset = bytes.length - 27; offset >= 0; offset--) {
    if (bytes.readUInt32BE(offset) !== 0x4f676753) continue; // "OggS"
    const granule = bytes.readBigUInt64LE(offset + 6);
    if (granule > 0n) return Number(granule) / 48;
  }
  return 1000;
}

socket.addEventListener("message", (event) => {
  const raw = typeof event.data === "string" ? event.data : "<binary frame>";
  replies.push(raw);
  if (options.json) {
    console.log(raw);
  } else {
    describe(raw);
  }
  // SKILL_ACTION carries the speech and ends the turn.
  if (raw.includes('"SKILL_ACTION"')) setTimeout(() => finish(null), 250);
});

socket.addEventListener("error", (event) => {
  finish(opened
    ? `socket error: ${event.message ?? event.type ?? "unknown"}`
    : `could not connect to ${endpoint} - is the cloud running and reachable on that port?`);
});

socket.addEventListener("close", (event) => {
  if (replies.length === 0) finish(`socket closed before any reply (code ${event.code})`);
});

function send(payload) {
  socket.send(JSON.stringify(payload));
}

function describe(raw) {
  let frame;
  try {
    frame = JSON.parse(raw);
  } catch {
    console.log(`  <unparseable frame> ${raw.slice(0, 200)}`);
    return;
  }

  const type = frame.type ?? "?";
  const intent = frame?.data?.nlu?.intent;
  const heard = frame?.data?.asr?.text;
  const esml = frame?.data?.action?.config?.jcp?.config?.play?.esml;
  const elapsed = `+${Date.now() - askedAt}ms`;

  if (intent) console.log(`  ${type.padEnd(13)} ${elapsed.padEnd(9)} intent=${intent}`);
  else console.log(`  ${type.padEnd(13)} ${elapsed}`);

  if (heard && audioBytes) console.log(`  ${"".padEnd(13)} ${"".padEnd(9)} heard: "${heard}"`);

  if (esml) {
    const speech = esml.replace(/<[^>]*>/g, "").trim();
    if (speech) {
      spoken = true;
      console.log(`  ${"".padEnd(13)} ${"".padEnd(9)} says: "${speech}"`);
    }
  }
}

function finish(problem) {
  clearTimeout(deadline);
  try {
    socket.close();
  } catch {
    // already closing
  }

  console.log("");
  if (problem) {
    console.log(`FAILED     ${problem}`);
    if (replies.length > 0) console.log(`           got ${replies.length} frame(s) before giving up`);
    process.exit(1);
  }

  console.log(spoken
    ? `OK         the cloud answered in ${Date.now() - askedAt}ms`
    : "FAILED     replies arrived but none contained speech");
  process.exit(spoken ? 0 : 1);
}

function warn(message) {
  console.log(`note: ${message}`);
}

function fail(message) {
  console.error(`error: ${message}`);
  process.exit(2);
}

function printUsage() {
  console.log('usage: say-to-jibo.mjs "<what to say>" [--server ws://host:8765] [--robot <id>]');
  console.log("                       [--audio <file.ogg>] [--timeout ms] [--insecure] [--json]");
  console.log("");
  console.log("  text mode  tests intent routing and the reply");
  console.log("  --audio    also tests speech recognition; synthesize the audio with:");
  console.log('               espeak-ng -w /tmp/say.wav "verify me"');
  console.log("               ffmpeg -y -i /tmp/say.wav -ar 16000 -ac 1 -c:a libopus -f ogg /tmp/say.ogg");
}
