/* Compares candidate noise-floor estimators across the diagnostic cases, so the estimator is
   chosen on measurements rather than on a guess. Throwaway: delete after use. */
import path from "node:path";
const FF = path.join(process.env.LOCALAPPDATA, "Microsoft", "WinGet", "Links", "ffmpeg.exe");
const DIR = path.join(process.env.TEMP, "africorpus-work", "cases");

import { spawnSync } from "node:child_process";
function run(args) {
  const r = spawnSync(FF, args, { encoding: "utf8", maxBuffer: 1 << 28 });
  return { out: r.stdout || "", err: r.stderr || "" };
}

const num = (s, re) => { const m = s.match(re); return m ? Number(m[1]) : null; };

function pct(sorted, p) {
  if (!sorted.length) return null;
  return sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * p))];
}

const files = [
  "01-clear-speech.wav", "02-edge-silence.wav", "03-interior-silence.wav",
  "04-background-noise.wav", "05-very-quiet.wav", "07c-silence-only.wav",
  "08a-language-a.wav", "08b-language-b.wav",
];

const head = ["file", "peak", "mean", "gaps", "gapFloor", "p5", "p10", "p25", "wins"];
console.log(head.map((h, i) => h.padEnd(i === 0 ? 26 : 10)).join(""));

for (const n of files) {
  const f = path.join(DIR, n);

  const a = run(["-hide_banner", "-nostats", "-i", f, "-af", "silencedetect=noise=-40dB:d=0.5,volumedetect", "-f", "null", "-"]);
  const peak = num(a.err, /max_volume:\s*(-?[\d.]+) dB/);
  const mean = num(a.err, /mean_volume:\s*(-?[\d.]+) dB/);
  const starts = [...a.err.matchAll(/silence_start:\s*(-?[\d.]+)/g)].map(m => Number(m[1]));
  const ends = [...a.err.matchAll(/silence_end:\s*(-?[\d.]+)/g)].map(m => Number(m[1]));

  // current estimator: meter the middle of the longest interior gap
  let gapFloor = null;
  const spans = starts.map((s, i) => ({ start: s, end: ends[i] ?? null }))
    .filter(s => s.end !== null && s.start > 0.2)
    .map(s => ({ ...s, dur: s.end - s.start }))
    .sort((x, y) => y.dur - x.dur);
  if (spans.length) {
    const g = spans[0];
    const w = Math.min(2, g.dur * 0.6);
    const st = g.start + (g.dur - w) / 2;
    const b = run(["-hide_banner", "-nostats", "-ss", String(st), "-t", String(w), "-i", f, "-af", "volumedetect", "-f", "null", "-"]);
    gapFloor = num(b.err, /mean_volume:\s*(-?[\d.]+) dB/);
  }

  // candidate estimator: low percentile of short-window RMS, no gap required
  const c = run(["-hide_banner", "-nostats", "-i", f,
    "-af", "astats=metadata=1:reset=1:measure_perchannel=none:length=0.2,ametadata=print:key=lavfi.astats.Overall.RMS_level:file=-",
    "-f", "null", "-"]);
  const src = c.out + c.err;
  const vals = [...src.matchAll(/RMS_level=(-?[\d.]+|-inf)/g)]
    .map(m => m[1]).filter(v => v !== "-inf").map(Number)
    .filter(Number.isFinite).sort((x, y) => x - y);

  const row = [
    n,
    peak ?? "n/a",
    mean ?? "n/a",
    spans.length,
    gapFloor ?? "none",
    pct(vals, 0.05)?.toFixed(1) ?? "n/a",
    pct(vals, 0.10)?.toFixed(1) ?? "n/a",
    pct(vals, 0.25)?.toFixed(1) ?? "n/a",
    vals.length,
  ];
  console.log(row.map((v, i) => String(v).padEnd(i === 0 ? 26 : 10)).join(""));
}
