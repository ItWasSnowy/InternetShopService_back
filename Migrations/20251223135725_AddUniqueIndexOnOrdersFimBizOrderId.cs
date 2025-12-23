using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternetShopService_back.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueIndexOnOrdersFimBizOrderId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_FimBizOrderId",
                table: "Orders");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_FimBizOrderId",
                table: "Orders",
                column: "FimBizOrderId",
                unique: true,
                filter: "\"FimBizOrderId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_FimBizOrderId",
                table: "Orders");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_FimBizOrderId",
                table: "Orders",
                column: "FimBizOrderId");
        }
    }
}
