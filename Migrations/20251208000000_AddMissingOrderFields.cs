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
            // Добавляем FimBizOrderId, если его нет
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_name = 'Orders' AND column_name = 'FimBizOrderId'
                    ) THEN
                        ALTER TABLE ""Orders"" ADD COLUMN ""FimBizOrderId"" integer NULL;
                    END IF;
                END $$;
            ");

            // Добавляем SyncedWithFimBizAt, если его нет
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_name = 'Orders' AND column_name = 'SyncedWithFimBizAt'
                    ) THEN
                        ALTER TABLE ""Orders"" ADD COLUMN ""SyncedWithFimBizAt"" timestamp with time zone NULL;
                    END IF;
                END $$;
            ");

            // Создаем индекс для FimBizOrderId, если его нет
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_indexes 
                        WHERE tablename = 'Orders' AND indexname = 'IX_Orders_FimBizOrderId'
                    ) THEN
                        CREATE INDEX ""IX_Orders_FimBizOrderId"" ON ""Orders"" (""FimBizOrderId"");
                    END IF;
                END $$;
            ");

            // Удаляем старую колонку CarrierId, если она существует
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_name = 'Orders' AND column_name = 'CarrierId'
                    ) THEN
                        ALTER TABLE ""Orders"" DROP COLUMN ""CarrierId"";
                    END IF;
                END $$;
            ");

            // Добавляем новую колонку Carrier, если ее нет
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_name = 'Orders' AND column_name = 'Carrier'
                    ) THEN
                        ALTER TABLE ""Orders"" ADD COLUMN ""Carrier"" character varying(500) NULL;
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Откат: удаляем Carrier и возвращаем CarrierId
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_name = 'Orders' AND column_name = 'Carrier'
                    ) THEN
                        ALTER TABLE ""Orders"" DROP COLUMN ""Carrier"";
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_name = 'Orders' AND column_name = 'CarrierId'
                    ) THEN
                        ALTER TABLE ""Orders"" ADD COLUMN ""CarrierId"" uuid NULL;
                    END IF;
                END $$;
            ");

            // Удаляем индекс и колонки FimBiz
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM pg_indexes 
                        WHERE tablename = 'Orders' AND indexname = 'IX_Orders_FimBizOrderId'
                    ) THEN
                        DROP INDEX ""IX_Orders_FimBizOrderId"";
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_name = 'Orders' AND column_name = 'FimBizOrderId'
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
                        WHERE table_name = 'Orders' AND column_name = 'SyncedWithFimBizAt'
                    ) THEN
                        ALTER TABLE ""Orders"" DROP COLUMN ""SyncedWithFimBizAt"";
                    END IF;
                END $$;
            ");
        }
    }
}

