using Microsoft.EntityFrameworkCore;

namespace Eldf.Api;

public sealed class UserAccount
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string InterfaceLanguage { get; set; } = "am";
    public string Role { get; set; } = "contributor";
    public string ReferralCode { get; set; } = "";
    public int? ReferredByAgentId { get; set; }
    public UserAccount? ReferredByAgent { get; set; }
    public bool IsActive { get; set; } = true;
    public string ConsentReference { get; set; } = "CONSENT-TERMS-v1-AUDIO-v1";
    public DateTime ConsentedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<SpeakerProfile> Speakers { get; set; } = [];
    public List<LoginSession> Sessions { get; set; } = [];
}

public sealed class LoginSession
{
    public int Id { get; set; }
    public int UserAccountId { get; set; }
    public UserAccount User { get; set; } = null!;
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
}

public sealed class SpeakerProfile
{
    public int Id { get; set; }
    public int UserAccountId { get; set; }
    public UserAccount User { get; set; } = null!;
    public string SpeakerId { get; set; } = "";
    public string FullName { get; set; } = "";
    public string AgeRange { get; set; } = "";
    public string Gender { get; set; } = "";
    public string Education { get; set; } = "";
    public string Language { get; set; } = "";
    public string Region { get; set; } = "";
    // Kept for the rows already collected. The speaker form no longer asks for a dialect,
    // because the region and the chosen language already carry that information.
    public string Dialect { get; set; } = "";
}

public sealed class WorkSubmission
{
    public int Id { get; set; }
    public int UserAccountId { get; set; }
    public UserAccount User { get; set; } = null!;
    public string ProjectLanguage { get; set; } = "";
    public int DurationSeconds { get; set; }
    public string AudioPath { get; set; } = "";
    public string Transcript { get; set; } = "";
    public string Translation { get; set; } = "";
    public string Status { get; set; } = "Awaiting review";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? AssignedAgentId { get; set; }
    public string AgentReviewComment { get; set; } = "";
    public string AdminReviewComment { get; set; } = "";
    public string AdminRejectionReason { get; set; } = "";
    public int? AgentReviewedById { get; set; }
    public int? AdminReviewedById { get; set; }
    public DateTime? AgentReviewedAtUtc { get; set; }
    public DateTime? AdminReviewedAtUtc { get; set; }
    public decimal? AwardAmount { get; set; }
    public string RewardCurrency { get; set; } = "ETB";
}

public sealed class AgentReferral
{
    public int Id { get; set; }
    public int AgentUserId { get; set; }
    public UserAccount Agent { get; set; } = null!;
    public string Code { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class WorkDraft
{
    public int Id { get; set; }
    public int UserAccountId { get; set; }
    public UserAccount User { get; set; } = null!;
    public string ProjectLanguage { get; set; } = "";
    public string TranslationLanguage { get; set; } = "";
    public string AudioPath { get; set; } = "";
    public int DurationSeconds { get; set; }
    public string Transcript { get; set; } = "";
    public string Translation { get; set; } = "";
    public string SpeakersJson { get; set; } = "[]";
    public string RecordingType { get; set; } = "Conversation";
    public string ConsentReference { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class WorkReviewEvent
{
    public int Id { get; set; }
    public int SubmissionId { get; set; }
    public WorkSubmission Submission { get; set; } = null!;
    public int ReviewerUserId { get; set; }
    public string ReviewerRole { get; set; } = "";
    public string Decision { get; set; } = "";
    public string Comment { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SupportTicket
{
    public int Id { get; set; }
    public int CreatedByUserId { get; set; }
    public UserAccount CreatedBy { get; set; } = null!;
    public int? AssignedAgentUserId { get; set; }
    public string Subject { get; set; } = "";
    public string Status { get; set; } = "Open";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<SupportMessage> Messages { get; set; } = [];
}

public sealed class SupportMessage
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public SupportTicket Ticket { get; set; } = null!;
    public int UserAccountId { get; set; }
    public UserAccount User { get; set; } = null!;
    public string Body { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PaymentRequest
{
    public int Id { get; set; }
    public int UserAccountId { get; set; }
    public UserAccount User { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ETB";
    public string Status { get; set; } = "Pending admin review";
    public string ReviewNote { get; set; } = "";
    public string PaymentProofReference { get; set; } = "";
    public int? ReviewedByUserId { get; set; }
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }
}

public sealed class ActivityEvent
{
    public int Id { get; set; }
    public int? ActorUserId { get; set; }
    public string ActorRole { get; set; } = "";
    public string EventType { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Details { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// What the audio quality pipeline measured and decided for one submission.
/// Kept apart from <see cref="WorkSubmission"/> so the review and payment flow is unaffected and
/// a rerun only rewrites this row. The original upload path lives on the submission and the
/// pipeline only ever reads it.
/// </summary>
public sealed class AudioProcessingRecord
{
    public int Id { get; set; }
    public int SubmissionId { get; set; }
    public WorkSubmission Submission { get; set; } = null!;
    /// <summary>queued | running | done | failed</summary>
    public string Status { get; set; } = "queued";
    /// <summary>PASS | AUTO_FIXED | MANUAL_REVIEW | RECORD_AGAIN</summary>
    public string Verdict { get; set; } = "";
    public int AttemptCount { get; set; }
    public string ContributorMessage { get; set; } = "";
    public string ContributorMessageAmharic { get; set; } = "";
    public string OriginalSha256 { get; set; } = "";
    public long OriginalSizeBytes { get; set; }
    public double OriginalDurationSeconds { get; set; }
    public int OriginalSampleRate { get; set; }
    public int OriginalChannels { get; set; }
    public string ProcessedPath { get; set; } = "";
    public string ProcessedSha256 { get; set; } = "";
    public long ProcessedSizeBytes { get; set; }
    public double ProcessedDurationSeconds { get; set; }
    public string DeliveryZipPath { get; set; } = "";
    public double SecondsRemoved { get; set; }
    public bool NoiseReductionApplied { get; set; }
    public bool EdgeTrimApplied { get; set; }
    public bool InteriorSilenceCapped { get; set; }
    public string PipelineVersion { get; set; } = "";
    public string ReportJson { get; set; } = "{}";
    public string Error { get; set; } = "";
    public DateTime QueuedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

public sealed class SiteContent
{
    public int Id { get; set; }
    public string Vision { get; set; } = "የአፍሪካ እያንዳንዱ ድምፅ በዲጂታል ዓለም ውስጥ ቦታ እንዲኖረው።";
    public string VisionEnglish { get; set; } = "Every African voice deserves a place in the digital world.";
    public string Mission { get; set; } = "የአፍሪካን ቋንቋዎች በፈቃድ የተመሠረተ ድምፅ፣ ጽሑፍ፣ ትርጉምና ጥራት ማረጋገጫ ሥራ እንሰበስባለን።";
    public string MissionEnglish { get; set; } = "We collect consent-led audio, transcription, translation, and quality-reviewed data across Africa's languages.";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? UpdatedByUserId { get; set; }
}

public sealed class EldfDbContext(DbContextOptions<EldfDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<LoginSession> Sessions => Set<LoginSession>();
    public DbSet<SpeakerProfile> Speakers => Set<SpeakerProfile>();
    public DbSet<WorkSubmission> Submissions => Set<WorkSubmission>();
    public DbSet<AgentReferral> AgentReferrals => Set<AgentReferral>();
    public DbSet<WorkDraft> Drafts => Set<WorkDraft>();
    public DbSet<WorkReviewEvent> ReviewEvents => Set<WorkReviewEvent>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<SupportMessage> SupportMessages => Set<SupportMessage>();
    public DbSet<PaymentRequest> PaymentRequests => Set<PaymentRequest>();
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();
    public DbSet<SiteContent> SiteContents => Set<SiteContent>();
    public DbSet<AudioProcessingRecord> AudioProcessing => Set<AudioProcessingRecord>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<UserAccount>(e =>
        {
            e.ToTable("eldf_users"); e.HasIndex(x => x.Phone).IsUnique();
            e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.FullName).HasColumnName("full_name").HasMaxLength(160);
            e.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(40); e.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(256);
            e.Property(x => x.InterfaceLanguage).HasColumnName("interface_language").HasMaxLength(2); e.Property(x => x.Role).HasColumnName("role").HasMaxLength(20);
            e.Property(x => x.ReferralCode).HasColumnName("referral_code").HasMaxLength(80); e.Property(x => x.ConsentReference).HasColumnName("consent_reference").HasMaxLength(80);
            e.Property(x => x.ConsentedAtUtc).HasColumnName("consented_at"); e.Property(x => x.CreatedAtUtc).HasColumnName("created_at");
            e.Property(x => x.ReferredByAgentId).HasColumnName("referred_by_agent_id"); e.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            e.HasOne(x => x.ReferredByAgent).WithMany().HasForeignKey(x => x.ReferredByAgentId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<LoginSession>(e =>
        {
            e.ToTable("eldf_sessions"); e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.UserAccountId).HasColumnName("user_id");
            e.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64); e.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at");
        });
        model.Entity<SpeakerProfile>(e =>
        {
            e.ToTable("eldf_speakers"); e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.UserAccountId).HasColumnName("user_id");
            e.Property(x => x.SpeakerId).HasColumnName("speaker_id").HasMaxLength(40);
            e.Property(x => x.FullName).HasColumnName("full_name").HasMaxLength(160).HasDefaultValue("");
            e.Property(x => x.AgeRange).HasColumnName("age_range").HasMaxLength(40);
            e.Property(x => x.Gender).HasColumnName("gender").HasMaxLength(40); e.Property(x => x.Education).HasColumnName("education").HasMaxLength(100);
            e.Property(x => x.Language).HasColumnName("language").HasMaxLength(160); e.Property(x => x.Region).HasColumnName("region").HasMaxLength(160); e.Property(x => x.Dialect).HasColumnName("dialect").HasMaxLength(160);
        });
        model.Entity<WorkSubmission>(e =>
        {
            e.ToTable("eldf_submissions"); e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.UserAccountId).HasColumnName("user_id");
            e.Property(x => x.ProjectLanguage).HasColumnName("project_language").HasMaxLength(160); e.Property(x => x.DurationSeconds).HasColumnName("duration_seconds");
            e.Property(x => x.AudioPath).HasColumnName("audio_path").HasMaxLength(500); e.Property(x => x.Transcript).HasColumnName("transcript");
            e.Property(x => x.Translation).HasColumnName("translation"); e.Property(x => x.Status).HasColumnName("status").HasMaxLength(40); e.Property(x => x.CreatedAtUtc).HasColumnName("created_at");
            e.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at").HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.AssignedAgentId).HasColumnName("assigned_agent_id");
            e.Property(x => x.AgentReviewComment).HasColumnName("agent_review_comment"); e.Property(x => x.AdminReviewComment).HasColumnName("admin_review_comment");
            e.Property(x => x.AdminRejectionReason).HasColumnName("admin_rejection_reason"); e.Property(x => x.AgentReviewedById).HasColumnName("agent_reviewed_by_id");
            e.Property(x => x.AdminReviewedById).HasColumnName("admin_reviewed_by_id"); e.Property(x => x.AgentReviewedAtUtc).HasColumnName("agent_reviewed_at");
            e.Property(x => x.AdminReviewedAtUtc).HasColumnName("admin_reviewed_at"); e.Property(x => x.AwardAmount).HasColumnName("award_amount").HasColumnType("decimal(18,2)");
            e.Property(x => x.RewardCurrency).HasColumnName("reward_currency").HasMaxLength(3).HasDefaultValue("ETB");
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.AssignedAgentId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<AgentReferral>(e =>
        {
            e.ToTable("eldf_agent_referrals"); e.HasIndex(x => x.AgentUserId).IsUnique(); e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.AgentUserId).HasColumnName("agent_user_id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(64); e.Property(x => x.CreatedAtUtc).HasColumnName("created_at");
            e.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentUserId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<WorkDraft>(e =>
        {
            e.ToTable("eldf_work_drafts"); e.HasIndex(x => new { x.UserAccountId, x.UpdatedAtUtc });
            e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.UserAccountId).HasColumnName("user_id");
            e.Property(x => x.ProjectLanguage).HasColumnName("project_language").HasMaxLength(160); e.Property(x => x.TranslationLanguage).HasColumnName("translation_language").HasMaxLength(160);
            e.Property(x => x.AudioPath).HasColumnName("audio_path").HasMaxLength(500); e.Property(x => x.DurationSeconds).HasColumnName("duration_seconds");
            e.Property(x => x.Transcript).HasColumnName("transcript"); e.Property(x => x.Translation).HasColumnName("translation");
            e.Property(x => x.SpeakersJson).HasColumnName("speakers_json"); e.Property(x => x.RecordingType).HasColumnName("recording_type").HasMaxLength(80);
            e.Property(x => x.ConsentReference).HasColumnName("consent_reference").HasMaxLength(80); e.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at");
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserAccountId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<WorkReviewEvent>(e =>
        {
            e.ToTable("eldf_review_events"); e.HasIndex(x => new { x.SubmissionId, x.CreatedAtUtc });
            e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.SubmissionId).HasColumnName("submission_id"); e.Property(x => x.ReviewerUserId).HasColumnName("reviewer_user_id");
            e.Property(x => x.ReviewerRole).HasColumnName("reviewer_role").HasMaxLength(20); e.Property(x => x.Decision).HasColumnName("decision").HasMaxLength(40);
            e.Property(x => x.Comment).HasColumnName("comment"); e.Property(x => x.CreatedAtUtc).HasColumnName("created_at");
            e.HasOne(x => x.Submission).WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<SupportTicket>(e =>
        {
            e.ToTable("eldf_support_tickets"); e.HasIndex(x => new { x.AssignedAgentUserId, x.Status });
            e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id"); e.Property(x => x.AssignedAgentUserId).HasColumnName("assigned_agent_user_id");
            e.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(200); e.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at"); e.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at");
            e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.AssignedAgentUserId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<SupportMessage>(e =>
        {
            e.ToTable("eldf_support_messages"); e.HasIndex(x => new { x.TicketId, x.CreatedAtUtc });
            e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.TicketId).HasColumnName("ticket_id"); e.Property(x => x.UserAccountId).HasColumnName("user_id");
            e.Property(x => x.Body).HasColumnName("body"); e.Property(x => x.CreatedAtUtc).HasColumnName("created_at");
            e.HasOne(x => x.Ticket).WithMany(x => x.Messages).HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserAccountId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<PaymentRequest>(e =>
        {
            e.ToTable("eldf_payment_requests"); e.HasIndex(x => new { x.UserAccountId, x.RequestedAtUtc });
            e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.UserAccountId).HasColumnName("user_id"); e.Property(x => x.Amount).HasColumnName("amount").HasColumnType("decimal(18,2)");
            e.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3); e.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
            e.Property(x => x.ReviewNote).HasColumnName("review_note"); e.Property(x => x.PaymentProofReference).HasColumnName("payment_proof_reference").HasMaxLength(500);
            e.Property(x => x.ReviewedByUserId).HasColumnName("reviewed_by_user_id"); e.Property(x => x.RequestedAtUtc).HasColumnName("requested_at");
            e.Property(x => x.ReviewedAtUtc).HasColumnName("reviewed_at"); e.Property(x => x.PaidAtUtc).HasColumnName("paid_at");
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserAccountId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<ActivityEvent>(e =>
        {
            e.ToTable("eldf_activity_events"); e.HasIndex(x => x.CreatedAtUtc);
            e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.ActorUserId).HasColumnName("actor_user_id"); e.Property(x => x.ActorRole).HasColumnName("actor_role").HasMaxLength(20);
            e.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(100); e.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(80);
            e.Property(x => x.EntityId).HasColumnName("entity_id").HasMaxLength(80); e.Property(x => x.Details).HasColumnName("details").HasMaxLength(2000);
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at");
        });
        model.Entity<AudioProcessingRecord>(e =>
        {
            e.ToTable("eldf_audio_processing");
            e.HasIndex(x => x.SubmissionId).IsUnique();
            e.HasIndex(x => new { x.Status, x.QueuedAtUtc });
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.SubmissionId).HasColumnName("submission_id");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("queued");
            e.Property(x => x.Verdict).HasColumnName("verdict").HasMaxLength(20).HasDefaultValue("");
            e.Property(x => x.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0);
            e.Property(x => x.ContributorMessage).HasColumnName("contributor_message").HasMaxLength(1000).HasDefaultValue("");
            e.Property(x => x.ContributorMessageAmharic).HasColumnName("contributor_message_am").HasMaxLength(1000).HasDefaultValue("");
            e.Property(x => x.OriginalSha256).HasColumnName("original_sha256").HasMaxLength(64).HasDefaultValue("");
            e.Property(x => x.OriginalSizeBytes).HasColumnName("original_size_bytes").HasDefaultValue(0L);
            e.Property(x => x.OriginalDurationSeconds).HasColumnName("original_duration_seconds").HasDefaultValue(0d);
            e.Property(x => x.OriginalSampleRate).HasColumnName("original_sample_rate").HasDefaultValue(0);
            e.Property(x => x.OriginalChannels).HasColumnName("original_channels").HasDefaultValue(0);
            e.Property(x => x.ProcessedPath).HasColumnName("processed_path").HasMaxLength(500).HasDefaultValue("");
            e.Property(x => x.ProcessedSha256).HasColumnName("processed_sha256").HasMaxLength(64).HasDefaultValue("");
            e.Property(x => x.ProcessedSizeBytes).HasColumnName("processed_size_bytes").HasDefaultValue(0L);
            e.Property(x => x.ProcessedDurationSeconds).HasColumnName("processed_duration_seconds").HasDefaultValue(0d);
            e.Property(x => x.DeliveryZipPath).HasColumnName("delivery_zip_path").HasMaxLength(500).HasDefaultValue("");
            e.Property(x => x.SecondsRemoved).HasColumnName("seconds_removed").HasDefaultValue(0d);
            e.Property(x => x.NoiseReductionApplied).HasColumnName("noise_reduction_applied").HasDefaultValue(false);
            e.Property(x => x.EdgeTrimApplied).HasColumnName("edge_trim_applied").HasDefaultValue(false);
            e.Property(x => x.InteriorSilenceCapped).HasColumnName("interior_silence_capped").HasDefaultValue(false);
            e.Property(x => x.PipelineVersion).HasColumnName("pipeline_version").HasMaxLength(60).HasDefaultValue("");
            e.Property(x => x.ReportJson).HasColumnName("report_json").HasDefaultValue("{}");
            e.Property(x => x.Error).HasColumnName("error").HasMaxLength(2000).HasDefaultValue("");
            e.Property(x => x.QueuedAtUtc).HasColumnName("queued_at");
            e.Property(x => x.StartedAtUtc).HasColumnName("started_at");
            e.Property(x => x.CompletedAtUtc).HasColumnName("completed_at");
            e.HasOne(x => x.Submission).WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<SiteContent>(e =>
        {
            e.ToTable("eldf_site_content"); e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.Vision).HasColumnName("vision").HasMaxLength(2000);
            e.Property(x => x.VisionEnglish).HasColumnName("vision_en").HasMaxLength(2000);
            e.Property(x => x.Mission).HasColumnName("mission").HasMaxLength(4000); e.Property(x => x.MissionEnglish).HasColumnName("mission_en").HasMaxLength(4000);
            e.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at"); e.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");
        });
    }
}

public sealed record UserView(int id, string name, string phone, string role, string language);
// `name` is new and `dialect` is no longer collected by the form, but both stay on the record
// so older clients and the rows already stored keep working.
public sealed record SpeakerInput(string? speakerId, string? name, string? ageRange, string? gender, string? educationLevel, string? preferredLanguage, string? region, string? dialect);
