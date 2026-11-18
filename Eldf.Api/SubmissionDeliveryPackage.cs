using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Eldf.Api;

public static class SubmissionDeliveryPackage
{
    private static readonly string[] RequiredEntries = ["transcript.txt", "translation.txt", "metadata.json"];

    public static async Task<string> EnsureAsync(
        WorkSubmission submission,
        AudioProcessingRecord? processing,
        AudioStoragePaths paths,
        CancellationToken ct = default)
    {
        var originalPath = paths.Resolve(submission.AudioPath);
        if (originalPath is null || !File.Exists(originalPath))
            throw new FileNotFoundException("The submitted audio file is not available.");

        var code = SubmissionPackageNames.Create(submission.User.FullName, submission.Id);
        var deliveryDirectory = paths.DeliveryDirectory(submission.Id);
        Directory.CreateDirectory(deliveryDirectory);
        var packagePath = Path.Combine(deliveryDirectory, $"{code}.zip");
        if (HasRequiredEntries(packagePath, submission, processing)) return packagePath;

        var tempPath = packagePath + ".building-" + Guid.NewGuid().ToString("N");
        try
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            await using (var stream = File.Create(tempPath))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var extension = Path.GetExtension(originalPath);
                if (extension.Length > 12 || extension.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '.')) extension = ".audio";
                archive.CreateEntryFromFile(originalPath, $"{code}_audio_original{extension.ToLowerInvariant()}", CompressionLevel.NoCompression);

                var processedPath = paths.Resolve(processing?.ProcessedPath ?? "");
                if (processedPath is not null && File.Exists(processedPath))
                    archive.CreateEntryFromFile(processedPath, $"{code}_audio_processed.wav", CompressionLevel.NoCompression);

                await WriteTextAsync(archive, "transcript.txt", submission.Transcript, ct);
                await WriteTextAsync(archive, "translation.txt", submission.Translation, ct);

                var metadata = new
                {
                    submissionCode = code,
                    language = submission.ProjectLanguage,
                    durationSeconds = submission.DurationSeconds,
                    submittedAtUtc = submission.CreatedAtUtc,
                    approvedAtUtc = submission.AdminReviewedAtUtc,
                    status = submission.Status,
                    originalAudio = $"{code}_audio_original{extension.ToLowerInvariant()}",
                    processedAudio = processedPath is not null && File.Exists(processedPath) ? $"{code}_audio_processed.wav" : null,
                    transcriptSha256 = Sha256(submission.Transcript),
                    translationSha256 = Sha256(submission.Translation),
                    audioQualityStatus = processing?.Status,
                    audioQualityVerdict = processing?.Verdict
                };
                var metadataEntry = archive.CreateEntry("metadata.json", CompressionLevel.Optimal);
                await using var metadataStream = metadataEntry.Open();
                await JsonSerializer.SerializeAsync(metadataStream, metadata, cancellationToken: ct);
            }

            File.Move(tempPath, packagePath, true);
            return packagePath;
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static bool HasRequiredEntries(string packagePath, WorkSubmission submission, AudioProcessingRecord? processing)
    {
        if (!File.Exists(packagePath)) return false;
        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            if (!RequiredEntries.All(name => archive.GetEntry(name) is not null)
                || !archive.Entries.Any(x => x.FullName.Contains("_audio_original.", StringComparison.Ordinal))) return false;
            using var metadataStream = archive.GetEntry("metadata.json")!.Open();
            using var metadata = JsonDocument.Parse(metadataStream);
            var root = metadata.RootElement;
            return root.GetProperty("status").GetString() == submission.Status
                && root.GetProperty("transcriptSha256").GetString() == Sha256(submission.Transcript)
                && root.GetProperty("translationSha256").GetString() == Sha256(submission.Translation)
                && root.GetProperty("audioQualityStatus").GetString() == processing?.Status;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException) { return false; }
    }

    private static async Task WriteTextAsync(ZipArchive archive, string name, string text, CancellationToken ct)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(text.AsMemory(), ct);
    }

    private static string Sha256(string value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
