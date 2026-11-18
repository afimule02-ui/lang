using Eldf.Api;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Eldf.Api.Migrations;

/// <summary>Additive audit table for per-speaker consent. Down intentionally preserves records.</summary>
[DbContext(typeof(EldfDbContext))]
[Migration("202611030001_AddSpeakerConsents")]
public sealed class AddSpeakerConsents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        IF OBJECT_ID(N'dbo.eldf_speaker_consents', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.eldf_speaker_consents (
                id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_eldf_speaker_consents PRIMARY KEY,
                user_id int NOT NULL,
                speaker_profile_id int NOT NULL,
                consent_version nvarchar(80) NOT NULL,
                language nvarchar(8) NOT NULL CONSTRAINT DF_eldf_speaker_consents_language DEFAULT (N'am'),
                core_consent bit NOT NULL,
                ai_development_consent bit NOT NULL CONSTRAINT DF_eldf_speaker_consents_ai DEFAULT (0),
                research_sharing_consent bit NOT NULL CONSTRAINT DF_eldf_speaker_consents_research DEFAULT (0),
                public_release_consent bit NOT NULL CONSTRAINT DF_eldf_speaker_consents_public DEFAULT (0),
                consented_at datetime2 NOT NULL CONSTRAINT DF_eldf_speaker_consents_at DEFAULT (SYSUTCDATETIME()),
                withdrawn_at datetime2 NULL,
                CONSTRAINT FK_eldf_speaker_consents_user FOREIGN KEY (user_id) REFERENCES dbo.eldf_users(id),
                CONSTRAINT FK_eldf_speaker_consents_speaker FOREIGN KEY (speaker_profile_id) REFERENCES dbo.eldf_speakers(id)
            );
            CREATE INDEX IX_eldf_speaker_consents_profile_date ON dbo.eldf_speaker_consents(speaker_profile_id, consented_at);
        END;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
