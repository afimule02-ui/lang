using System.Security.Cryptography;
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
    private const string DefaultVision = "የአፍሪካ እያንዳንዱ ድምፅ በዲጂታል ዓለም ውስጥ ቦታ እንዲኖረው።";
    private const string DefaultVisionEnglish = "Every African voice deserves a place in the digital world.";
    private const string DefaultMission = "የአፍሪካን ቋንቋዎች በፈቃድ የተመሠረተ ድምፅ፣ ጽሑፍ፣ ትርጉምና ጥራት ማረጋገጫ ሥራ እንሰበስባለን።";
    private const string DefaultMissionEnglish = "We collect consent-led audio, transcription, translation, and quality-reviewed data across Africa's languages.";

    public static void MapWorkflowApi(this WebApplication app)
    {
        app.MapGet("/api/site-content", async (EldfDbContext db) =>
        {
            var content = await db.SiteContents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1);
            return Results.Json(new { vision = new { am = content?.Vision ?? DefaultVision, en = content?.VisionEnglish ?? DefaultVisionEnglish }, mission = new { am = content?.Mission ?? DefaultMission, en = content?.MissionEnglish ?? DefaultMissionEnglish } });
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
                return Results.Json(new
                {
                    role = user.Role,
                    profile = View(user),
                    users = users.Select(x => AdminUserView(x, referrals.GetValueOrDefault(x.Id))).ToList(),
                    submissions = submissions.Select(x => SubmissionView(x, reviews.GetValueOrDefault(x.Id, []))).ToList(),
                    supportTickets = tickets.Select(TicketView).ToList(),
                    paymentRequests = payments.Select(PaymentView).ToList(),
                    siteContent = new { vision = new { am = content?.Vision ?? DefaultVision, en = content?.VisionEnglish ?? DefaultVisionEnglish }, mission = new { am = content?.Mission ?? DefaultMission, en = content?.MissionEnglish ?? DefaultMissionEnglish } },
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
                    .Where(x => teamIds.Contains(x.UserAccountId))
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
            var ownDrafts = await db.Drafts.AsNoTracking().Where(x => x.UserAccountId == user.Id).OrderByDescending(x => x.UpdatedAtUtc).Take(100).ToListAsync();
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

        app.MapGet("/api/drafts/{id:int}/audio", async (int id, HttpRequest request, EldfDbContext db, IConfiguration configuration, IWebHostEnvironment environment) =>
        {
            var user = await SessionAuth.CurrentUser(request, db);
            if (user is null) return Problem(401, "Please sign in to continue.");
            var draft = await db.Drafts.SingleOrDefaultAsync(x => x.Id == id && x.UserAccountId == user.Id);
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
            WorkDraft? draft = id > 0 ? await db.Drafts.SingleOrDefaultAsync(x => x.Id == id && x.UserAccountId == user.Id) : null;
            if (id > 0 && draft is null) return Problem(404, "The saved draft was not found.");
            draft ??= new WorkDraft { UserAccountId = user.Id, ProjectLanguage = "" };
            draft.ProjectLanguage = WorkflowEvents.Limit(form["language"], 160);
            draft.TranslationLanguage = WorkflowEvents.Limit(form["translation_language"], 160);
            draft.Transcript = WorkflowEvents.Limit(form["transcript"], 2_000_000);
            draft.Translation = WorkflowEvents.Limit(form["translation"], 2_000_000);
            draft.RecordingType = WorkflowEvents.Limit(form["recording_type"], 80, "Conversation");
            draft.ConsentReference = WorkflowEvents.Limit(form["consent_reference"], 80);
            draft.SpeakersJson = SafeJsonArray(form["speakers_json"].ToString());
            draft.DurationSeconds = int.TryParse(form["duration_seconds"], out var duration) ? Math.Clamp(duration, 0, 3600) : draft.DurationSeconds;
            draft.UpdatedAtUtc = DateTime.UtcNow;
            var file = form.Files.GetFile("file");
            string? newPath = null;
            if (file is { Length: > 0 })
            {
                if (file.Length > configuration.GetValue<long>("Audio:MaxBytes", 250L * 1024 * 1024)) return Problem(413, "The draft audio is too large.");
                var folder = Path.Combine(DataDirectory(configuration, environment), "drafts", user.Id.ToString());
                Directory.CreateDirectory(folder);
                var ext = SafeExtension(file.FileName);
                newPath = Path.Combine("drafts", user.Id.ToString(), Guid.NewGuid().ToString("N") + ext);
                var full = SafeDataPath(configuration, environment, newPath);
                if (full is null) return Problem(400, "Invalid audio path.");
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                await using var output = File.Create(full);
                await file.CopyToAsync(output);
            }
            var oldPath = draft.AudioPath;
            if (newPath is not null) draft.AudioPath = newPath;
            if (id == 0) db.Drafts.Add(draft);
            await db.SaveChangesAsync();
            if (newPath is not null && oldPath.Length > 0) DeleteSafeDataFile(configuration, environment, oldPath);
            await WorkflowEvents.RecordAsync(db, user, id == 0 ? "Work draft created" : "Work draft saved", "Draft", draft.Id.ToString(), draft.ProjectLanguage);
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

        app.MapPatch("/api/admin/users/{id:int}", async (int id, HttpRequest request, EldfDbContext db) =>
        {
            var admin = await SessionAuth.CurrentUser(request, db);
            if (admin is null) return Problem(401, "Please sign in to continue.");
            if (admin.Role != "admin") return Problem(403, "Administrator access is required.");
            var target = await db.Users.SingleOrDefaultAsync(x => x.Id == id);
            if (target is null) return Problem(404, "The account was not found.");
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
            return Results.Json(AdminUserView(target, await db.AgentReferrals.Where(x => x.AgentUserId == target.Id).Select(x => x.Code).SingleOrDefaultAsync()));
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

        // The buyer delivery package: original and processed as separate files plus metadata.
        app.MapGet("/api/submissions/{id:int}/delivery", async (int id, HttpRequest request, EldfDbContext db, AudioStoragePaths paths) =>
        {
            var admin = await SessionAuth.CurrentUser(request, db);
            if (admin is null) return Problem(401, "Please sign in to continue.");
            if (admin.Role != "admin") return Problem(403, "Administrator access is required.");
            var record = await db.AudioProcessing.AsNoTracking().SingleOrDefaultAsync(x => x.SubmissionId == id);
            if (record is null || record.DeliveryZipPath.Length == 0) return Problem(404, "No delivery package has been built for this work yet.");
            var full = paths.Resolve(record.DeliveryZipPath);
            if (full is null || !File.Exists(full)) return Problem(404, "The delivery package is not available on the server.");
            await WorkflowEvents.RecordAsync(db, admin, "Delivery package downloaded", "Submission", id.ToString());
            return Results.File(full, "application/zip", $"ELDF_SUBMISSION_{id}.zip");
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

    private static async Task<Dictionary<int, List<object>>> ReviewHistory(EldfDbContext db, List<int> ids)
    {
        if (ids.Count == 0) return [];
        var events = await db.ReviewEvents.AsNoTracking().Where(x => ids.Contains(x.SubmissionId)).OrderBy(x => x.CreatedAtUtc).ToListAsync();
        return events.GroupBy(x => x.SubmissionId).ToDictionary(g => g.Key, g => g.Select(x => (object)new { id = x.Id, role = x.ReviewerRole, decision = x.Decision, comment = x.Comment, createdAt = x.CreatedAtUtc }).ToList());
    }

    private static object SubmissionView(WorkSubmission submission, List<object> history) => new
    {
        id = submission.Id,
        contributorId = submission.UserAccountId,
        contributorName = submission.User.FullName,
        contributorPhone = submission.User.Phone,
        language = submission.ProjectLanguage,
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
        language = draft.ProjectLanguage,
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
public sealed record PaymentDecisionInput(string? Decision, string? Note);
public sealed record AdminPaymentDecisionInput(string? Decision, string? Note, string? ProofReference);
public sealed record SiteContentInput(string? VisionAm, string? VisionEn, string? MissionAm, string? MissionEn);
