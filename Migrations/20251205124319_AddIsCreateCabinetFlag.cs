using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternetShopService_back.Migrations
{
    /// <inheritdoc />
    public partial class AddIsCreateCabinetFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCreateCabinet",
                table: "Counterparties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Counterparties_IsCreateCabinet",
                table: "Counterparties",
                column: "IsCreateCabinet");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Counterparties_IsCreateCabinet",
                table: "Counterparties");

            migrationBuilder.DropColumn(
                name: "IsCreateCabinet",
                table: "Counterparties");
        }
    }
}
