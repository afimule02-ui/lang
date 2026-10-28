using System.Globalization;
using System.Diagnostics;
// CreateEntryFromFile is an extension method on ZipArchive, so the namespace must be imported
// rather than the types fully qualified.
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Eldf.Api;

/// <summary>
/// Settings for the mobile audio quality and standardisation pipeline.
/// These are the platform defaults. A buyer or dataset specification overrides them through
/// the <c>Audio:*</c> configuration section, which is why nothing here is hard-coded.
/// </summary>
public sealed class AudioPipelineOptions
{
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string FfprobePath { get; set; } = "ffprobe";

    // Delivery standard.
    public int TargetSampleRate { get; set; } = 48_000;
    public int TargetChannels { get; set; } = 1;
    public int TargetBitDepth { get; set; } = 16;        // 16 or 24
    public double TargetLoudnessLufs { get; set; } = -20;
    public double TargetTruePeakDb { get; set; } = -3;
    public double TargetLoudnessRange { get; set; } = 11;

    // Quality gates.
    public double SilenceThresholdDb { get; set; } = -35;  // below this counts as silence
    public double MinSilenceSeconds { get; set; } = 0.5;   // shorter gaps are natural speech rhythm
    public double EdgePaddingSeconds { get; set; } = 0.25; // keep this much around the first/last word
    public double MaxInteriorSilenceSeconds { get; set; } = 1.5; // long pauses are capped, never deleted
    public double TooQuietPeakDb { get; set; } = -30;      // peak below this is unusable
    public double LowPeakDb { get; set; } = -18;           // peak below this needs a lift
    public double ClippingRatioLimit { get; set; } = 0.005; // 0.5% clipped samples
    public double MinSpeechRatio { get; set; } = 0.35;      // speech must fill at least this share
    public double NoiseFloorWarnDb { get; set; } = -45;     // room tone above this is loud in absolute terms
    /// <summary>
    /// Minimum gap between the speech peak and the room tone. Below this the noise is close
    /// enough to the voice to matter. Both this and <see cref="NoiseFloorWarnDb"/> must be
    /// breached before a recording is called noisy.
    /// </summary>
    public double MinSignalMarginDb { get; set; } = 25;
    public int MinSeconds { get; set; } = 1800;
    public int MaxSeconds { get; set; } = 3600;

    // Noise reduction is opt-in per deployment because it can blur consonants.
    public bool EnableNoiseReduction { get; set; } = true;
    /// <summary>
    /// FFmpeg's <c>afftdn</c> noise-floor parameter in dB. It is a floor, not a reduction amount,
    /// and FFmpeg rejects anything outside [-80, -20]. -25 is the conservative value the existing
    /// browser-side preparation endpoint already uses, so speech detail is preserved.
    /// </summary>
    public double NoiseFloorNf { get; set; } = -25;

    public const string PipelineVersion = "africorpus-audio-1.0";
}

public enum AudioVerdict { PASS, AUTO_FIXED, MANUAL_REVIEW, RECORD_AGAIN }

/// <summary>Technical fingerprint of a file on disk. Used for both the original and the result.</summary>
public sealed record AudioFingerprint(
    string FileName, long SizeBytes, string Sha256, string Format, string Codec,
    double DurationSeconds, int SampleRate, int Channels, int BitDepth);

/// <summary>One stretch of silence, in seconds from the start of the file.</summary>
public sealed record SilenceSpan(double Start, double End)
{
    public double Duration => Math.Max(0, End - Start);
}

/// <summary>Everything the analysis stage measured.</summary>
public sealed class AudioMeasurements
{
    public double PeakDb { get; set; } = double.NegativeInfinity;
    public double MeanDb { get; set; } = double.NegativeInfinity;
    public double RmsDb { get; set; } = double.NegativeInfinity;
    public double LoudnessLufs { get; set; }
    public double TruePeakDb { get; set; }
    public double LoudnessRange { get; set; }
    /// <summary>
    /// Whether the loudness figures above come from FFmpeg or are still at their defaults.
    /// Without this flag an unread analysis is indistinguishable from a recording that genuinely
    /// measures 0 LUFS, and callers end up treating the default as a measurement.
    /// </summary>
    public bool LoudnessMeasured { get; set; }
    public long ClippedSamples { get; set; }
    public long TotalSamples { get; set; }
    public double ClippedRatio => TotalSamples > 0 ? (double)ClippedSamples / TotalSamples : 0;
    public List<SilenceSpan> SilenceSpans { get; set; } = [];
    public double TotalSilenceSeconds => SilenceSpans.Sum(x => x.Duration);
    public double SpeechSeconds { get; set; }
    public double SpeechRatio { get; set; }
    public double NoiseFloorDb { get; set; } = double.NegativeInfinity;
    public bool Readable { get; set; }
    public string? ReadError { get; set; }
}

/// <summary>The audit record written next to every processed file and into the database.</summary>
public sealed class AudioQualityReport
{
    public int SubmissionId { get; set; }
    public string PipelineVersion { get; set; } = AudioPipelineOptions.PipelineVersion;
    public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;
    public string Verdict { get; set; } = nameof(AudioVerdict.MANUAL_REVIEW);
    public List<string> Findings { get; set; } = [];
    public string ContributorMessage { get; set; } = "";
    public string ContributorMessageAmharic { get; set; } = "";
    public AudioFingerprint? Original { get; set; }
    public AudioFingerprint? Processed { get; set; }
    public AudioMeasurements? Before { get; set; }
    public AudioMeasurements? After { get; set; }
    public List<SilenceSpan> RemovedSilence { get; set; } = [];
    public double SecondsRemoved { get; set; }
    public bool NoiseReductionApplied { get; set; }
    public bool EdgeTrimApplied { get; set; }
    public bool InteriorSilenceCapped { get; set; }
    public Dictionary<string, object?> Settings { get; set; } = [];
    public string? Error { get; set; }

    // Buyer-facing dataset context. Deliberately carries no contributor name or phone number.
    public string Language { get; set; } = "";
    public string Region { get; set; } = "";
    public string RecordingType { get; set; } = "";
    public string ConsentReference { get; set; } = "";
    public string ContributorReference { get; set; } = "";
}

/// <summary>
/// Runs the mobile audio quality and standardisation pipeline with FFmpeg and ffprobe.
///
/// The original upload is never modified or deleted: every stage writes to a new file, and a
/// failure leaves the submission's stored audio exactly as the contributor sent it.
/// </summary>
public sealed class AudioPipeline(AudioPipelineOptions options, ILogger<AudioPipeline> log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    // ---------------------------------------------------------------- process helpers

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        string exe, IEnumerable<string> args, CancellationToken ct)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };
        foreach (var a in args) p.StartInfo.ArgumentList.Add(a);
        try { p.Start(); }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException(
                $"Required audio tool '{exe}' was not found. Install FFmpeg and make ffmpeg/ffprobe available on PATH.");
        }
        var outTask = p.StandardOutput.ReadToEndAsync(ct);
        var errTask = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return (p.ExitCode, await outTask, await errTask);
    }

    private static double ParseDouble(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;

    private static string Inv(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- fingerprint

    public async Task<AudioFingerprint> FingerprintAsync(string path, CancellationToken ct)
    {
        var (code, stdout, stderr) = await RunAsync(options.FfprobePath,
            ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", path], ct);
        if (code != 0) throw new InvalidOperationException("ffprobe could not read the file: " + stderr.Trim());

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        var format = root.TryGetProperty("format", out var f) ? f : default;
        var stream = root.TryGetProperty("streams", out var ss) && ss.GetArrayLength() > 0 ? ss[0] : default;

        double duration = 0;
        if (format.ValueKind == JsonValueKind.Object && format.TryGetProperty("duration", out var d))
            duration = ParseDouble(d.GetString() ?? "0");

        int sampleRate = 0, channels = 0, bits = 0;
        string codec = "", formatName = "";
        if (stream.ValueKind == JsonValueKind.Object)
        {
            if (stream.TryGetProperty("sample_rate", out var sr)) sampleRate = (int)ParseDouble(sr.GetString() ?? "0");
            if (stream.TryGetProperty("channels", out var ch) && ch.TryGetInt32(out var c)) channels = c;
            if (stream.TryGetProperty("bits_per_sample", out var bp) && bp.TryGetInt32(out var b) && b > 0) bits = b;
            if (bits == 0 && stream.TryGetProperty("bits_per_raw_sample", out var br)) bits = (int)ParseDouble(br.GetString() ?? "0");
            if (stream.TryGetProperty("codec_name", out var cn)) codec = cn.GetString() ?? "";
        }
        if (format.ValueKind == JsonValueKind.Object && format.TryGetProperty("format_name", out var fn))
            formatName = fn.GetString() ?? "";

        var info = new FileInfo(path);
        return new AudioFingerprint(
            Path.GetFileName(path), info.Length, await Sha256Async(path, ct),
            formatName, codec, duration, sampleRate, channels, bits);
    }

    private static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(fs, ct)).ToLowerInvariant();
    }

    // ---------------------------------------------------------------- measurement

    public async Task<AudioMeasurements> MeasureAsync(string path, double durationSeconds, CancellationToken ct)
    {
        var m = new AudioMeasurements();
        try
        {
            // volumedetect gives mean and max level; astats gives clipped-sample counts.
            var (_, _, volErr) = await RunAsync(options.FfmpegPath,
                ["-hide_banner", "-nostats", "-i", path, "-af", "volumedetect", "-f", "null", "-"], ct);
            var max = Regex.Match(volErr, @"max_volume:\s*(-?[\d.]+) dB");
            var mean = Regex.Match(volErr, @"mean_volume:\s*(-?[\d.]+) dB");
            if (max.Success) m.PeakDb = ParseDouble(max.Groups[1].Value);
            if (mean.Success) m.MeanDb = ParseDouble(mean.Groups[1].Value);

            var (_, _, statsErr) = await RunAsync(options.FfmpegPath,
                ["-hide_banner", "-nostats", "-i", path, "-af", "astats=measure_perchannel=none", "-f", "null", "-"], ct);
            var rms = Regex.Match(statsErr, @"RMS level dB:\s*(-?[\d.inf]+)");
            if (rms.Success) m.RmsDb = ParseDouble(rms.Groups[1].Value);
            var peakCount = Regex.Match(statsErr, @"Peak count:\s*(\d+)");
            if (peakCount.Success) m.ClippedSamples = long.Parse(peakCount.Groups[1].Value);
            var samples = Regex.Match(statsErr, @"Number of samples:\s*(\d+)");
            if (samples.Success) m.TotalSamples = long.Parse(samples.Groups[1].Value);

            // ebur128 via loudnorm's analysis pass gives integrated loudness and true peak.
            var (_, _, lnErr) = await RunAsync(options.FfmpegPath,
                ["-hide_banner", "-nostats", "-i", path, "-af", "loudnorm=print_format=json", "-f", "null", "-"], ct);
            var loudnessJson = ExtractLoudnormJson(lnErr);
            if (loudnessJson is not null)
            {
                try
                {
                    using var ln = JsonDocument.Parse(loudnessJson);
                    var r = ln.RootElement;
                    if (r.TryGetProperty("input_i", out var i)) m.LoudnessLufs = ParseDouble(i.GetString() ?? "0");
                    if (r.TryGetProperty("input_tp", out var tp)) m.TruePeakDb = ParseDouble(tp.GetString() ?? "0");
                    if (r.TryGetProperty("input_lra", out var lra)) m.LoudnessRange = ParseDouble(lra.GetString() ?? "0");
                    m.LoudnessMeasured = true;
                }
                catch (JsonException ex)
                {
                    // Visible rather than swallowed: a silent failure here used to leave the
                    // loudness at 0, which quietly disabled the PASS verdict for every recording.
                    log.LogWarning(ex, "Could not read the loudnorm analysis for {Path}", path);
                }
            }
            else
            {
                log.LogWarning("FFmpeg printed no loudnorm analysis block for {Path}", path);
            }

            m.SilenceSpans = await DetectSilenceAsync(path, durationSeconds, ct);
            var silence = m.TotalSilenceSeconds;
            m.SpeechSeconds = Math.Max(0, durationSeconds - silence);
            m.SpeechRatio = durationSeconds > 0 ? m.SpeechSeconds / durationSeconds : 0;
            m.NoiseFloorDb = await MeasureNoiseFloorAsync(path, m, ct);
            m.Readable = true;
        }
        catch (InvalidOperationException ex)
        {
            m.Readable = false;
            m.ReadError = ex.Message;
        }
        return m;
    }

    /// <summary>
    /// Pulls the loudnorm analysis object out of an FFmpeg log.
    ///
    /// FFmpeg prints the JSON block and then carries on logging: the muxing-overhead line and the
    /// final "size=/time=" progress line both come after it. Slicing from the opening brace to the
    /// end of stderr therefore produced a document with trailing content, which
    /// <see cref="JsonDocument"/> rejects. The resulting exception was swallowed, so every
    /// recording silently reported a loudness of 0 - and because the "already at target loudness"
    /// test compares against that value, no recording could ever reach the PASS verdict.
    ///
    /// The block is delimited by brace depth instead of assumed to run to the end of the output.
    /// </summary>
    private static string? ExtractLoudnormJson(string log)
    {
        // Anchor on the filter's own log prefix so an unrelated brace elsewhere is not mistaken
        // for the start of the report.
        var marker = log.LastIndexOf("Parsed_loudnorm", StringComparison.Ordinal);
        var start = log.IndexOf('{', marker >= 0 ? marker : 0);
        if (start < 0) return null;

        var depth = 0;
        for (var i = start; i < log.Length; i++)
        {
            if (log[i] == '{') depth++;
            else if (log[i] == '}' && --depth == 0) return log[start..(i + 1)];
        }
        return null; // truncated output: no balanced object to read
    }

    /// <summary>
    /// Measures the real noise floor by metering inside the longest gap between words, which is
    /// the only part of the file where what remains is the room rather than the speaker.
    ///
    /// An earlier version approximated the floor as the overall mean level minus 6 dB. That is
    /// not a noise floor at all: for ordinary speech the mean sits around -20 dB, so every
    /// recording looked noisy and the pipeline sent clean audio to human review.
    /// Returns negative infinity when the file has no measurable gap, and the noise checks then
    /// skip rather than guess.
    /// </summary>
    private async Task<double> MeasureNoiseFloorAsync(string path, AudioMeasurements m, CancellationToken ct)
    {
        // Ignore a silence that touches either edge: that is usually dead air before the
        // microphone was in position, not the room tone during the conversation.
        var gap = m.SilenceSpans
            .Where(s => s.Start > 0.2 && s.Duration >= Math.Max(0.4, options.MinSilenceSeconds))
            .OrderByDescending(s => s.Duration)
            .FirstOrDefault();
        if (gap is null) return double.NegativeInfinity;

        // Meter the middle of the gap, away from the decay of the previous word.
        var window = Math.Min(2.0, gap.Duration * 0.6);
        var start = gap.Start + (gap.Duration - window) / 2;

        var (_, _, err) = await RunAsync(options.FfmpegPath,
        [
            "-hide_banner", "-nostats", "-ss", Inv(start), "-t", Inv(window),
            "-i", path, "-af", "volumedetect", "-f", "null", "-"
        ], ct);

        var mean = Regex.Match(err, @"mean_volume:\s*(-?[\d.]+) dB");
        return mean.Success ? ParseDouble(mean.Groups[1].Value) : double.NegativeInfinity;
    }

    private async Task<List<SilenceSpan>> DetectSilenceAsync(string path, double duration, CancellationToken ct)
    {
        var (_, _, err) = await RunAsync(options.FfmpegPath,
        [
            "-hide_banner", "-nostats", "-i", path,
            "-af", $"silencedetect=noise={Inv(options.SilenceThresholdDb)}dB:d={Inv(options.MinSilenceSeconds)}",
            "-f", "null", "-"
        ], ct);

        var spans = new List<SilenceSpan>();
        double? open = null;
        foreach (var line in err.Split('\n'))
        {
            var start = Regex.Match(line, @"silence_start:\s*(-?[\d.]+)");
            if (start.Success) { open = ParseDouble(start.Groups[1].Value); continue; }
            var end = Regex.Match(line, @"silence_end:\s*([\d.]+)");
            if (end.Success && open.HasValue)
            {
                spans.Add(new SilenceSpan(Math.Max(0, open.Value), ParseDouble(end.Groups[1].Value)));
                open = null;
            }
        }
        // A silence that runs to the end of the file never emits silence_end.
        if (open.HasValue && duration > open.Value) spans.Add(new SilenceSpan(open.Value, duration));
        return spans;
    }

    // ---------------------------------------------------------------- quality gate

    /// <summary>
    /// Decides whether the file can be repaired at all. A dead or silence-only recording is
    /// refused here, before any processing is attempted.
    /// </summary>
    public (AudioVerdict Verdict, List<string> Findings, string En, string Am) Screen(
        AudioMeasurements m, double duration)
    {
        var findings = new List<string>();

        if (!m.Readable)
            return (AudioVerdict.RECORD_AGAIN, ["unreadable: " + (m.ReadError ?? "unknown")],
                "This file could not be opened. Please record again.",
                "ይህ ፋይል መከፈት አልቻለም። እባክዎ እንደገና ይቅረጹ።");

        if (duration <= 0.05)
            return (AudioVerdict.RECORD_AGAIN, ["empty file"],
                "The file contains no audio. Please record again.",
                "ፋይሉ ምንም ድምፅ የለውም። እባክዎ እንደገና ይቅረጹ።");

        if (double.IsNegativeInfinity(m.PeakDb) || m.PeakDb <= -60)
            return (AudioVerdict.RECORD_AGAIN, [$"silence only (peak {Inv(m.PeakDb)} dB)"],
                "This recording is silent. Check the microphone and record again.",
                "ይህ ቀረጻ ድምፅ የለውም። ማይክሮፎኑን ይፈትሹና እንደገና ይቅረጹ።");

        // Level is checked before the speech ratio: a recording that is merely too quiet reads as
        // silence end to end, and "move closer to the microphone" is the useful instruction,
        // whereas "no speech was detected" would send the contributor looking for the wrong fault.
        if (m.PeakDb < options.TooQuietPeakDb)
            return (AudioVerdict.RECORD_AGAIN, [$"far too quiet (peak {Inv(m.PeakDb)} dB)"],
                "Your audio is very low. Move closer to the microphone and speak at a normal volume.",
                "ድምፅዎ በጣም ዝቅተኛ ነው። ማይክሮፎኑን ቀርበው በመደበኛ ድምፅ ይናገሩ።");

        if (m.SpeechRatio < 0.02)
            return (AudioVerdict.RECORD_AGAIN, [$"almost no speech (speech ratio {m.SpeechRatio:P1})"],
                "Almost no speech was detected. Please record again.",
                "ምንም የሚሰማ ንግግር አልተገኘም። እባክዎ እንደገና ይቅረጹ።");

        if (m.ClippedRatio > options.ClippingRatioLimit)
            return (AudioVerdict.RECORD_AGAIN, [$"clipped ({m.ClippedRatio:P2} of samples)"],
                "The spoken words are clipped. Please record once more with a lower microphone level.",
                "የተነገሩት ቃላት ተቆርጠዋል። እባክዎ የማይክሮፎኑን ደረጃ ዝቅ አድርገው እንደገና ይቅረጹ።");

        // Repairable observations.
        if (m.PeakDb < options.LowPeakDb) findings.Add($"low level (peak {Inv(m.PeakDb)} dB) - will be normalised");
        if (IsNoisy(m)) findings.Add($"background noise (floor {Inv(m.NoiseFloorDb)} dB, {Inv(SignalMarginDb(m))} dB below the speech peak)");
        var interior = m.SilenceSpans.Where(s => s.Start > 0.3 && s.Duration > options.MaxInteriorSilenceSeconds).ToList();
        if (interior.Count > 0) findings.Add($"{interior.Count} interior silences longer than {Inv(options.MaxInteriorSilenceSeconds)}s - will be shortened, not removed");
        if (m.SpeechRatio < options.MinSpeechRatio) findings.Add($"speech fills only {m.SpeechRatio:P0} of the recording");
        if (duration < options.MinSeconds) findings.Add($"shorter than the required {options.MinSeconds / 60} minutes");
        if (duration > options.MaxSeconds) findings.Add($"longer than the {options.MaxSeconds / 60}-minute limit");

        return (AudioVerdict.PASS, findings, "", "");
    }

    /// <summary>
    /// Whether normalisation moved the loudness enough for the recording to count as altered.
    /// An unmeasured loudness is not treated as a change: that is what made every recording look
    /// altered and left the PASS verdict unreachable.
    /// </summary>
    private bool LoudnessWasChanged(AudioMeasurements m) =>
        m.LoudnessMeasured && Math.Abs(m.LoudnessLufs - options.TargetLoudnessLufs) > 1;

    /// <summary>How far the speech peak sits above the room tone. A rough signal-to-noise margin.</summary>
    private static double SignalMarginDb(AudioMeasurements m) =>
        double.IsNegativeInfinity(m.NoiseFloorDb) || double.IsNegativeInfinity(m.PeakDb)
            ? double.PositiveInfinity
            : m.PeakDb - m.NoiseFloorDb;

    /// <summary>
    /// Noisy means both that the room tone is loud in absolute terms and that it is close to the
    /// speech. Requiring both avoids condemning a quietly recorded but perfectly clear take, and
    /// an unmeasurable floor is never treated as a fault.
    /// </summary>
    private bool IsNoisy(AudioMeasurements m) =>
        !double.IsNegativeInfinity(m.NoiseFloorDb)
        && m.NoiseFloorDb > options.NoiseFloorWarnDb
        && SignalMarginDb(m) < options.MinSignalMarginDb;

    // ---------------------------------------------------------------- processing

    /// <summary>
    /// Produces the standardised copy. The original path is only ever read.
    /// Edge silence is cut precisely from the measured first and last speech boundary with
    /// padding, interior silence is capped rather than deleted, and the result is converted to
    /// the delivery standard in the same pass.
    /// </summary>
    public async Task<(string ProcessedPath, bool EdgeTrim, bool InteriorCapped, bool Denoised, List<SilenceSpan> Removed, double SecondsRemoved)>
        BuildProcessedAsync(string originalPath, string processedPath, AudioMeasurements m, double duration, CancellationToken ct)
    {
        // Where does speech actually begin and end?
        var leading = m.SilenceSpans.FirstOrDefault(s => s.Start <= 0.05);
        var trailing = m.SilenceSpans.LastOrDefault(s => s.End >= duration - 0.05);

        var startAt = leading is null ? 0 : Math.Max(0, leading.End - options.EdgePaddingSeconds);
        var endAt = trailing is null ? duration : Math.Min(duration, trailing.Start + options.EdgePaddingSeconds);
        if (endAt - startAt < 1) { startAt = 0; endAt = duration; }   // never trim to nothing

        var removed = new List<SilenceSpan>();
        if (startAt > 0.01) removed.Add(new SilenceSpan(0, startAt));
        if (endAt < duration - 0.01) removed.Add(new SilenceSpan(endAt, duration));
        var edgeTrim = removed.Count > 0;

        var filters = new List<string>();

        // Cap long interior pauses. stop_silence keeps this much of every silence it finds, so
        // natural word gaps and the speech rhythm of the language survive; only the excess goes.
        var interior = m.SilenceSpans
            .Where(s => s.Start > startAt + 0.3 && s.End < endAt - 0.3 && s.Duration > options.MaxInteriorSilenceSeconds)
            .ToList();
        var interiorCapped = interior.Count > 0;
        if (interiorCapped)
        {
            filters.Add($"silenceremove=stop_periods=-1:stop_duration={Inv(options.MaxInteriorSilenceSeconds)}" +
                        $":stop_silence={Inv(options.MaxInteriorSilenceSeconds)}:stop_threshold={Inv(options.SilenceThresholdDb)}dB");
            foreach (var s in interior)
                removed.Add(new SilenceSpan(s.Start + options.MaxInteriorSilenceSeconds, s.End));
        }

        // Noise reduction only when the floor is genuinely high, and gently.
        var denoise = options.EnableNoiseReduction && IsNoisy(m);
        // Clamped because FFmpeg refuses to build the graph at all for an out-of-range nf, which
        // would fail the whole standardisation pass rather than just skipping the denoiser.
        if (denoise) filters.Add($"afftdn=nf={Inv(Math.Clamp(options.NoiseFloorNf, -80, -20))}");

        // Loudness and peak. alimiter guarantees the peak ceiling so normalisation cannot clip.
        filters.Add($"loudnorm=I={Inv(options.TargetLoudnessLufs)}:TP={Inv(options.TargetTruePeakDb)}:LRA={Inv(options.TargetLoudnessRange)}");
        filters.Add($"alimiter=limit={Inv(Math.Pow(10, options.TargetTruePeakDb / 20))}");

        var codec = options.TargetBitDepth >= 24 ? "pcm_s24le" : "pcm_s16le";
        var args = new List<string> { "-hide_banner", "-nostats", "-y" };
        if (startAt > 0.01) { args.Add("-ss"); args.Add(Inv(startAt)); }
        args.Add("-i"); args.Add(originalPath);
        if (endAt < duration - 0.01) { args.Add("-t"); args.Add(Inv(endAt - startAt)); }
        args.Add("-af"); args.Add(string.Join(",", filters));
        args.Add("-ac"); args.Add(options.TargetChannels.ToString());
        args.Add("-ar"); args.Add(options.TargetSampleRate.ToString());
        args.Add("-c:a"); args.Add(codec);
        args.Add(processedPath);

        Directory.CreateDirectory(Path.GetDirectoryName(processedPath)!);
        var (code, _, err) = await RunAsync(options.FfmpegPath, args, ct);
        if (code != 0 || !File.Exists(processedPath))
            throw new InvalidOperationException("FFmpeg could not produce the standardised copy: " +
                (string.IsNullOrWhiteSpace(err) ? "unknown error" : err.Trim()[^Math.Min(400, err.Trim().Length)..]));

        var secondsRemoved = removed.Sum(x => x.Duration);
        log.LogInformation("Audio standardised: {Original} -> {Processed}, {Removed:0.##}s of silence removed",
            Path.GetFileName(originalPath), Path.GetFileName(processedPath), secondsRemoved);
        return (processedPath, edgeTrim, interiorCapped, denoise, removed, secondsRemoved);
    }

    // ---------------------------------------------------------------- full run

    /// <summary>
    /// Runs every stage and writes <c>processed.wav</c>, <c>metadata.json</c> and the delivery ZIP.
    /// Never throws for audio problems: a bad recording comes back as a verdict.
    /// </summary>
    public async Task<AudioQualityReport> RunAsync(
        int submissionId, string originalPath, string workDirectory, string deliveryDirectory,
        AudioQualityReport seed, CancellationToken ct = default)
    {
        var report = seed;
        report.SubmissionId = submissionId;
        report.ProcessedAtUtc = DateTime.UtcNow;
        report.Settings = new Dictionary<string, object?>
        {
            ["targetSampleRate"] = options.TargetSampleRate,
            ["targetChannels"] = options.TargetChannels,
            ["targetBitDepth"] = options.TargetBitDepth,
            ["targetLoudnessLufs"] = options.TargetLoudnessLufs,
            ["targetTruePeakDb"] = options.TargetTruePeakDb,
            ["silenceThresholdDb"] = options.SilenceThresholdDb,
            ["minSilenceSeconds"] = options.MinSilenceSeconds,
            ["edgePaddingSeconds"] = options.EdgePaddingSeconds,
            ["maxInteriorSilenceSeconds"] = options.MaxInteriorSilenceSeconds,
            ["noiseReductionEnabled"] = options.EnableNoiseReduction,
        };

        try
        {
            if (!File.Exists(originalPath))
            {
                report.Verdict = nameof(AudioVerdict.RECORD_AGAIN);
                report.Error = "the stored original file is missing";
                report.ContributorMessage = "The recording could not be found on the server. Please submit it again.";
                report.ContributorMessageAmharic = "ቀረጻው በሰርቨሩ ላይ አልተገኘም። እባክዎ እንደገና ይላኩ።";
                return report;
            }

            report.Original = await FingerprintAsync(originalPath, ct);
            var duration = report.Original.DurationSeconds;
            report.Before = await MeasureAsync(originalPath, duration, ct);

            var (verdict, findings, en, am) = Screen(report.Before, duration);
            report.Findings = findings;
            if (verdict == AudioVerdict.RECORD_AGAIN)
            {
                report.Verdict = nameof(AudioVerdict.RECORD_AGAIN);
                report.ContributorMessage = en;
                report.ContributorMessageAmharic = am;
                return report;   // the original stays exactly where it is
            }

            Directory.CreateDirectory(workDirectory);
            var processedPath = Path.Combine(workDirectory, "processed.wav");
            var (_, edge, interior, denoised, removed, secondsRemoved) =
                await BuildProcessedAsync(originalPath, processedPath, report.Before, duration, ct);

            report.EdgeTrimApplied = edge;
            report.InteriorSilenceCapped = interior;
            report.NoiseReductionApplied = denoised;
            report.RemovedSilence = removed;
            report.SecondsRemoved = secondsRemoved;
            report.Processed = await FingerprintAsync(processedPath, ct);
            report.After = await MeasureAsync(processedPath, report.Processed.DurationSeconds, ct);

            // Re-verify the delivery standard actually came out as asked.
            var problems = new List<string>();
            if (report.Processed.SampleRate != options.TargetSampleRate) problems.Add($"sample rate is {report.Processed.SampleRate}, expected {options.TargetSampleRate}");
            if (report.Processed.Channels != options.TargetChannels) problems.Add($"channels is {report.Processed.Channels}, expected {options.TargetChannels}");
            if (report.After.ClippedRatio > options.ClippingRatioLimit) problems.Add("the standardised copy clips");
            if (report.After.SpeechSeconds < 1) problems.Add("the standardised copy has no speech left");
            if (report.Processed.DurationSeconds < 0.5) problems.Add("the standardised copy is empty");

            // Speech safety: the repair must not have gutted the recording.
            if (duration > 0 && report.Processed.DurationSeconds < duration * 0.5)
                problems.Add($"processing removed more than half the audio ({Inv(duration)}s -> {Inv(report.Processed.DurationSeconds)}s)");

            if (problems.Count > 0)
            {
                report.Findings.AddRange(problems);
                report.Verdict = nameof(AudioVerdict.MANUAL_REVIEW);
                report.ContributorMessage = "The audio quality could not be confirmed automatically. It has been sent for human review.";
                report.ContributorMessageAmharic = "የድምፅ ጥራቱ በራስ-ሰር ሊረጋገጥ አልቻለም። ለሰው ግምገማ ተልኳል።";
            }
            // Loudness only counts as a change when it was actually measured. If the analysis
            // could not be read, fall back to the repair flags rather than assuming a change.
            else if (edge || interior || denoised || LoudnessWasChanged(report.Before))
            {
                report.Verdict = nameof(AudioVerdict.AUTO_FIXED);
                report.ContributorMessage = "Your audio was accepted and prepared automatically. A reviewer will confirm it.";
                report.ContributorMessageAmharic = "ድምፅዎ ተቀብሎ በራስ-ሰር ተዘጋጅቷል። ገምጋሚ ያረጋግጠዋል።";
            }
            else
            {
                report.Verdict = nameof(AudioVerdict.PASS);
                report.ContributorMessage = "Your audio was accepted.";
                report.ContributorMessageAmharic = "ድምፅህ ተቀባይነት አግኝቷል።";
            }

            // Noise still high after the attempt is a human-review case, not a silent pass.
            if (IsNoisy(report.After) && report.Verdict != nameof(AudioVerdict.MANUAL_REVIEW))
            {
                report.Findings.Add("background noise is still high after processing");
                report.Verdict = nameof(AudioVerdict.MANUAL_REVIEW);
                report.ContributorMessage = "Background noise was detected. Please record in a quieter place next time.";
                report.ContributorMessageAmharic = "የጀርባ ጫጫታ ተገኝቷል። እባክዎ በሚቀጥለው ጊዜ በጸጥታ ቦታ ይቅረጹ።";
            }

            await WriteMetadataAsync(workDirectory, report, ct);
            await BuildDeliveryZipAsync(submissionId, originalPath, processedPath, workDirectory, deliveryDirectory, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // A pipeline failure must never cost the contributor their recording.
            log.LogError(ex, "Audio pipeline failed for submission {Id}", submissionId);
            report.Verdict = nameof(AudioVerdict.MANUAL_REVIEW);
            report.Error = ex.Message;
            report.ContributorMessage = "The audio quality could not be confirmed automatically. It has been sent for human review.";
            report.ContributorMessageAmharic = "የድምፅ ጥራቱ በራስ-ሰር ሊረጋገጥ አልቻለም። ለሰው ግምገማ ተልኳል።";
            try { await WriteMetadataAsync(workDirectory, report, ct); } catch { /* best effort */ }
        }
        return report;
    }

    public string Serialize(AudioQualityReport report) => JsonSerializer.Serialize(report, Json);

    private async Task WriteMetadataAsync(string workDirectory, AudioQualityReport report, CancellationToken ct)
    {
        Directory.CreateDirectory(workDirectory);
        await File.WriteAllTextAsync(Path.Combine(workDirectory, "metadata.json"), Serialize(report), Encoding.UTF8, ct);
    }

    /// <summary>
    /// One ZIP per submission holding the original and the standardised copy as separate files,
    /// plus the metadata. The two are deliberately never mixed into a single WAV: a buyer has to
    /// be able to tell which is the untouched recording and which is the processed one.
    /// </summary>
    private async Task BuildDeliveryZipAsync(
        int submissionId, string originalPath, string processedPath,
        string workDirectory, string deliveryDirectory, CancellationToken ct)
    {
        Directory.CreateDirectory(deliveryDirectory);
        var zipPath = Path.Combine(deliveryDirectory, $"ELDF_SUBMISSION_{submissionId}.zip");
        var temp = zipPath + ".building";
        if (File.Exists(temp)) File.Delete(temp);

        await using (var fs = File.Create(temp))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            // Keep the original's real extension so nothing pretends a webm upload is a WAV.
            var originalName = "original" + Path.GetExtension(originalPath).ToLowerInvariant();
            zip.CreateEntryFromFile(originalPath, originalName, CompressionLevel.NoCompression);
            zip.CreateEntryFromFile(processedPath, "processed.wav", CompressionLevel.NoCompression);
            var meta = Path.Combine(workDirectory, "metadata.json");
            if (File.Exists(meta)) zip.CreateEntryFromFile(meta, "metadata.json", CompressionLevel.Optimal);
        }

        if (File.Exists(zipPath)) File.Delete(zipPath);
        File.Move(temp, zipPath);
        log.LogInformation("Delivery package written: {Zip}", zipPath);
    }
}
