using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Eldf.Api;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = Environment.GetEnvironmentVariable("ELDF_SQLSERVER_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Set ELDF_SQLSERVER_CONNECTION_STRING (or ConnectionStrings:DefaultConnection) before starting the API.");

builder.Services.AddDbContext<EldfDbContext>(options => options.UseSqlServer(connectionString));
var origins = (Environment.GetEnvironmentVariable("ELDF_ALLOWED_ORIGINS") ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
if (origins.Length == 0) origins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials().WithExposedHeaders("X-Audio-Duration", "X-Audio-Trimmed")));
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ELDF_URLS") ?? "http://localhost:8001");

var app = builder.Build();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
var dataDirectory = Path.GetFullPath(Environment.GetEnvironmentVariable("ELDF_DATA_DIR") ?? builder.Configuration["DataDirectory"] ?? "../../eldf-server-data", app.Environment.ContentRootPath);
Directory.CreateDirectory(dataDirectory);
var audioDirectory = Path.Combine(dataDirectory, "recordings");
Directory.CreateDirectory(audioDirectory);
var maxBytes = builder.Configuration.GetValue<long>("Audio:MaxBytes", 250L * 1024 * 1024);
var minSeconds = builder.Configuration.GetValue("Audio:MinSeconds", 1800);
var maxSeconds = builder.Configuration.GetValue("Audio:MaxSeconds", 3600);
var ffmpegPath = builder.Configuration["Audio:FfmpegPath"] ?? "ffmpeg";
var ffprobePath = builder.Configuration["Audio:FfprobePath"] ?? "ffprobe";

// Creates the initial tables for a new database. Use EF migrations for later schema changes.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<EldfDbContext>();
    await db.Database.EnsureCreatedAsync();
    await SeedAsync(db);
}

app.MapGet("/health", () => Results.Json(new { status = "ok", service = "ELDF .NET API" }));

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
    var user = new UserAccount { FullName = name, Phone = phone, PasswordHash = HashPassword(password), InterfaceLanguage = language, ReferralCode = form["referral_code"].ToString().Trim(), Role = role };
    db.Users.Add(user);
    try { await db.SaveChangesAsync(); }
    catch (DbUpdateException) { return Problem(409, "An account already uses this phone number."); }
    await IssueSession(db, user, request, app);
    return Results.Json(new { user = View(user) });
});

app.MapPost("/api/auth/login", async (HttpRequest request, EldfDbContext db) =>
{
    var form = await request.ReadFormAsync();
    var phone = NormalizePhone(form["phone"].ToString());
    var user = await db.Users.SingleOrDefaultAsync(x => x.Phone == phone);
    if (user is null || !VerifyPassword(form["password"].ToString(), user.PasswordHash)) return Problem(401, "Phone number or password is incorrect.");
    await IssueSession(db, user, request, app);
    return Results.Json(new { user = View(user) });
});

app.MapGet("/api/auth/me", async (HttpRequest request, EldfDbContext db) =>
{
    var user = await CurrentUser(request, db);
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

app.MapPost("/api/speakers", async (HttpRequest request, EldfDbContext db) =>
{
    var user = await CurrentUser(request, db);
    if (user is null) return Problem(401, "Please sign in to continue.");
    using var payload = await JsonDocument.ParseAsync(request.Body);
    if (!payload.RootElement.TryGetProperty("speakers", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() < 2)
        return Problem(422, "At least two speaker profiles are required.");
    var entries = JsonSerializer.Deserialize<List<SpeakerInput>>(list.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    db.Speakers.RemoveRange(db.Speakers.Where(x => x.UserAccountId == user.Id));
    for (var i = 0; i < entries.Count; i++)
    {
        var entry = entries[i];
        db.Speakers.Add(new SpeakerProfile { UserAccountId = user.Id, SpeakerId = Limit(entry.speakerId, 40, $"SPK-{i + 1:000}"), AgeRange = Limit(entry.ageRange, 40), Gender = Limit(entry.gender, 40), Education = Limit(entry.educationLevel, 100), Language = Limit(entry.preferredLanguage, 160), Region = Limit(entry.region, 160), Dialect = Limit(entry.dialect, 160) });
    }
    await db.SaveChangesAsync();
    return Results.Json(new { ok = true, count = entries.Count });
});

app.MapPost("/api/submissions", async (HttpRequest request, EldfDbContext db) =>
{
    var user = await CurrentUser(request, db);
    if (user is null) return Problem(401, "Please sign in to continue.");
    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0 || file.Length > maxBytes) return Problem(413, "The recording is empty or larger than the server upload limit.");
    var transcript = form["transcript"].ToString();
    var translation = form["translation"].ToString();
    if (string.IsNullOrWhiteSpace(transcript) || string.IsNullOrWhiteSpace(translation)) return Problem(422, "Add the transcription and translation before submitting.");
    var safeExtension = Path.GetExtension(Path.GetFileName(file.FileName)).ToLowerInvariant();
    if (safeExtension.Length > 10 || safeExtension.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '.')) safeExtension = ".audio";
    var tempPath = Path.Combine(Path.GetTempPath(), $"eldf-{Guid.NewGuid():N}{safeExtension}");
    try
    {
        await using (var output = File.Create(tempPath)) await file.CopyToAsync(output);
        var seconds = await GetAudioDuration(tempPath, ffprobePath);
        if (seconds < minSeconds) return Problem(422, "The actual audio file is shorter than 30 minutes and cannot be submitted.");
        if (seconds > maxSeconds) return Problem(422, "The recording exceeds the server's maximum duration.");
        var userFolder = Path.Combine(audioDirectory, user.Id.ToString());
        Directory.CreateDirectory(userFolder);
        var fileName = Guid.NewGuid().ToString("N") + (new[] { ".webm", ".wav", ".ogg", ".m4a", ".mp3" }.Contains(safeExtension) ? safeExtension : ".audio");
        var targetPath = Path.Combine(userFolder, fileName);
        File.Move(tempPath, targetPath);
        var submission = new WorkSubmission { UserAccountId = user.Id, ProjectLanguage = Limit(form["language"], 160), DurationSeconds = seconds, AudioPath = Path.GetRelativePath(dataDirectory, targetPath), Transcript = transcript, Translation = translation };
        db.Submissions.Add(submission);
        await db.SaveChangesAsync();
        return Results.Json(new { ok = true, submissionId = submission.Id, status = submission.Status, minimumSeconds = minSeconds });
    }
    catch (InvalidOperationException ex) { return Problem(422, ex.Message); }
    finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
});

app.MapDelete("/api/submissions/{id:int}", async (int id, HttpRequest request, EldfDbContext db) =>
{
    var user = await CurrentUser(request, db);
    if (user is null) return Problem(401, "Please sign in to continue.");
    var submission = await db.Submissions.SingleOrDefaultAsync(x => x.Id == id && (x.UserAccountId == user.Id || user.Role == "admin"));
    if (submission is null) return Problem(404, "Recording was not found.");
    db.Submissions.Remove(submission);
    await db.SaveChangesAsync();
    var fullPath = Path.GetFullPath(Path.Combine(dataDirectory, submission.AudioPath));
    if (fullPath.StartsWith(audioDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath)) File.Delete(fullPath);
    return Results.Json(new { ok = true });
});

app.MapPost("/api/audio/process", async (HttpRequest request) =>
{
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

app.MapFallbackToFile("index.html");

app.Run();

static async Task SeedAsync(EldfDbContext db)
{
    if (await db.Users.AnyAsync()) return;

    var users = new[]
    {
        new UserAccount { FullName = "Abebe Girma",   Phone = "+251911000001", Role = "admin",       InterfaceLanguage = "am", ReferralCode = "",       PasswordHash = HashPassword("Admin@2026!!") },
        new UserAccount { FullName = "Tigist Haile",  Phone = "+251911000002", Role = "agent",       InterfaceLanguage = "am", ReferralCode = "",       PasswordHash = HashPassword("Agent@2026!!") },
        new UserAccount { FullName = "Dawit Bekele",  Phone = "+251911000003", Role = "contributor", InterfaceLanguage = "am", ReferralCode = "REF-001", PasswordHash = HashPassword("Contrib@2026!!") },
        new UserAccount { FullName = "Meron Tadesse", Phone = "+251911000004", Role = "contributor", InterfaceLanguage = "en", ReferralCode = "REF-001", PasswordHash = HashPassword("Contrib@2026!!") },
    };
    db.Users.AddRange(users);
    await db.SaveChangesAsync();

    // Speakers for each contributor (minimum 2 required)
    var speakerData = new[]
    {
        new SpeakerProfile { UserAccountId = users[2].Id, SpeakerId = "SPK-001", AgeRange = "25-34", Gender = "Male",   Education = "Bachelor",      Language = "Amharic", Region = "Addis Ababa", Dialect = "Central" },
        new SpeakerProfile { UserAccountId = users[2].Id, SpeakerId = "SPK-002", AgeRange = "35-44", Gender = "Female", Education = "High School",   Language = "Amharic", Region = "Oromia",      Dialect = "Northern" },
        new SpeakerProfile { UserAccountId = users[3].Id, SpeakerId = "SPK-001", AgeRange = "18-24", Gender = "Female", Education = "Master",        Language = "Amharic", Region = "Tigray",      Dialect = "Eastern"  },
        new SpeakerProfile { UserAccountId = users[3].Id, SpeakerId = "SPK-002", AgeRange = "45-54", Gender = "Male",   Education = "PhD",           Language = "Amharic", Region = "SNNPR",       Dialect = "Southern" },
    };
    db.Speakers.AddRange(speakerData);

    // Sample submissions (audio path is a placeholder — no real file)
    var submissions = new[]
    {
        new WorkSubmission { UserAccountId = users[2].Id, ProjectLanguage = "Amharic", DurationSeconds = 1920, AudioPath = "recordings/sample/dawit_session1.wav",  Transcript = "ይህ የናሙና ጽሑፍ ነው።",   Translation = "This is a sample transcript.",  Status = "Awaiting review", CreatedAtUtc = DateTime.UtcNow.AddDays(-3) },
        new WorkSubmission { UserAccountId = users[3].Id, ProjectLanguage = "Amharic", DurationSeconds = 2100, AudioPath = "recordings/sample/meron_session1.wav",  Transcript = "ሌላ የናሙና ጽሑፍ።",     Translation = "Another sample transcript.",   Status = "Awaiting review", CreatedAtUtc = DateTime.UtcNow.AddDays(-1) },
    };
    db.Submissions.AddRange(submissions);
    await db.SaveChangesAsync();
}

static IResult Problem(int status, string detail) => Results.Json(new { detail }, statusCode: status);
static string NormalizePhone(string phone)
{
    phone = string.Concat(phone.Where(c => !char.IsWhiteSpace(c)));
    return phone.Length == 9 && phone.All(char.IsDigit) ? "+251" + phone : phone;
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
    var isHttps = request.IsHttps || string.Equals(Environment.GetEnvironmentVariable("ELDF_COOKIE_SECURE"), "true", StringComparison.OrdinalIgnoreCase) || app.Configuration.GetValue("Cookies:Secure", false);
    request.HttpContext.Response.Cookies.Append("eldf_session", token, new CookieOptions { HttpOnly = true, Secure = isHttps, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromDays(14), Path = "/" });
}
static async Task<UserAccount?> CurrentUser(HttpRequest request, EldfDbContext db)
{
    var token = request.Cookies["eldf_session"];
    if (string.IsNullOrWhiteSpace(token)) return null;
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    var session = await db.Sessions.Include(x => x.User).SingleOrDefaultAsync(x => x.TokenHash == hash && x.ExpiresAtUtc > DateTime.UtcNow);
    return session?.User;
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
