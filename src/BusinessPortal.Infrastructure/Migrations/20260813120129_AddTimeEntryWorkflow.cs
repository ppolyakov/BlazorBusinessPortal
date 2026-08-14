using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BusinessPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTimeEntryWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TimeEntryActivities",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TimeEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<string>(type: "text", nullable: false),
                    TargetUserId = table.Column<string>(type: "text", nullable: true),
                    TargetLabel = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeEntryActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeEntryActivities_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TimeEntryActivities_AspNetUsers_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TimeEntryActivities_TimeEntries_TimeEntryId",
                        column: x => x.TimeEntryId,
                        principalTable: "TimeEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntryActivities_ActorUserId",
                table: "TimeEntryActivities",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntryActivities_OrganizationId_TimeEntryId_OccurredAtUtc",
                table: "TimeEntryActivities",
                columns: new[] { "OrganizationId", "TimeEntryId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntryActivities_TargetUserId",
                table: "TimeEntryActivities",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntryActivities_TimeEntryId",
                table: "TimeEntryActivities",
                column: "TimeEntryId");

            migrationBuilder.Sql(
                """
                INSERT INTO "TimeEntryActivities"
                    ("OrganizationId", "TimeEntryId", "ActorUserId", "Type", "FromStatus", "ToStatus", "Comment", "OccurredAtUtc")
                SELECT
                    entry."OrganizationId", entry."Id", entry."UserId", 'Created', NULL, 'Draft',
                    'Time entry created.', entry."CreatedAtUtc"
                FROM "TimeEntries" AS entry;

                INSERT INTO "TimeEntryActivities"
                    ("OrganizationId", "TimeEntryId", "ActorUserId", "TargetLabel", "Type", "FromStatus", "ToStatus", "Comment", "OccurredAtUtc")
                SELECT
                    entry."OrganizationId", entry."Id", entry."UserId", 'Management team', 'Submitted', 'Draft', 'Submitted',
                    'Submitted for manager review.', COALESCE(entry."SubmittedAtUtc", entry."UpdatedAtUtc")
                FROM "TimeEntries" AS entry
                WHERE entry."Status" <> 'Draft';

                INSERT INTO "TimeEntryActivities"
                    ("OrganizationId", "TimeEntryId", "ActorUserId", "TargetUserId", "Type", "FromStatus", "ToStatus", "Comment", "OccurredAtUtc")
                SELECT
                    entry."OrganizationId", entry."Id", COALESCE(entry."ReviewedByUserId", entry."UserId"), entry."UserId",
                    CASE WHEN entry."Status" = 'Approved' THEN 'Approved' ELSE 'Rejected' END,
                    'Submitted', entry."Status",
                    CASE WHEN entry."Status" = 'Rejected'
                        THEN COALESCE(entry."ReviewComment", 'Returned for changes.')
                        ELSE 'Time entry approved and moved to history.' END,
                    COALESCE(entry."ReviewedAtUtc", entry."UpdatedAtUtc")
                FROM "TimeEntries" AS entry
                WHERE entry."Status" IN ('Approved', 'Rejected');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TimeEntryActivities");
        }
    }
}
