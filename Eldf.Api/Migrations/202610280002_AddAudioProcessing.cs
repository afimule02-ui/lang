using Eldf.Api;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Eldf.Api.Migrations;

/// <summary>
/// Adds <c>dbo.eldf_audio_processing</c>: one row per submission recording what the audio
/// quality pipeline measured and decided.
///
/// It is a separate table rather than extra columns on <c>eldf_submissions</c> so the review and
/// payment flow is untouched, and so a pipeline rerun only rewrites its own row. The original
/// upload's path stays on the submission and is never modified by the pipeline.
///
/// Additive and idempotent, and <c>Down</c> is intentionally empty so a rollback cannot discard
/// collected quality evidence.
/// </summary>
[DbContext(typeof(EldfDbContext))]
[Migration("202610280002_AddAudioProcessing")]
public sealed class AddAudioProcessing : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.eldf_audio_processing', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.eldf_audio_processing (
                    id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_eldf_audio_processing PRIMARY KEY,
                    submission_id int NOT NULL,
                    -- queued | running | done | failed
                    status nvarchar(20) NOT NULL CONSTRAINT DF_eldf_audio_processing_status DEFAULT (N'queued'),
                    -- PASS | AUTO_FIXED | MANUAL_REVIEW | RECORD_AGAIN
                    verdict nvarchar(20) NOT NULL CONSTRAINT DF_eldf_audio_processing_verdict DEFAULT (N''),
                    attempt_count int NOT NULL CONSTRAINT DF_eldf_audio_processing_attempts DEFAULT (0),
                    contributor_message nvarchar(1000) NOT NULL CONSTRAINT DF_eldf_audio_processing_msg DEFAULT (N''),
                    contributor_message_am nvarchar(1000) NOT NULL CONSTRAINT DF_eldf_audio_processing_msg_am DEFAULT (N''),
                    original_sha256 nvarchar(64) NOT NULL CONSTRAINT DF_eldf_audio_processing_osha DEFAULT (N''),
                    original_size_bytes bigint NOT NULL CONSTRAINT DF_eldf_audio_processing_osize DEFAULT (0),
                    original_duration_seconds float NOT NULL CONSTRAINT DF_eldf_audio_processing_odur DEFAULT (0),
                    original_sample_rate int NOT NULL CONSTRAINT DF_eldf_audio_processing_osr DEFAULT (0),
                    original_channels int NOT NULL CONSTRAINT DF_eldf_audio_processing_och DEFAULT (0),
                    processed_path nvarchar(500) NOT NULL CONSTRAINT DF_eldf_audio_processing_ppath DEFAULT (N''),
                    processed_sha256 nvarchar(64) NOT NULL CONSTRAINT DF_eldf_audio_processing_psha DEFAULT (N''),
                    processed_size_bytes bigint NOT NULL CONSTRAINT DF_eldf_audio_processing_psize DEFAULT (0),
                    processed_duration_seconds float NOT NULL CONSTRAINT DF_eldf_audio_processing_pdur DEFAULT (0),
                    delivery_zip_path nvarchar(500) NOT NULL CONSTRAINT DF_eldf_audio_processing_zip DEFAULT (N''),
                    seconds_removed float NOT NULL CONSTRAINT DF_eldf_audio_processing_removed DEFAULT (0),
                    noise_reduction_applied bit NOT NULL CONSTRAINT DF_eldf_audio_processing_nr DEFAULT (0),
                    edge_trim_applied bit NOT NULL CONSTRAINT DF_eldf_audio_processing_edge DEFAULT (0),
                    interior_silence_capped bit NOT NULL CONSTRAINT DF_eldf_audio_processing_interior DEFAULT (0),
                    pipeline_version nvarchar(60) NOT NULL CONSTRAINT DF_eldf_audio_processing_ver DEFAULT (N''),
                    report_json nvarchar(max) NOT NULL CONSTRAINT DF_eldf_audio_processing_report DEFAULT (N'{}'),
                    error nvarchar(2000) NOT NULL CONSTRAINT DF_eldf_audio_processing_error DEFAULT (N''),
                    queued_at datetime2 NOT NULL CONSTRAINT DF_eldf_audio_processing_queued DEFAULT (SYSUTCDATETIME()),
                    started_at datetime2 NULL,
                    completed_at datetime2 NULL,
                    CONSTRAINT FK_eldf_audio_processing_submission
                        FOREIGN KEY (submission_id) REFERENCES dbo.eldf_submissions(id)
                );
                -- One row per submission: a retry updates it instead of creating a duplicate.
                CREATE UNIQUE INDEX UX_eldf_audio_processing_submission
                    ON dbo.eldf_audio_processing(submission_id);
                CREATE INDEX IX_eldf_audio_processing_status
                    ON dbo.eldf_audio_processing(status, queued_at);
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
