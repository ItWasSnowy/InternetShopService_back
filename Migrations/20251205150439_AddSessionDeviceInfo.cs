using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternetShopService_back.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionDeviceInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeviceInfo",
                table: "Sessions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeviceName",
                table: "Sessions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "Sessions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserAgent",
                table: "Sessions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_UserAccountId_IsActive",
                table: "Sessions",
                columns: new[] { "UserAccountId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sessions_UserAccountId_IsActive",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "DeviceInfo",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "DeviceName",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "UserAgent",
                table: "Sessions");
        }
    }
}
