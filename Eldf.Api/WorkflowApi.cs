using System.Security.Cryptography;
using System.IO.Compression;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Eldf.Api;
using Microsoft.EntityFrameworkCore;

namespace Eldf.Api;

public static class SessionAuth
{
    public static async Task<UserAccount?> CurrentUser(HttpRequest request, EldfDbContext db)
    {
        var token = request.Cookies["eldf_session"];
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        var session = await db.Sessions.Include(x => x.User)
            .SingleOrDefaultAsync(x => x.TokenHash == hash && x.ExpiresAtUtc > DateTime.UtcNow);
        return session?.User is { IsActive: true } user ? user : null;
    }
}

public static class AgentReferralCodes
{
    public static async Task<AgentReferral> EnsureAsync(EldfDbContext db, UserAccount agent)
    {
        if (agent.Role != "agent" || !agent.IsActive) throw new InvalidOperationException("An active agent account is required.");
        var current = await db.AgentReferrals.SingleOrDefaultAsync(x => x.AgentUserId == agent.Id);
        if (current is not null) return current;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var entry = new AgentReferral
            {
                AgentUserId = agent.Id,
                Code = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant()
            };
            db.AgentReferrals.Add(entry);
            try
            {
                await db.SaveChangesAsync();
                return entry;
            }
            catch (DbUpdateException)
            {
                db.Entry(entry).State = EntityState.Detached;
                current = await db.AgentReferrals.SingleOrDefaultAsync(x => x.AgentUserId == agent.Id);
                if (current is not null) return current;
            }
        }
        throw new InvalidOperationException("A unique agent referral code could not be created.");
    }
}

public static class WorkflowEvents
{
    public static async Task RecordAsync(EldfDbContext db, UserAccount? actor, string type, string entity, string entityId, string details = "")
    {
        db.ActivityEvents.Add(new ActivityEvent
        {
            ActorUserId = actor?.Id,
            ActorRole = actor?.Role ?? "system",
            EventType = Limit(type, 100),
            EntityType = Limit(entity, 80),
            EntityId = Limit(entityId, 80),
            Details = Limit(details, 2000),
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public static string Limit(string? value, int max, string fallback = "") => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim()[..Math.Min(max, value.Trim().Length)];
}

public static class WorkflowApiExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string DefaultVision = "የአፍሪካ ቋንቋ ሁሉ በዲጂታል ዓለም ድምፁ ይሰማ።";
    private const string DefaultVisionEnglish = "Every African language deserves a voice in the digital world.";
    private const string DefaultMission = "ከአፍሪካ ማኅበረሰቦች ጋር በመስራት ቋንቋዎችን እንሰበስባለን፣ እናስቀምጣለን፣ ለትምህርትና ለዲጂታል አጠቃቀም እናዳርሳለን።";
    private const string DefaultMissionEnglish = "We work with African communities to collect, preserve, and share language data for learning and digital use.";

    public static void MapWorkflowApi(this WebApplication app)
    {
        app.MapPost("/api/auth/password-reset/request", async (HttpRequest request, EldfDbContext db) =>
        {
            var input = await request.ReadFromJsonAsync<PasswordResetRequestInput>(JsonOptions);
            var phone = NormalizeInternationalPhone(input?.Phone ?? "");
            if (phone.Length < 8 || phone.Length > 16 || phone[0] != '+') return Results.Accepted();
            var account = await db.Users.SingleOrDefaultAsync(x => x.Phone == phone && x.IsActive);
            if (account is null) return Results.Accepted();
            var recentRequest = await db.ActivityEvents.AnyAsync(x => x.EventType == "Password reset requested" && x.EntityType == "User" && x.EntityId == account.Id.ToString() && x.CreatedAtUtc > DateTime.UtcNow.AddMinutes(-15));
            if (!recentRequest) await WorkflowEvents.RecordAsync(db, null, "Password reset requested", "User", account.Id.ToString(), "Verify the caller using the registered phone before issuing a temporary password.");
            return Results.Accepted();
        });

        app.MapPost("/api/auth/change-password", async (HttpRequest request, EldfDbContext db) =>
        {
            var user = await SessionAuth.CurrentUser(request, db);
            if (user is null) return Problem(401, "Please sign in to continue.");
            var input = await request.ReadFromJsonAsync<PasswordChangeInput>(JsonOptions);
            var currentPassword = input?.CurrentPassword ?? "";
            var newPassword = input?.NewPassword ?? "";
            if (!VerifyPassword(currentPassword, user.PasswordHash)) return Problem(422, "The current password is incorrect.");
            if (newPassword.Length < 10) return Problem(422, "Use a password with at least 10 characters.");
            if (newPassword == currentPassword) return Problem(422, "Choose a new password different from the current one.");
            user.PasswordHash = HashPassword(newPassword);
            var sessionToken = request.Cookies["eldf_session"] ?? "";
            var currentTokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionToken))).ToLowerInvariant();
            var otherSessions = await db.Sessions.Where(x => x.UserAccountId == user.Id && x.TokenHash != currentTokenHash).ToListAsync();
            db.Sessions.RemoveRange(otherSessions);
            await db.SaveChangesAsync();
            await WorkflowEvents.RecordAsync(db, user, "Password changed", "User", user.Id.ToString());
            return Results.Json(new { ok = true });
        });

        app.MapPatch("/api/auth/admin-profile", async (HttpRequest request, EldfDbContext db) =>
        {
            var admin = await SessionAuth.CurrentUser(request, db);
            if (admin is null) return Problem(401, "Please sign in to continue.");
            if (admin.Role != "admin") return Problem(403, "Administrator access is required.");
            var input = await request.ReadFromJsonAsync<AdminProfileUpdateInput>(JsonOptions);
            var name = input?.FullName?.Trim() ?? "";
            var phone = NormalizeInternationalPhone(input?.Phone ?? "");
            if (name.Length is < 2 or > 160) return Problem(422, "Enter a name between 2 and 160 characters.");
            var phoneDigits = string.Concat(phone.Where(char.IsDigit));
            if (!phone.StartsWith('+') || phoneDigits.Length is < 8 or > 15) return Problem(422, "Enter a valid international phone number.");
            if (!VerifyPassword(input?.CurrentPassword ?? "", admin.PasswordHash)) return Problem(422, "The current password is incorrect.");
            if (await db.Users.AnyAsync(x => x.Id != admin.Id && x.Phone == phone)) return Problem(409, "Another account already uses that phone number.");
            admin.FullName = name;
            admin.Phone = phone;
            var token = request.Cookies["eldf_session"] ?? "";
            var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
            var otherSessions = await db.Sessions.Where(x => x.UserAccountId == admin.Id && x.TokenHash != tokenHash).ToListAsync();
            db.Sessions.RemoveRange(otherSessions);
            await db.SaveChangesAsync();
            await WorkflowEvents.RecordAsync(db, admin, "Administrator profile updated", "User", admin.Id.ToString());
            return Results.Json(new { user = View(admin) });
        });

        app.MapGet("/api/site-content", async (EldfDbContext db) =>
        {
            var content = await db.SiteContents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1);
            return Results.Json(new { vision = new { am = AfricanWideCopy(content?.Vision, DefaultVision), en = AfricanWideCopy(content?.VisionEnglish, DefaultVisionEnglish) }, mission = new { am = AfricanWideCopy(content?.Mission, DefaultMission), en = AfricanWideCopy(content?.MissionEnglish, DefaultMissionEnglish) } });
        });

        app.MapGet("/api/admin/speaker-consents", async (HttpRequest request, EldfDbContext db) =>
        {
            var admin = await SessionAuth.CurrentUser(request, db);
            if (admin is null) return Problem(401, "Please sign in to continue.");
            if (admin.Role != "admin") return Problem(403, "Administrator access is required.");
            var records = await db.SpeakerConsents.AsNoTracking().Include(x => x.Speaker).Include(x => x.User)
                .OrderByDescending(x => x.ConsentedAtUtc).Take(2000)
                .Select(x => new { x.Id, x.UserAccountId, userName = x.User.FullName, x.SpeakerProfileId, x.Speaker.SpeakerId,
                    x.ConsentVersion, x.Language, x.CoreConsent, x.AiDevelopmentConsent, x.ResearchSharingConsent,
                    x.PublicReleaseConsent, x.ConsentedAtUtc, x.WithdrawnAtUtc }).ToListAsync();
            return Results.Json(records);
        });

        app.MapGet("/api/workspace", async (HttpRequest request, EldfDbContext db) =>
        {
            var user = await SessionAuth.CurrentUser(request, db);
            if (user is null) return Problem(401, "Please sign in to continue.");

            if (user.Role == "admin")
            {
                var users = await db.Users.AsNoTracking().OrderBy(x => x.CreatedAtUtc).ToListAsync();
                var submissions = await db.Submissions.AsNoTracking().Include(x => x.User).OrderByDescending(x => x.CreatedAtUtc).Take(1000).ToListAsync();
                var tickets = await db.SupportTickets.AsNoTracking().Include(x => x.CreatedBy).Include(x => x.Messages).ThenInclude(x => x.User).OrderByDescending(x => x.UpdatedAtUtc).Take(500).ToListAsync();
                var payments = await db.PaymentRequests.AsNoTracking().Include(x => x.User).OrderByDescending(x => x.RequestedAtUtc).Take(500).ToListAsync();
                var content = await db.SiteContents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1);
                var events = await db.ActivityEvents.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(500).ToListAsync();
                var reviews = await ReviewHistory(db, submissions.Select(x => x.Id).ToList());
                var referrals = await db.AgentReferrals.AsNoTracking().ToDictionaryAsync(x => x.AgentUserId, x => x.Code);
                var deliveryReadyIds = await db.AudioProcessing.AsNoTracking()
                    .Where(x => x.Status == "done" && x.DeliveryZipPath != "")
                    .Select(x => x.SubmissionId).ToListAsync();
                var deliveryReady = deliveryReadyIds.ToHashSet();
                var drafts = await db.Drafts.AsNoTracking().Include(x => x.User).Include(x => x.AssignedUser)
                    .OrderByDescending(x => x.UpdatedAtUtc).Take(1000).ToListAsync();
                return Results.Json(new
                {
                    role = user.Role,
                    profile = View(user),
                    users = users.Select(x => AdminUserView(x, referrals.GetValueOrDefault(x.Id))).ToList(),
                    submissions = submissions.Select(x => SubmissionView(x, reviews.GetValueOrDefault(x.Id, []), deliveryReady.Contains(x.Id))).ToList(),
                    drafts = drafts.Select(x => AdminDraftView(x)).ToList(),
                    supportTickets = tickets.Select(TicketView).ToList(),
                    paymentRequests = payments.Select(PaymentView).ToList(),
                    siteContent = new { vision = new { am = AfricanWideCopy(content?.Vision, DefaultVision), en = AfricanWideCopy(content?.VisionEnglish, DefaultVisionEnglish) }, mission = new { am = AfricanWideCopy(content?.Mission, DefaultMission), en = AfricanWideCopy(content?.MissionEnglish, DefaultMissionEnglish) } },
                    events = events.Select(EventView).ToList(),
                    report = BuildReport(submissions, users, payments, tickets)
                });
            }

            if (user.Role == "agent")
            {
                var referral = await AgentReferralCodes.EnsureAsync(db, user);
                var submissions = await db.Submissions.AsNoTracking().Include(x => x.User)
                    .Where(x => x.AssignedAgentId == user.Id || x.User.ReferredByAgentId == user.Id || x.UserAccountId == user.Id)
                    .OrderByDescending(x => x.UpdatedAtUtc).Take(500).ToListAsync();
                var team = await db.Users.AsNoTracking().Where(x => x.ReferredByAgentId == user.Id)
                    .OrderByDescending(x => x.CreatedAtUtc).Select(x => new { id = x.Id, name = x.FullName, phone = x.Phone, role = x.Role, registeredAt = x.CreatedAtUtc }).ToListAsync();
                var teamIds = team.Select(x => x.id).ToList();
                var tickets = await db.SupportTickets.AsNoTracking().Include(x => x.CreatedBy).Include(x => x.Messages).ThenInclude(x => x.User)
                    .Where(x => x.AssignedAgentUserId == user.Id || teamIds.Contains(x.CreatedByUserId))
                    .OrderByDescending(x => x.UpdatedAtUtc).Take(300).ToListAsync();
                var payments = await db.PaymentRequests.AsNoTracking().Include(x => x.User)
                    .Where(x => x.UserAccountId == user.Id || teamIds.Contains(x.UserAccountId))
                    .OrderByDescending(x => x.RequestedAtUtc).Take(300).ToListAsync();
                var reviews = await ReviewHistory(db, submissions.Select(x => x.Id).ToList());
                var drafts = await db.Drafts.AsNoTracking().Where(x => x.UserAccountId == user.Id).OrderByDescending(x => x.UpdatedAtUtc).ToListAsync();
                // Work a team member has started but not yet submitted. The agent needs this to see an
                // "In Progress" state at all; unsubmitted work exists only as a draft row.
                // Metadata only: no transcript, no translation, no audio URL. The agent reads the
                // content once the contributor submits it for review.
                var teamDrafts = await db.Drafts.AsNoTracking().Include(x => x.User)
                    .Where(x => teamIds.Contains(x.UserAccountId) || (x.AssignedUserAccountId.HasValue && teamIds.Contains(x.AssignedUserAccountId.Value)))
                    .OrderByDescending(x => x.UpdatedAtUtc).Take(300)
                    .Select(x => new
                    {
                        id = x.Id,
                        contributorId = x.UserAccountId,
                        contributorName = x.User.FullName,
                        language = x.ProjectLanguage,
                        translationLanguage = x.TranslationLanguage,
                        durationSeconds = x.DurationSeconds,
                        hasAudio = x.AudioPath.Length > 0,
                        hasTranscript = x.Transcript.Length > 0,
                        hasTranslation = x.Translation.Length > 0,
                        recordingType = x.RecordingType,
                        updatedAt = x.UpdatedAtUtc
                    }).ToListAsync();
                return Results.Json(new
                {
                    role = user.Role,
                    profile = View(user),
                    referralCode = referral.Code,
                    contributors = team,
                    submissions = submissions.Select(x => SubmissionView(x, reviews.GetValueOrDefault(x.Id, []))).ToList(),
                    drafts = drafts.Select(DraftView).ToList(),
                    teamDrafts,
                    supportTickets = tickets.Select(TicketView).ToList(),
                    paymentRequests = payments.Select(PaymentView).ToList(),
                    balance = await AvailableBalance(db, user.Id)
                });
            }

            var ownSubmissions = await db.Submissions.AsNoTracking().Include(x => x.User).Where(x => x.UserAccountId == user.Id)
                .OrderByDescending(x => x.UpdatedAtUtc).Take(500).ToListAsync();
            var ownTickets = await db.SupportTickets.AsNoTracking().Include(x => x.CreatedBy).Include(x => x.Messages).ThenInclude(x => x.User)
                .Where(x => x.CreatedByUserId == user.Id).OrderByDescending(x => x.UpdatedAtUtc).Take(100).ToListAsync();
            var ownPayments = await db.PaymentRequests.AsNoTracking().Include(x => x.User).Where(x => x.UserAccountId == user.Id)
                .OrderByDescending(x => x.RequestedAtUtc).Take(100).ToListAsync();
            var ownDrafts = await db.Drafts.AsNoTracking().Include(x => x.User).Include(x => x.AssignedUser)
                .Where(x => x.UserAccountId == user.Id || x.AssignedUserAccountId == user.Id)
                .OrderByDescending(x => x.UpdatedAtUtc).Take(100).ToListAsync();
            var ownReviews = await ReviewHistory(db, ownSubmissions.Select(x => x.Id).ToList());
            return Results.Json(new
            {
                role = user.Role,
                profile = View(user),
                assignedAgentId = user.ReferredByAgentId,
                submissions = ownSubmissions.Select(x => SubmissionView(x, ownReviews.GetValueOrDefault(x.Id, []))).ToList(),
                drafts = ownDrafts.Select(DraftView).ToList(),
                supportTickets = ownTickets.Select(TicketView).ToList(),
                paymentRequests = ownPayments.Select(PaymentView).ToList(),
                balance = await AvailableBalance(db, user.Id)
            });
        });

        app.MapGet("/api/submissions/{id:int}/audio", async (int id, HttpRequest request, EldfDbContext db, IConfiguration configuration, IWebHostEnvironment environment) =>
        {
            var user = await SessionAuth.CurrentUser(request, db);
            if (user is null) return Problem(401, "Please sign in to continue.");
            var submission = await db.Submissions.Include(x => x.User).SingleOrDefaultAsync(x => x.Id == id);
            if (submission is null) return Problem(404, "The recording was not found.");
            if (!CanSeeSubmission(user, submission)) return Problem(403, "You do not have access to this recording.");
            var fullPath = SafeDataPath(configuration, environment, submission.AudioPath);
            if (fullPath is null || !File.Exists(fullPath)) return Problem(404, "The audio file is not available on the server.");
            return Results.File(fullPath, AudioContentType(fullPath), enableRangeProcessing: true);
        });

        app.MapGet("/api/drafts/{id:int}", async (int id, HttpRequest request, EldfDbContext db) =>
        {
            var user = await SessionAuth.CurrentUser(request, db);
            if (user is null) return Problem(401, "Please sign in to continue.");
            var draft = await db.Drafts.AsNoTracking().Include(x => x.User).Include(x => x.AssignedUser)
                .SingleOrDefaultAsync(x => x.Id == id && (x.UserAccountId == user.Id || x.AssignedUserAccountId == user.Id || user.Role == "admin"));
            if (draft is null) return Problem(404, "The saved draft was not found.");
            var speakerIds = JsonSerializer.Deserialize<List<string>>(SafeJsonArray(draft.SpeakersJson)) ?? [];
            var profiles = await db.Speakers.AsNoTracking().Where(x => x.UserAccountId == draft.UserAccountId && speakerIds.Contains(x.SpeakerId)).ToListAsync();
            var profileIds = profiles.Select(x => x.Id).ToArray();
            var latestConsents = await db.SpeakerConsents.AsNoTracking()
                .Where(x => x.UserAccountId == draft.UserAccountId && profileIds.Contains(x.SpeakerProfileId) && x.ConsentVersion == "speaker-consent-v1")
                .OrderByDescending(x => x.ConsentedAtUtc).ToListAsync();
            var currentBySpeaker = profiles.ToDictionary(profile => profile.SpeakerId, profile =>
            {
                var latest = latestConsents.FirstOrDefault(consent => consent.SpeakerProfileId == profile.Id);
                return latest is { CoreConsent: true, WithdrawnAtUtc: null };
            }, StringComparer.Ordinal);
            return Results.Json(DraftResumeView(draft, speakerIds, currentBySpeaker));
        });

        app.MapGet("/api/drafts/{id:int}/audio", async (int id, HttpRequest request, EldfDbContext db, IConfiguration configuration, IWebHostEnvironment environment) =>
        {
            var user = await SessionAuth.CurrentUser(request, db);
            if (user is null) return Problem(401, "Please sign in to continue.");
            var draft = await db.Drafts.SingleOrDefaultAsync(x => x.Id == id && (x.UserAccountId == user.Id || x.AssignedUserAccountId == user.Id || user.Role == "admin"));
            if (draft is null) return Problem(404, "The saved draft was not found.");
            var fullPath = SafeDataPath(configuration, environment, draft.AudioPath);
            if (fullPath is null || !File.Exists(fullPath)) return Problem(404, "The draft audio is not available.");
            return Results.File(fullPath, AudioContentType(fullPath), enableRangeProcessing: true);
        });

        app.MapPost("/api/drafts", async (HttpRequest request, EldfDbContext db, IConfiguration configuration, IWebHostEnvironment environment) =>
        {
            var user = await SessionAuth.CurrentUser(request, db);
            if (user is null) return Problem(401, "Please sign in to continue.");
            if (user.Role is not ("contributor" or "agent")) return Problem(403, "This role cannot save contributor drafts.");
            var form = await request.ReadFormAsync();
            var id = int.TryParse(form["draft_id"], out var parsed) ? parsed : 0;
            WorkDraft? draft = id > 0 ? await db.Drafts.SingleOrDefaultAsync(x => x.Id == id && (x.UserAccountId == user.Id || x.AssignedUserAccountId == user.Id)) : null;
            if (id > 0 && draft is null) return Problem(404, "The saved draft was not found.");
            draft ??= new WorkDraft { UserAccountId = user.Id, ProjectLanguage = "" };
            draft.ProjectLanguage = WorkflowEvents.Limit(form["language"], 160);
            draft.TopicTitle = WorkflowEvents.Limit(form["topic_title"], 160);
            draft.TranslationLanguage = WorkflowEvents.Limit(form["translation_language"], 160);
            draft.Transcript = WorkflowEvents.Limit(form["transcript"], 2_000_000);
            draft.Translation = WorkflowEvents.Limit(form["translation"], 2_000_000);
            draft.RecordingType = WorkflowEvents.Limit(form["recording_type"], 80, "Conversation");
            draft.ConsentReference = WorkflowEvents.Limit(form["consent_reference"], 80);
            draft.SpeakersJson = SafeJsonArray(form["speakers_json"].ToString());
            draft.DurationSeconds = int.TryParse(form["duration_seconds"], out var duration) ? Math.Clamp(duration, 0, 3600) : draft.DurationSeconds;
            draft.UpdatedAtUtc = DateTime.UtcNow;
            var audioSegments = form.Files.GetFiles("audio_segments");
            var file = form.Files.GetFile("file");
            if (audioSegments.Count > 0 && file is not null) return Problem(422, "Upload either one recording or its recording parts, not both.");
            IReadOnlyList<IFormFile> audioFiles = audioSegments.Count > 0
                ? audioSegments
                : file is null ? Array.Empty<IFormFile>() : [file];
            var maxBytes = configuration.GetValue<long>("Audio:MaxBytes", 250L * 1024 * 1024);
            if (audioFiles.Count > 100 || audioFiles.Any(item => item.Length == 0) || audioFiles.Sum(item => item.Length) > maxBytes)
                return Problem(413, "The draft audio is empty, has too many parts, or is larger than the server upload limit.");
            string? newPath = null;
            if (audioFiles.Count > 0)
            {
                var folder = Path.Combine(DataDirectory(configuration, environment), "drafts", draft.UserAccountId.ToString());
                Directory.CreateDirectory(folder);
                var ext = audioFiles.Count > 1 ? ".webm" : SafeExtension(audioFiles[0].FileName);
                newPath = Path.Combine("drafts", draft.UserAccountId.ToString(), Guid.NewGuid().ToString("N") + ext);
                var full = SafeDataPath(configuration, environment, newPath);
                if (full is null) return Problem(400, "Invalid audio path.");
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                var partPaths = new List<string>();
                var mergedTemp = Path.Combine(Path.GetTempPath(), $"eldf-draft-{Guid.NewGuid():N}.webm");
                try
                {
                    if (audioFiles.Count == 1)
                    {
                        await using var output = File.Create(full);
                        await audioFiles[0].CopyToAsync(output);
                    }
                    else
                    {
                        foreach (var audioFile in audioFiles)
                        {
                            var partExtension = audioFile.ContentType.Contains("ogg", StringComparison.OrdinalIgnoreCase) ? ".ogg"
                                : audioFile.ContentType.Contains("mp4", StringComparison.OrdinalIgnoreCase) ? ".m4a"
                                : audioFile.ContentType.Contains("wav", StringComparison.OrdinalIgnoreCase) ? ".wav" : ".webm";
                            var partPath = Path.Combine(Path.GetTempPath(), $"eldf-draft-part-{Guid.NewGuid():N}{partExtension}");
                            partPaths.Add(partPath);
                            await using var output = File.Create(partPath);
                            await audioFile.CopyToAsync(output);
                        }
                        var arguments = new List<string> { "-y" };
                        foreach (var partPath in partPaths) arguments.AddRange(["-i", partPath]);
                        var streams = string.Concat(Enumerable.Range(0, partPaths.Count).Select(index => $"[{index}:a:0]"));
                        arguments.AddRange(["-filter_complex", $"{streams}concat=n={partPaths.Count}:v=0:a=1[outa]", "-map", "[outa]", "-vn", "-c:a", "libopus", "-b:a", "64k", "-ac", "1", "-ar", "48000", mergedTemp]);
                        await RunProcessForDrafts(configuration["Audio:FfmpegPath"] ?? "ffmpeg", arguments);
                        File.Move(mergedTemp, full);
                    }
                }
                catch
                {
                    if (File.Exists(full)) File.Delete(full);
                    throw;
                }
                finally
                {
                    if (File.Exists(mergedTemp)) File.Delete(mergedTemp);
                    foreach (var partPath in partPaths) if (File.Exists(partPath)) File.Delete(partPath);
                }
            }
            var oldPath = draft.AudioPath;
            if (newPath is not null) draft.AudioPath = newPath;
            if (id == 0) db.Drafts.Add(draft);
            await db.SaveChangesAsync();
            if (newPath is not null && oldPath.Length > 0) DeleteSafeDataFile(configuration, environment, oldPath);
            if (id == 0) await WorkflowEvents.RecordAsync(db, user, "Work draft created", "Draft", draft.Id.ToString(), draft.ProjectLanguage);
            return Results.Json(DraftView(draft));
        });

        app.MapDelete("/api/drafts/{id:int}", async (int id, HttpRequest request, EldfDbContext db, IConfiguration configuration, IWebHostEnvironment environment) =>
        {
            var user = await SessionAuth.CurrentUser(request, db);
            if (user is null) return Problem(401, "Please sign in to continue.");
            var draft = await db.Drafts.SingleOrDefaultAsync(x => x.Id == id && x.UserAccountId == user.Id);
            if (draft is null) return Problem(404, "The saved draft was not found.");
            db.Drafts.Remove(draft);
            await db.SaveChangesAsync();
            DeleteSafeDataFile(configuration, environment, draft.AudioPath);
            await WorkflowEvents.RecordAsync(db, user, "Work draft deleted", "Draft", id.ToString());
            return Results.Json(new { ok = true });
        });

        app.MapPost("/api/submissions/{id:int}/resubmit", async (int id, HttpRequest request, EldfDbContext db) =>
        {
            var user = await SessionAuth.CurrentUser(request, db);
            if (user is null) return Problem(401, "Please sign in to continue.");
            if (user.Role != "contributor") return Problem(403, "Contributor access is required.");
            var submission = await db.Submissions.SingleOrDefaultAsync(x => x.Id == id && x.UserAccountId == user.Id);
            if (submission is null) return Problem(404, "The work was not found.");
            if (submission.Status is not ("Returned by agent" or "Returned by admin" or "Returned for correction")) return Problem(409, "This work is not waiting for a correction.");
            var input = await request.ReadFromJsonAsync<ResubmitInput>(JsonOptions);
            if (input is null || string.IsNullOrWhiteSpace(input.Transcript) || string.IsNullOrWhiteSpace(input.Translation)) return Problem(422, "Add both the transcript and translation before resubmitting.");
            submission.Transcript = WorkflowEvents.Limit(input.Transcript, 2_000_000);
            submission.Translation = WorkflowEvents.Limit(input.Translation, 2_000_000);
            submission.Status = submission.AssignedAgentId.HasValue ? "Awaiting agent review" : "Awaiting admin review";
            submission.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await WorkflowEvents.RecordAsync(db, user, "Work corrections resubmitted", "Submission", id.ToString());
            return Results.Json(new { ok = true, status = submission.Status });
        });

        app.MapPost("/api/submissions/{id:int}/agent-review", async (int id, HttpRequest request, EldfDbContext db) =>
        {
            var user = await SessionAuth.CurrentUser(request, db);
            if (user is null) return Problem(401, "Please sign in to continue.");
            if (user.Role != "agent") return Problem(403, "Agent access is required.");
            var submission = await db.Submissions.Include(x => x.User).SingleOrDefaultAsync(x => x.Id == id);
            if (submission is null) return Problem(404, "The work was not found.");
            if (submission.AssignedAgentId != user.Id && submission.User.ReferredByAgentId != user.Id) return Problem(403, "This work is outside your assigned team.");
            if (submission.Status is not ("Awaiting agent review" or "Returned for correction")) return Problem(409, "This work is not waiting for agent review.");
            var input = await request.ReadFromJsonAsync<ReviewInput>(JsonOptions);
            var decision = input?.Decision?.Trim().ToLowerInvariant() ?? "";
            var comment = WorkflowEvents.Limit(input?.Comment, 4000);
            if (decision is not ("accept" or "return")) return Problem(422, "Choose accept or return.");
            if (decision == "return" && string.IsNullOrWhiteSpace(comment)) return Problem(422, "Add a comment before returning this work.");
            submission.Status = decision == "accept" ? "Awaiting admin review" : "Returned by agent";
            submission.AgentReviewComment = comment;
            submission.AgentReviewedById = user.Id;
            submission.AgentReviewedAtUtc = DateTime.UtcNow;
            submission.UpdatedAtUtc = DateTime.UtcNow;
            db.ReviewEvents.Add(new WorkReviewEvent { SubmissionId = id, ReviewerUserId = user.Id, ReviewerRole = "agent", Decision = decision, Comment = comment });
            await db.SaveChangesAsync();
            await WorkflowEvents.RecordAsync(db, user, decision == "accept" ? "Agent forwarded work to admin" : "Agent returned work", "Submission", id.ToString(), comment);
            return Results.Json(new { ok = true, status = submission.Status });
        });

        app.MapPost("/api/submissions/{id:int}/admin-review", async (int id, HttpRequest request, EldfDbContext db) =>
        {
            var user = await SessionAuth.CurrentUser(request, db);
            if (user is null) return Problem(401, "Please sign in to continue.");
            if (user.Role != "admin") return Problem(403, "Administrator access is required.");
            var submission = await db.Submissions.SingleOrDefaultAsync(x => x.Id == id);
            if (submission is null) return Problem(404, "The work was not found.");
            if (submission.Status is not ("Awaiting admin review" or "Returned by admin" or "Awaiting review")) return Problem(409, "This work is not waiting for final review.");
            var input = await request.ReadFromJsonAsync<AdminReviewInput>(JsonOptions);
            var decision = input?.Decision?.Trim().ToLowerInvariant() ?? "";
            var comment = WorkflowEvents.Limit(input?.Comment, 4000);
            var reason = WorkflowEvents.Limit(input?.Reason, 4000);
            if (decision is not ("approve" or "return" or "reject")) return Problem(422, "Choose approve, return, or reject.");
            if (decision == "return" && string.IsNullOrWhiteSpace(comment)) return Problem(422, "Add a correction comment before returning this work.");
            if (decision == "reject" && string.IsNullOrWhiteSpace(reason)) return Problem(422, "Add a reason before rejecting this work.");
            if (input?.AwardAmount is < 0) return Problem(422, "The award amount cannot be negative.");
            submission.Status = decision switch { "approve" => "Approved", "return" => "Returned by admin", _ => "Rejected" };
            submission.AdminReviewComment = comment;
            submission.AdminRejectionReason = reason;
            submission.AdminReviewedById = user.Id;
            submission.AdminReviewedAtUtc = DateTime.UtcNow;
            submission.UpdatedAtUtc = DateTime.UtcNow;
            submission.AwardAmount = decision == "approve" ? input?.AwardAmount : null;
            submission.RewardCurrency = WorkflowEvents.Limit(input?.Currency, 3, "ETB");
            db.ReviewEvents.Add(new WorkReviewEvent { SubmissionId = id, ReviewerUserId = user.Id, ReviewerRole = "admin", Decision = decision, Comment = decision == "reject" ? reason : comment });
            await db.SaveChangesAsync();
            await WorkflowEvents.RecordAsync(db, user, "Admin final review: " + decision, "Submission", id.ToString(), decision == "reject" ? reason : comment);
            return Results.Json(new { ok = true, status = submission.Status, awardAmount = submission.AwardAmount, currency = submission.RewardCurrency });
        });

        app.MapGet("/api/admin/users", async (HttpRequest request, EldfDbContext db) =>
        {
            var user = await SessionAuth.CurrentUser(request, db);
            if (user is null) return Problem(401, "Please sign in to continue.");
            if (user.Role != "admin") return Problem(403, "Administrator access is required.");
            var users = await db.Users.AsNoTracking().OrderBy(x => x.FullName).ToListAsync();
            var referrals = await db.AgentReferrals.AsNoTracking().ToDictionaryAsync(x => x.AgentUserId, x => x.Code);
            return Results.Json(users.Select(x => AdminUserView(x, referrals.GetValueOrDefault(x.Id))));
        });

        app.MapPatch("/api/admin/drafts/{id:int}/assignment", async (int id, HttpRequest request, EldfDbContext db) =>
        {
            var admin = await SessionAuth.CurrentUser(request, db);
            if (admin is null) return Problem(401, "Please sign in to continue.");
            if (admin.Role != "admin") return Problem(403, "Administrator access is required.");
            var input = await request.ReadFromJsonAsync<AdminDraftAssignmentInput>(JsonOptions);
            var draft = await db.Drafts.Include(x => x.User).SingleOrDefaultAsync(x => x.Id == id);
            if (draft is null) return Problem(404, "The interrupted work was not found.");
            var targetId = input?.AssignedUserId;
            if (targetId.HasValue)
            {
                var target = await db.Users.SingleOrDefaultAsync(x => x.Id == targetId.Value && x.IsActive && x.Role == "contributor");
                if (target is null) return Problem(422, "Choose an active contributor to receive this work.");
                if (target.Id == draft.UserAccountId) draft.AssignedUserAccountId = null;
                else draft.AssignedUserAccountId = target.Id;
            }
            else draft.AssignedUserAccountId = null;
            draft.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await WorkflowEvents.RecordAsync(db, admin, "Interrupted work reassigned", "Draft", draft.Id.ToString(), draft.AssignedUserAccountId?.ToString() ?? draft.UserAccountId.ToString());
            return Results.Json(new { id = draft.Id, ownerUserId = draft.UserAccountId, assignedUserId = draft.AssignedUserAccountId });
        });

        app.MapPatch("/api/admin/submissions/{id:int}/assignment", async (int id, HttpRequest request, EldfDbContext db) =>
        {
            var admin = await SessionAuth.CurrentUser(request, db);
            if (admin is null) return Problem(401, "Please sign in to continue.");
            if (admin.Role != "admin") return Problem(403, "Administrator access is required.");
            var input = await request.ReadFromJsonAsync<AdminSubmissionAssignmentInput>(JsonOptions);
            var submission = await db.Submissions.SingleOrDefaultAsync(x => x.Id == id);
            if (submission is null) return Problem(404, "The submitted work was not found.");
            if (submission.Status is not ("Awaiting agent review" or "Awaiting admin review" or "Awaiting review"))
                return Problem(409, "Only work waiting for review can be assigned.");
            if (input?.AgentId is int agentId)
            {
                var agent = await db.Users.SingleOrDefaultAsync(x => x.Id == agentId && x.Role == "agent" && x.IsActive);
                if (agent is null) return Problem(422, "Choose an active agent to receive this review task.");
                submission.AssignedAgentId = agent.Id;
                submission.Status = "Awaiting agent review";
            }
            else
            {
                submission.AssignedAgentId = null;
                submission.Status = "Awaiting admin review";
            }
            submission.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await WorkflowEvents.RecordAsync(db, admin, "Review task assigned", "Submission", id.ToString(), submission.AssignedAgentId?.ToString() ?? "admin");
            return Results.Json(new { id = submission.Id, assignedAgentId = submission.AssignedAgentId, status = submission.Status });
        });

        app.MapPatch("/api/admin/users/{id:int}", async (int id, HttpRequest request, EldfDbContext db) =>
        {
            var admin = await SessionAuth.CurrentUser(request, db);
            if (admin is null) return Problem(401, "Please sign in to continue.");
            if (admin.Role != "admin") return Problem(403, "Administrator access is required.");
            var target = await db.Users.SingleOrDefaultAsync(x => x.Id == id);
            if (target is null) return Problem(404, "The account was not found.");
            var previousRole = target.Role;
            var input = await request.ReadFromJsonAsync<AdminUserUpdateInput>(JsonOptions);
            if (input is null || input.Role is not ("contributor" or "agent" or "admin")) return Problem(422, "Choose a valid account role.");
            if (target.Id == admin.Id && input.Role != "admin") return Problem(409, "You cannot remove your own administrator role while signed in.");
            if (target.Id == admin.Id && input.IsActive == false) return Problem(409, "You cannot deactivate your current administrator account.");
            target.FullName = WorkflowEvents.Limit(input.FullName, 160, target.FullName);
            target.Role = input.Role;
            target.IsActive = input.IsActive ?? target.IsActive;
            if (input.AgentId.HasValue)
            {
                var agent = await db.Users.SingleOrDefaultAsync(x => x.Id == input.AgentId && x.Role == "agent" && x.IsActive);
                if (agent is null) return Problem(422, "Choose an active agent.");
                if (agent.Id == target.Id) return Problem(422, "An agent cannot be assigned to their own account.");
                target.ReferredByAgentId = agent.Id;
            }
            else if (input.ClearAgentAssignment == true || target.Role == "agent" || target.Role == "admin") target.ReferredByAgentId = null;
            if (target.Role == "agent") await AgentReferralCodes.EnsureAsync(db, target);
            await db.SaveChangesAsync();
            await WorkflowEvents.RecordAsync(db, admin, "Account role or assignment updated", "User", id.ToString(), target.Role);
            if (previousRole != "agent" && target.Role == "agent")
                await WorkflowEvents.RecordAsync(db, admin, "Agent role assigned", "User", id.ToString(), "The account can now use the Agent workspace with its existing login.");
            return Results.Json(AdminUserView(target, await db.AgentReferrals.Where(x => x.AgentUserId == target.Id).Select(x => x.Code).SingleOrDefaultAsync()));
        });

        app.MapPost("/api/admin/users/{id:int}/password-reset", async (int id, HttpRequest request, EldfDbContext db) =>
        {
            var admin = await SessionAuth.CurrentUser(request, db);
            if (admin is null) return Problem(401, "Please sign in to continue.");
            if (admin.Role != "admin") return Problem(403, "Administrator access is required.");
            var target = await db.Users.SingleOrDefaultAsync(x => x.Id == id && x.IsActive);
            if (target is null) return Problem(404, "The active account was not found.");
            var latestResetEvent = await db.ActivityEvents.Where(x => x.EntityType == "User" && x.EntityId == id.ToString() && (x.EventType == "Password reset requested" || x.EventType == "Password reset completed"))
                .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync();
            if (latestResetEvent is null || latestResetEvent.EventType != "Password reset requested") return Problem(409, "There is no pending password reset request for this account.");

            var temporaryPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Rfc2898DeriveBytes.Pbkdf2(temporaryPassword, salt, 310_000, HashAlgorithmName.SHA256, 32);
            target.PasswordHash = $"pbkdf2_sha256$310000${Convert.ToHexString(salt).ToLowerInvariant()}${Convert.ToHexString(hash).ToLowerInvariant()}";
            var sessions = await db.Sessions.Where(x => x.UserAccountId == target.Id).ToListAsync();
            db.Sessions.RemoveRange(sessions);
            latestResetEvent.EventType = "Password reset completed";
            latestResetEvent.Details = "Admin issued a temporary password after verifying the requester by phone.";
            await db.SaveChangesAsync();
            await WorkflowEvents.RecordAsync(db, admin, "Temporary password issued", "User", target.Id.ToString());
            return Results.Json(new { temporaryPassword });
        });

        app.MapGet("/api/support/tickets", async (HttpRequest request, EldfDbContext db) =>
        {
            var user = await SessionAuth.CurrentUser(request, db);
            if (user is null) return Problem(401, "Please sign in to continue.");
            var query = db.SupportTickets.AsNoTracking().Include(x => x.CreatedBy).Include(x => x.Messages).ThenInclude(x => x.User).AsQueryable();
            if (user.Role == "agent") query = query.Where(x => x.AssignedAgentUserId == user.Id || x.CreatedBy.ReferredByAgentId == user.Id);
            else if (user.Role != "admin") query = query.Where(x => x.CreatedByUserId == user.Id);
            var tickets = await query.OrderByDescending(x => x.UpdatedAtUtc).Take(500).ToListAsync();
            return Results.Json(tickets.Select(TicketView));
        });

        app.MapPost("/api/support/tickets", async (HttpRequest request, EldfDbContext db) =>
        {
            var user = await SessionAuth.CurrentUser(request, db);
            if (user is null) return Problem(401, "Please sign in to continue.");
            var input = await request.ReadFromJsonAsync<SupportCreateInput>(JsonOptions);
            var subject = WorkflowEvents.Limit(input?.Subject, 200);
            var body = WorkflowEvents.Limit(input?.Message, 4000);
            if (subject.Length < 3 || body.Length < 2) return Problem(422, "Enter a short subject and a message.");
            var ticket = new SupportTicket { CreatedByUserId = user.Id, AssignedAgentUserId = user.Role == "contributor" ? user.ReferredByAgentId : null, Subject = subject };
            ticket.Messages.Add(new SupportMessage { UserAccountId = user.Id, Body = body });
            db.SupportTickets.Add(ticket);
            await db.SaveChangesAsync();
            await WorkflowEvents.RecordAsync(db, user, "Support case opened", "SupportTicket", ticket.Id.ToString());
            return Results.Json(new { id = ticket.Id, status = ticket.Status }, statusCode: StatusCodes.Status201Created);
        });

        app.MapPost("/api/support/tickets/{id:int}/messages", async (int id, HttpRequest request, EldfDbContext db) =>
        {
            var user = await SessionAuth.CurrentUser(request, db);
            if (user is null) return Problem(401, "Please sign in to continue.");
            var ticket = await db.SupportTickets.Include(x => x.CreatedBy).SingleOrDefaultAsync(x => x.Id == id);
            if (ticket is null) return Problem(404, "The support case was not found.");
            if (!CanAccessTicket(user, ticket)) return Problem(403, "This support case is outside your account or team.");
            if (ticket.Status == "Closed") return Problem(409, "This support case is closed.");
            var input = await request.ReadFromJsonAsync<SupportMessageInput>(JsonOptions);
            var body = WorkflowEvents.Limit(input?.Message, 4000);
            if (body.Length < 2) return Problem(422, "Write a message before sending.");
            db.SupportMessages.Add(new SupportMessage { TicketId = id, UserAccountId = user.Id, Body = body });
            ticket.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await WorkflowEvents.RecordAsync(db, user, "Support message sent", "SupportTicket", id.ToString());
            return Results.Json(new { ok = true });
        });

        app.MapPatch("/api/support/tickets/{id:int}", async (int id, HttpRequest request, EldfDbContext db) =>
        {
            var user = await SessionAuth.CurrentUser(request, db);
            if (user is null) return Problem(401, "Please sign in to continue.");
            var ticket = await db.SupportTickets.Include(x => x.CreatedBy).SingleOrDefaultAsync(x => x.Id == id);
            if (ticket is null) return Problem(404, "The support case was not found.");
            if (user.Role == "admin")
            {
                var input = await request.ReadFromJsonAsync<AdminTicketUpdateInput>(JsonOptions);
                if (input is null || input.Status is not ("Open" or "In progress" or "Escalated" or "Closed")) return Problem(422, "Choose a valid support status.");
                if (input.AssignedAgentId.HasValue)
                {
                    var agent = await db.Users.SingleOrDefaultAsync(x => x.Id == input.AssignedAgentId && x.Role == "agent" && x.IsActive);
                    if (agent is null) return Problem(422, "Choose an active agent.");
                    ticket.AssignedAgentUserId = agent.Id;
                }
                ticket.Status = input.Status;
            }
            else if (user.Role == "agent" && ticket.AssignedAgentUserId == user.Id)
            {
                var input = await request.ReadFromJsonAsync<AgentTicketUpdateInput>(JsonOptions);
                if (input?.Escalate != true) return Problem(403, "An agent can only escalate an assigned support case.");
                ticket.Status = "Escalated";
                ticket.AssignedAgentUserId = null;
            }
            else return Problem(403, "You cannot update this support case.");
            ticket.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await WorkflowEvents.RecordAsync(db, user, "Support case updated", "SupportTicket", id.ToString(), ticket.Status);
            return Results.Json(new { ok = true, status = ticket.Status });
        });

        app.MapGet("/api/payments/requests", async (HttpRequest request, EldfDbContext db) =>
        {
            var user = await SessionAuth.CurrentUser(request, db);
            if (user is null) return Problem(401, "Please sign in to continue.");
            var query = db.PaymentRequests.AsNoTracking().Include(x => x.User).AsQueryable();
            if (user.Role == "agent") query = query.Where(x => x.UserAccountId == user.Id || x.User.ReferredByAgentId == user.Id);
            else if (user.Role != "admin") query = query.Where(x => x.UserAccountId == user.Id);
            return Results.Json((await query.OrderByDescending(x => x.RequestedAtUtc).Take(500).ToListAsync()).Select(PaymentView));
        });

        app.MapPost("/api/payments/requests", async (HttpRequest request, EldfDbContext db) =>
        {
            var user = await SessionAuth.CurrentUser(request, db);
            if (user is null) return Problem(401, "Please sign in to continue.");
            if (user.Role is not ("contributor" or "agent")) return Problem(403, "Only contributors and agents can request a payout.");
            var input = await request.ReadFromJsonAsync<PaymentRequestInput>(JsonOptions);
            if (input is null || input.Amount < 1000 || input.Amount > 10000) return Problem(422, "A payout request must be between 1,000 and 10,000 ETB.");
            var balance = await AvailableBalance(db, user.Id);
            if (input.Amount > balance) return Problem(422, "The request is higher than your available approved balance.");
            var payment = new PaymentRequest { UserAccountId = user.Id, Amount = decimal.Round(input.Amount, 2), Currency = "ETB", Status = user.ReferredByAgentId.HasValue ? "Pending agent review" : "Pending admin review" };
            db.PaymentRequests.Add(payment);
            await db.SaveChangesAsync();
            await WorkflowEvents.RecordAsync(db, user, "Payout requested", "PaymentRequest", payment.Id.ToString());
            return Results.Json(PaymentView(payment), statusCode: StatusCodes.Status201Created);
        });

        app.MapPost("/api/payments/requests/{id:int}/agent-review", async (int id, HttpRequest request, EldfDbContext db) =>
        {
            var agent = await SessionAuth.CurrentUser(request, db);
            if (agent is null) return Problem(401, "Please sign in to continue.");
            if (agent.Role != "agent") return Problem(403, "Agent access is required.");
            var payment = await db.PaymentRequests.Include(x => x.User).SingleOrDefaultAsync(x => x.Id == id);
            if (payment is null) return Problem(404, "The payout request was not found.");
            if (payment.User.ReferredByAgentId != agent.Id || payment.Status != "Pending agent review") return Problem(403, "This request is not awaiting review by your team.");
            var input = await request.ReadFromJsonAsync<PaymentDecisionInput>(JsonOptions);
            var decision = input?.Decision?.Trim().ToLowerInvariant() ?? "";
            if (decision is not ("accept" or "return")) return Problem(422, "Choose accept or return.");
            var note = WorkflowEvents.Limit(input?.Note, 2000);
            if (decision == "return" && note.Length < 2) return Problem(422, "Add a reason before returning the request.");
            payment.Status = decision == "accept" ? "Pending admin review" : "Returned by agent";
            payment.ReviewNote = note;
            payment.ReviewedByUserId = agent.Id;
            payment.ReviewedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await WorkflowEvents.RecordAsync(db, agent, "Agent payout review: " + decision, "PaymentRequest", id.ToString());
            return Results.Json(PaymentView(payment));
        });

        app.MapPost("/api/admin/payments/{id:int}/review", async (int id, HttpRequest request, EldfDbContext db) =>
        {
            var admin = await SessionAuth.CurrentUser(request, db);
            if (admin is null) return Problem(401, "Please sign in to continue.");
            if (admin.Role != "admin") return Problem(403, "Administrator access is required.");
            var payment = await db.PaymentRequests.SingleOrDefaultAsync(x => x.Id == id);
            if (payment is null) return Problem(404, "The payout request was not found.");
            var input = await request.ReadFromJsonAsync<AdminPaymentDecisionInput>(JsonOptions);
            var decision = input?.Decision?.Trim().ToLowerInvariant() ?? "";
            if (decision is not ("approve" or "reject" or "paid")) return Problem(422, "Choose approve, reject, or mark paid.");
            var note = WorkflowEvents.Limit(input?.Note, 2000);
            var proof = WorkflowEvents.Limit(input?.ProofReference, 500);
            if (decision == "reject" && note.Length < 2) return Problem(422, "Add a reason before rejecting the request.");
            if (decision == "paid" && (payment.Status != "Approved" || proof.Length < 3)) return Problem(422, "Mark a payout paid only after approval and after entering its payment proof reference.");
            if (decision == "approve" && payment.Status is not ("Pending admin review" or "Returned by agent")) return Problem(409, "This payout is not ready for admin approval.");
            payment.Status = decision switch { "approve" => "Approved", "reject" => "Rejected", _ => "Paid" };
            payment.ReviewNote = note;
            payment.PaymentProofReference = decision == "paid" ? proof : payment.PaymentProofReference;
            payment.ReviewedByUserId = admin.Id;
            payment.ReviewedAtUtc = DateTime.UtcNow;
            if (decision == "paid") payment.PaidAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await WorkflowEvents.RecordAsync(db, admin, "Admin payout status: " + payment.Status, "PaymentRequest", id.ToString());
            return Results.Json(PaymentView(payment));
        });

        app.MapPatch("/api/admin/site-content", async (HttpRequest request, EldfDbContext db) =>
        {
            var admin = await SessionAuth.CurrentUser(request, db);
            if (admin is null) return Problem(401, "Please sign in to continue.");
            if (admin.Role != "admin") return Problem(403, "Administrator access is required.");
            var input = await request.ReadFromJsonAsync<SiteContentInput>(JsonOptions);
            var vision = WorkflowEvents.Limit(input?.VisionAm, 2000);
            var visionEn = WorkflowEvents.Limit(input?.VisionEn, 2000);
            var mission = WorkflowEvents.Limit(input?.MissionAm, 4000);
            var missionEn = WorkflowEvents.Limit(input?.MissionEn, 4000);
            if (vision.Length < 4 || visionEn.Length < 4 || mission.Length < 4 || missionEn.Length < 4) return Problem(422, "Enter the vision and mission in Amharic and English.");
            var content = await db.SiteContents.SingleOrDefaultAsync(x => x.Id == 1) ?? new SiteContent { Id = 1 };
            content.Vision = vision;
            content.VisionEnglish = visionEn;
            content.Mission = mission;
            content.MissionEnglish = missionEn;
            content.UpdatedAtUtc = DateTime.UtcNow;
            content.UpdatedByUserId = admin.Id;
            if (db.Entry(content).State == EntityState.Detached) db.SiteContents.Add(content);
            await db.SaveChangesAsync();
            await WorkflowEvents.RecordAsync(db, admin, "Cover vision and mission updated", "SiteContent", "1");
            return Results.Json(new { vision = new { am = content.Vision, en = content.VisionEnglish }, mission = new { am = content.Mission, en = content.MissionEnglish } });
        });

        // Progress of the audio quality pipeline. The contributor polls this after submitting so
        // the page shows a status instead of freezing while FFmpeg works.
        app.MapGet("/api/submissions/{id:int}/audio-quality", async (int id, HttpRequest request, EldfDbContext db) =>
        {
            var user = await SessionAuth.CurrentUser(request, db);
            if (user is null) return Problem(401, "Please sign in to continue.");
            var submission = await db.Submissions.AsNoTracking().Include(x => x.User).SingleOrDefaultAsync(x => x.Id == id);
            if (submission is null) return Problem(404, "The work was not found.");
            if (!CanSeeSubmission(user, submission)) return Problem(403, "You do not have access to this recording.");

            var record = await db.AudioProcessing.AsNoTracking().SingleOrDefaultAsync(x => x.SubmissionId == id);
            if (record is null) return Results.Json(new { status = "not queued", verdict = "", message = "", messageAm = "" });

            // Contributors get the verdict and the guidance message. The full technical report is
            // only exposed to the agent and the administrator who review the work.
            var detailed = user.Role is "admin" or "agent";
            return Results.Json(new
            {
                status = record.Status,
                verdict = record.Verdict,
                message = record.ContributorMessage,
                messageAm = record.ContributorMessageAmharic,
                attemptCount = record.AttemptCount,
                secondsRemoved = Math.Round(record.SecondsRemoved, 2),
                noiseReductionApplied = record.NoiseReductionApplied,
                edgeTrimApplied = record.EdgeTrimApplied,
                interiorSilenceCapped = record.InteriorSilenceCapped,
                original = new { durationSeconds = Math.Round(record.OriginalDurationSeconds, 2), sizeBytes = record.OriginalSizeBytes, sampleRate = record.OriginalSampleRate, channels = record.OriginalChannels, sha256 = record.OriginalSha256 },
                processed = new { durationSeconds = Math.Round(record.ProcessedDurationSeconds, 2), sizeBytes = record.ProcessedSizeBytes, sha256 = record.ProcessedSha256 },
                hasDeliveryPackage = record.DeliveryZipPath.Length > 0,
                pipelineVersion = record.PipelineVersion,
                queuedAt = record.QueuedAtUtc,
                completedAt = record.CompletedAtUtc,
                report = detailed && record.ReportJson.Length > 2 ? JsonDocument.Parse(record.ReportJson).RootElement.Clone() : (object?)null,
                error = detailed ? record.Error : "",
            });
        });

        // Re-run the pipeline. Only an administrator, because it rewrites the processed copy.
        // The original upload is untouched either way.
        app.MapPost("/api/submissions/{id:int}/audio-quality/retry", async (int id, HttpRequest request, EldfDbContext db, AudioProcessingQueue queue) =>
        {
            var admin = await SessionAuth.CurrentUser(request, db);
            if (admin is null) return Problem(401, "Please sign in to continue.");
            if (admin.Role != "admin") return Problem(403, "Administrator access is required.");
            if (!await db.Submissions.AnyAsync(x => x.Id == id)) return Problem(404, "The work was not found.");

            var record = await db.AudioProcessing.SingleOrDefaultAsync(x => x.SubmissionId == id);
            if (record is null) { record = new AudioProcessingRecord { SubmissionId = id }; db.AudioProcessing.Add(record); }
            record.Status = "queued";
            record.Error = "";
            record.CompletedAtUtc = null;
            await db.SaveChangesAsync();
            await queue.EnqueueAsync(id);
            await WorkflowEvents.RecordAsync(db, admin, "Audio quality rerun requested", "Submission", id.ToString());
            return Results.Json(new { ok = true, status = record.Status });
        });

        // Approved work is packaged on demand, including text and metadata, and then retained on the data volume.
        app.MapGet("/api/submissions/{id:int}/delivery", async (int id, HttpRequest request, EldfDbContext db, AudioStoragePaths paths) =>
        {
            var admin = await SessionAuth.CurrentUser(request, db);
            if (admin is null) return Problem(401, "Please sign in to continue.");
            if (admin.Role != "admin") return Problem(403, "Administrator access is required.");
            var submission = await db.Submissions.Include(x => x.User).SingleOrDefaultAsync(x => x.Id == id && x.Status == "Approved");
            if (submission is null) return Problem(404, "Approved work was not found.");
            var record = await db.AudioProcessing.SingleOrDefaultAsync(x => x.SubmissionId == id);
            string full;
            try { full = await SubmissionDeliveryPackage.EnsureAsync(submission, record, paths, request.HttpContext.RequestAborted); }
            catch (FileNotFoundException) { return Problem(404, "The original audio file is not available on the server."); }
            if (record is not null)
            {
                record.DeliveryZipPath = paths.ToRelative(full);
                await db.SaveChangesAsync();
            }
            await WorkflowEvents.RecordAsync(db, admin, "Delivery package downloaded", "Submission", id.ToString());
            return Results.File(full, "application/zip", Path.GetFileName(full));
        });

        app.MapGet("/api/admin/dataset-export", async (HttpRequest request, EldfDbContext db, AudioStoragePaths paths) =>
        {
            var admin = await SessionAuth.CurrentUser(request, db);
            if (admin is null) return Problem(401, "Please sign in to continue.");
            if (admin.Role != "admin") return Problem(403, "Administrator access is required.");
            var values = request.Query["ids"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (values.Length is < 1 or > 100 || values.Any(value => !int.TryParse(value, out var parsed) || parsed <= 0))
                return Problem(422, "Select between 1 and 100 approved submissions for export.");
            var ids = values.Select(int.Parse).Distinct().ToArray();
            if (ids.Length != values.Length) return Problem(422, "Each submission can appear only once in an export.");
            var submissions = await db.Submissions.AsNoTracking().Include(x => x.User)
                .Where(x => ids.Contains(x.Id) && x.Status == "Approved").ToListAsync();
            if (submissions.Count != ids.Length) return Problem(404, "One or more selected approved submissions were not found.");

            var packages = new List<(string Name, string Path)>();
            foreach (var submission in submissions)
            {
                var processing = await db.AudioProcessing.SingleOrDefaultAsync(x => x.SubmissionId == submission.Id);
                string packagePath;
                try { packagePath = await SubmissionDeliveryPackage.EnsureAsync(submission, processing, paths, request.HttpContext.RequestAborted); }
                catch (FileNotFoundException) { return Problem(404, $"The original audio for submission {submission.Id} is not available."); }
                if (processing is not null && processing.DeliveryZipPath != paths.ToRelative(packagePath))
                {
                    processing.DeliveryZipPath = paths.ToRelative(packagePath);
                    await db.SaveChangesAsync();
                }
                var code = SubmissionPackageNames.Create(submission.User.FullName, submission.Id);
                packages.Add(($"{code}.zip", packagePath));
            }
            await WorkflowEvents.RecordAsync(db, admin, "Bulk dataset export downloaded", "Dataset", "approved", $"{packages.Count} approved packages");
            return Results.Stream(async output =>
            {
                using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
                foreach (var package in packages)
                {
                    await using var source = File.OpenRead(package.Path);
                    using var packageArchive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
                    foreach (var packageEntry in packageArchive.Entries)
                    {
                        var path = $"{Path.GetFileNameWithoutExtension(package.Name)}/{packageEntry.FullName}";
                        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
                        await using var entryStream = entry.Open();
                        await using var packageEntryStream = packageEntry.Open();
                        await packageEntryStream.CopyToAsync(entryStream, request.HttpContext.RequestAborted);
                    }
                }
            }, "application/zip", "AfriLisan-approved-dataset.zip");
        });

        app.MapGet("/api/admin/events", async (HttpRequest request, EldfDbContext db) =>
        {
            var admin = await SessionAuth.CurrentUser(request, db);
            if (admin is null) return Problem(401, "Please sign in to continue.");
            if (admin.Role != "admin") return Problem(403, "Administrator access is required.");
            var events = await db.ActivityEvents.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(1000).ToListAsync();
            return Results.Json(events.Select(EventView));
        });
    }

    private static IResult Problem(int status, string detail) => Results.Json(new { detail }, statusCode: status);
    private static UserView View(UserAccount user) => new(user.Id, user.FullName, user.Phone, user.Role, user.InterfaceLanguage);

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 310_000, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2_sha256$310000${Convert.ToHexString(salt).ToLowerInvariant()}${Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static bool VerifyPassword(string password, string stored)
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

    private static async Task<Dictionary<int, List<object>>> ReviewHistory(EldfDbContext db, List<int> ids)
    {
        if (ids.Count == 0) return [];
        var events = await db.ReviewEvents.AsNoTracking().Where(x => ids.Contains(x.SubmissionId)).OrderBy(x => x.CreatedAtUtc).ToListAsync();
        return events.GroupBy(x => x.SubmissionId).ToDictionary(g => g.Key, g => g.Select(x => (object)new { id = x.Id, role = x.ReviewerRole, decision = x.Decision, comment = x.Comment, createdAt = x.CreatedAtUtc }).ToList());
    }

    private static object SubmissionView(WorkSubmission submission, List<object> history, bool hasDeliveryPackage = false) => new
    {
        id = submission.Id,
        submissionCode = SubmissionPackageNames.Create(submission.User.FullName, submission.Id),
        hasDeliveryPackage,
        contributorId = submission.UserAccountId,
        contributorName = submission.User.FullName,
        contributorPhone = submission.User.Phone,
        language = submission.ProjectLanguage,
        topicTitle = submission.TopicTitle,
        durationSeconds = submission.DurationSeconds,
        audioUrl = $"/api/submissions/{submission.Id}/audio",
        transcript = submission.Transcript,
        translation = submission.Translation,
        status = submission.Status,
        agentComment = submission.AgentReviewComment,
        adminComment = submission.AdminReviewComment,
        rejectionReason = submission.AdminRejectionReason,
        assignedAgentId = submission.AssignedAgentId,
        awardAmount = submission.AwardAmount,
        currency = submission.RewardCurrency,
        createdAt = submission.CreatedAtUtc,
        updatedAt = submission.UpdatedAtUtc,
        reviewHistory = history
    };

    private static object DraftView(WorkDraft draft) => new
    {
        id = draft.Id,
        ownerUserId = draft.UserAccountId,
        ownerName = draft.User?.FullName ?? "",
        assignedUserId = draft.AssignedUserAccountId,
        assignedUserName = draft.AssignedUser?.FullName ?? "",
        language = draft.ProjectLanguage,
        topicTitle = draft.TopicTitle,
        translationLanguage = draft.TranslationLanguage,
        hasAudio = draft.AudioPath.Length > 0,
        audioUrl = draft.AudioPath.Length > 0 ? $"/api/drafts/{draft.Id}/audio" : null,
        durationSeconds = draft.DurationSeconds,
        transcript = draft.Transcript,
        translation = draft.Translation,
        speakers = ParseJsonArray(draft.SpeakersJson),
        recordingType = draft.RecordingType,
        updatedAt = draft.UpdatedAtUtc
    };

    private static object AdminDraftView(WorkDraft draft) => new
    {
        id = draft.Id,
        ownerUserId = draft.UserAccountId,
        ownerName = draft.User.FullName,
        ownerPhone = draft.User.Phone,
        assignedUserId = draft.AssignedUserAccountId,
        assignedUserName = draft.AssignedUser?.FullName ?? "",
        language = draft.ProjectLanguage,
        topicTitle = draft.TopicTitle,
        durationSeconds = draft.DurationSeconds,
        hasAudio = !string.IsNullOrWhiteSpace(draft.AudioPath),
        hasTranscript = !string.IsNullOrWhiteSpace(draft.Transcript),
        hasTranslation = !string.IsNullOrWhiteSpace(draft.Translation),
        updatedAt = draft.UpdatedAtUtc
    };

    private static object DraftResumeView(WorkDraft draft, List<string> speakerIds, Dictionary<string, bool> consentBySpeaker) => new
    {
        id = draft.Id,
        ownerUserId = draft.UserAccountId,
        ownerName = draft.User?.FullName ?? "",
        assignedUserId = draft.AssignedUserAccountId,
        language = draft.ProjectLanguage,
        topicTitle = draft.TopicTitle,
        translationLanguage = draft.TranslationLanguage,
        hasAudio = !string.IsNullOrWhiteSpace(draft.AudioPath),
        audioUrl = draft.AudioPath.Length > 0 ? $"/api/drafts/{draft.Id}/audio" : null,
        durationSeconds = draft.DurationSeconds,
        transcript = draft.Transcript,
        translation = draft.Translation,
        speakers = speakerIds.Select(id => new { speakerId = id, coreConsent = consentBySpeaker.GetValueOrDefault(id) }).ToList(),
        recordingType = draft.RecordingType,
        updatedAt = draft.UpdatedAtUtc
    };

    private static object AdminUserView(UserAccount user, string? referralCode) => new
    {
        id = user.Id,
        name = user.FullName,
        phone = user.Phone,
        role = user.Role,
        language = user.InterfaceLanguage,
        active = user.IsActive,
        agentId = user.ReferredByAgentId,
        referralCode,
        registeredAt = user.CreatedAtUtc
    };

    private static object TicketView(SupportTicket ticket) => new
    {
        id = ticket.Id,
        subject = ticket.Subject,
        status = ticket.Status,
        createdById = ticket.CreatedByUserId,
        createdByName = ticket.CreatedBy.FullName,
        createdByPhone = ticket.CreatedBy.Phone,
        assignedAgentId = ticket.AssignedAgentUserId,
        createdAt = ticket.CreatedAtUtc,
        updatedAt = ticket.UpdatedAtUtc,
        messages = ticket.Messages.OrderBy(x => x.CreatedAtUtc).Select(x => new { id = x.Id, userId = x.UserAccountId, name = x.User.FullName, role = x.User.Role, body = x.Body, createdAt = x.CreatedAtUtc })
    };

    private static object PaymentView(PaymentRequest payment) => new
    {
        id = payment.Id,
        userId = payment.UserAccountId,
        userName = payment.User?.FullName,
        userPhone = payment.User?.Phone,
        amount = payment.Amount,
        currency = payment.Currency,
        status = payment.Status,
        reviewNote = payment.ReviewNote,
        proofReference = payment.PaymentProofReference,
        requestedAt = payment.RequestedAtUtc,
        reviewedAt = payment.ReviewedAtUtc,
        paidAt = payment.PaidAtUtc
    };

    private static object EventView(ActivityEvent e) => new { id = e.Id, actorId = e.ActorUserId, role = e.ActorRole, type = e.EventType, entity = e.EntityType, entityId = e.EntityId, details = e.Details, createdAt = e.CreatedAtUtc };

    private static string AfricanWideCopy(string? saved, string fallback) =>
        string.IsNullOrWhiteSpace(saved) || saved.Contains("Ethiopia", StringComparison.OrdinalIgnoreCase) || saved.Contains("Ethiopian", StringComparison.OrdinalIgnoreCase) || saved.Contains("ኢትዮጵ", StringComparison.Ordinal)
            ? fallback
            : saved;

    private static string NormalizeInternationalPhone(string phone)
    {
        var trimmed = phone.Trim();
        var digits = string.Concat(trimmed.Where(char.IsDigit));
        if (trimmed.StartsWith('+') && digits.Length is >= 8 and <= 15) return "+" + digits;
        if (trimmed.StartsWith("00", StringComparison.Ordinal) && digits.Length is >= 10 and <= 17) return "+" + digits[2..];
        if (digits.StartsWith("251", StringComparison.Ordinal) && digits.Length == 12) return "+" + digits;
        if (digits.StartsWith('0') && digits.Length == 10) return "+251" + digits[1..];
        return digits.Length == 9 ? "+251" + digits : trimmed;
    }

    private static object BuildReport(List<WorkSubmission> submissions, List<UserAccount> users, List<PaymentRequest> payments, List<SupportTicket> tickets) => new
    {
        audioHours = Math.Round(submissions.Sum(x => (double)x.DurationSeconds) / 3600d, 2),
        approved = submissions.Count(x => x.Status == "Approved"),
        returned = submissions.Count(x => x.Status.StartsWith("Returned", StringComparison.OrdinalIgnoreCase)),
        pendingReview = submissions.Count(x => x.Status.Contains("review", StringComparison.OrdinalIgnoreCase)),
        contributors = users.Count(x => x.Role == "contributor"),
        agents = users.Count(x => x.Role == "agent"),
        rejected = submissions.Count(x => x.Status == "Rejected"),
        byLanguage = submissions.GroupBy(x => x.ProjectLanguage).Select(g => new { language = g.Key, audioHours = Math.Round(g.Sum(x => (double)x.DurationSeconds) / 3600d, 2), submissions = g.Count(), approved = g.Count(x => x.Status == "Approved"), returned = g.Count(x => x.Status.StartsWith("Returned", StringComparison.OrdinalIgnoreCase)) }).OrderBy(x => x.language),
        // The admin Reports tab renders a "By date" table; without this key the table was always empty.
        byDate = submissions.GroupBy(x => x.CreatedAtUtc.Date).OrderByDescending(g => g.Key).Take(90).Select(g => new { date = g.Key.ToString("yyyy-MM-dd"), audioHours = Math.Round(g.Sum(x => (double)x.DurationSeconds) / 3600d, 2), submissions = g.Count(), approved = g.Count(x => x.Status == "Approved"), returned = g.Count(x => x.Status.StartsWith("Returned", StringComparison.OrdinalIgnoreCase)) }),
        byAgent = submissions.GroupBy(x => x.AssignedAgentId).Select(g => new { agentId = g.Key, agent = users.SingleOrDefault(u => u.Id == g.Key)?.FullName ?? "Unassigned", submissions = g.Count(), approved = g.Count(x => x.Status == "Approved"), audioHours = Math.Round(g.Sum(x => (double)x.DurationSeconds) / 3600d, 2) }).OrderBy(x => x.agent),
        byContributor = submissions.GroupBy(x => x.UserAccountId).Select(g => new { contributorId = g.Key, contributor = g.First().User.FullName, submissions = g.Count(), approved = g.Count(x => x.Status == "Approved"), audioHours = Math.Round(g.Sum(x => (double)x.DurationSeconds) / 3600d, 2) }).OrderBy(x => x.contributor),
        payments = payments.GroupBy(x => x.Status).Select(g => new { status = g.Key, count = g.Count(), total = g.Sum(x => x.Amount) }),
        support = tickets.GroupBy(x => x.Status).Select(g => new { status = g.Key, count = g.Count() })
    };

    private static bool CanSeeSubmission(UserAccount user, WorkSubmission submission) => user.Role == "admin" || user.Id == submission.UserAccountId || (user.Role == "agent" && (submission.AssignedAgentId == user.Id || submission.User.ReferredByAgentId == user.Id));
    private static bool CanAccessTicket(UserAccount user, SupportTicket ticket) => user.Role == "admin" || ticket.CreatedByUserId == user.Id || (user.Role == "agent" && (ticket.AssignedAgentUserId == user.Id || ticket.CreatedBy.ReferredByAgentId == user.Id));

    private static async Task<decimal> AvailableBalance(EldfDbContext db, int userId)
    {
        var earned = await db.Submissions.Where(x => x.UserAccountId == userId && x.Status == "Approved" && x.AwardAmount != null).SumAsync(x => x.AwardAmount ?? 0m);
        var reserved = await db.PaymentRequests.Where(x => x.UserAccountId == userId && x.Status != "Rejected" && x.Status != "Returned by agent").SumAsync(x => x.Amount);
        return Math.Max(0m, earned - reserved);
    }

    private static string SafeJsonArray(string? value)
    {
        try { using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(value) ? "[]" : value); return json.RootElement.ValueKind == JsonValueKind.Array ? json.RootElement.GetRawText() : "[]"; }
        catch { return "[]"; }
    }

    private static object ParseJsonArray(string value)
    {
        try { return JsonDocument.Parse(SafeJsonArray(value)).RootElement.Clone(); }
        catch { return Array.Empty<object>(); }
    }

    // IConfiguration and IWebHostEnvironment are real DI services. WebApplication is not, so taking it as a
    // minimal-API handler parameter made ASP.NET infer it as a JSON body and broke endpoint construction.
    private static string DataDirectory(IConfiguration configuration, IWebHostEnvironment environment) =>
        Path.GetFullPath(Environment.GetEnvironmentVariable("ELDF_DATA_DIR") ?? configuration["DataDirectory"] ?? "../../eldf-server-data", environment.ContentRootPath);

    private static string? SafeDataPath(IConfiguration configuration, IWebHostEnvironment environment, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) return null;
        var root = Path.GetFullPath(DataDirectory(configuration, environment)) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative));
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    private static void DeleteSafeDataFile(IConfiguration configuration, IWebHostEnvironment environment, string relative)
    {
        var path = SafeDataPath(configuration, environment, relative);
        if (path is not null && File.Exists(path)) File.Delete(path);
    }

    private static async Task RunProcessForDrafts(string executable, List<string> arguments)
    {
        try
        {
            using var process = new Process { StartInfo = new ProcessStartInfo
            {
                FileName = executable, RedirectStandardError = true, RedirectStandardOutput = true,
                UseShellExecute = false, CreateNoWindow = true
            } };
            foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            var stderr = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            await Task.WhenAll(stderr, stdout);
            if (process.ExitCode != 0) throw new InvalidOperationException("The recording parts could not be combined. Keep the local draft and try again.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("The server audio tool is unavailable. The browser still has the local draft.");
        }
    }

    private static string SafeExtension(string fileName)
    {
        var ext = Path.GetExtension(Path.GetFileName(fileName)).ToLowerInvariant();
        return ext.Length <= 10 && ext.All(c => char.IsAsciiLetterOrDigit(c) || c == '.') && ext is ".wav" or ".webm" or ".ogg" or ".m4a" or ".mp3" ? ext : ".audio";
    }

    private static string AudioContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".wav" => "audio/wav", ".webm" => "audio/webm", ".ogg" => "audio/ogg", ".m4a" => "audio/mp4", ".mp3" => "audio/mpeg", _ => "application/octet-stream"
    };
}

public sealed record ReviewInput(string? Decision, string? Comment);
public sealed record AdminReviewInput(string? Decision, string? Comment, string? Reason, decimal? AwardAmount, string? Currency);
public sealed record ResubmitInput(string? Transcript, string? Translation);
public sealed record AdminUserUpdateInput(string? FullName, string? Role, bool? IsActive, int? AgentId, bool? ClearAgentAssignment);
public sealed record SupportCreateInput(string? Subject, string? Message);
public sealed record SupportMessageInput(string? Message);
public sealed record AdminTicketUpdateInput(string? Status, int? AssignedAgentId);
public sealed record AgentTicketUpdateInput(bool? Escalate);
public sealed record PaymentRequestInput(decimal Amount);
public sealed record PasswordResetRequestInput(string? Phone);
public sealed record PasswordChangeInput(string? CurrentPassword, string? NewPassword);
public sealed record AdminProfileUpdateInput(string? FullName, string? Phone, string? CurrentPassword);
public sealed record AdminDraftAssignmentInput(int? AssignedUserId);
public sealed record AdminSubmissionAssignmentInput(int? AgentId);
public sealed record PaymentDecisionInput(string? Decision, string? Note);
public sealed record AdminPaymentDecisionInput(string? Decision, string? Note, string? ProofReference);
public sealed record SiteContentInput(string? VisionAm, string? VisionEn, string? MissionAm, string? MissionEn);
