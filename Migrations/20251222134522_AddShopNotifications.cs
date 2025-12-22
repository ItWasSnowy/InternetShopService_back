using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternetShopService_back.Migrations
{
    /// <inheritdoc />
    public partial class AddShopNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShopNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CounterpartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ObjectType = table.Column<int>(type: "integer", nullable: false),
                    ObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopNotifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShopNotifications_CounterpartyId",
                table: "ShopNotifications",
                column: "CounterpartyId");

            migrationBuilder.CreateIndex(
                name: "IX_ShopNotifications_CounterpartyId_IsRead_CreatedAt",
                table: "ShopNotifications",
                columns: new[] { "CounterpartyId", "IsRead", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ShopNotifications_CounterpartyId_ObjectType_ObjectId",
                table: "ShopNotifications",
                columns: new[] { "CounterpartyId", "ObjectType", "ObjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_ShopNotifications_CreatedAt",
                table: "ShopNotifications",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ShopNotifications_IsRead",
                table: "ShopNotifications",
                column: "IsRead");

            migrationBuilder.CreateIndex(
                name: "IX_ShopNotifications_UserAccountId",
                table: "ShopNotifications",
                column: "UserAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShopNotifications");
        }
    }
}
