using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternetShopService_back.Migrations
{
    /// <inheritdoc />
    public partial class AddFimBizOrderSyncFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FimBizOrderId",
                table: "Orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SyncedWithFimBizAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_FimBizOrderId",
                table: "Orders",
                column: "FimBizOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_FimBizOrderId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FimBizOrderId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SyncedWithFimBizAt",
                table: "Orders");
        }
    }
}

