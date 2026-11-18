using Eldf.Api;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Eldf.Api.Migrations;

[DbContext(typeof(EldfDbContext))]
[Migration("202611180001_AddWorkDraftAssignment")]
public sealed class AddWorkDraftAssignment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.eldf_work_drafts', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.eldf_work_drafts', N'assigned_user_id') IS NULL
                    ALTER TABLE dbo.eldf_work_drafts ADD assigned_user_id int NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_eldf_work_drafts_assigned_user')
                    ALTER TABLE dbo.eldf_work_drafts ADD CONSTRAINT FK_eldf_work_drafts_assigned_user
                        FOREIGN KEY (assigned_user_id) REFERENCES dbo.eldf_users(id);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_eldf_work_drafts_assigned_user_id_updated_at' AND object_id = OBJECT_ID(N'dbo.eldf_work_drafts'))
                    CREATE INDEX IX_eldf_work_drafts_assigned_user_id_updated_at ON dbo.eldf_work_drafts(assigned_user_id, updated_at);
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.eldf_work_drafts', N'U') IS NOT NULL
            BEGIN
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_eldf_work_drafts_assigned_user_id_updated_at' AND object_id = OBJECT_ID(N'dbo.eldf_work_drafts'))
                    DROP INDEX IX_eldf_work_drafts_assigned_user_id_updated_at ON dbo.eldf_work_drafts;
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_eldf_work_drafts_assigned_user')
                    ALTER TABLE dbo.eldf_work_drafts DROP CONSTRAINT FK_eldf_work_drafts_assigned_user;
                IF COL_LENGTH(N'dbo.eldf_work_drafts', N'assigned_user_id') IS NOT NULL
                    ALTER TABLE dbo.eldf_work_drafts DROP COLUMN assigned_user_id;
            END;
            """);
    }
}
