using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace InternetShopService_back.Migrations;

public partial class AddOrderEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "OrderEvents",
            columns: table => new
            {
                SequenceNumber = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                EventType = table.Column<int>(type: "integer", nullable: false),
                EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                Data = table.Column<string>(type: "text", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OrderEvents", x => x.SequenceNumber);
            });

        migrationBuilder.CreateIndex(
            name: "IX_OrderEvents_UserId",
            table: "OrderEvents",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_OrderEvents_SequenceNumber",
            table: "OrderEvents",
            column: "SequenceNumber");

        migrationBuilder.CreateIndex(
            name: "IX_OrderEvents_CreatedAt",
            table: "OrderEvents",
            column: "CreatedAt");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "OrderEvents");
    }
}
