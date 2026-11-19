using Eldf.Api;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Eldf.Api.Migrations;

[DbContext(typeof(EldfDbContext))]
[Migration("202611190001_AddTopicTitles")]
public sealed class AddTopicTitles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.eldf_work_drafts', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.eldf_work_drafts', N'topic_title') IS NULL
                ALTER TABLE dbo.eldf_work_drafts ADD topic_title nvarchar(160) NOT NULL CONSTRAINT DF_eldf_work_drafts_topic_title DEFAULT N'';
            IF OBJECT_ID(N'dbo.eldf_submissions', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.eldf_submissions', N'topic_title') IS NULL
                ALTER TABLE dbo.eldf_submissions ADD topic_title nvarchar(160) NOT NULL CONSTRAINT DF_eldf_submissions_topic_title DEFAULT N'';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Topic titles contain user data; roll back application code without dropping these columns.");
    }
}
