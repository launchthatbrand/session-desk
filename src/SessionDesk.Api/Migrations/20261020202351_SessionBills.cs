using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SessionDesk.Api.Migrations
{
    /// <inheritdoc />
    public partial class SessionBills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "body",
                table: "bills",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "citation",
                table: "bills",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "body",
                table: "bills");

            migrationBuilder.DropColumn(
                name: "citation",
                table: "bills");
        }
    }
}
