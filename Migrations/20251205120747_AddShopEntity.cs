using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternetShopService_back.Migrations
{
    /// <inheritdoc />
    public partial class AddShopEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Сначала создаем таблицу Shops
            migrationBuilder.CreateTable(
                name: "Shops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    FimBizCompanyId = table.Column<int>(type: "integer", nullable: false),
                    FimBizOrganizationId = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shops", x => x.Id);
                });

            // Создаем индексы для Shops
            migrationBuilder.CreateIndex(
                name: "IX_Shops_Domain",
                table: "Shops",
                column: "Domain",
                unique: true,
                filter: "\"Domain\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Shops_FimBizCompanyId",
                table: "Shops",
                column: "FimBizCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Shops_FimBizCompanyId_FimBizOrganizationId",
                table: "Shops",
                columns: new[] { "FimBizCompanyId", "FimBizOrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Shops_IsActive",
                table: "Shops",
                column: "IsActive");

            // Добавляем ShopId в UserAccounts как nullable сначала
            migrationBuilder.AddColumn<Guid>(
                name: "ShopId",
                table: "UserAccounts",
                type: "uuid",
                nullable: true);

            // Создаем дефолтный Shop и обновляем существующие UserAccounts
            var defaultShopId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            
            migrationBuilder.Sql($@"
                -- Создаем дефолтный Shop для существующих данных
                -- FimBizCompanyId = 0 означает, что это дефолтный магазин для всех компаний
                -- В реальной ситуации нужно будет создать Shop для каждой компании вручную или через API
                INSERT INTO ""Shops"" (""Id"", ""Name"", ""FimBizCompanyId"", ""FimBizOrganizationId"", ""IsActive"", ""CreatedAt"", ""UpdatedAt"")
                VALUES ('{defaultShopId}', 'Default Shop', 0, NULL, true, TIMESTAMP '{now:yyyy-MM-dd HH:mm:ss}', TIMESTAMP '{now:yyyy-MM-dd HH:mm:ss}');
                
                -- Обновляем все существующие UserAccounts, связывая их с дефолтным Shop
                -- В реальной ситуации нужно будет обновить ShopId на основе FimBizCompanyId контрагента
                UPDATE ""UserAccounts""
                SET ""ShopId"" = '{defaultShopId}'
                WHERE ""ShopId"" IS NULL;
            ");

            // Делаем ShopId обязательным после заполнения
            migrationBuilder.AlterColumn<Guid>(
                name: "ShopId",
                table: "UserAccounts",
                type: "uuid",
                nullable: false);

            // Создаем индекс и внешний ключ
            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_ShopId",
                table: "UserAccounts",
                column: "ShopId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserAccounts_Shops_ShopId",
                table: "UserAccounts",
                column: "ShopId",
                principalTable: "Shops",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserAccounts_Shops_ShopId",
                table: "UserAccounts");

            migrationBuilder.DropTable(
                name: "Shops");

            migrationBuilder.DropIndex(
                name: "IX_UserAccounts_ShopId",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "ShopId",
                table: "UserAccounts");
        }
    }
}
