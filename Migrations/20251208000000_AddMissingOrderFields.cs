using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternetShopService_back.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingOrderFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Используем SQL с проверками для безопасного добавления колонок
            // Это позволяет применять миграцию даже если некоторые колонки уже существуют
            
            // 1. Добавляем FimBizOrderId
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns 
                    WHERE table_schema = 'public' AND table_name = 'Orders' AND column_name = 'FimBizOrderId'
                ) THEN
                    ALTER TABLE ""Orders"" ADD COLUMN ""FimBizOrderId"" integer NULL;
                END IF;
            ");

            // 2. Добавляем SyncedWithFimBizAt
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns 
                    WHERE table_schema = 'public' AND table_name = 'Orders' AND column_name = 'SyncedWithFimBizAt'
                ) THEN
                    ALTER TABLE ""Orders"" ADD COLUMN ""SyncedWithFimBizAt"" timestamp with time zone NULL;
                END IF;
            ");

            // 3. Удаляем старую колонку CarrierId (если существует)
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 FROM information_schema.columns 
                    WHERE table_schema = 'public' AND table_name = 'Orders' AND column_name = 'CarrierId'
                ) THEN
                    ALTER TABLE ""Orders"" DROP COLUMN ""CarrierId"";
                END IF;
            ");

            // 4. Добавляем новую колонку Carrier
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns 
                    WHERE table_schema = 'public' AND table_name = 'Orders' AND column_name = 'Carrier'
                ) THEN
                    ALTER TABLE ""Orders"" ADD COLUMN ""Carrier"" character varying(500) NULL;
                END IF;
            ");

            // 5. Создаем индекс для FimBizOrderId (если его нет)
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM pg_indexes 
                    WHERE schemaname = 'public' AND tablename = 'Orders' AND indexname = 'IX_Orders_FimBizOrderId'
                ) THEN
                    CREATE INDEX ""IX_Orders_FimBizOrderId"" ON ""Orders"" (""FimBizOrderId"");
                END IF;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Откат миграции
            
            // Удаляем индекс
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM pg_indexes 
                        WHERE schemaname = 'public' AND tablename = 'Orders' AND indexname = 'IX_Orders_FimBizOrderId'
                    ) THEN
                        DROP INDEX ""IX_Orders_FimBizOrderId"";
                    END IF;
                END $$;
            ");

            // Удаляем новые колонки
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' AND table_name = 'Orders' AND column_name = 'Carrier'
                    ) THEN
                        ALTER TABLE ""Orders"" DROP COLUMN ""Carrier"";
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' AND table_name = 'Orders' AND column_name = 'FimBizOrderId'
                    ) THEN
                        ALTER TABLE ""Orders"" DROP COLUMN ""FimBizOrderId"";
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' AND table_name = 'Orders' AND column_name = 'SyncedWithFimBizAt'
                    ) THEN
                        ALTER TABLE ""Orders"" DROP COLUMN ""SyncedWithFimBizAt"";
                    END IF;
                END $$;
            ");

            // Возвращаем CarrierId
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' AND table_name = 'Orders' AND column_name = 'CarrierId'
                    ) THEN
                        ALTER TABLE ""Orders"" ADD COLUMN ""CarrierId"" uuid NULL;
                    END IF;
                END $$;
            ");
        }
    }
}
