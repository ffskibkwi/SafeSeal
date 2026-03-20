using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SafeSeal.Core;

public sealed class DocumentCatalogService : IDocumentCatalogService
{
    private const string DocumentsTable = "Documents";
    private readonly SafeSealStorageOptions _options;

    public DocumentCatalogService(SafeSealStorageOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_options.RootDirectory);
        Directory.CreateDirectory(_options.VaultDirectory);
        Directory.CreateDirectory(_options.InternalDirectory);

        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(ct);

        await ExecuteNonQueryAsync(connection, "PRAGMA encoding = 'UTF-8';", ct);

        bool tableExists = await TableExistsAsync(connection, DocumentsTable, ct);
        if (!tableExists)
        {
            await CreateDocumentsSchemaAsync(connection, ct);
            return;
        }

        HashSet<string> columns = await GetColumnSetAsync(connection, DocumentsTable, ct);
        bool needsMigration =
            !columns.Contains("NameKey")
            || !columns.Contains("IsDeleting")
            || !columns.Contains("DeletingSinceUtc")
            || !columns.Contains("DeleteOperationId")
            || !columns.Contains("RequiresIntervention")
            || !columns.Contains("InterventionReason")
            || !columns.Contains("InterventionSinceUtc");

        if (needsMigration)
        {
            await MigrateToV3Async(connection, columns, ct);
            return;
        }

        await EnsureIndexesAsync(connection, ct);
        await NormalizeNameKeysAsync(connection, ct);
    }

    public async Task<IReadOnlyList<DocumentEntry>> GetAllAsync(CancellationToken ct)
    {
        List<DocumentEntry> results = new();

        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(ct);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, DisplayName, StoredFileName, OriginalExtension, CreatedUtc, UpdatedUtc
            FROM Documents
            WHERE IsDeleted = 0 AND IsDeleting = 0 AND RequiresIntervention = 0
            ORDER BY UpdatedUtc DESC;
            """;

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadDocument(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<DeletingDocumentEntry>> GetDeletingAsync(CancellationToken ct)
    {
        List<DeletingDocumentEntry> results = new();

        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(ct);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, StoredFileName, DeleteOperationId, RequiresIntervention, InterventionReason, DeletingSinceUtc
            FROM Documents
            WHERE IsDeleted = 0 AND IsDeleting = 1
            ORDER BY UpdatedUtc DESC;
            """;

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            string opId = reader.IsDBNull(2)
                ? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)
                : NormalizeText(reader.GetString(2));

            bool requiresIntervention = !reader.IsDBNull(3) && reader.GetInt32(3) != 0;
            string? reason = reader.IsDBNull(4) ? null : NormalizeText(reader.GetString(4));
            DateTime? deletingSince = null;

            if (!reader.IsDBNull(5)
                && DateTime.TryParse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
            {
                deletingSince = parsed;
            }

            results.Add(new DeletingDocumentEntry(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                opId,
                requiresIntervention,
                reason,
                deletingSince));
        }

        return results;
    }

    public async Task<DocumentEntry?> FindByNameAsync(string displayName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(ct);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, DisplayName, StoredFileName, OriginalExtension, CreatedUtc, UpdatedUtc
            FROM Documents
            WHERE IsDeleted = 0 AND IsDeleting = 0 AND NameKey = $nameKey
            LIMIT 1;
            """;

        AddTextParameter(command, "$nameKey", BuildNameKey(displayName));

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return ReadDocument(reader);
        }

        return null;
    }

    public async Task UpsertAsync(DocumentEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(ct);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Documents (
                Id,
                DisplayName,
                NameKey,
                StoredFileName,
                OriginalExtension,
                CreatedUtc,
                UpdatedUtc,
                IsDeleted,
                IsDeleting,
                DeletingSinceUtc,
                DeleteOperationId,
                RequiresIntervention,
                InterventionReason,
                InterventionSinceUtc)
            VALUES (
                $id,
                $name,
                $nameKey,
                $storedFile,
                $extension,
                $createdUtc,
                $updatedUtc,
                0,
                0,
                NULL,
                NULL,
                0,
                NULL,
                NULL)
            ON CONFLICT(Id) DO UPDATE SET
                DisplayName = excluded.DisplayName,
                NameKey = excluded.NameKey,
                StoredFileName = excluded.StoredFileName,
                OriginalExtension = excluded.OriginalExtension,
                CreatedUtc = excluded.CreatedUtc,
                UpdatedUtc = excluded.UpdatedUtc,
                IsDeleted = 0,
                IsDeleting = 0,
                DeletingSinceUtc = NULL,
                DeleteOperationId = NULL,
                RequiresIntervention = 0,
                InterventionReason = NULL,
                InterventionSinceUtc = NULL;
            """;

        AddTextParameter(command, "$id", entry.Id.ToString("D", CultureInfo.InvariantCulture));
        AddTextParameter(command, "$name", NormalizeText(entry.DisplayName));
        AddTextParameter(command, "$nameKey", BuildNameKey(entry.DisplayName));
        AddTextParameter(command, "$storedFile", NormalizeText(entry.StoredFileName));
        AddTextParameter(command, "$extension", NormalizeText(entry.OriginalExtension));
        AddTextParameter(command, "$createdUtc", entry.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
        AddTextParameter(command, "$updatedUtc", entry.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture));

        try
        {
            await command.ExecuteNonQueryAsync(ct);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("A document with the same display name already exists.", ex);
        }
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct)
    {
        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(ct);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Documents
            SET IsDeleted = 1,
                IsDeleting = 0,
                DeletingSinceUtc = NULL,
                DeleteOperationId = NULL,
                RequiresIntervention = 0,
                InterventionReason = NULL,
                InterventionSinceUtc = NULL,
                UpdatedUtc = $updatedUtc
            WHERE Id = $id;
            """;

        AddTextParameter(command, "$id", id.ToString("D", CultureInfo.InvariantCulture));
        AddTextParameter(command, "$updatedUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkDeletingAsync(Guid id, string opId, DateTime startedUtc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opId))
        {
            throw new ArgumentException("Delete operation id cannot be empty.", nameof(opId));
        }

        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(ct);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Documents
            SET IsDeleting = 1,
                DeletingSinceUtc = $startedUtc,
                DeleteOperationId = $opId,
                RequiresIntervention = 0,
                InterventionReason = NULL,
                InterventionSinceUtc = NULL,
                UpdatedUtc = $startedUtc
            WHERE Id = $id
              AND IsDeleted = 0
              AND IsDeleting = 0;
            """;

        AddTextParameter(command, "$id", id.ToString("D", CultureInfo.InvariantCulture));
        AddTextParameter(command, "$opId", NormalizeText(opId));
        AddTextParameter(command, "$startedUtc", startedUtc.ToString("O", CultureInfo.InvariantCulture));

        int rows = await command.ExecuteNonQueryAsync(ct);
        if (rows == 0)
        {
            throw new InvalidOperationException("Document is not in an active state for deletion.");
        }
    }

    public async Task FinalizeDeleteAsync(Guid id, string opId, DateTime updatedUtc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opId))
        {
            throw new ArgumentException("Delete operation id cannot be empty.", nameof(opId));
        }

        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(ct);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Documents
            SET IsDeleted = 1,
                IsDeleting = 0,
                DeletingSinceUtc = NULL,
                DeleteOperationId = NULL,
                RequiresIntervention = 0,
                InterventionReason = NULL,
                InterventionSinceUtc = NULL,
                UpdatedUtc = $updatedUtc
            WHERE Id = $id
              AND IsDeleted = 0
              AND IsDeleting = 1
              AND DeleteOperationId = $opId;
            """;

        AddTextParameter(command, "$id", id.ToString("D", CultureInfo.InvariantCulture));
        AddTextParameter(command, "$opId", NormalizeText(opId));
        AddTextParameter(command, "$updatedUtc", updatedUtc.ToString("O", CultureInfo.InvariantCulture));

        int rows = await command.ExecuteNonQueryAsync(ct);
        if (rows == 0)
        {
            throw new InvalidOperationException("Delete finalization failed because the delete state changed.");
        }
    }

    public async Task RecoverFromDeletingAsync(Guid id, string opId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opId))
        {
            return;
        }

        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(ct);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Documents
            SET IsDeleting = 0,
                DeletingSinceUtc = NULL,
                DeleteOperationId = NULL,
                RequiresIntervention = 0,
                InterventionReason = NULL,
                InterventionSinceUtc = NULL,
                UpdatedUtc = $updatedUtc
            WHERE Id = $id
              AND IsDeleted = 0
              AND IsDeleting = 1
              AND DeleteOperationId = $opId;
            """;

        AddTextParameter(command, "$id", id.ToString("D", CultureInfo.InvariantCulture));
        AddTextParameter(command, "$opId", NormalizeText(opId));
        AddTextParameter(command, "$updatedUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkInterventionRequiredAsync(Guid id, string opId, string reason, DateTime utc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opId))
        {
            throw new ArgumentException("Delete operation id cannot be empty.", nameof(opId));
        }

        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(ct);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Documents
            SET IsDeleting = 1,
                RequiresIntervention = 1,
                InterventionReason = $reason,
                InterventionSinceUtc = $utc,
                UpdatedUtc = $utc
            WHERE Id = $id
              AND IsDeleted = 0
              AND DeleteOperationId = $opId;
            """;

        AddTextParameter(command, "$id", id.ToString("D", CultureInfo.InvariantCulture));
        AddTextParameter(command, "$opId", NormalizeText(opId));
        AddTextParameter(command, "$reason", NormalizeText(reason));
        AddTextParameter(command, "$utc", utc.ToString("O", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(ct);
    }

    private SqliteConnection CreateConnection()
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = _options.CatalogPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        };

        return new SqliteConnection(builder.ToString());
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName, CancellationToken ct)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name LIMIT 1;";
        AddTextParameter(command, "$name", tableName);

        object? result = await command.ExecuteScalarAsync(ct);
        return result is not null;
    }

    private static async Task<HashSet<string>> GetColumnSetAsync(SqliteConnection connection, string tableName, CancellationToken ct)
    {
        HashSet<string> columns = new(StringComparer.OrdinalIgnoreCase);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static async Task CreateDocumentsSchemaAsync(SqliteConnection connection, CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS Documents (
                Id TEXT PRIMARY KEY,
                DisplayName TEXT NOT NULL,
                NameKey TEXT NOT NULL,
                StoredFileName TEXT NOT NULL,
                OriginalExtension TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                IsDeleting INTEGER NOT NULL DEFAULT 0,
                DeletingSinceUtc TEXT NULL,
                DeleteOperationId TEXT NULL,
                RequiresIntervention INTEGER NOT NULL DEFAULT 0,
                InterventionReason TEXT NULL,
                InterventionSinceUtc TEXT NULL
            );
            """,
            ct);

        await EnsureIndexesAsync(connection, ct);
    }

    private static async Task EnsureIndexesAsync(SqliteConnection connection, CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            connection,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS UX_Documents_NameKey_Active
            ON Documents(NameKey)
            WHERE IsDeleted = 0;
            """,
            ct);

        await ExecuteNonQueryAsync(
            connection,
            """
            CREATE INDEX IF NOT EXISTS IX_Documents_UpdatedUtc
            ON Documents(UpdatedUtc DESC);
            """,
            ct);

        await ExecuteNonQueryAsync(
            connection,
            """
            CREATE INDEX IF NOT EXISTS IX_Documents_IsDeleting
            ON Documents(IsDeleting, RequiresIntervention, UpdatedUtc DESC);
            """,
            ct);
    }

    private static async Task NormalizeNameKeysAsync(SqliteConnection connection, CancellationToken ct)
    {
        List<(string Id, string NameKey)> updates = new();

        await using (SqliteCommand select = connection.CreateCommand())
        {
            select.CommandText = "SELECT Id, DisplayName, NameKey FROM Documents;";
            await using SqliteDataReader reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                string id = reader.GetString(0);
                string displayName = reader.GetString(1);
                string existingNameKey = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                string normalizedNameKey = BuildNameKey(displayName);

                if (!string.Equals(existingNameKey, normalizedNameKey, StringComparison.Ordinal))
                {
                    updates.Add((id, normalizedNameKey));
                }
            }
        }

        foreach ((string id, string nameKey) in updates)
        {
            await using SqliteCommand update = connection.CreateCommand();
            update.CommandText = "UPDATE Documents SET NameKey = $nameKey WHERE Id = $id;";
            AddTextParameter(update, "$id", id);
            AddTextParameter(update, "$nameKey", nameKey);
            await update.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task MigrateToV3Async(SqliteConnection connection, HashSet<string> existingColumns, CancellationToken ct)
    {
        string deletingProjection = existingColumns.Contains("IsDeleting") ? "IsDeleting" : "0";
        string deletingSinceProjection = existingColumns.Contains("DeletingSinceUtc") ? "DeletingSinceUtc" : "NULL";
        string deleteOperationProjection = existingColumns.Contains("DeleteOperationId") ? "DeleteOperationId" : "NULL";
        string interventionProjection = existingColumns.Contains("RequiresIntervention") ? "RequiresIntervention" : "0";
        string interventionReasonProjection = existingColumns.Contains("InterventionReason") ? "InterventionReason" : "NULL";
        string interventionSinceProjection = existingColumns.Contains("InterventionSinceUtc") ? "InterventionSinceUtc" : "NULL";

        List<MigratedRow> rows = new();

        await using (SqliteCommand select = connection.CreateCommand())
        {
            select.CommandText =
                $"SELECT Id, DisplayName, StoredFileName, OriginalExtension, CreatedUtc, UpdatedUtc, IsDeleted, {deletingProjection}, {deletingSinceProjection}, {deleteOperationProjection}, {interventionProjection}, {interventionReasonProjection}, {interventionSinceProjection} FROM Documents;";

            await using SqliteDataReader reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                string displayName = NormalizeText(reader.GetString(1));
                rows.Add(new MigratedRow
                {
                    Id = NormalizeText(reader.GetString(0)),
                    DisplayName = displayName,
                    NameKey = BuildNameKey(displayName),
                    StoredFileName = NormalizeText(reader.GetString(2)),
                    OriginalExtension = NormalizeText(reader.GetString(3)),
                    CreatedUtc = NormalizeUtc(reader.GetString(4)),
                    UpdatedUtc = NormalizeUtc(reader.GetString(5)),
                    IsDeleted = reader.GetInt32(6) != 0,
                    IsDeleting = reader.GetInt32(7) != 0,
                    DeletingSinceUtc = reader.IsDBNull(8) ? null : NormalizeUtc(reader.GetString(8)),
                    DeleteOperationId = reader.IsDBNull(9) ? null : NormalizeText(reader.GetString(9)),
                    RequiresIntervention = !reader.IsDBNull(10) && reader.GetInt32(10) != 0,
                    InterventionReason = reader.IsDBNull(11) ? null : NormalizeText(reader.GetString(11)),
                    InterventionSinceUtc = reader.IsDBNull(12) ? null : NormalizeUtc(reader.GetString(12)),
                });
            }
        }

        rows.Sort(static (a, b) => string.CompareOrdinal(b.UpdatedUtc, a.UpdatedUtc));
        HashSet<string> seenActiveNameKeys = new(StringComparer.Ordinal);

        foreach (MigratedRow row in rows)
        {
            if (row.IsDeleted)
            {
                row.IsDeleting = false;
                row.DeletingSinceUtc = null;
                row.DeleteOperationId = null;
                row.RequiresIntervention = false;
                row.InterventionReason = null;
                row.InterventionSinceUtc = null;
                continue;
            }

            if (seenActiveNameKeys.Contains(row.NameKey))
            {
                row.IsDeleted = true;
                row.IsDeleting = false;
                row.DeletingSinceUtc = null;
                row.DeleteOperationId = null;
                row.RequiresIntervention = false;
                row.InterventionReason = null;
                row.InterventionSinceUtc = null;
                continue;
            }

            seenActiveNameKeys.Add(row.NameKey);

            if (!row.IsDeleting)
            {
                row.DeletingSinceUtc = null;
                row.DeleteOperationId = null;
                row.RequiresIntervention = false;
                row.InterventionReason = null;
                row.InterventionSinceUtc = null;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(row.DeleteOperationId))
                {
                    row.DeleteOperationId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
                }

                if (!row.RequiresIntervention)
                {
                    row.InterventionReason = null;
                    row.InterventionSinceUtc = null;
                }
                else if (string.IsNullOrWhiteSpace(row.InterventionSinceUtc))
                {
                    row.InterventionSinceUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                }
            }
        }

        await ExecuteNonQueryAsync(connection, "BEGIN IMMEDIATE TRANSACTION;", ct);
        try
        {
            await ExecuteNonQueryAsync(
                connection,
                """
                CREATE TABLE Documents_v3 (
                    Id TEXT PRIMARY KEY,
                    DisplayName TEXT NOT NULL,
                    NameKey TEXT NOT NULL,
                    StoredFileName TEXT NOT NULL,
                    OriginalExtension TEXT NOT NULL,
                    CreatedUtc TEXT NOT NULL,
                    UpdatedUtc TEXT NOT NULL,
                    IsDeleted INTEGER NOT NULL DEFAULT 0,
                    IsDeleting INTEGER NOT NULL DEFAULT 0,
                    DeletingSinceUtc TEXT NULL,
                    DeleteOperationId TEXT NULL,
                    RequiresIntervention INTEGER NOT NULL DEFAULT 0,
                    InterventionReason TEXT NULL,
                    InterventionSinceUtc TEXT NULL
                );
                """,
                ct);

            foreach (MigratedRow row in rows)
            {
                await using SqliteCommand insert = connection.CreateCommand();
                insert.CommandText =
                    """
                    INSERT INTO Documents_v3 (
                        Id,
                        DisplayName,
                        NameKey,
                        StoredFileName,
                        OriginalExtension,
                        CreatedUtc,
                        UpdatedUtc,
                        IsDeleted,
                        IsDeleting,
                        DeletingSinceUtc,
                        DeleteOperationId,
                        RequiresIntervention,
                        InterventionReason,
                        InterventionSinceUtc)
                    VALUES (
                        $id,
                        $displayName,
                        $nameKey,
                        $storedFileName,
                        $originalExtension,
                        $createdUtc,
                        $updatedUtc,
                        $isDeleted,
                        $isDeleting,
                        $deletingSinceUtc,
                        $deleteOperationId,
                        $requiresIntervention,
                        $interventionReason,
                        $interventionSinceUtc);
                    """;

                AddTextParameter(insert, "$id", row.Id);
                AddTextParameter(insert, "$displayName", row.DisplayName);
                AddTextParameter(insert, "$nameKey", row.NameKey);
                AddTextParameter(insert, "$storedFileName", row.StoredFileName);
                AddTextParameter(insert, "$originalExtension", row.OriginalExtension);
                AddTextParameter(insert, "$createdUtc", row.CreatedUtc);
                AddTextParameter(insert, "$updatedUtc", row.UpdatedUtc);
                AddIntegerParameter(insert, "$isDeleted", row.IsDeleted ? 1 : 0);
                AddIntegerParameter(insert, "$isDeleting", row.IsDeleting ? 1 : 0);
                AddNullableTextParameter(insert, "$deletingSinceUtc", row.DeletingSinceUtc);
                AddNullableTextParameter(insert, "$deleteOperationId", row.DeleteOperationId);
                AddIntegerParameter(insert, "$requiresIntervention", row.RequiresIntervention ? 1 : 0);
                AddNullableTextParameter(insert, "$interventionReason", row.InterventionReason);
                AddNullableTextParameter(insert, "$interventionSinceUtc", row.InterventionSinceUtc);

                await insert.ExecuteNonQueryAsync(ct);
            }

            await ExecuteNonQueryAsync(connection, "DROP TABLE Documents;", ct);
            await ExecuteNonQueryAsync(connection, "ALTER TABLE Documents_v3 RENAME TO Documents;", ct);
            await EnsureIndexesAsync(connection, ct);
            await ExecuteNonQueryAsync(connection, "COMMIT;", ct);
        }
        catch
        {
            await ExecuteNonQueryAsync(connection, "ROLLBACK;", ct);
            throw;
        }
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection connection, string sql, CancellationToken ct)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string BuildNameKey(string value)
    {
        return NormalizeText(value);
    }

    private static string NormalizeUtc(string raw)
    {
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
        {
            return parsed.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }

        return DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    }

    private static void AddTextParameter(SqliteCommand command, string name, string value)
    {
        SqliteParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.SqliteType = SqliteType.Text;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static void AddNullableTextParameter(SqliteCommand command, string name, string? value)
    {
        SqliteParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.SqliteType = SqliteType.Text;
        parameter.Value = value is null ? DBNull.Value : value;
        command.Parameters.Add(parameter);
    }

    private static void AddIntegerParameter(SqliteCommand command, string name, int value)
    {
        SqliteParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.SqliteType = SqliteType.Integer;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string NormalizeText(string value)
    {
        return (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);
    }

    private static DocumentEntry ReadDocument(SqliteDataReader reader)
    {
        string id = reader.GetString(0);
        string displayName = reader.GetString(1);
        string storedFileName = reader.GetString(2);
        string originalExtension = reader.GetString(3);
        string createdUtc = reader.GetString(4);
        string updatedUtc = reader.GetString(5);

        return new DocumentEntry(
            Guid.Parse(id),
            displayName,
            storedFileName,
            originalExtension,
            DateTime.Parse(createdUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            DateTime.Parse(updatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    private sealed class MigratedRow
    {
        public required string Id { get; init; }

        public required string DisplayName { get; init; }

        public required string NameKey { get; set; }

        public required string StoredFileName { get; init; }

        public required string OriginalExtension { get; init; }

        public required string CreatedUtc { get; init; }

        public required string UpdatedUtc { get; init; }

        public bool IsDeleted { get; set; }

        public bool IsDeleting { get; set; }

        public string? DeletingSinceUtc { get; set; }

        public string? DeleteOperationId { get; set; }

        public bool RequiresIntervention { get; set; }

        public string? InterventionReason { get; set; }

        public string? InterventionSinceUtc { get; set; }
    }
}
