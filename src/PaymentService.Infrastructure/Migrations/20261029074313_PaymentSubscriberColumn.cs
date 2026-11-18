using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PaymentSubscriberColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestingService",
                table: "PaymentTransaction");

            migrationBuilder.AddColumn<byte>(
                name: "PaymentSubscriber",
                table: "PaymentTransaction",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentSubscriber",
                table: "PaymentTransaction");

            migrationBuilder.AddColumn<string>(
                name: "RequestingService",
                table: "PaymentTransaction",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");
        }
    }
}
