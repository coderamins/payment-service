IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [IdempotencyRecord] (
    [Id] uniqueidentifier NOT NULL,
    [IdempotencyKey] nvarchar(128) NOT NULL,
    [RequestPath] nvarchar(256) NOT NULL,
    [RequestBodyHash] nvarchar(64) NOT NULL,
    [ResponseStatusCode] int NOT NULL,
    [ResponseBodyJson] nvarchar(max) NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_IdempotencyRecord] PRIMARY KEY ([Id])
);

CREATE TABLE [OutboxMessage] (
    [Id] uniqueidentifier NOT NULL,
    [EventType] nvarchar(128) NOT NULL,
    [PayloadJson] nvarchar(max) NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [PublishedAtUtc] datetime2 NULL,
    [RetryCount] int NOT NULL,
    [LastError] nvarchar(1024) NULL,
    CONSTRAINT [PK_OutboxMessage] PRIMARY KEY ([Id])
);

CREATE TABLE [PaymentTransaction] (
    [Id] uniqueidentifier NOT NULL,
    [OrderId] nvarchar(64) NOT NULL,
    [RequestingService] nvarchar(64) NOT NULL,
    [AmountInRials] bigint NOT NULL,
    [GatewayProvider] nvarchar(32) NOT NULL,
    [Status] nvarchar(32) NOT NULL,
    [GatewayRefId] nvarchar(128) NULL,
    [GatewaySaleReferenceId] nvarchar(128) NULL,
    [GatewayInvoiceId] bigint NOT NULL,
    [MaskedCardNumber] nvarchar(32) NULL,
    [GatewayResultCode] nvarchar(16) NULL,
    [FailureReason] nvarchar(512) NULL,
    [ReturnUrl] nvarchar(2048) NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [UpdatedAtUtc] datetime2 NULL,
    CONSTRAINT [PK_PaymentTransaction] PRIMARY KEY ([Id])
);

CREATE UNIQUE INDEX [IX_IdempotencyRecord_IdempotencyKey_RequestPath] ON [IdempotencyRecord] ([IdempotencyKey], [RequestPath]);

CREATE INDEX [IX_OutboxMessage_CreatedAtUtc] ON [OutboxMessage] ([CreatedAtUtc]);

CREATE INDEX [IX_PaymentTransaction_GatewayRefId] ON [PaymentTransaction] ([GatewayRefId]);

CREATE INDEX [IX_PaymentTransaction_OrderId] ON [PaymentTransaction] ([OrderId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261003105519_InitialDb', N'10.0.12');

COMMIT;
GO

