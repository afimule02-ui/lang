using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;

namespace Eldf.Api;

/// <summary>
/// Hands submissions to the audio pipeline without blocking the upload request.
/// The queue is in-memory, but the work item is only an id: the authoritative state lives in
/// <c>dbo.eldf_audio_processing</c>, so a restart can pick up anything left queued.
/// </summary>
public sealed class AudioProcessingQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>(
        new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(int submissionId, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(submissionId, ct);

    public IAsyncEnumerable<int> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

/// <summary>
/// Drains the queue one submission at a time. FFmpeg on a 30 to 60 minute recording is CPU
/// heavy, so the work is deliberately serialised rather than run in parallel.
/// </summary>
public sealed class AudioProcessingWorker(
    AudioProcessingQueue queue,
    IServiceScopeFactory scopes,
    AudioPipeline pipeline,
    AudioStoragePaths paths,
    ILogger<AudioProcessingWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Anything left "queued" or "running" from a previous process is retried once on start.
        await RequeueUnfinishedAsync(stoppingToken);

        await foreach (var submissionId in queue.ReadAllAsync(stoppingToken))
        {
            try { await ProcessAsync(submissionId, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Audio worker failed on submission {Id}", submissionId); }
        }
    }

    private async Task RequeueUnfinishedAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EldfDbContext>();
            var pending = await db.AudioProcessing
                .Where(x => x.Status == "queued" || x.Status == "running")
                .Select(x => x.SubmissionId)
                .ToListAsync(ct);
            foreach (var id in pending) await queue.EnqueueAsync(id, ct);
            if (pending.Count > 0) log.LogInformation("Re-queued {Count} unfinished audio job(s)", pending.Count);
        }
        catch (Exception ex) { log.LogWarning(ex, "Could not re-queue unfinished audio jobs"); }
    }

    private async Task ProcessAsync(int submissionId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EldfDbContext>();

        var submission = await db.Submissions.Include(x => x.User).SingleOrDefaultAsync(x => x.Id == submissionId, ct);
        if (submission is null) { log.LogWarning("Audio job for missing submission {Id}", submissionId); return; }

        var record = await db.AudioProcessing.SingleOrDefaultAsync(x => x.SubmissionId == submissionId, ct);
        if (record is null)
        {
            record = new AudioProcessingRecord { SubmissionId = submissionId };
            db.AudioProcessing.Add(record);
        }
        if (record.Status == "done") return;   // already finished; never process twice

        record.Status = "running";
        record.StartedAtUtc = DateTime.UtcNow;
        record.AttemptCount += 1;
        record.PipelineVersion = AudioPipelineOptions.PipelineVersion;
        await db.SaveChangesAsync(ct);

        var originalPath = paths.Resolve(submission.AudioPath);
        var work = paths.WorkDirectory(submissionId);
        var delivery = paths.DeliveryDirectory(submissionId);

        // Speaker region is useful dataset context; the contributor's identity is not included.
        var region = await db.Speakers.AsNoTracking()
            .Where(x => x.UserAccountId == submission.UserAccountId && x.Region != "")
            .Select(x => x.Region).FirstOrDefaultAsync(ct) ?? "";

        var seed = new AudioQualityReport
        {
            Language = submission.ProjectLanguage,
            Region = region,
            RecordingType = "Conversation",
            ConsentReference = submission.User?.ConsentReference ?? "",
            // A stable pseudonymous reference, so a buyer can group a speaker's work without
            // ever receiving their name or phone number.
            ContributorReference = $"ELDF-C{submission.UserAccountId:D6}",
        };

        var report = await pipeline.RunAsync(submissionId, originalPath ?? "", work, delivery, seed, ct);

        record.Verdict = report.Verdict;
        record.ContributorMessage = Trim(report.ContributorMessage, 1000);
        record.ContributorMessageAmharic = Trim(report.ContributorMessageAmharic, 1000);
        record.Error = Trim(report.Error ?? "", 2000);
        record.ReportJson = pipeline.Serialize(report);
        record.SecondsRemoved = report.SecondsRemoved;
        record.NoiseReductionApplied = report.NoiseReductionApplied;
        record.EdgeTrimApplied = report.EdgeTrimApplied;
        record.InteriorSilenceCapped = report.InteriorSilenceCapped;

        if (report.Original is { } o)
        {
            record.OriginalSha256 = o.Sha256;
            record.OriginalSizeBytes = o.SizeBytes;
            record.OriginalDurationSeconds = o.DurationSeconds;
            record.OriginalSampleRate = o.SampleRate;
            record.OriginalChannels = o.Channels;
        }
        if (report.Processed is { } p)
        {
            record.ProcessedPath = paths.ToRelative(Path.Combine(work, "processed.wav"));
            record.ProcessedSha256 = p.Sha256;
            record.ProcessedSizeBytes = p.SizeBytes;
            record.ProcessedDurationSeconds = p.DurationSeconds;
            var zip = Path.Combine(delivery, $"ELDF_SUBMISSION_{submissionId}.zip");
            if (File.Exists(zip)) record.DeliveryZipPath = paths.ToRelative(zip);
        }

        record.Status = string.IsNullOrEmpty(report.Error) ? "done" : "failed";
        record.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await WorkflowEvents.RecordAsync(db, null, "Audio quality " + record.Verdict,
            "Submission", submissionId.ToString(),
            $"removed {report.SecondsRemoved:0.##}s silence; noise reduction {(report.NoiseReductionApplied ? "on" : "off")}");

        log.LogInformation("Submission {Id}: audio verdict {Verdict} ({Status})", submissionId, record.Verdict, record.Status);
    }

    private static string Trim(string value, int max) =>
        string.IsNullOrEmpty(value) ? "" : value[..Math.Min(max, value.Length)];
}

/// <summary>
/// Resolves the data directory for audio. Shared so the pipeline, the worker and the download
/// endpoints all agree on where files live, and so a path can never escape the data root.
/// </summary>
public sealed class AudioStoragePaths(IConfiguration configuration, IWebHostEnvironment environment)
{
    public string Root { get; } = Path.GetFullPath(
        Environment.GetEnvironmentVariable("ELDF_DATA_DIR") ?? configuration["DataDirectory"] ?? "../../eldf-server-data",
        environment.ContentRootPath);

    public string WorkDirectory(int submissionId) => Path.Combine(Root, "processed", submissionId.ToString());
    public string DeliveryDirectory(int submissionId) => Path.Combine(Root, "delivery", submissionId.ToString());

    public string ToRelative(string fullPath) => Path.GetRelativePath(Root, fullPath);

    /// <summary>Resolves a stored relative path, refusing anything that points outside the root.</summary>
    public string? Resolve(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) return null;
        var root = Path.GetFullPath(Root) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}
