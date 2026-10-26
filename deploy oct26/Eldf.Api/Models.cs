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
    public string AgeRange { get; set; } = "";
    public string Gender { get; set; } = "";
    public string Education { get; set; } = "";
    public string Language { get; set; } = "";
    public string Region { get; set; } = "";
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
}

public sealed class EldfDbContext(DbContextOptions<EldfDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<LoginSession> Sessions => Set<LoginSession>();
    public DbSet<SpeakerProfile> Speakers => Set<SpeakerProfile>();
    public DbSet<WorkSubmission> Submissions => Set<WorkSubmission>();

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
            e.Property(x => x.SpeakerId).HasColumnName("speaker_id").HasMaxLength(40); e.Property(x => x.AgeRange).HasColumnName("age_range").HasMaxLength(40);
            e.Property(x => x.Gender).HasColumnName("gender").HasMaxLength(40); e.Property(x => x.Education).HasColumnName("education").HasMaxLength(100);
            e.Property(x => x.Language).HasColumnName("language").HasMaxLength(160); e.Property(x => x.Region).HasColumnName("region").HasMaxLength(160); e.Property(x => x.Dialect).HasColumnName("dialect").HasMaxLength(160);
        });
        model.Entity<WorkSubmission>(e =>
        {
            e.ToTable("eldf_submissions"); e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.UserAccountId).HasColumnName("user_id");
            e.Property(x => x.ProjectLanguage).HasColumnName("project_language").HasMaxLength(160); e.Property(x => x.DurationSeconds).HasColumnName("duration_seconds");
            e.Property(x => x.AudioPath).HasColumnName("audio_path").HasMaxLength(500); e.Property(x => x.Transcript).HasColumnName("transcript");
            e.Property(x => x.Translation).HasColumnName("translation"); e.Property(x => x.Status).HasColumnName("status").HasMaxLength(40); e.Property(x => x.CreatedAtUtc).HasColumnName("created_at");
        });
    }
}

public sealed record UserView(int id, string name, string phone, string role, string language);
public sealed record SpeakerInput(string? speakerId, string? ageRange, string? gender, string? educationLevel, string? preferredLanguage, string? region, string? dialect);
