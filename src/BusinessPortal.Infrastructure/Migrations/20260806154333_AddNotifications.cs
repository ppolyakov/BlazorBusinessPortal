using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BusinessPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientUserId = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TargetUrl = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    EntityId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_AspNetUsers_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_OrganizationId_EntityType_EntityId",
                table: "Notifications",
                columns: new[] { "OrganizationId", "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_OrganizationId_RecipientUserId_ReadAtUtc_Crea~",
                table: "Notifications",
                columns: new[] { "OrganizationId", "RecipientUserId", "ReadAtUtc", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId",
                table: "Notifications",
                column: "RecipientUserId");

            migrationBuilder.Sql(
                """
                INSERT INTO "Notifications"
                    ("OrganizationId", "RecipientUserId", "Type", "Title", "Message", "TargetUrl", "EntityType", "EntityId", "CreatedAtUtc", "ReadAtUtc")
                SELECT
                    entry."OrganizationId",
                    recipient."Id",
                    'TimeEntrySubmitted',
                    'Time entry awaiting approval',
                    owner."DisplayName" || ' submitted a time entry for approval.',
                    '/approvals',
                    'TimeEntry',
                    entry."Id"::text,
                    COALESCE(entry."SubmittedAtUtc", entry."UpdatedAtUtc", NOW()),
                    NULL
                FROM "TimeEntries" AS entry
                INNER JOIN "AspNetUsers" AS owner ON owner."Id" = entry."UserId"
                INNER JOIN "AspNetUsers" AS recipient
                    ON recipient."OrganizationId" = entry."OrganizationId"
                    AND recipient."IsActive" = TRUE
                    AND recipient."Id" <> entry."UserId"
                WHERE entry."Status" = 'Submitted'
                    AND EXISTS (
                        SELECT 1
                        FROM "AspNetUserRoles" AS user_role
                        INNER JOIN "AspNetRoles" AS role ON role."Id" = user_role."RoleId"
                        WHERE user_role."UserId" = recipient."Id"
                            AND role."Name" IN ('Administrator', 'Manager'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notifications");
        }
    }
}
