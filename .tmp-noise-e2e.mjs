/* Two checks:
   1. Why cases 1 / 8a / 8b came back AUTO_FIXED with no trim and no denoise: prints the measured
      input loudness against the -20 LUFS delivery target.
   2. End-to-end run of the corrected background-noise fixture and its clean control, to establish
      whether the noise path works when its precondition (a pause to meter) is actually present.
   Throwaway: delete after use. */
import fs from "node:fs";
import path from "node:path";

const BASE = "http://localhost:8003";
const WORK = path.join(process.env.TEMP, "africorpus-work");
const CASES = path.join(WORK, "cases");
const PW = "LongEnough15chars";

let pass = 0, fail = 0;
const check = (n, ok, d = "") => { if (ok) { pass++; console.log("  PASS  " + n); } else { fail++; console.log("  FAIL  " + n + "  >> " + d); } };

console.log("=== 1. loudness is why those cases are AUTO_FIXED rather than PASS ===");
const prior = JSON.parse(fs.readFileSync(path.join(WORK, "pipeline-results.json"), "utf8"));
console.log("  target loudness: -20 LUFS, tolerance 1 LU\n");
console.log("  " + "case".padEnd(34) + "verdict".padEnd(14) + "inLUFS".padEnd(10) + "offBy".padEnd(9) + "trim/cap/denoise");
for (const r of prior) {
  if (!r.q) continue;
  const before = r.q.before ?? r.q.Before ?? {};
  const lufs = before.loudnessLufs ?? before.LoudnessLufs;
  const off = Number.isFinite(lufs) ? Math.abs(lufs - -20).toFixed(1) : "n/a";
  console.log("  " + r.label.padEnd(34) + String(r.verdict).padEnd(14) +
    String(Number.isFinite(lufs) ? lufs.toFixed(1) : "n/a").padEnd(10) + String(off).padEnd(9) +
    `${r.q.edgeTrimApplied}/${r.q.interiorSilenceCapped}/${r.q.noiseReductionApplied}`);
}

function newSession() { return { cookies: new Map() }; }
async function api(session, method, p, { body, contentType } = {}) {
  const headers = {};
  if (contentType) headers["Content-Type"] = contentType;
  if (session?.cookies.size) headers["Cookie"] = [...session.cookies].map(([k, v]) => `${k}=${v}`).join("; ");
  const res = await fetch(BASE + p, { method, headers, body, redirect: "manual" });
  if (session) for (const raw of res.headers.getSetCookie?.() ?? []) {
    const [pair] = raw.split(";"); const i = pair.indexOf("=");
    const n = pair.slice(0, i).trim(), v = pair.slice(i + 1).trim();
    if (!v) session.cookies.delete(n); else session.cookies.set(n, v);
  }
  const text = await res.text();
  let json = null; try { json = JSON.parse(text); } catch { }
  return { status: res.status, text, json };
}
const form = o => new URLSearchParams(o).toString();
const sleep = ms => new Promise(r => setTimeout(r, ms));

console.log("\n=== 2. background noise, end to end ===");
const stamp = String(Date.now()).slice(-7);
const session = newSession();
let r = await api(session, "POST", "/api/auth/register", {
  body: form({ name: "Noise Fixture Contributor", phone: "94" + stamp, password: PW, language: "am", terms: "true", consent: "true" }),
  contentType: "application/x-www-form-urlencoded",
});
if (r.status !== 200) { console.error("could not register: " + r.status + " " + r.text); process.exit(1); }
console.log("registered contributor #" + r.json.user.id);

// Expectation per fixture: whether the denoiser should fire, given the measured floor.
const fixtures = [
  ["04b noisy, room tone about -40 dB, pauses present", "04b-noisy-with-gaps.wav", true],
  ["04c clean control, identical structure, no bed", "04c-clean-with-gaps.wav", false],
];

for (const [label, file, expectDenoise] of fixtures) {
  const full = path.join(CASES, file);
  console.log("\n" + "-".repeat(76) + "\n" + label + "   (" + file + ")");
  const fd = new FormData();
  fd.set("file", new Blob([fs.readFileSync(full)], { type: "audio/wav" }), file);
  fd.set("transcript", "noise fixture " + file);
  fd.set("translation", "noise fixture " + file);
  fd.set("language", "Amharic");
  const sub = await api(session, "POST", "/api/submissions", { body: fd });
  if (sub.status !== 200) { check(label + ": upload accepted", false, sub.status + " " + sub.text.slice(0, 160)); continue; }
  const id = sub.json.submissionId;
  console.log("  submission #" + id);

  let q = null;
  for (let i = 0; i < 120; i++) {
    await sleep(1500);
    q = await api(session, "GET", `/api/submissions/${id}/audio-quality`);
    if (q.json && ["done", "failed"].includes(q.json.status)) break;
  }
  if (!q?.json) { check(label + ": pipeline reported a status", false, "no response"); continue; }

  const b = q.json.before ?? {};
  console.log(`  measured floor : ${b.noiseFloorDb ?? "n/a"} dB     peak: ${b.peakDb ?? "n/a"} dB     silence spans: ${(b.silenceSpans ?? []).length}`);
  console.log(`  verdict        : ${q.json.verdict}    denoise=${q.json.noiseReductionApplied}`);
  console.log(`  findings       : ${(q.json.findings ?? []).join(" | ") || "(none)"}`);
  console.log(`  message        : ${q.json.message}`);

  check(label + `: denoiser ${expectDenoise ? "fires" : "stays off"}`,
    q.json.noiseReductionApplied === expectDenoise, `got ${q.json.noiseReductionApplied}`);
  if (expectDenoise) {
    check(label + ": the noise is named in the findings",
      (q.json.findings ?? []).some(f => /noise/i.test(f)), JSON.stringify(q.json.findings));
  } else {
    check(label + ": a clean recording is not called noisy",
      !(q.json.findings ?? []).some(f => /background noise/i.test(f)), JSON.stringify(q.json.findings));
  }
  check(label + ": a standardised copy was still produced", (q.json.processed?.sizeBytes ?? 0) > 1000,
    String(q.json.processed?.sizeBytes));
}

console.log("\n" + "=".repeat(76) + `\nPASSED: ${pass}    FAILED: ${fail}`);
process.exit(fail === 0 ? 0 : 1);
