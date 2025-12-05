using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternetShopService_back.Migrations
{
    /// <inheritdoc />
    public partial class AddFimBizSyncFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FimBizCompanyId",
                table: "Counterparties",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FimBizContractorId",
                table: "Counterparties",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FimBizOrganizationId",
                table: "Counterparties",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastSyncVersion",
                table: "Counterparties",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Counterparties_FimBizCompanyId_FimBizOrganizationId",
                table: "Counterparties",
                columns: new[] { "FimBizCompanyId", "FimBizOrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Counterparties_FimBizContractorId",
                table: "Counterparties",
                column: "FimBizContractorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Counterparties_FimBizCompanyId_FimBizOrganizationId",
                table: "Counterparties");

            migrationBuilder.DropIndex(
                name: "IX_Counterparties_FimBizContractorId",
                table: "Counterparties");

            migrationBuilder.DropColumn(
                name: "FimBizCompanyId",
                table: "Counterparties");

            migrationBuilder.DropColumn(
                name: "FimBizContractorId",
                table: "Counterparties");

            migrationBuilder.DropColumn(
                name: "FimBizOrganizationId",
                table: "Counterparties");

            migrationBuilder.DropColumn(
                name: "LastSyncVersion",
                table: "Counterparties");
        }
    }
}
