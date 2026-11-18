using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class refactorcolumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_PaymentTransaction_ActivePerOrder",
                table: "PaymentTransaction");

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "PaymentTransaction",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "PaymentTransaction",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "PaymentTransaction",
                newName: "PaymentTransactionId");

            migrationBuilder.AlterColumn<decimal>(
                name: "AmountInRials",
                table: "PaymentTransaction",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<string>(
                name: "ResultToken",
                table: "PaymentTransaction",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "PaymentTransaction",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AlterColumn<string>(
                name: "ResponseBodyJson",
                table: "IdempotencyRecord",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransaction_ResultToken",
                table: "PaymentTransaction",
                column: "ResultToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PaymentTransaction_ActivePerOrder",
                table: "PaymentTransaction",
                columns: new[] { "PaymentSubscriber", "OrderId" },
                unique: true,
                filter: "[PaymentStatusId] IN (0, 1, 2, 3, 4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentTransaction_ResultToken",
                table: "PaymentTransaction");

            migrationBuilder.DropIndex(
                name: "UX_PaymentTransaction_ActivePerOrder",
                table: "PaymentTransaction");

            migrationBuilder.DropColumn(
                name: "ResultToken",
                table: "PaymentTransaction");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "PaymentTransaction");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "PaymentTransaction",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "PaymentTransaction",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "PaymentTransactionId",
                table: "PaymentTransaction",
                newName: "Id");

            migrationBuilder.AlterColumn<long>(
                name: "AmountInRials",
                table: "PaymentTransaction",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<string>(
                name: "ResponseBodyJson",
                table: "IdempotencyRecord",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(4000)",
                oldMaxLength: 4000);

            migrationBuilder.CreateIndex(
                name: "UX_PaymentTransaction_ActivePerOrder",
                table: "PaymentTransaction",
                columns: new[] { "PaymentSubscriber", "OrderId" },
                unique: true,
                filter: "[PaymentStatusId] IN ('0','1','2','3','4')");
        }
    }
}
