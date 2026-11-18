using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Eldf.Api;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = Environment.GetEnvironmentVariable("ELDF_SQLSERVER_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("DefaultConnection");
var databaseConfigured = !string.IsNullOrWhiteSpace(connectionString);
// Register the service even when no connection string is present. Minimal API uses DI registrations
// to infer EldfDbContext parameters; without this registration it treats them as request bodies and
// fails endpoint construction, which also breaks database-independent routes such as /health.
builder.Services.AddDbContext<EldfDbContext>(options =>
{
    if (databaseConfigured)
        options.UseSqlServer(connectionString!);
});
var origins = (Environment.GetEnvironmentVariable("ELDF_ALLOWED_ORIGINS") ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
if (origins.Length == 0) origins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials().WithExposedHeaders("X-Audio-Duration", "X-Audio-Trimmed")));
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ELDF_URLS") ?? "http://localhost:8001");

// Upload size limits must be raised to match Audio:MaxBytes.
// Kestrel defaults to a 30 MB request body and multipart form parsing defaults to 128 MB, while a
// single valid submission is a recording of 30 to 60 minutes: roughly 56 MB at 16 kHz mono and about
// 170 MB after the server converts to 48 kHz mono. With the defaults in place Kestrel rejected every
// real recording with "Request body too large" before the handler ever ran, so the 30-minute minimum
// and the 250 MB ceiling could never both be satisfied.
var configuredMaxBytes = builder.Configuration.GetValue<long>("Audio:MaxBytes", 250L * 1024 * 1024);
// Small headroom for the multipart boundaries and the transcript/translation fields.
var requestBodyCeiling = configuredMaxBytes + (8L * 1024 * 1024);
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = requestBodyCeiling);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(form =>
{
    form.MultipartBodyLengthLimit = requestBodyCeiling;
    form.MultipartHeadersLengthLimit = 64 * 1024;
    form.ValueLengthLimit = int.MaxValue;   // the transcript and translation fields allow 2,000,000 characters
});

// Mobile audio quality and standardisation pipeline. Settings come from the Audio section so a
// buyer specification can override the delivery standard without a code change.
builder.Services.AddSingleton(sp =>
{
    var c = sp.GetRequiredService<IConfiguration>();
    var o = new AudioPipelineOptions();
    c.GetSection("Audio").Bind(o);
    o.FfmpegPath = c["Audio:FfmpegPath"] ?? o.FfmpegPath;
    o.FfprobePath = c["Audio:FfprobePath"] ?? o.FfprobePath;
    o.MinSeconds = c.GetValue("Audio:MinSeconds", o.MinSeconds);
    o.MaxSeconds = c.GetValue("Audio:MaxSeconds", o.MaxSeconds);
    return o;
});
builder.Services.AddSingleton<AudioStoragePaths>();
builder.Services.AddSingleton<AudioPipeline>();
builder.Services.AddSingleton<AudioProcessingQueue>();
if (databaseConfigured)
    builder.Services.AddHostedService<AudioProcessingWorker>();

var app = builder.Build();
app.UseCors();
app.Use(async (context, next) =>
{
    if (!databaseConfigured && context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new
        {
            title = "Database configuration missing",
            status = StatusCodes.Status503ServiceUnavailable,
            detail = "Database-backed API routes are unavailable until a database connection is configured."
        });
        return;
    }

    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
if (!databaseConfigured)
    app.Logger.LogWarning("Database configuration is missing; health and static routes remain available, and /api requests return 503.");
var dataDirectory = Path.GetFullPath(Environment.GetEnvironmentVariable("ELDF_DATA_DIR") ?? builder.Configuration["DataDirectory"] ?? "../../eldf-server-data", app.Environment.ContentRootPath);
Directory.CreateDirectory(dataDirectory);
var audioDirectory = Path.Combine(dataDirectory, "recordings");
Directory.CreateDirectory(audioDirectory);
var maxBytes = configuredMaxBytes;
var minSeconds = builder.Configuration.GetValue("Audio:MinSeconds", 1800);
var maxSeconds = builder.Configuration.GetValue("Audio:MaxSeconds", 3600);
var ffmpegPath = builder.Configuration["Audio:FfmpegPath"] ?? "ffmpeg";
var ffprobePath = builder.Configuration["Audio:FfprobePath"] ?? "ffprobe";

// Schema changes are managed exclusively through EF Core migrations. Normal application startup
// never creates or migrates a database; an operator must explicitly set this process environment
// variable to "true" for a deliberate migration run against the already-configured target.
var applyMigrations = string.Equals(
    Environment.GetEnvironmentVariable("ELDF_APPLY_MIGRATIONS"),
    "true",
    StringComparison.OrdinalIgnoreCase);
var migrationsOnly = string.Equals(
    Environment.GetEnvironmentVariable("ELDF_MIGRATIONS_ONLY"),
    "true",
    StringComparison.OrdinalIgnoreCase);

if (migrationsOnly && (!applyMigrations || !databaseConfigured))
    throw new InvalidOperationException("ELDF_MIGRATIONS_ONLY requires ELDF_APPLY_MIGRATIONS=true and a configured database.");

if (applyMigrations && databaseConfigured)
{
    app.Logger.LogWarning("Database migrations explicitly enabled with ELDF_APPLY_MIGRATIONS=true.");
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<EldfDbContext>();
    await db.Database.MigrateAsync();
    if (migrationsOnly)
    {
        app.Logger.LogInformation("Database migration completed; migrations-only process is exiting before hosted services start.");
        return;
    }
}
else if (applyMigrations)
{
    app.Logger.LogError("Database migrations were requested but skipped because database configuration is missing.");
}
else
{
    app.Logger.LogInformation("Database migrations skipped; set ELDF_APPLY_MIGRATIONS=true only for a deliberate migration run.");
}

app.MapGet("/health", () => Results.Json(new
{
    status = "ok",
    service = "AfriLisan AI API",
    database = databaseConfigured ? "configured_not_checked" : "not_configured"
}));

app.MapPost("/api/auth/register", async (HttpRequest request, EldfDbContext db) =>
{
    var form = await request.ReadFormAsync();
    var name = (form["name"].ToString()).Trim();
    var phone = NormalizePhone(form["phone"].ToString());
    var password = form["password"].ToString();
    var language = form["language"].ToString() is "am" or "en" ? form["language"].ToString() : "am";
    if (!Checked(form["terms"].ToString()) || !Checked(form["consent"].ToString())) return Problem(422, "Accept the terms and audio consent before creating an account.");
    if (name.Length < 2 || phone.Length < 7 || password.Length < 10) return Problem(422, "Enter your name, a valid phone number, and a password with at least 10 characters.");
    if (await db.Users.AnyAsync(x => x.Phone == phone)) return Problem(409, "An account already uses this phone number.");
    var role = StaffRole(phone, form["staff_code"].ToString());
    var suppliedReferral = NormalizeReferralCode(form["referral_code"].ToString());
    AgentReferral? referrer = null;
    if (suppliedReferral.Length > 0 && role == "contributor")
    {
        referrer = await db.AgentReferrals.Include(x => x.Agent).SingleOrDefaultAsync(x => x.Code == suppliedReferral && x.Agent.Role == "agent" && x.Agent.IsActive);
        if (referrer is null) return Problem(422, "That agent referral link or code is not valid.");
    }
    var user = new UserAccount { FullName = name, Phone = phone, PasswordHash = HashPassword(password), InterfaceLanguage = language, ReferredByAgentId = referrer?.AgentUserId, Role = role };
    db.Users.Add(user);
    try { await db.SaveChangesAsync(); }
    catch (DbUpdateException) { return Problem(409, "An account already uses this phone number."); }
    if (role == "agent") await AgentReferralCodes.EnsureAsync(db, user);
    await WorkflowEvents.RecordAsync(db, user, "Account registered", "User", user.Id.ToString(), role);
    return Results.Json(new { user = View(user) });
});

app.MapPost("/api/auth/login", async (HttpRequest request, EldfDbContext db) =>
{
    var form = await request.ReadFormAsync();
    var phone = NormalizePhone(form["phone"].ToString());
    var user = await db.Users.SingleOrDefaultAsync(x => x.Phone == phone);
    if (user is null || !user.IsActive || !VerifyPassword(form["password"].ToString(), user.PasswordHash)) return Problem(401, "Phone number or password is incorrect.");
    string? agentNotice = null;
    if (user.Role == "agent")
    {
        var assignment = await db.ActivityEvents.AsNoTracking()
            .Where(x => x.EventType == "Agent role assigned" && x.EntityType == "User" && x.EntityId == user.Id.ToString())
            .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync();
        if (assignment is not null)
        {
            var alreadyNotified = await db.ActivityEvents.AnyAsync(x => x.EventType == "Agent access notification delivered" && x.EntityType == "User" && x.EntityId == user.Id.ToString() && x.CreatedAtUtc >= assignment.CreatedAtUtc);
            if (!alreadyNotified)
            {
                agentNotice = "Your account now has agent access. Sign in with your existing phone number and password. Your referral link is available in the Agent workspace.";
                db.ActivityEvents.Add(new Eldf.Api.ActivityEvent { ActorUserId = user.Id, ActorRole = "agent", EventType = "Agent access notification delivered", EntityType = "User", EntityId = user.Id.ToString(), Details = "Agent workspace access notice shown after sign-in.", CreatedAtUtc = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
        }
    }
    await IssueSession(db, user, request, app);
    return Results.Json(new { user = View(user), agentNotice });
});

app.MapGet("/api/auth/me", async (HttpRequest request, EldfDbContext db) =>
{
    var user = await SessionAuth.CurrentUser(request, db);
    return user is null ? Problem(401, "Please sign in to continue.") : Results.Json(new { user = View(user) });
});

app.MapPost("/api/auth/logout", async (HttpRequest request, EldfDbContext db) =>
{
    var raw = request.Cookies["eldf_session"];
    if (!string.IsNullOrWhiteSpace(raw))
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        var session = await db.Sessions.SingleOrDefaultAsync(x => x.TokenHash == hash);
        if (session is not null) { db.Sessions.Remove(session); await db.SaveChangesAsync(); }
    }
    request.HttpContext.Response.Cookies.Delete("eldf_session", new CookieOptions { Path = "/", SameSite = SameSiteMode.Lax });
    return Results.Json(new { ok = true });
});

app.MapGet("/api/speakers", async (HttpRequest request, EldfDbContext db) =>
{
    var user = await SessionAuth.CurrentUser(request, db);
    if (user is null) return Problem(401, "Please sign in to continue.");
    if (user.Role != "contributor") return Problem(403, "Contributor access is required.");
    var profiles = await db.Speakers.AsNoTracking().Where(x => x.UserAccountId == user.Id).OrderBy(x => x.Id).ToListAsync();
    var result = new List<object>();
    foreach (var speaker in profiles)
    {
        var consent = await db.SpeakerConsents.AsNoTracking().Where(x => x.UserAccountId == user.Id && x.SpeakerProfileId == speaker.Id)
            .OrderByDescending(x => x.ConsentedAtUtc).FirstOrDefaultAsync();
        result.Add(new { speakerId = speaker.SpeakerId, name = speaker.FullName, ageRange = speaker.AgeRange, gender = speaker.Gender,
            educationLevel = speaker.Education, preferredLanguage = speaker.Language, region = speaker.Region, dialect = speaker.Dialect,
            coreConsent = consent is { CoreConsent: true, WithdrawnAtUtc: null }, aiDevelopmentConsent = consent?.AiDevelopmentConsent ?? false,
            researchSharingConsent = consent?.ResearchSharingConsent ?? false, consentVersion = consent?.ConsentVersion,
            consentLanguage = consent?.Language });
    }
    return Results.Json(result);
});

app.MapPost("/api/speakers", async (HttpRequest request, EldfDbContext db) =>
{
    const string consentVersion = "speaker-consent-v1";
    var user = await SessionAuth.CurrentUser(request, db);
    if (user is null) return Problem(401, "Please sign in to continue.");
    if (user.Role != "contributor") return Problem(403, "Contributor access is required.");
    using var payload = await JsonDocument.ParseAsync(request.Body);
    if (!payload.RootElement.TryGetProperty("speakers", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() < 1)
        return Problem(422, "At least one speaker profile is required.");
    var entries = JsonSerializer.Deserialize<List<SpeakerInput>>(list.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    var existing = await db.Speakers.Where(x => x.UserAccountId == user.Id).ToListAsync();
    var existingSpeakerIds = existing.Select(x => x.Id).ToArray();
    var latestConsents = existingSpeakerIds.Length == 0 ? [] : await db.SpeakerConsents
        .Where(x => x.UserAccountId == user.Id && existingSpeakerIds.Contains(x.SpeakerProfileId))
        .OrderByDescending(x => x.ConsentedAtUtc).ToListAsync();
    var latestConsentBySpeaker = latestConsents.GroupBy(x => x.SpeakerProfileId)
        .ToDictionary(group => group.Key, group => group.First());
    for (var i = 0; i < entries.Count; i++)
    {
        var entry = entries[i];
        if (string.IsNullOrWhiteSpace(entry.name) || string.IsNullOrWhiteSpace(entry.ageRange)
            || string.IsNullOrWhiteSpace(entry.preferredLanguage))
            return Problem(422, "Enter each speaker's name, age range, and preferred language.");
        if (!entry.CoreConsent || entry.ConsentVersion != consentVersion)
            return Problem(422, "Current speaker consent is required before continuing.");
        var speakerId = Limit(entry.speakerId, 40, $"SPK-{i + 1:000}");
        var speaker = existing.FirstOrDefault(x => x.SpeakerId == speakerId);
        if (speaker is null) { speaker = new SpeakerProfile { UserAccountId = user.Id, SpeakerId = speakerId }; db.Speakers.Add(speaker); }
        speaker.FullName = Limit(entry.name, 160, speaker.FullName);
        speaker.AgeRange = Limit(entry.ageRange, 40); speaker.Gender = Limit(entry.gender, 40); speaker.Education = Limit(entry.educationLevel, 100);
        speaker.Language = Limit(entry.preferredLanguage, 160); speaker.Region = Limit(entry.region, 160);
        // The form stopped asking for a dialect; keep whatever an older client still sends.
        speaker.Dialect = Limit(entry.dialect, 160, speaker.Dialect);
        var consentLanguage = entry.ConsentLanguage == "en" ? "en" : "am";
        var alreadyRecorded = speaker.Id != 0 && latestConsentBySpeaker.TryGetValue(speaker.Id, out var latestConsent)
            && latestConsent.WithdrawnAtUtc is null && latestConsent.ConsentVersion == consentVersion
            && latestConsent.Language == consentLanguage && latestConsent.CoreConsent
            && latestConsent.AiDevelopmentConsent == entry.AiDevelopmentConsent
            && latestConsent.ResearchSharingConsent == entry.ResearchSharingConsent;
        if (!alreadyRecorded) db.SpeakerConsents.Add(new SpeakerConsentRecord
        {
            UserAccountId = user.Id, Speaker = speaker, ConsentVersion = consentVersion,
            Language = consentLanguage, CoreConsent = true,
            AiDevelopmentConsent = entry.AiDevelopmentConsent, ResearchSharingConsent = entry.ResearchSharingConsent,
            // Public release remains disabled until the consent license has received legal approval.
            PublicReleaseConsent = false, ConsentedAtUtc = DateTime.UtcNow
        });
    }
    await db.SaveChangesAsync();
    return Results.Json(new { ok = true, count = entries.Count });
});

app.MapPost("/api/speakers/{speakerId}/consent/withdrawal", async (string speakerId, HttpRequest request, EldfDbContext db) =>
{
    var user = await SessionAuth.CurrentUser(request, db);
    if (user is null) return Problem(401, "Please sign in to continue.");
    if (user.Role != "contributor") return Problem(403, "Contributor access is required.");
    var speaker = await db.Speakers.SingleOrDefaultAsync(x => x.UserAccountId == user.Id && x.SpeakerId == speakerId);
    if (speaker is null) return Problem(404, "Speaker profile was not found.");
    var latest = await db.SpeakerConsents.Where(x => x.UserAccountId == user.Id && x.SpeakerProfileId == speaker.Id)
        .OrderByDescending(x => x.ConsentedAtUtc).FirstOrDefaultAsync();
    if (latest is not null && latest.WithdrawnAtUtc is null)
    {
        latest.WithdrawnAtUtc = DateTime.UtcNow;
        db.ActivityEvents.Add(new Eldf.Api.ActivityEvent { ActorUserId = user.Id, ActorRole = user.Role, EventType = "Speaker consent withdrawn",
            EntityType = "Speaker", EntityId = speakerId, Details = "Core speaker consent withdrawal recorded.", CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }
    return Results.Json(new { ok = true, withdrawnAtUtc = latest?.WithdrawnAtUtc });
});

app.MapPost("/api/submissions", async (HttpRequest request, EldfDbContext db) =>
{
    var user = await SessionAuth.CurrentUser(request, db);
    if (user is null) return Problem(401, "Please sign in to continue.");
    if (user.Role != "contributor") return Problem(403, "Contributor access is required.");
    var form = await request.ReadFormAsync();
    WorkDraft? sourceDraft = null;
    var draftOwnerId = user.Id;
    if (int.TryParse(form["draft_id"], out var draftId) && draftId > 0)
    {
        sourceDraft = await db.Drafts.SingleOrDefaultAsync(x => x.Id == draftId && (x.UserAccountId == user.Id || x.AssignedUserAccountId == user.Id));
        if (sourceDraft is null) return Problem(404, "The assigned draft was not found.");
        draftOwnerId = sourceDraft.UserAccountId;
    }
    try
    {
        var ids = JsonSerializer.Deserialize<List<string>>(form["speakers_json"].ToString()) ?? [];
        if (ids.Count == 0) return Problem(422, "Select speakers with current consent before submitting.");
        foreach (var speakerId in ids.Distinct(StringComparer.Ordinal))
        {
            var speaker = await db.Speakers.SingleOrDefaultAsync(x => x.UserAccountId == draftOwnerId && x.SpeakerId == speakerId);
            if (speaker is null) return Problem(422, "A selected speaker profile was not found.");
            var currentConsent = await db.SpeakerConsents.Where(x => x.UserAccountId == draftOwnerId && x.SpeakerProfileId == speaker.Id)
                .OrderByDescending(x => x.ConsentedAtUtc).FirstOrDefaultAsync();
            if (currentConsent is null || !currentConsent.CoreConsent || currentConsent.WithdrawnAtUtc is not null || currentConsent.ConsentVersion != "speaker-consent-v1")
                return Problem(422, "Current speaker consent is required before submitting this recording.");
        }
    }
    catch (JsonException) { return Problem(422, "Select speakers with current consent before submitting."); }
    var audioSegments = form.Files.GetFiles("audio_segments");
    var file = form.Files.GetFile("file");
    if (audioSegments.Count > 0 && file is not null) return Problem(422, "Upload either one recording or its recording parts, not both.");
    IReadOnlyList<IFormFile> audioFiles = audioSegments.Count > 0
        ? audioSegments
        : file is null ? Array.Empty<IFormFile>() : [file];
    if (audioFiles.Count == 0 || audioFiles.Count > 100 || audioFiles.Any(item => item.Length == 0) || audioFiles.Sum(item => item.Length) > maxBytes)
        return Problem(413, "The recording is empty, has too many parts, or is larger than the server upload limit.");
    var transcript = form["transcript"].ToString();
    var translation = form["translation"].ToString();
    if (string.IsNullOrWhiteSpace(transcript) || string.IsNullOrWhiteSpace(translation)) return Problem(422, "Add the transcription and translation before submitting.");
    var safeExtension = audioFiles.Count > 1 ? ".webm" : Path.GetExtension(Path.GetFileName(audioFiles[0].FileName)).ToLowerInvariant();
    if (safeExtension.Length > 10 || safeExtension.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '.')) safeExtension = ".audio";
    var tempPath = Path.Combine(Path.GetTempPath(), $"eldf-{Guid.NewGuid():N}{safeExtension}");
    var partPaths = new List<string>();
    try
    {
        if (audioFiles.Count == 1)
        {
            await using var output = File.Create(tempPath);
            await audioFiles[0].CopyToAsync(output);
        }
        else
        {
            foreach (var audioFile in audioFiles)
            {
                var partExtension = audioFile.ContentType.Contains("ogg", StringComparison.OrdinalIgnoreCase) ? ".ogg"
                    : audioFile.ContentType.Contains("mp4", StringComparison.OrdinalIgnoreCase) ? ".m4a"
                    : audioFile.ContentType.Contains("wav", StringComparison.OrdinalIgnoreCase) ? ".wav" : ".webm";
                var partPath = Path.Combine(Path.GetTempPath(), $"eldf-part-{Guid.NewGuid():N}{partExtension}");
                partPaths.Add(partPath);
                await using var output = File.Create(partPath);
                await audioFile.CopyToAsync(output);
            }
            var arguments = new List<string> { "-y" };
            foreach (var partPath in partPaths) arguments.AddRange(["-i", partPath]);
            var streams = string.Concat(Enumerable.Range(0, partPaths.Count).Select(index => $"[{index}:a:0]"));
            arguments.AddRange(["-filter_complex", $"{streams}concat=n={partPaths.Count}:v=0:a=1[outa]", "-map", "[outa]", "-vn", "-c:a", "libopus", "-b:a", "64k", "-ac", "1", "-ar", "48000", tempPath]);
            await RunProcess(ffmpegPath, arguments.ToArray());
        }
        var seconds = await GetAudioDuration(tempPath, ffprobePath);
        if (seconds < minSeconds) return Problem(422, "The actual audio file is shorter than 30 minutes and cannot be submitted.");
        if (seconds > maxSeconds) return Problem(422, "The recording exceeds the server's maximum duration.");
        var userFolder = Path.Combine(audioDirectory, draftOwnerId.ToString());
        Directory.CreateDirectory(userFolder);
        var fileName = Guid.NewGuid().ToString("N") + (new[] { ".webm", ".wav", ".ogg", ".m4a", ".mp3" }.Contains(safeExtension) ? safeExtension : ".audio");
        var targetPath = Path.Combine(userFolder, fileName);
        File.Move(tempPath, targetPath);
        var draftOwner = draftOwnerId == user.Id ? user : await db.Users.SingleAsync(x => x.Id == draftOwnerId);
        var submission = new WorkSubmission { UserAccountId = draftOwner.Id, AssignedAgentId = draftOwner.ReferredByAgentId, ProjectLanguage = Limit(form["language"], 160), DurationSeconds = seconds, AudioPath = Path.GetRelativePath(dataDirectory, targetPath), Transcript = transcript, Translation = translation, Status = draftOwner.ReferredByAgentId.HasValue ? "Awaiting agent review" : "Awaiting admin review", UpdatedAtUtc = DateTime.UtcNow };
        db.Submissions.Add(submission);
        await db.SaveChangesAsync();
        await WorkflowEvents.RecordAsync(db, user, "Work submitted", "Submission", submission.Id.ToString(), submission.ProjectLanguage);

        // Queue the quality pipeline instead of running it inline: standardising a 30 to 60
        // minute recording takes far longer than a request should. The upload is already saved,
        // so the contributor is never left waiting and never gets a duplicate submission.
        var queue = request.HttpContext.RequestServices.GetRequiredService<AudioProcessingQueue>();
        db.AudioProcessing.Add(new AudioProcessingRecord { SubmissionId = submission.Id, Status = "queued" });
        await db.SaveChangesAsync();
        await queue.EnqueueAsync(submission.Id);
        if (sourceDraft is not null)
        {
            db.Drafts.Remove(sourceDraft);
            await db.SaveChangesAsync();
            var draftPath = Path.GetFullPath(Path.Combine(dataDirectory, sourceDraft.AudioPath));
            var draftsRoot = Path.GetFullPath(Path.Combine(dataDirectory, "drafts")) + Path.DirectorySeparatorChar;
            if (draftPath.StartsWith(draftsRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(draftPath)) File.Delete(draftPath);
        }

        return Results.Json(new { ok = true, submissionId = submission.Id, status = submission.Status, minimumSeconds = minSeconds, audioQuality = "queued" });
    }
    catch (InvalidOperationException ex) { return Problem(422, ex.Message); }
    finally
    {
        if (File.Exists(tempPath)) File.Delete(tempPath);
        foreach (var partPath in partPaths) if (File.Exists(partPath)) File.Delete(partPath);
    }
});

app.MapDelete("/api/submissions/{id:int}", async (int id, HttpRequest request, EldfDbContext db) =>
{
    var user = await SessionAuth.CurrentUser(request, db);
    if (user is null) return Problem(401, "Please sign in to continue.");
    var submission = await db.Submissions.SingleOrDefaultAsync(x => x.Id == id && (x.UserAccountId == user.Id || user.Role == "admin"));
    if (submission is null) return Problem(404, "Recording was not found.");
    db.Submissions.Remove(submission);
    await db.SaveChangesAsync();
    var fullPath = Path.GetFullPath(Path.Combine(dataDirectory, submission.AudioPath));
    if (fullPath.StartsWith(audioDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath)) File.Delete(fullPath);
    return Results.Json(new { ok = true });
});

app.MapPost("/api/audio/process", async (HttpRequest request, EldfDbContext db) =>
{
    var user = await SessionAuth.CurrentUser(request, db);
    if (user is null) return Problem(401, "Please sign in to continue.");
    if (user.Role != "contributor") return Problem(403, "Contributor access is required.");
    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0 || file.Length > maxBytes) return Problem(413, "The audio file is empty or too large.");
    var tempDirectory = Path.Combine(Path.GetTempPath(), $"eldf-process-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDirectory);
    var input = Path.Combine(tempDirectory, "input" + Path.GetExtension(file.FileName));
    var output = Path.Combine(tempDirectory, "prepared.wav");
    try
    {
        await using (var stream = File.Create(input)) await file.CopyToAsync(stream);
        var duration = await GetAudioDuration(input, ffprobePath);
        if (duration > maxSeconds) return Problem(422, $"Audio is longer than the {maxSeconds / 60}-minute processing limit.");
        // Mild FFmpeg denoising, edge-silence trim, mono 48 kHz PCM, normalized to -3 dB peak.
        await RunProcess(ffmpegPath, ["-y", "-i", input, "-af", "silenceremove=start_periods=1:start_duration=0.3:start_threshold=-40dB:stop_periods=1:stop_duration=0.3:stop_threshold=-40dB,afftdn=nf=-25,loudnorm=I=-20:TP=-3:LRA=11", "-ac", "1", "-ar", "48000", "-c:a", "pcm_s16le", output]);
        var bytes = await File.ReadAllBytesAsync(output);
        var response = Results.File(bytes, "audio/wav", "prepared.wav");
        request.HttpContext.Response.Headers["X-Audio-Duration"] = duration.ToString();
        request.HttpContext.Response.Headers["X-Audio-Trimmed"] = "true";
        return response;
    }
    catch (InvalidOperationException ex) { return Problem(422, ex.Message); }
    finally { if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true); }
});

app.MapWorkflowApi();

// Unknown /api routes must answer with JSON 404, not the SPA shell. Serving index.html with HTTP 200
// for a missing endpoint made the frontend treat a routing mistake as a successful response.
app.MapFallback("/api/{*rest}", () => Results.Json(new { detail = "This API endpoint does not exist." }, statusCode: 404));
app.MapFallbackToFile("index.html");

app.Run();

static IResult Problem(int status, string detail) => Results.Json(new { detail }, statusCode: status);
static string NormalizePhone(string phone)
{
    var digits = string.Concat(phone.Where(char.IsDigit));
    if (digits.StartsWith("251", StringComparison.Ordinal) && digits.Length == 12) return "+" + digits;
    if (digits.StartsWith('0') && digits.Length == 10) return "+251" + digits[1..];
    return digits.Length == 9 ? "+251" + digits : phone.Trim();
}
static bool Checked(string value) => value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("on", StringComparison.OrdinalIgnoreCase) || value == "1";
static string Limit(string? value, int length, string fallback = "") => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim()[..Math.Min(value.Trim().Length, length)];
static string StaffRole(string phone, string code)
{
    var adminPhones = (Environment.GetEnvironmentVariable("ELDF_ADMIN_PHONES") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var agentPhones = (Environment.GetEnvironmentVariable("ELDF_AGENT_PHONES") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var adminToken = Environment.GetEnvironmentVariable("ELDF_ADMIN_ENROLLMENT_TOKEN") ?? "";
    var agentToken = Environment.GetEnvironmentVariable("ELDF_AGENT_ENROLLMENT_TOKEN") ?? "";
    if (adminToken.Length > 0 && adminPhones.Contains(phone) && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(code), Encoding.UTF8.GetBytes(adminToken))) return "admin";
    if (agentToken.Length > 0 && agentPhones.Contains(phone) && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(code), Encoding.UTF8.GetBytes(agentToken))) return "agent";
    return "contributor";
}
static string HashPassword(string password)
{
    var salt = RandomNumberGenerator.GetBytes(16);
    var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 310_000, HashAlgorithmName.SHA256, 32);
    return $"pbkdf2_sha256$310000${Convert.ToHexString(salt).ToLowerInvariant()}${Convert.ToHexString(hash).ToLowerInvariant()}";
}
static bool VerifyPassword(string password, string stored)
{
    try
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2_sha256") return false;
        var expected = Convert.FromHexString(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromHexString(parts[2]), int.Parse(parts[1]), HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
    catch { return false; }
}
static UserView View(UserAccount user) => new(user.Id, user.FullName, user.Phone, user.Role, user.InterfaceLanguage);
static async Task IssueSession(EldfDbContext db, UserAccount user, HttpRequest request, WebApplication app)
{
    var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(40)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    db.Sessions.Add(new LoginSession { UserAccountId = user.Id, TokenHash = tokenHash, ExpiresAtUtc = DateTime.UtcNow.AddDays(14) });
    await db.SaveChangesAsync();
    var isHttps = request.IsHttps || app.Environment.IsProduction() || string.Equals(Environment.GetEnvironmentVariable("ELDF_COOKIE_SECURE"), "true", StringComparison.OrdinalIgnoreCase) || app.Configuration.GetValue("Cookies:Secure", false);
    request.HttpContext.Response.Cookies.Append("eldf_session", token, new CookieOptions { HttpOnly = true, Secure = isHttps, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromDays(14), Path = "/" });
}
static string NormalizeReferralCode(string value)
{
    value = value.Trim();
    if (Uri.TryCreate(value, UriKind.Absolute, out var uri)) value = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query)["ref"].ToString();
    return value.Trim().ToLowerInvariant();
}
static async Task<int> GetAudioDuration(string file, string ffprobePath)
{
    var output = await RunProcess(ffprobePath, ["-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", file]);
    if (!double.TryParse(output.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) || !double.IsFinite(seconds) || seconds < 0)
        throw new InvalidOperationException("The server could not read the audio duration. Check that FFmpeg and FFprobe are installed and that the audio format is supported.");
    return (int)Math.Round(seconds);
}
static async Task<string> RunProcess(string executable, string[] arguments)
{
    try
    {
        using var process = new Process { StartInfo = new ProcessStartInfo { FileName = executable, RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true } };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? "Audio processing failed." : stderr.Trim());
        return stdout;
    }
    catch (System.ComponentModel.Win32Exception) { throw new InvalidOperationException($"Required audio tool '{executable}' was not found. Install FFmpeg and make ffmpeg/ffprobe available on PATH."); }
}
