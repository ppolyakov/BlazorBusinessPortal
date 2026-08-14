using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BusinessPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClientContactAndDirectedWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SubmittedToUserId",
                table: "TimeEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                table: "Clients",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Number",
                table: "Clients",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                WITH numbered AS (
                    SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "OrganizationId" ORDER BY "CreatedAtUtc", "Id")::integer AS number
                    FROM "Clients"
                )
                UPDATE "Clients" AS target SET "Number" = numbered.number
                FROM numbered WHERE target."Id" = numbered."Id";

                INSERT INTO "PublicNumberCounters" ("OrganizationId", "EntityType", "LastNumber")
                SELECT "OrganizationId", 'Client', MAX("Number") FROM "Clients" GROUP BY "OrganizationId"
                ON CONFLICT ("OrganizationId", "EntityType")
                DO UPDATE SET "LastNumber" = EXCLUDED."LastNumber";

                CREATE TRIGGER "TR_Clients_PublicNumber" BEFORE INSERT ON "Clients"
                    FOR EACH ROW EXECUTE FUNCTION assign_public_number('Client');

                UPDATE "Clients"
                SET "ContactPhone" = CASE "Name"
                    WHEN 'Arcadia Retail' THEN '+1 312 555 0142'
                    WHEN 'Cedar Health' THEN '+1 617 555 0198'
                    WHEN 'Fjord Logistics' THEN '+45 32 55 71 20'
                    WHEN 'Helio Foods' THEN '+44 20 7946 0183'
                    ELSE "ContactPhone"
                END
                WHERE "ContactPhone" IS NULL;

                UPDATE "TimeEntries" SET "Status" = 'Returned' WHERE "Status" = 'Rejected';
                UPDATE "TimeEntryActivities" SET "Type" = 'Returned' WHERE "Type" = 'Rejected';
                UPDATE "TimeEntryActivities" SET "FromStatus" = 'Returned' WHERE "FromStatus" = 'Rejected';
                UPDATE "TimeEntryActivities" SET "ToStatus" = 'Returned' WHERE "ToStatus" = 'Rejected';
                UPDATE "Notifications" SET "Type" = 'TimeEntryReturned' WHERE "Type" = 'TimeEntryRejected';
                UPDATE "AuditEntries" SET "Action" = 'TimeEntryReturned' WHERE "Action" = 'TimeEntryRejected';

                UPDATE "TimeEntries" AS entry
                SET "SubmittedToUserId" = COALESCE(
                    entry."ReviewedByUserId",
                    (
                        SELECT account."Id"
                        FROM "AspNetUsers" account
                        JOIN "AspNetUserRoles" user_role ON user_role."UserId" = account."Id"
                        JOIN "AspNetRoles" role ON role."Id" = user_role."RoleId"
                        WHERE account."OrganizationId" = entry."OrganizationId"
                          AND account."Id" <> entry."UserId"
                          AND role."Name" IN ('Administrator', 'Manager')
                        ORDER BY CASE role."Name" WHEN 'Manager' THEN 0 ELSE 1 END, account."DisplayName"
                        LIMIT 1
                    )
                )
                WHERE entry."Status" IN ('Submitted', 'Approved', 'Returned');

                UPDATE "TimeEntryActivities" activity
                SET "TargetUserId" = entry."SubmittedToUserId", "TargetLabel" = NULL
                FROM "TimeEntries" entry
                WHERE activity."TimeEntryId" = entry."Id"
                  AND activity."TargetUserId" IS NULL
                  AND activity."TargetLabel" = 'Management team';
                """);

            migrationBuilder.CreateTable(
                name: "WorkItemActivities",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<string>(type: "text", nullable: false),
                    TargetUserId = table.Column<string>(type: "text", nullable: true),
                    Type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkItemActivities_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItemActivities_AspNetUsers_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkItemActivities_WorkItems_WorkItemId",
                        column: x => x.WorkItemId,
                        principalTable: "WorkItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_SubmittedToUserId",
                table: "TimeEntries",
                column: "SubmittedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_OrganizationId_Number",
                table: "Clients",
                columns: new[] { "OrganizationId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemActivities_ActorUserId",
                table: "WorkItemActivities",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemActivities_OrganizationId_WorkItemId_OccurredAtUtc",
                table: "WorkItemActivities",
                columns: new[] { "OrganizationId", "WorkItemId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemActivities_TargetUserId",
                table: "WorkItemActivities",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemActivities_WorkItemId",
                table: "WorkItemActivities",
                column: "WorkItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_TimeEntries_AspNetUsers_SubmittedToUserId",
                table: "TimeEntries",
                column: "SubmittedToUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql(
                """
                INSERT INTO "WorkItemActivities" ("OrganizationId", "WorkItemId", "ActorUserId", "TargetUserId", "Type", "FromStatus", "ToStatus", "Comment", "OccurredAtUtc")
                SELECT item."OrganizationId", item."Id",
                       COALESCE(item."AssignedToUserId", manager."Id"), item."AssignedToUserId",
                       'Created', NULL, item."Status", 'Legacy work item imported into workflow history.', item."CreatedAtUtc"
                FROM "WorkItems" item
                LEFT JOIN LATERAL (
                    SELECT account."Id"
                    FROM "AspNetUsers" account
                    WHERE account."OrganizationId" = item."OrganizationId"
                    ORDER BY account."IsActive" DESC, account."DisplayName"
                    LIMIT 1
                ) manager ON TRUE
                WHERE COALESCE(item."AssignedToUserId", manager."Id") IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS "TR_Clients_PublicNumber" ON "Clients";
                DELETE FROM "PublicNumberCounters" WHERE "EntityType" = 'Client';
                UPDATE "TimeEntries" SET "Status" = 'Rejected' WHERE "Status" = 'Returned';
                UPDATE "TimeEntryActivities" SET "Type" = 'Rejected' WHERE "Type" = 'Returned';
                UPDATE "TimeEntryActivities" SET "FromStatus" = 'Rejected' WHERE "FromStatus" = 'Returned';
                UPDATE "TimeEntryActivities" SET "ToStatus" = 'Rejected' WHERE "ToStatus" = 'Returned';
                UPDATE "Notifications" SET "Type" = 'TimeEntryRejected' WHERE "Type" = 'TimeEntryReturned';
                UPDATE "AuditEntries" SET "Action" = 'TimeEntryRejected' WHERE "Action" = 'TimeEntryReturned';
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_TimeEntries_AspNetUsers_SubmittedToUserId",
                table: "TimeEntries");

            migrationBuilder.DropTable(
                name: "WorkItemActivities");

            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_SubmittedToUserId",
                table: "TimeEntries");

            migrationBuilder.DropIndex(
                name: "IX_Clients_OrganizationId_Number",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "SubmittedToUserId",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "ContactPhone",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "Clients");
        }
    }
}
