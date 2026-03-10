using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoApp.Migrations
{
    /// <inheritdoc />
    public partial class EpicsAndDayPlanner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShowLifeGoal",
                table: "AppMeta");

            migrationBuilder.AddColumn<string>(
                name: "EpicId",
                table: "Tasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsMissed",
                table: "Tasks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DayScheduleBlocks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    TaskId = table.Column<string>(type: "text", nullable: true),
                    Label = table.Column<string>(type: "text", nullable: false),
                    StartMinutes = table.Column<int>(type: "integer", nullable: false),
                    EndMinutes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DayScheduleBlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DayScheduleBlocks_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Epics",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Color = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Epics", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_EpicId",
                table: "Tasks",
                column: "EpicId");

            migrationBuilder.CreateIndex(
                name: "IX_DayScheduleBlocks_Date",
                table: "DayScheduleBlocks",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_DayScheduleBlocks_TaskId",
                table: "DayScheduleBlocks",
                column: "TaskId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Epics_EpicId",
                table: "Tasks",
                column: "EpicId",
                principalTable: "Epics",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ── Migrate LifeGoal tasks → Epics ────────────────────────────────────
            // Each distinct LifeGoal task title becomes its own Epic.
            // The task itself is re-levelled to Daily and linked to the new Epic.
            // A palette of distinct colors is cycled through.
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    colors TEXT[] := ARRAY[
                        '#ec4899','#8b5cf6','#06b6d4','#f59e0b',
                        '#10b981','#ef4444','#6366f1','#84cc16'
                    ];
                    color_index INT := 0;
                    r RECORD;
                    new_epic_id TEXT;
                BEGIN
                    FOR r IN
                        SELECT DISTINCT ""Title""
                        FROM ""Tasks""
                        WHERE ""Level"" = 'LifeGoal'
                        ORDER BY ""Title""
                    LOOP
                        new_epic_id := gen_random_uuid()::text;

                        INSERT INTO ""Epics"" (""Id"", ""Title"", ""Color"", ""CreatedAt"")
                        VALUES (
                            new_epic_id,
                            r.""Title"",
                            colors[(color_index % array_length(colors, 1)) + 1],
                            NOW()
                        );

                        UPDATE ""Tasks""
                        SET ""EpicId"" = new_epic_id,
                            ""Level""  = 'Daily'
                        WHERE ""Level"" = 'LifeGoal'
                          AND ""Title"" = r.""Title"";

                        color_index := color_index + 1;
                    END LOOP;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_Epics_EpicId",
                table: "Tasks");

            migrationBuilder.DropTable(
                name: "DayScheduleBlocks");

            migrationBuilder.DropTable(
                name: "Epics");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_EpicId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "EpicId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "IsMissed",
                table: "Tasks");

            migrationBuilder.AddColumn<bool>(
                name: "ShowLifeGoal",
                table: "AppMeta",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
