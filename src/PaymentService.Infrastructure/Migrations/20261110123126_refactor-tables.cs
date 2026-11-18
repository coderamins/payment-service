using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class refactortables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "PaymentTransaction",
                newName: "ModifiedAt");

            migrationBuilder.RenameColumn(
                name: "PaymentSubscriber",
                table: "PaymentTransaction",
                newName: "PaymentSubscriberId");

            migrationBuilder.RenameColumn(
                name: "PublishedAtUtc",
                table: "OutboxMessage",
                newName: "PublishedAt");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "OutboxMessage",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "OutboxMessage",
                newName: "OutboxMessageId");

            migrationBuilder.RenameIndex(
                name: "IX_OutboxMessage_CreatedAtUtc",
                table: "OutboxMessage",
                newName: "IX_OutboxMessage_CreatedAt");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "IdempotencyRecord",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "IdempotencyRecord",
                newName: "IdempotencyRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PaymentSubscriberId",
                table: "PaymentTransaction",
                newName: "PaymentSubscriber");

            migrationBuilder.RenameColumn(
                name: "ModifiedAt",
                table: "PaymentTransaction",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "PublishedAt",
                table: "OutboxMessage",
                newName: "PublishedAtUtc");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "OutboxMessage",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "OutboxMessageId",
                table: "OutboxMessage",
                newName: "Id");

            migrationBuilder.RenameIndex(
                name: "IX_OutboxMessage_CreatedAt",
                table: "OutboxMessage",
                newName: "IX_OutboxMessage_CreatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "IdempotencyRecord",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "IdempotencyRecordId",
                table: "IdempotencyRecord",
                newName: "Id");
        }
    }
}
