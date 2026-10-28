/* Builds a background-noise fixture that actually reproduces the condition, plus a clean control
   with identical structure. The existing case 4 cannot exercise the noise path: it is one
   continuous 90 s block with no pause, and amix normalised the mix, so no gap exists to meter and
   the bed ends up at the same level as everything else.

   Here the speech is broken by pauses longer than MinSilenceSeconds (0.5 s) and the bed is summed
   with normalize=0 so its level is preserved. Reports the floor the pipeline's own estimator
   would read. Throwaway: delete after use. */
import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";

const FF = path.join(process.env.LOCALAPPDATA, "Microsoft", "WinGet", "Links", "ffmpeg.exe");
const OUT = path.join(process.env.TEMP, "africorpus-work", "cases");

const run = args => {
  const r = spawnSync(FF, ["-hide_banner", ...args], { encoding: "utf8", maxBuffer: 1 << 28 });
  return { out: r.stdout || "", err: r.stderr || "", code: r.status };
};
const num = (s, re) => { const m = s.match(re); return m ? Number(m[1]) : null; };

// Same speech proxy the original fixtures use, so only the noise bed and the pauses differ.
const speech = (dur, amp = 0.22) =>
  `anoisesrc=color=pink:sample_rate=44100:duration=${dur}:amplitude=${amp},highpass=f=120,lowpass=f=3800`;
const quiet = dur => `anullsrc=r=44100:cl=mono:d=${dur}`;

const SEGMENTS = [
  { dur: 20 }, { dur: 1.5, silence: true }, { dur: 25 },
  { dur: 2.0, silence: true }, { dur: 20 }, { dur: 1.5, silence: true }, { dur: 15 },
];
const TOTAL = SEGMENTS.reduce((a, s) => a + s.dur, 0);

/** Concatenated speech/pause bed, optionally with a constant noise floor summed on top. */
function build(name, noiseAmp) {
  const inputs = [];
  const labels = [];
  SEGMENTS.forEach((s, i) => {
    inputs.push("-f", "lavfi", "-i", s.silence ? quiet(s.dur) : speech(s.dur));
    labels.push(`[${i}:a]`);
  });
  let filter = `${labels.join("")}concat=n=${SEGMENTS.length}:v=0:a=1[dry]`;
  let map = "[dry]";
  if (noiseAmp !== null) {
    inputs.push("-f", "lavfi", "-i",
      `anoisesrc=color=white:sample_rate=44100:duration=${TOTAL}:amplitude=${noiseAmp}`);
    // normalize=0 sums instead of averaging, so the bed keeps the level it was generated at.
    filter += `;[dry][${SEGMENTS.length}:a]amix=inputs=2:duration=shortest:normalize=0[out]`;
    map = "[out]";
  }
  const file = path.join(OUT, name);
  const r = run(["-loglevel", "error", "-y", ...inputs, "-filter_complex", filter,
    "-map", map, "-ac", "1", "-ar", "44100", "-c:a", "pcm_s16le", file]);
  if (r.code !== 0) { console.error("build failed for " + name + ": " + r.err.slice(0, 400)); process.exit(1); }
  return file;
}

/** Replicates the pipeline's estimator: longest interior gap at -35 dB, metered in its middle. */
function pipelineFloor(file) {
  const a = run(["-nostats", "-i", file, "-af", "silencedetect=noise=-35dB:d=0.5,volumedetect", "-f", "null", "-"]);
  const peak = num(a.err, /max_volume:\s*(-?[\d.]+) dB/);
  const starts = [...a.err.matchAll(/silence_start:\s*(-?[\d.]+)/g)].map(m => Number(m[1]));
  const ends = [...a.err.matchAll(/silence_end:\s*(-?[\d.]+)/g)].map(m => Number(m[1]));
  const spans = starts.map((s, i) => ({ start: s, end: ends[i] }))
    .filter(s => Number.isFinite(s.end) && s.start > 0.2)
    .map(s => ({ ...s, dur: s.end - s.start }))
    .sort((x, y) => y.dur - x.dur);
  if (!spans.length) return { peak, gaps: 0, floor: null };
  const g = spans[0];
  const w = Math.min(2, g.dur * 0.6);
  const st = g.start + (g.dur - w) / 2;
  const b = run(["-nostats", "-ss", String(st), "-t", String(w), "-i", file, "-af", "volumedetect", "-f", "null", "-"]);
  return { peak, gaps: spans.length, floor: num(b.err, /mean_volume:\s*(-?[\d.]+) dB/) };
}

const WARN = -45, MARGIN = 25;
console.log(`structure: ${TOTAL}s, pauses of 1.5/2.0/1.5s (all above the 0.5s minimum)\n`);
console.log("amp        peak      gaps   floor     margin   floor>-45  margin<25  => noisy");

const candidates = [[null, "04c-clean-with-gaps.wav"], [0.006, null], [0.010, null], [0.017, null], [0.030, null]];
const picked = [];
for (const [amp, fixedName] of candidates) {
  const name = fixedName ?? `.probe-noise-${String(amp).replace(".", "")}.wav`;
  const file = build(name, amp);
  const r = pipelineFloor(file);
  const margin = r.floor === null ? null : Number((r.peak - r.floor).toFixed(1));
  const c1 = r.floor !== null && r.floor > WARN;
  const c2 = margin !== null && margin < MARGIN;
  console.log([
    String(amp ?? "none").padEnd(10), String(r.peak).padEnd(9), String(r.gaps).padEnd(6),
    String(r.floor ?? "none").padEnd(9), String(margin ?? "n/a").padEnd(8),
    String(c1).padEnd(10), String(c2).padEnd(10), String(c1 && c2),
  ].join(" "));
  picked.push({ amp, name, file, floor: r.floor, peak: r.peak, margin, noisy: c1 && c2, fixedName });
}

// Keep the clean control and the lowest amplitude that the estimator genuinely calls noisy.
const winner = picked.find(p => p.amp !== null && p.noisy);
if (winner) {
  const dest = path.join(OUT, "04b-noisy-with-gaps.wav");
  fs.copyFileSync(winner.file, dest);
  console.log(`\nchose amplitude ${winner.amp} -> 04b-noisy-with-gaps.wav  (floor ${winner.floor} dB, margin ${winner.margin} dB)`);
} else {
  console.log("\nno candidate amplitude produced a measurable noisy verdict");
}
for (const p of picked) if (!p.fixedName) fs.rmSync(p.file, { force: true });
console.log("control kept: 04c-clean-with-gaps.wav");
