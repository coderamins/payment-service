BEGIN TRANSACTION;
ALTER TABLE [PaymentTransaction] ADD [SplitsJson] nvarchar(2000) NULL;

ALTER TABLE [PaymentTransaction] ADD [TerminalNumber] int NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261102124124_splited-model', N'10.0.12');

COMMIT;
GO

BEGIN TRANSACTION;
EXEC sp_rename N'[PaymentTransaction].[UpdatedAt]', N'ModifiedAt', 'COLUMN';

EXEC sp_rename N'[PaymentTransaction].[PaymentSubscriber]', N'PaymentSubscriberId', 'COLUMN';

EXEC sp_rename N'[OutboxMessage].[PublishedAtUtc]', N'PublishedAt', 'COLUMN';

EXEC sp_rename N'[OutboxMessage].[CreatedAtUtc]', N'CreatedAt', 'COLUMN';

EXEC sp_rename N'[OutboxMessage].[Id]', N'OutboxMessageId', 'COLUMN';

EXEC sp_rename N'[OutboxMessage].[IX_OutboxMessage_CreatedAtUtc]', N'IX_OutboxMessage_CreatedAt', 'INDEX';

EXEC sp_rename N'[IdempotencyRecord].[CreatedAtUtc]', N'CreatedAt', 'COLUMN';

EXEC sp_rename N'[IdempotencyRecord].[Id]', N'IdempotencyRecordId', 'COLUMN';

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261110123126_refactor-tables', N'10.0.12');

COMMIT;
GO

BEGIN TRANSACTION;
DROP INDEX [IX_PaymentTransaction_ResultToken] ON [PaymentTransaction];

DECLARE @var nvarchar(max);
SELECT @var = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PaymentTransaction]') AND [c].[name] = N'ResultToken');
IF @var IS NOT NULL EXEC(N'ALTER TABLE [PaymentTransaction] DROP CONSTRAINT ' + @var + ';');
ALTER TABLE [PaymentTransaction] DROP COLUMN [ResultToken];

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261111105114_remove-resultToken', N'10.0.12');

COMMIT;
GO

BEGIN TRANSACTION;
EXEC sp_rename N'[PaymentTransaction].[GatewayProvider]', N'GatewayProviderId', 'COLUMN';

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261114112012_gatewayProviderId', N'10.0.12');

COMMIT;
GO

