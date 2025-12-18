using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternetShopService_back.Migrations
{
    /// <inheritdoc />
    public partial class ChangeNomenclatureGroupIdToInt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Для Discounts: просто удаляем старую колонку и создаем новую
            // Старые тестовые данные станут NULL
            migrationBuilder.Sql(@"
                ALTER TABLE ""Discounts"" 
                ADD COLUMN ""NomenclatureGroupId_temp"" integer NULL;
                
                ALTER TABLE ""Discounts"" 
                DROP COLUMN ""NomenclatureGroupId"";
                
                ALTER TABLE ""Discounts"" 
                RENAME COLUMN ""NomenclatureGroupId_temp"" TO ""NomenclatureGroupId"";
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Откат для Discounts - конвертируем int обратно в GUID формат
            migrationBuilder.Sql(@"
                ALTER TABLE ""Discounts"" 
                ADD COLUMN ""NomenclatureGroupId_temp"" uuid NULL;
                
                UPDATE ""Discounts""
                SET ""NomenclatureGroupId_temp"" = CASE
                    WHEN ""NomenclatureGroupId"" IS NULL THEN NULL
                    ELSE CAST('00000000-0000-0000-0000-' || LPAD(""NomenclatureGroupId""::text, 12, '0') AS uuid)
                END;
                
                ALTER TABLE ""Discounts"" 
                DROP COLUMN ""NomenclatureGroupId"";
                
                ALTER TABLE ""Discounts"" 
                RENAME COLUMN ""NomenclatureGroupId_temp"" TO ""NomenclatureGroupId"";
            ");
        }
    }
}
