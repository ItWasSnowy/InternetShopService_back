using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternetShopService_back.Migrations
{
    /// <inheritdoc />
    public partial class FixUrlPhotosJsonColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Безопасно добавляем столбец UrlPhotosJson, если его нет
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' AND table_name = 'OrderItems' AND column_name = 'UrlPhotosJson'
                    ) THEN
                        ALTER TABLE ""OrderItems"" ADD COLUMN ""UrlPhotosJson"" character varying(2000) NULL;
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Безопасно удаляем столбец UrlPhotosJson, если он существует
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' AND table_name = 'OrderItems' AND column_name = 'UrlPhotosJson'
                    ) THEN
                        ALTER TABLE ""OrderItems"" DROP COLUMN ""UrlPhotosJson"";
                    END IF;
                END $$;
            ");
        }
    }
}
