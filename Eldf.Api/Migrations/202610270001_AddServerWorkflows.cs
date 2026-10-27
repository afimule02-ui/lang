using Eldf.Api;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Eldf.Api.Migrations;

[DbContext(typeof(EldfDbContext))]
[Migration("202610270001_AddServerWorkflows")]
public sealed class AddServerWorkflows : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.eldf_users', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.eldf_users', N'is_active') IS NULL
                    ALTER TABLE dbo.eldf_users ADD is_active bit NOT NULL CONSTRAINT DF_eldf_users_is_active DEFAULT (1) WITH VALUES;
                IF COL_LENGTH(N'dbo.eldf_users', N'referred_by_agent_id') IS NULL
                    ALTER TABLE dbo.eldf_users ADD referred_by_agent_id int NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_eldf_users_referred_by_agent')
                    ALTER TABLE dbo.eldf_users ADD CONSTRAINT FK_eldf_users_referred_by_agent FOREIGN KEY (referred_by_agent_id) REFERENCES dbo.eldf_users(id);
            END;

            IF OBJECT_ID(N'dbo.eldf_submissions', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.eldf_submissions', N'updated_at') IS NULL
                    ALTER TABLE dbo.eldf_submissions ADD updated_at datetime2 NOT NULL CONSTRAINT DF_eldf_submissions_updated_at DEFAULT (SYSUTCDATETIME()) WITH VALUES;
                IF COL_LENGTH(N'dbo.eldf_submissions', N'assigned_agent_id') IS NULL ALTER TABLE dbo.eldf_submissions ADD assigned_agent_id int NULL;
                IF COL_LENGTH(N'dbo.eldf_submissions', N'agent_review_comment') IS NULL ALTER TABLE dbo.eldf_submissions ADD agent_review_comment nvarchar(max) NOT NULL CONSTRAINT DF_eldf_submissions_agent_comment DEFAULT (N'') WITH VALUES;
                IF COL_LENGTH(N'dbo.eldf_submissions', N'admin_review_comment') IS NULL ALTER TABLE dbo.eldf_submissions ADD admin_review_comment nvarchar(max) NOT NULL CONSTRAINT DF_eldf_submissions_admin_comment DEFAULT (N'') WITH VALUES;
                IF COL_LENGTH(N'dbo.eldf_submissions', N'admin_rejection_reason') IS NULL ALTER TABLE dbo.eldf_submissions ADD admin_rejection_reason nvarchar(max) NOT NULL CONSTRAINT DF_eldf_submissions_admin_reason DEFAULT (N'') WITH VALUES;
                IF COL_LENGTH(N'dbo.eldf_submissions', N'agent_reviewed_by_id') IS NULL ALTER TABLE dbo.eldf_submissions ADD agent_reviewed_by_id int NULL;
                IF COL_LENGTH(N'dbo.eldf_submissions', N'admin_reviewed_by_id') IS NULL ALTER TABLE dbo.eldf_submissions ADD admin_reviewed_by_id int NULL;
                IF COL_LENGTH(N'dbo.eldf_submissions', N'agent_reviewed_at') IS NULL ALTER TABLE dbo.eldf_submissions ADD agent_reviewed_at datetime2 NULL;
                IF COL_LENGTH(N'dbo.eldf_submissions', N'admin_reviewed_at') IS NULL ALTER TABLE dbo.eldf_submissions ADD admin_reviewed_at datetime2 NULL;
                IF COL_LENGTH(N'dbo.eldf_submissions', N'award_amount') IS NULL ALTER TABLE dbo.eldf_submissions ADD award_amount decimal(18,2) NULL;
                IF COL_LENGTH(N'dbo.eldf_submissions', N'reward_currency') IS NULL ALTER TABLE dbo.eldf_submissions ADD reward_currency nvarchar(3) NOT NULL CONSTRAINT DF_eldf_submissions_reward_currency DEFAULT (N'ETB') WITH VALUES;
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_eldf_submissions_assigned_agent')
                    ALTER TABLE dbo.eldf_submissions ADD CONSTRAINT FK_eldf_submissions_assigned_agent FOREIGN KEY (assigned_agent_id) REFERENCES dbo.eldf_users(id);
            END;

            IF OBJECT_ID(N'dbo.eldf_agent_referrals', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.eldf_agent_referrals (
                    id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_eldf_agent_referrals PRIMARY KEY,
                    agent_user_id int NOT NULL,
                    code nvarchar(64) NOT NULL,
                    created_at datetime2 NOT NULL CONSTRAINT DF_eldf_agent_referrals_created_at DEFAULT (SYSUTCDATETIME()),
                    CONSTRAINT FK_eldf_agent_referrals_agent FOREIGN KEY (agent_user_id) REFERENCES dbo.eldf_users(id)
                );
                CREATE UNIQUE INDEX UX_eldf_agent_referrals_agent ON dbo.eldf_agent_referrals(agent_user_id);
                CREATE UNIQUE INDEX UX_eldf_agent_referrals_code ON dbo.eldf_agent_referrals(code);
            END;

            IF OBJECT_ID(N'dbo.eldf_work_drafts', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.eldf_work_drafts (
                    id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_eldf_work_drafts PRIMARY KEY,
                    user_id int NOT NULL,
                    project_language nvarchar(160) NOT NULL,
                    translation_language nvarchar(160) NOT NULL CONSTRAINT DF_eldf_work_drafts_target DEFAULT (N''),
                    audio_path nvarchar(500) NOT NULL CONSTRAINT DF_eldf_work_drafts_audio DEFAULT (N''),
                    duration_seconds int NOT NULL CONSTRAINT DF_eldf_work_drafts_duration DEFAULT (0),
                    transcript nvarchar(max) NOT NULL CONSTRAINT DF_eldf_work_drafts_transcript DEFAULT (N''),
                    translation nvarchar(max) NOT NULL CONSTRAINT DF_eldf_work_drafts_translation DEFAULT (N''),
                    speakers_json nvarchar(max) NOT NULL CONSTRAINT DF_eldf_work_drafts_speakers DEFAULT (N'[]'),
                    recording_type nvarchar(80) NOT NULL CONSTRAINT DF_eldf_work_drafts_type DEFAULT (N'Conversation'),
                    consent_reference nvarchar(80) NOT NULL CONSTRAINT DF_eldf_work_drafts_consent DEFAULT (N''),
                    updated_at datetime2 NOT NULL CONSTRAINT DF_eldf_work_drafts_updated DEFAULT (SYSUTCDATETIME()),
                    CONSTRAINT FK_eldf_work_drafts_user FOREIGN KEY (user_id) REFERENCES dbo.eldf_users(id)
                );
                CREATE INDEX IX_eldf_work_drafts_user_updated ON dbo.eldf_work_drafts(user_id, updated_at);
            END;

            IF OBJECT_ID(N'dbo.eldf_review_events', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.eldf_review_events (
                    id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_eldf_review_events PRIMARY KEY,
                    submission_id int NOT NULL,
                    reviewer_user_id int NOT NULL,
                    reviewer_role nvarchar(20) NOT NULL,
                    decision nvarchar(40) NOT NULL,
                    comment nvarchar(max) NOT NULL CONSTRAINT DF_eldf_review_events_comment DEFAULT (N''),
                    created_at datetime2 NOT NULL CONSTRAINT DF_eldf_review_events_created DEFAULT (SYSUTCDATETIME()),
                    CONSTRAINT FK_eldf_review_events_submission FOREIGN KEY (submission_id) REFERENCES dbo.eldf_submissions(id)
                );
                CREATE INDEX IX_eldf_review_events_submission_created ON dbo.eldf_review_events(submission_id, created_at);
            END;

            IF OBJECT_ID(N'dbo.eldf_support_tickets', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.eldf_support_tickets (
                    id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_eldf_support_tickets PRIMARY KEY,
                    created_by_user_id int NOT NULL,
                    assigned_agent_user_id int NULL,
                    subject nvarchar(200) NOT NULL,
                    status nvarchar(32) NOT NULL CONSTRAINT DF_eldf_support_tickets_status DEFAULT (N'Open'),
                    created_at datetime2 NOT NULL CONSTRAINT DF_eldf_support_tickets_created DEFAULT (SYSUTCDATETIME()),
                    updated_at datetime2 NOT NULL CONSTRAINT DF_eldf_support_tickets_updated DEFAULT (SYSUTCDATETIME()),
                    CONSTRAINT FK_eldf_support_tickets_creator FOREIGN KEY (created_by_user_id) REFERENCES dbo.eldf_users(id),
                    CONSTRAINT FK_eldf_support_tickets_agent FOREIGN KEY (assigned_agent_user_id) REFERENCES dbo.eldf_users(id)
                );
                CREATE INDEX IX_eldf_support_tickets_agent_status ON dbo.eldf_support_tickets(assigned_agent_user_id, status);
            END;

            IF OBJECT_ID(N'dbo.eldf_support_messages', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.eldf_support_messages (
                    id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_eldf_support_messages PRIMARY KEY,
                    ticket_id int NOT NULL,
                    user_id int NOT NULL,
                    body nvarchar(max) NOT NULL,
                    created_at datetime2 NOT NULL CONSTRAINT DF_eldf_support_messages_created DEFAULT (SYSUTCDATETIME()),
                    CONSTRAINT FK_eldf_support_messages_ticket FOREIGN KEY (ticket_id) REFERENCES dbo.eldf_support_tickets(id),
                    CONSTRAINT FK_eldf_support_messages_user FOREIGN KEY (user_id) REFERENCES dbo.eldf_users(id)
                );
                CREATE INDEX IX_eldf_support_messages_ticket_created ON dbo.eldf_support_messages(ticket_id, created_at);
            END;

            IF OBJECT_ID(N'dbo.eldf_payment_requests', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.eldf_payment_requests (
                    id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_eldf_payment_requests PRIMARY KEY,
                    user_id int NOT NULL,
                    amount decimal(18,2) NOT NULL,
                    currency nvarchar(3) NOT NULL CONSTRAINT DF_eldf_payment_requests_currency DEFAULT (N'ETB'),
                    status nvarchar(32) NOT NULL CONSTRAINT DF_eldf_payment_requests_status DEFAULT (N'Pending admin review'),
                    review_note nvarchar(max) NOT NULL CONSTRAINT DF_eldf_payment_requests_note DEFAULT (N''),
                    payment_proof_reference nvarchar(500) NOT NULL CONSTRAINT DF_eldf_payment_requests_proof DEFAULT (N''),
                    reviewed_by_user_id int NULL,
                    requested_at datetime2 NOT NULL CONSTRAINT DF_eldf_payment_requests_requested DEFAULT (SYSUTCDATETIME()),
                    reviewed_at datetime2 NULL,
                    paid_at datetime2 NULL,
                    CONSTRAINT FK_eldf_payment_requests_user FOREIGN KEY (user_id) REFERENCES dbo.eldf_users(id)
                );
                CREATE INDEX IX_eldf_payment_requests_user_requested ON dbo.eldf_payment_requests(user_id, requested_at);
            END;

            IF OBJECT_ID(N'dbo.eldf_activity_events', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.eldf_activity_events (
                    id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_eldf_activity_events PRIMARY KEY,
                    actor_user_id int NULL,
                    actor_role nvarchar(20) NOT NULL,
                    event_type nvarchar(100) NOT NULL,
                    entity_type nvarchar(80) NOT NULL,
                    entity_id nvarchar(80) NOT NULL,
                    details nvarchar(2000) NOT NULL CONSTRAINT DF_eldf_activity_events_details DEFAULT (N''),
                    created_at datetime2 NOT NULL CONSTRAINT DF_eldf_activity_events_created DEFAULT (SYSUTCDATETIME())
                );
                CREATE INDEX IX_eldf_activity_events_created ON dbo.eldf_activity_events(created_at);
            END;

            IF OBJECT_ID(N'dbo.eldf_site_content', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.eldf_site_content (
                    id int NOT NULL CONSTRAINT PK_eldf_site_content PRIMARY KEY,
                    vision nvarchar(2000) NOT NULL,
                    vision_en nvarchar(2000) NOT NULL CONSTRAINT DF_eldf_site_content_vision_en DEFAULT (N'Every African voice deserves a place in the digital world.'),
                    mission nvarchar(4000) NOT NULL,
                    mission_en nvarchar(4000) NOT NULL CONSTRAINT DF_eldf_site_content_mission_en DEFAULT (N'We collect consent-led audio, transcription, translation, and quality-reviewed data across Africa''s languages.'),
                    updated_at datetime2 NOT NULL CONSTRAINT DF_eldf_site_content_updated DEFAULT (SYSUTCDATETIME()),
                    updated_by_user_id int NULL
                );
            END;
            IF COL_LENGTH(N'dbo.eldf_site_content', N'vision_en') IS NULL
                ALTER TABLE dbo.eldf_site_content ADD vision_en nvarchar(2000) NOT NULL CONSTRAINT DF_eldf_site_content_vision_en_upgrade DEFAULT (N'Every African voice deserves a place in the digital world.') WITH VALUES;
            IF COL_LENGTH(N'dbo.eldf_site_content', N'mission_en') IS NULL
                ALTER TABLE dbo.eldf_site_content ADD mission_en nvarchar(4000) NOT NULL CONSTRAINT DF_eldf_site_content_mission_en_upgrade DEFAULT (N'We collect consent-led audio, transcription, translation, and quality-reviewed data across Africa''s languages.') WITH VALUES;
            IF NOT EXISTS (SELECT 1 FROM dbo.eldf_site_content WHERE id = 1)
                INSERT INTO dbo.eldf_site_content(id, vision, vision_en, mission, mission_en) VALUES (1, N'የአፍሪካ እያንዳንዱ ድምፅ በዲጂታል ዓለም ውስጥ ቦታ እንዲኖረው።', N'Every African voice deserves a place in the digital world.', N'የአፍሪካን ቋንቋዎች በፈቃድ የተመሠረተ ድምፅ፣ ጽሑፍ፣ ትርጉምና ጥራት ማረጋገጫ ሥራ እንሰበስባለን።', N'We collect consent-led audio, transcription, translation, and quality-reviewed data across Africa''s languages.');
            """);
    }

    // The migration only adds structures; rollback intentionally keeps collected data intact.
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
