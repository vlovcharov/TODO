using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TodoApp.Migrations
{
    /// <inheritdoc />
    public partial class TaskCompletionsRefactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Step 1: Add new date columns alongside the old text columns ──────────
            migrationBuilder.AddColumn<DateOnly>(
                name: "ScheduledDate",
                table: "Tasks",
                type: "date",
                nullable: true);  // nullable temporarily so we can populate it first

            migrationBuilder.AddColumn<DateOnly>(
                name: "OriginalScheduledDate",
                table: "Tasks",
                type: "date",
                nullable: true);

            // ── Step 2: Copy text → date (Postgres casts "yyyy-MM-dd" text to date) ─
            migrationBuilder.Sql(@"
                UPDATE ""Tasks""
                SET ""ScheduledDate"" = ""ScheduledDateStr""::date
                WHERE ""ScheduledDateStr"" IS NOT NULL AND ""ScheduledDateStr"" <> '';
            ");

            migrationBuilder.Sql(@"
                UPDATE ""Tasks""
                SET ""OriginalScheduledDate"" = ""OriginalScheduledDateStr""::date
                WHERE ""OriginalScheduledDateStr"" IS NOT NULL AND ""OriginalScheduledDateStr"" <> '';
            ");

            // ── Step 3: Make ScheduledDate non-nullable now that it's populated ─────
            migrationBuilder.Sql(@"
                UPDATE ""Tasks"" SET ""ScheduledDate"" = CURRENT_DATE WHERE ""ScheduledDate"" IS NULL;
            ");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "ScheduledDate",
                table: "Tasks",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            // ── Step 4: Drop old text columns ────────────────────────────────────────
            migrationBuilder.DropColumn(name: "ScheduledDateStr",         table: "Tasks");
            migrationBuilder.DropColumn(name: "OriginalScheduledDateStr", table: "Tasks");

            // ── Step 5: Create TaskCompletions table ──────────────────────────────────
            migrationBuilder.CreateTable(
                name: "TaskCompletions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy",
                            Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TaskId      = table.Column<string>(type: "text",                       nullable: false),
                    Date        = table.Column<DateOnly>(type: "date",                     nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskCompletions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskCompletions_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskCompletions_TaskId_Date",
                table: "TaskCompletions",
                columns: new[] { "TaskId", "Date" },
                unique: true);

            // ── Step 6: Migrate RecurringCompletions → TaskCompletions ───────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""TaskCompletions"" (""TaskId"", ""Date"", ""CompletedAt"")
                SELECT rc.""TaskId"",
                       rc.""DateStr""::date,
                       NOW()
                FROM ""RecurringCompletions"" rc
                ON CONFLICT (""TaskId"", ""Date"") DO NOTHING;
            ");

            // ── Step 7: Migrate IsCompleted = true tasks → TaskCompletions ───────────
            // For non-recurring tasks that were marked done, create a completion
            // record on their scheduled date so history is preserved.
            migrationBuilder.Sql(@"
                INSERT INTO ""TaskCompletions"" (""TaskId"", ""Date"", ""CompletedAt"")
                SELECT t.""Id"",
                       t.""ScheduledDate"",
                       COALESCE(t.""UpdatedAt"", NOW())
                FROM ""Tasks"" t
                WHERE t.""IsCompleted"" = true
                  AND t.""RecurrenceMask"" = 0
                ON CONFLICT (""TaskId"", ""Date"") DO NOTHING;
            ");

            // ── Step 8: Drop old RecurringCompletions table ───────────────────────────
            migrationBuilder.DropTable(name: "RecurringCompletions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskCompletions");

            migrationBuilder.DropColumn(
                name: "OriginalScheduledDate",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ScheduledDate",
                table: "Tasks");

            migrationBuilder.AddColumn<string>(
                name: "OriginalScheduledDateStr",
                table: "Tasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScheduledDateStr",
                table: "Tasks",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "RecurringCompletions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TaskId = table.Column<string>(type: "text", nullable: false),
                    DateStr = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringCompletions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringCompletions_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringCompletions_TaskId_DateStr",
                table: "RecurringCompletions",
                columns: new[] { "TaskId", "DateStr" },
                unique: true);
        }
    }
}
