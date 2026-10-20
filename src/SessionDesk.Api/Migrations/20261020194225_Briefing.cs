using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SessionDesk.Api.Migrations
{
    /// <inheritdoc />
    public partial class Briefing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "trace_id",
                table: "audit_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ai_briefings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    committee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bill_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model = table.Column<string>(type: "text", nullable: false),
                    prompt_version = table.Column<string>(type: "text", nullable: false),
                    retrieved_bill_numbers = table.Column<string>(type: "text", nullable: false),
                    draft_text = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    withhold_reason = table.Column<string>(type: "text", nullable: true),
                    accepted_text = table.Column<string>(type: "text", nullable: true),
                    input_tokens = table.Column<int>(type: "integer", nullable: true),
                    output_tokens = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_briefings", x => x.id);
                    table.ForeignKey(
                        name: "fk_ai_briefings_bills_bill_id",
                        column: x => x.bill_id,
                        principalTable: "bills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ai_briefings_committees_committee_id",
                        column: x => x.committee_id,
                        principalTable: "committees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ai_briefings_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_trace_id",
                table: "audit_events",
                column: "trace_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_briefings_actor_user_id",
                table: "ai_briefings",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_briefings_bill_id",
                table: "ai_briefings",
                column: "bill_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_briefings_committee_id",
                table: "ai_briefings",
                column: "committee_id");

            migrationBuilder.AddForeignKey(
                name: "fk_audit_events_ai_briefings_trace_id",
                table: "audit_events",
                column: "trace_id",
                principalTable: "ai_briefings",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_audit_events_ai_briefings_trace_id",
                table: "audit_events");

            migrationBuilder.DropTable(
                name: "ai_briefings");

            migrationBuilder.DropIndex(
                name: "ix_audit_events_trace_id",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "trace_id",
                table: "audit_events");
        }
    }
}
