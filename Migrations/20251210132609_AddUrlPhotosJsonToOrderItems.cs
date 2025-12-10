using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternetShopService_back.Migrations
{
    /// <inheritdoc />
    public partial class AddUrlPhotosJsonToOrderItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Безопасно удаляем внешний ключ, если существует
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.table_constraints 
                        WHERE constraint_schema = 'public' 
                        AND table_name = 'Invoices' 
                        AND constraint_name = 'FK_Invoices_Counterparties_CounterpartyId'
                    ) THEN
                        ALTER TABLE ""Invoices"" DROP CONSTRAINT ""FK_Invoices_Counterparties_CounterpartyId"";
                    END IF;
                END $$;
            ");

            // Безопасно удаляем индексы, если существуют
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM pg_indexes 
                        WHERE schemaname = 'public' 
                        AND tablename = 'Invoices' 
                        AND indexname = 'IX_Invoices_CounterpartyId'
                    ) THEN
                        DROP INDEX IF EXISTS ""IX_Invoices_CounterpartyId"";
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM pg_indexes 
                        WHERE schemaname = 'public' 
                        AND tablename = 'Invoices' 
                        AND indexname = 'IX_Invoices_InvoiceNumber'
                    ) THEN
                        DROP INDEX IF EXISTS ""IX_Invoices_InvoiceNumber"";
                    END IF;
                END $$;
            ");

            // Безопасно удаляем столбцы, если существуют
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' AND table_name = 'Orders' AND column_name = 'CarrierId'
                    ) THEN
                        ALTER TABLE ""Orders"" DROP COLUMN ""CarrierId"";
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' AND table_name = 'Invoices' AND column_name = 'CounterpartyId'
                    ) THEN
                        ALTER TABLE ""Invoices"" DROP COLUMN ""CounterpartyId"";
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' AND table_name = 'Invoices' AND column_name = 'InvoiceDate'
                    ) THEN
                        ALTER TABLE ""Invoices"" DROP COLUMN ""InvoiceDate"";
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' AND table_name = 'Invoices' AND column_name = 'InvoiceNumber'
                    ) THEN
                        ALTER TABLE ""Invoices"" DROP COLUMN ""InvoiceNumber"";
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' AND table_name = 'Invoices' AND column_name = 'IsConfirmed'
                    ) THEN
                        ALTER TABLE ""Invoices"" DROP COLUMN ""IsConfirmed"";
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' AND table_name = 'Invoices' AND column_name = 'IsPaid'
                    ) THEN
                        ALTER TABLE ""Invoices"" DROP COLUMN ""IsPaid"";
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' AND table_name = 'Invoices' AND column_name = 'TotalAmount'
                    ) THEN
                        ALTER TABLE ""Invoices"" DROP COLUMN ""TotalAmount"";
                    END IF;
                END $$;
            ");

            // Безопасно добавляем столбцы в Orders, если их нет
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' AND table_name = 'Orders' AND column_name = 'Carrier'
                    ) THEN
                        ALTER TABLE ""Orders"" ADD COLUMN ""Carrier"" character varying(500) NULL;
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' AND table_name = 'Orders' AND column_name = 'FimBizOrderId'
                    ) THEN
                        ALTER TABLE ""Orders"" ADD COLUMN ""FimBizOrderId"" integer NULL;
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' AND table_name = 'Orders' AND column_name = 'SyncedWithFimBizAt'
                    ) THEN
                        ALTER TABLE ""Orders"" ADD COLUMN ""SyncedWithFimBizAt"" timestamp with time zone NULL;
                    END IF;
                END $$;
            ");

            // Добавляем UrlPhotosJson с проверкой существования столбца
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

            // Безопасно добавляем столбец PdfUrl в Invoices, если его нет
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' AND table_name = 'Invoices' AND column_name = 'PdfUrl'
                    ) THEN
                        ALTER TABLE ""Invoices"" ADD COLUMN ""PdfUrl"" character varying(500) NULL;
                    END IF;
                END $$;
            ");

            // Безопасно создаем индекс, если его нет
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_indexes 
                        WHERE schemaname = 'public' 
                        AND tablename = 'Orders' 
                        AND indexname = 'IX_Orders_FimBizOrderId'
                    ) THEN
                        CREATE INDEX ""IX_Orders_FimBizOrderId"" ON ""Orders"" (""FimBizOrderId"");
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Безопасно удаляем индекс, если существует
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM pg_indexes 
                        WHERE schemaname = 'public' 
                        AND tablename = 'Orders' 
                        AND indexname = 'IX_Orders_FimBizOrderId'
                    ) THEN
                        DROP INDEX IF EXISTS ""IX_Orders_FimBizOrderId"";
                    END IF;
                END $$;
            ");

            // Безопасно удаляем столбцы, если существуют
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

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' AND table_name = 'Invoices' AND column_name = 'PdfUrl'
                    ) THEN
                        ALTER TABLE ""Invoices"" DROP COLUMN ""PdfUrl"";
                    END IF;
                END $$;
            ");

            // Безопасно добавляем столбцы обратно, если их нет
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

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' AND table_name = 'Invoices' AND column_name = 'CounterpartyId'
                    ) THEN
                        ALTER TABLE ""Invoices"" ADD COLUMN ""CounterpartyId"" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' AND table_name = 'Invoices' AND column_name = 'InvoiceDate'
                    ) THEN
                        ALTER TABLE ""Invoices"" ADD COLUMN ""InvoiceDate"" timestamp with time zone NOT NULL DEFAULT '0001-01-01 00:00:00+00';
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' AND table_name = 'Invoices' AND column_name = 'InvoiceNumber'
                    ) THEN
                        ALTER TABLE ""Invoices"" ADD COLUMN ""InvoiceNumber"" character varying(50) NOT NULL DEFAULT '';
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' AND table_name = 'Invoices' AND column_name = 'IsConfirmed'
                    ) THEN
                        ALTER TABLE ""Invoices"" ADD COLUMN ""IsConfirmed"" boolean NOT NULL DEFAULT false;
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' AND table_name = 'Invoices' AND column_name = 'IsPaid'
                    ) THEN
                        ALTER TABLE ""Invoices"" ADD COLUMN ""IsPaid"" boolean NOT NULL DEFAULT false;
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' AND table_name = 'Invoices' AND column_name = 'TotalAmount'
                    ) THEN
                        ALTER TABLE ""Invoices"" ADD COLUMN ""TotalAmount"" numeric(18,2) NOT NULL DEFAULT 0;
                    END IF;
                END $$;
            ");

            // Безопасно создаем индексы, если их нет
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_indexes 
                        WHERE schemaname = 'public' 
                        AND tablename = 'Invoices' 
                        AND indexname = 'IX_Invoices_CounterpartyId'
                    ) THEN
                        CREATE INDEX ""IX_Invoices_CounterpartyId"" ON ""Invoices"" (""CounterpartyId"");
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_indexes 
                        WHERE schemaname = 'public' 
                        AND tablename = 'Invoices' 
                        AND indexname = 'IX_Invoices_InvoiceNumber'
                    ) THEN
                        CREATE UNIQUE INDEX ""IX_Invoices_InvoiceNumber"" ON ""Invoices"" (""InvoiceNumber"");
                    END IF;
                END $$;
            ");

            // Безопасно создаем внешний ключ, если его нет
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.table_constraints 
                        WHERE constraint_schema = 'public' 
                        AND table_name = 'Invoices' 
                        AND constraint_name = 'FK_Invoices_Counterparties_CounterpartyId'
                    ) THEN
                        ALTER TABLE ""Invoices"" ADD CONSTRAINT ""FK_Invoices_Counterparties_CounterpartyId"" 
                        FOREIGN KEY (""CounterpartyId"") REFERENCES ""Counterparties"" (""Id"") ON DELETE RESTRICT;
                    END IF;
                END $$;
            ");
        }
    }
}
