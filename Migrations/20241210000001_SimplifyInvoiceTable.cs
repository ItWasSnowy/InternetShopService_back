using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternetShopService_back.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyInvoiceTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Удаляем ненужные поля из таблицы Invoices
            // Оставляем только: Id, OrderId, PdfUrl, CreatedAt, UpdatedAt

            // Удаляем индекс по InvoiceNumber (если существует)
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM pg_indexes 
                        WHERE schemaname = 'public' AND tablename = 'Invoices' AND indexname = 'IX_Invoices_InvoiceNumber'
                    ) THEN
                        DROP INDEX ""IX_Invoices_InvoiceNumber"";
                    END IF;
                END $$;
            ");

            // Удаляем индекс по CounterpartyId (если существует)
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM pg_indexes 
                        WHERE schemaname = 'public' AND tablename = 'Invoices' AND indexname = 'IX_Invoices_CounterpartyId'
                    ) THEN
                        DROP INDEX ""IX_Invoices_CounterpartyId"";
                    END IF;
                END $$;
            ");

            // Удаляем внешний ключ на Counterparty (если существует)
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.table_constraints 
                        WHERE table_schema = 'public' 
                        AND table_name = 'Invoices' 
                        AND constraint_name = 'FK_Invoices_Counterparties_CounterpartyId'
                    ) THEN
                        ALTER TABLE ""Invoices"" DROP CONSTRAINT ""FK_Invoices_Counterparties_CounterpartyId"";
                    END IF;
                END $$;
            ");

            // Удаляем колонки
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
                        WHERE table_schema = 'public' AND table_name = 'Invoices' AND column_name = 'TotalAmount'
                    ) THEN
                        ALTER TABLE ""Invoices"" DROP COLUMN ""TotalAmount"";
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
                        WHERE table_schema = 'public' AND table_name = 'Invoices' AND column_name = 'FimBizBillId'
                    ) THEN
                        ALTER TABLE ""Invoices"" DROP COLUMN ""FimBizBillId"";
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Откат миграции - восстанавливаем удаленные поля
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' AND table_name = 'Invoices' AND column_name = 'CounterpartyId'
                    ) THEN
                        ALTER TABLE ""Invoices"" ADD COLUMN ""CounterpartyId"" uuid NULL;
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
                        ALTER TABLE ""Invoices"" ADD COLUMN ""InvoiceNumber"" character varying(50) NULL;
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
                        ALTER TABLE ""Invoices"" ADD COLUMN ""InvoiceDate"" timestamp with time zone NULL;
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
                        ALTER TABLE ""Invoices"" ADD COLUMN ""TotalAmount"" numeric(18,2) NULL;
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
                        WHERE table_schema = 'public' AND table_name = 'Invoices' AND column_name = 'FimBizBillId'
                    ) THEN
                        ALTER TABLE ""Invoices"" ADD COLUMN ""FimBizBillId"" integer NULL;
                    END IF;
                END $$;
            ");
        }
    }
}

