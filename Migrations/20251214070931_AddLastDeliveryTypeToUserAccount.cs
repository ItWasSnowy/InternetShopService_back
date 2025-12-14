using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternetShopService_back.Migrations
{
    /// <inheritdoc />
    public partial class AddLastDeliveryTypeToUserAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastDeliveryType",
                table: "UserAccounts",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastDeliveryType",
                table: "UserAccounts");
        }
    }
}
