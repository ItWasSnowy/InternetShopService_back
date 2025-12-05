using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternetShopService_back.Migrations
{
    /// <inheritdoc />
    public partial class AddPhoneCallFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccessFailedCount",
                table: "UserAccounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstFailedLoginAttempt",
                table: "UserAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewPhoneNumber",
                table: "UserAccounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PhoneCallDateTime",
                table: "UserAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneCallDigits",
                table: "UserAccounts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccessFailedCount",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "FirstFailedLoginAttempt",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "NewPhoneNumber",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "PhoneCallDateTime",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "PhoneCallDigits",
                table: "UserAccounts");
        }
    }
}
