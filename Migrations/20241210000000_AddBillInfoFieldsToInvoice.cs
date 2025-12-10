using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternetShopService_back.Migrations
{
    /// <inheritdoc />
    public partial class AddBillInfoFieldsToInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Добавляем FimBizBillId
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

            // Добавляем PdfUrl
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Удаляем PdfUrl
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

            // Удаляем FimBizBillId
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
    }
}

