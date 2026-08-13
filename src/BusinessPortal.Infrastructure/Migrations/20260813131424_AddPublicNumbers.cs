using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Number",
                table: "WorkItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Number",
                table: "TimeEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Number",
                table: "Projects",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                WITH numbered AS (
                    SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "OrganizationId" ORDER BY "CreatedAtUtc", "Id")::integer AS number
                    FROM "Projects"
                )
                UPDATE "Projects" AS target SET "Number" = numbered.number
                FROM numbered WHERE target."Id" = numbered."Id";

                WITH numbered AS (
                    SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "OrganizationId" ORDER BY "CreatedAtUtc", "Id")::integer AS number
                    FROM "WorkItems"
                )
                UPDATE "WorkItems" AS target SET "Number" = numbered.number
                FROM numbered WHERE target."Id" = numbered."Id";

                WITH numbered AS (
                    SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "OrganizationId" ORDER BY "CreatedAtUtc", "Id")::integer AS number
                    FROM "TimeEntries"
                )
                UPDATE "TimeEntries" AS target SET "Number" = numbered.number
                FROM numbered WHERE target."Id" = numbered."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_OrganizationId_Number",
                table: "WorkItems",
                columns: new[] { "OrganizationId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_OrganizationId_Number",
                table: "TimeEntries",
                columns: new[] { "OrganizationId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_OrganizationId_Number",
                table: "Projects",
                columns: new[] { "OrganizationId", "Number" },
                unique: true);

            migrationBuilder.Sql(
                """
                CREATE TABLE "PublicNumberCounters" (
                    "OrganizationId" uuid NOT NULL,
                    "EntityType" character varying(32) NOT NULL,
                    "LastNumber" integer NOT NULL,
                    CONSTRAINT "PK_PublicNumberCounters" PRIMARY KEY ("OrganizationId", "EntityType")
                );

                INSERT INTO "PublicNumberCounters" ("OrganizationId", "EntityType", "LastNumber")
                SELECT "OrganizationId", 'Project', MAX("Number") FROM "Projects" GROUP BY "OrganizationId";
                INSERT INTO "PublicNumberCounters" ("OrganizationId", "EntityType", "LastNumber")
                SELECT "OrganizationId", 'WorkItem', MAX("Number") FROM "WorkItems" GROUP BY "OrganizationId";
                INSERT INTO "PublicNumberCounters" ("OrganizationId", "EntityType", "LastNumber")
                SELECT "OrganizationId", 'TimeEntry', MAX("Number") FROM "TimeEntries" GROUP BY "OrganizationId";

                CREATE FUNCTION assign_public_number() RETURNS trigger AS $$
                BEGIN
                    IF NEW."Number" <= 0 THEN
                        INSERT INTO "PublicNumberCounters" ("OrganizationId", "EntityType", "LastNumber")
                        VALUES (NEW."OrganizationId", TG_ARGV[0], 1)
                        ON CONFLICT ("OrganizationId", "EntityType")
                        DO UPDATE SET "LastNumber" = "PublicNumberCounters"."LastNumber" + 1
                        RETURNING "LastNumber" INTO NEW."Number";
                    ELSE
                        INSERT INTO "PublicNumberCounters" ("OrganizationId", "EntityType", "LastNumber")
                        VALUES (NEW."OrganizationId", TG_ARGV[0], NEW."Number")
                        ON CONFLICT ("OrganizationId", "EntityType")
                        DO UPDATE SET "LastNumber" = GREATEST("PublicNumberCounters"."LastNumber", EXCLUDED."LastNumber");
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER "TR_Projects_PublicNumber" BEFORE INSERT ON "Projects"
                    FOR EACH ROW EXECUTE FUNCTION assign_public_number('Project');
                CREATE TRIGGER "TR_WorkItems_PublicNumber" BEFORE INSERT ON "WorkItems"
                    FOR EACH ROW EXECUTE FUNCTION assign_public_number('WorkItem');
                CREATE TRIGGER "TR_TimeEntries_PublicNumber" BEFORE INSERT ON "TimeEntries"
                    FOR EACH ROW EXECUTE FUNCTION assign_public_number('TimeEntry');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS "TR_Projects_PublicNumber" ON "Projects";
                DROP TRIGGER IF EXISTS "TR_WorkItems_PublicNumber" ON "WorkItems";
                DROP TRIGGER IF EXISTS "TR_TimeEntries_PublicNumber" ON "TimeEntries";
                DROP FUNCTION IF EXISTS assign_public_number();
                DROP TABLE IF EXISTS "PublicNumberCounters";
                """);

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_OrganizationId_Number",
                table: "WorkItems");

            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_OrganizationId_Number",
                table: "TimeEntries");

            migrationBuilder.DropIndex(
                name: "IX_Projects_OrganizationId_Number",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "Projects");
        }
    }
}
