using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IdempotencyRecord",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestPath = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RequestBodyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResponseStatusCode = table.Column<int>(type: "int", nullable: false),
                    ResponseBodyJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentTransaction",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestingService = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AmountInRials = table.Column<long>(type: "bigint", nullable: false),
                    GatewayProvider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    GatewayRefId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    GatewaySaleReferenceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    GatewayInvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    MaskedCardNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    GatewayResultCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ReturnUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentTransaction", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecord_IdempotencyKey_RequestPath",
                table: "IdempotencyRecord",
                columns: new[] { "IdempotencyKey", "RequestPath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_CreatedAtUtc",
                table: "OutboxMessage",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransaction_GatewayRefId",
                table: "PaymentTransaction",
                column: "GatewayRefId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransaction_OrderId",
                table: "PaymentTransaction",
                column: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdempotencyRecord");

            migrationBuilder.DropTable(
                name: "OutboxMessage");

            migrationBuilder.DropTable(
                name: "PaymentTransaction");
        }
    }
}
