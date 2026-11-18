using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class callbackkeypaymentstatusid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "PaymentTransaction");

            migrationBuilder.AlterColumn<byte>(
                name: "GatewayProvider",
                table: "PaymentTransaction",
                type: "tinyint",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32);

            migrationBuilder.AddColumn<Guid>(
                name: "CallbackKey",
                table: "PaymentTransaction",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<byte>(
                name: "PaymentStatusId",
                table: "PaymentTransaction",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransaction_CallbackKey",
                table: "PaymentTransaction",
                column: "CallbackKey");

            migrationBuilder.CreateIndex(
                name: "UX_PaymentTransaction_ActivePerOrder",
                table: "PaymentTransaction",
                columns: new[] { "PaymentSubscriber", "OrderId" },
                unique: true,
                filter: "[PaymentStatusId] IN ('0','1','2','3','4')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentTransaction_CallbackKey",
                table: "PaymentTransaction");

            migrationBuilder.DropIndex(
                name: "UX_PaymentTransaction_ActivePerOrder",
                table: "PaymentTransaction");

            migrationBuilder.DropColumn(
                name: "CallbackKey",
                table: "PaymentTransaction");

            migrationBuilder.DropColumn(
                name: "PaymentStatusId",
                table: "PaymentTransaction");

            migrationBuilder.AlterColumn<string>(
                name: "GatewayProvider",
                table: "PaymentTransaction",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "tinyint");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "PaymentTransaction",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");
        }
    }
}
