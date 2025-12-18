using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternetShopService_back.Migrations
{
    /// <inheritdoc />
    public partial class ChangeNomenclatureIdToInt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Конвертация для OrderItems
            migrationBuilder.Sql(@"
                ALTER TABLE ""OrderItems"" 
                ADD COLUMN ""NomenclatureId_temp"" integer NULL;
                
                UPDATE ""OrderItems""
                SET ""NomenclatureId_temp"" = CASE
                    WHEN ""NomenclatureId""::text LIKE '00000000-0000-0000-0000-%' THEN
                        CAST(LTRIM(SPLIT_PART(""NomenclatureId""::text, '-', 5), '0') AS integer)
                    WHEN LTRIM(SPLIT_PART(""NomenclatureId""::text, '-', 5), '0') = '' THEN
                        0
                    ELSE
                        0
                END;
                
                ALTER TABLE ""OrderItems"" 
                DROP COLUMN ""NomenclatureId"";
                
                ALTER TABLE ""OrderItems"" 
                RENAME COLUMN ""NomenclatureId_temp"" TO ""NomenclatureId"";
                
                ALTER TABLE ""OrderItems"" 
                ALTER COLUMN ""NomenclatureId"" SET NOT NULL;
            ");

            // Конвертация для CartItems
            migrationBuilder.Sql(@"
                ALTER TABLE ""CartItems"" 
                ADD COLUMN ""NomenclatureId_temp"" integer NULL;
                
                UPDATE ""CartItems""
                SET ""NomenclatureId_temp"" = CASE
                    WHEN ""NomenclatureId""::text LIKE '00000000-0000-0000-0000-%' THEN
                        CAST(LTRIM(SPLIT_PART(""NomenclatureId""::text, '-', 5), '0') AS integer)
                    WHEN LTRIM(SPLIT_PART(""NomenclatureId""::text, '-', 5), '0') = '' THEN
                        0
                    ELSE
                        0
                END;
                
                ALTER TABLE ""CartItems"" 
                DROP COLUMN ""NomenclatureId"";
                
                ALTER TABLE ""CartItems"" 
                RENAME COLUMN ""NomenclatureId_temp"" TO ""NomenclatureId"";
                
                ALTER TABLE ""CartItems"" 
                ALTER COLUMN ""NomenclatureId"" SET NOT NULL;
            ");

            // Конвертация для Discounts (nullable)
            migrationBuilder.Sql(@"
                ALTER TABLE ""Discounts"" 
                ADD COLUMN ""NomenclatureId_temp"" integer NULL;
                
                UPDATE ""Discounts""
                SET ""NomenclatureId_temp"" = CASE
                    WHEN ""NomenclatureId"" IS NULL THEN NULL
                    WHEN ""NomenclatureId""::text LIKE '00000000-0000-0000-0000-%' THEN
                        CASE 
                            WHEN LTRIM(SPLIT_PART(""NomenclatureId""::text, '-', 5), '0') = '' THEN 0
                            ELSE CAST(LTRIM(SPLIT_PART(""NomenclatureId""::text, '-', 5), '0') AS integer)
                        END
                    ELSE
                        NULL
                END;
                
                ALTER TABLE ""Discounts"" 
                DROP COLUMN ""NomenclatureId"";
                
                ALTER TABLE ""Discounts"" 
                RENAME COLUMN ""NomenclatureId_temp"" TO ""NomenclatureId"";
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Откат для OrderItems - конвертируем int обратно в GUID формат
            migrationBuilder.Sql(@"
                ALTER TABLE ""OrderItems"" 
                ADD COLUMN ""NomenclatureId_temp"" uuid NULL;
                
                UPDATE ""OrderItems""
                SET ""NomenclatureId_temp"" = CAST('00000000-0000-0000-0000-' || LPAD(""NomenclatureId""::text, 12, '0') AS uuid);
                
                ALTER TABLE ""OrderItems"" 
                DROP COLUMN ""NomenclatureId"";
                
                ALTER TABLE ""OrderItems"" 
                RENAME COLUMN ""NomenclatureId_temp"" TO ""NomenclatureId"";
                
                ALTER TABLE ""OrderItems"" 
                ALTER COLUMN ""NomenclatureId"" SET NOT NULL;
            ");

            // Откат для CartItems
            migrationBuilder.Sql(@"
                ALTER TABLE ""CartItems"" 
                ADD COLUMN ""NomenclatureId_temp"" uuid NULL;
                
                UPDATE ""CartItems""
                SET ""NomenclatureId_temp"" = CAST('00000000-0000-0000-0000-' || LPAD(""NomenclatureId""::text, 12, '0') AS uuid);
                
                ALTER TABLE ""CartItems"" 
                DROP COLUMN ""NomenclatureId"";
                
                ALTER TABLE ""CartItems"" 
                RENAME COLUMN ""NomenclatureId_temp"" TO ""NomenclatureId"";
                
                ALTER TABLE ""CartItems"" 
                ALTER COLUMN ""NomenclatureId"" SET NOT NULL;
            ");

            // Откат для Discounts
            migrationBuilder.Sql(@"
                ALTER TABLE ""Discounts"" 
                ADD COLUMN ""NomenclatureId_temp"" uuid NULL;
                
                UPDATE ""Discounts""
                SET ""NomenclatureId_temp"" = CASE
                    WHEN ""NomenclatureId"" IS NULL THEN NULL
                    ELSE CAST('00000000-0000-0000-0000-' || LPAD(""NomenclatureId""::text, 12, '0') AS uuid)
                END;
                
                ALTER TABLE ""Discounts"" 
                DROP COLUMN ""NomenclatureId"";
                
                ALTER TABLE ""Discounts"" 
                RENAME COLUMN ""NomenclatureId_temp"" TO ""NomenclatureId"";
            ");
        }
    }
}
