using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternetShopService_back.Migrations
{
    /// <inheritdoc />
    public partial class ChangeCarrierIdToString : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Удаляем старую колонку CarrierId (uuid)
            migrationBuilder.DropColumn(
                name: "CarrierId",
                table: "Orders");

            // Добавляем новую колонку Carrier (string)
            migrationBuilder.AddColumn<string>(
                name: "Carrier",
                table: "Orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Откат: удаляем Carrier и возвращаем CarrierId
            migrationBuilder.DropColumn(
                name: "Carrier",
                table: "Orders");

            migrationBuilder.AddColumn<Guid>(
                name: "CarrierId",
                table: "Orders",
                type: "uuid",
                nullable: true);
        }
    }
}

