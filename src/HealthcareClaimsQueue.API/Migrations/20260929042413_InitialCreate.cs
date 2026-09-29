using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthcareClaimsQueue.API.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_user",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    username = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_user", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "claim",
                columns: table => new
                {
                    claim_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    claim_number = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    member_id = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    provider_id = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    billed_amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    service_from = table.Column<DateTime>(type: "datetime2", nullable: false),
                    service_to = table.Column<DateTime>(type: "datetime2", nullable: false),
                    received_on = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_claim", x => x.claim_id);
                });

            migrationBuilder.CreateTable(
                name: "queue",
                columns: table => new
                {
                    queue_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    queue_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    queue_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_queue", x => x.queue_id);
                });

            migrationBuilder.CreateTable(
                name: "user_session",
                columns: table => new
                {
                    session_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    user_id = table.Column<int>(type: "int", nullable: false),
                    started_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ended_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_session", x => x.session_id);
                    table.ForeignKey(
                        name: "FK_user_session_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "review_task",
                columns: table => new
                {
                    task_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    claim_id = table.Column<int>(type: "int", nullable: false),
                    queue_id = table.Column<int>(type: "int", nullable: false),
                    priority = table.Column<byte>(type: "tinyint", nullable: false),
                    due_date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    outcome = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    assigned_to_user_id = table.Column<int>(type: "int", nullable: true),
                    assigned_by_user_id = table.Column<int>(type: "int", nullable: true),
                    locked_by_user_id = table.Column<int>(type: "int", nullable: true),
                    locked_on = table.Column<DateTime>(type: "datetime2", nullable: true),
                    lock_expires_on = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    closed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    closed_by_user_id = table.Column<int>(type: "int", nullable: true),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_task", x => x.task_id);
                    table.ForeignKey(
                        name: "FK_review_task_app_user_assigned_by_user_id",
                        column: x => x.assigned_by_user_id,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_review_task_app_user_assigned_to_user_id",
                        column: x => x.assigned_to_user_id,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_review_task_app_user_closed_by_user_id",
                        column: x => x.closed_by_user_id,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_review_task_app_user_locked_by_user_id",
                        column: x => x.locked_by_user_id,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_review_task_claim_claim_id",
                        column: x => x.claim_id,
                        principalTable: "claim",
                        principalColumn: "claim_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_review_task_queue_queue_id",
                        column: x => x.queue_id,
                        principalTable: "queue",
                        principalColumn: "queue_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_app_user_username",
                table: "app_user",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_claim_claim_number",
                table: "claim",
                column: "claim_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_queue_queue_code",
                table: "queue",
                column: "queue_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_review_task_assigned_by_user_id",
                table: "review_task",
                column: "assigned_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_review_task_assigned_to_user_id",
                table: "review_task",
                column: "assigned_to_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_task_claim",
                table: "review_task",
                column: "claim_id");

            migrationBuilder.CreateIndex(
                name: "IX_review_task_closed_by_user_id",
                table: "review_task",
                column: "closed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_review_task_locked_by_user_id",
                table: "review_task",
                column: "locked_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_task_queue_status",
                table: "review_task",
                columns: new[] { "queue_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_user_session_user_id",
                table: "user_session",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "review_task");

            migrationBuilder.DropTable(
                name: "user_session");

            migrationBuilder.DropTable(
                name: "claim");

            migrationBuilder.DropTable(
                name: "queue");

            migrationBuilder.DropTable(
                name: "app_user");
        }
    }
}
