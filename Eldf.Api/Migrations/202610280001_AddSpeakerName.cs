using Eldf.Api;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Eldf.Api.Migrations;

/// <summary>
/// Adds the speaker's name to <c>dbo.eldf_speakers</c>.
///
/// The speaker form now asks who is speaking, because the registering contributor is almost
/// always speaker 1 and the review and payment screens need to say which person was recorded.
/// The dialect field was removed from the form, but <c>dialect</c> is deliberately left in the
/// table: rows already collected keep their value and nothing is dropped.
///
/// Additive and idempotent, like the first migration, so it is safe to run against the
/// production database that is still on the older schema. <c>Down</c> is intentionally empty:
/// a rollback must never delete collected speaker data.
/// </summary>
[DbContext(typeof(EldfDbContext))]
[Migration("202610280001_AddSpeakerName")]
public sealed class AddSpeakerName : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.eldf_speakers', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.eldf_speakers', N'full_name') IS NULL
                ALTER TABLE dbo.eldf_speakers
                    ADD full_name nvarchar(160) NOT NULL
                    CONSTRAINT DF_eldf_speakers_full_name DEFAULT (N'') WITH VALUES;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
