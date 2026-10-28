/* Why is LoudnessLufs 0 for every file? Shows exactly what FFmpeg 9 prints for
   loudnorm=print_format=json and whether the pipeline's parse can find it.
   Throwaway: delete after use. */
import path from "node:path";
import { spawnSync } from "node:child_process";

const FF = path.join(process.env.LOCALAPPDATA, "Microsoft", "WinGet", "Links", "ffmpeg.exe");
const f = path.join(process.env.TEMP, "africorpus-work", "cases", "04b-noisy-with-gaps.wav");

const r = spawnSync(FF, ["-hide_banner", "-nostats", "-i", f, "-af", "loudnorm=print_format=json", "-f", "null", "-"],
  { encoding: "utf8", maxBuffer: 1 << 28 });

console.log("exit code  :", r.status);
console.log("stdout len :", (r.stdout || "").length);
console.log("stderr len :", (r.stderr || "").length);
console.log("\n----- stderr (last 1800 chars) -----");
console.log((r.stderr || "").slice(-1800));
console.log("\n----- stdout (last 800 chars) -----");
console.log((r.stdout || "").slice(-800));

// Replicate the pipeline's parse exactly.
const lnErr = r.stderr || "";
const jsonStart = lnErr.lastIndexOf("{");
console.log("\n----- pipeline parse -----");
console.log("lastIndexOf('{') =", jsonStart);
if (jsonStart >= 0) {
  const slice = lnErr.slice(jsonStart);
  console.log("slice starts:", JSON.stringify(slice.slice(0, 120)));
  try {
    const j = JSON.parse(slice);
    console.log("parsed ok. input_i =", j.input_i, " input_tp =", j.input_tp, " input_lra =", j.input_lra);
  } catch (e) {
    console.log("JSON.parse FAILED:", e.message);
  }
} else {
  console.log("no '{' found in stderr at all");
}
