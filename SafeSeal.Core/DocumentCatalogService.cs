using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SafeSeal.Core;

public sealed class DocumentCatalogService : IDocumentCatalogService
{
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

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA encoding = 'UTF-8';

            CREATE TABLE IF NOT EXISTS Documents (
                Id TEXT PRIMARY KEY,
                DisplayName TEXT NOT NULL UNIQUE,
                StoredFileName TEXT NOT NULL,
                OriginalExtension TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS IX_Documents_UpdatedUtc ON Documents(UpdatedUtc DESC);
            """;

        await command.ExecuteNonQueryAsync(ct);
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
            WHERE IsDeleted = 0
            ORDER BY UpdatedUtc DESC;
            """;

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadDocument(reader));
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
            WHERE IsDeleted = 0 AND DisplayName = $name
            LIMIT 1;
            """;

        AddTextParameter(command, "$name", NormalizeText(displayName));

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
            INSERT INTO Documents (Id, DisplayName, StoredFileName, OriginalExtension, CreatedUtc, UpdatedUtc, IsDeleted)
            VALUES ($id, $name, $storedFile, $extension, $createdUtc, $updatedUtc, 0)
            ON CONFLICT(Id) DO UPDATE SET
                DisplayName = excluded.DisplayName,
                StoredFileName = excluded.StoredFileName,
                OriginalExtension = excluded.OriginalExtension,
                CreatedUtc = excluded.CreatedUtc,
                UpdatedUtc = excluded.UpdatedUtc,
                IsDeleted = 0;
            """;

        AddTextParameter(command, "$id", entry.Id.ToString("D", CultureInfo.InvariantCulture));
        AddTextParameter(command, "$name", NormalizeText(entry.DisplayName));
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
            bool revived = await TryReviveDeletedNameCollisionAsync(connection, entry, ct);
            if (revived)
            {
                return;
            }

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
                UpdatedUtc = $updatedUtc
            WHERE Id = $id;
            """;

        AddTextParameter(command, "$id", id.ToString("D", CultureInfo.InvariantCulture));
        AddTextParameter(command, "$updatedUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));

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

    private static async Task<bool> TryReviveDeletedNameCollisionAsync(SqliteConnection connection, DocumentEntry entry, CancellationToken ct)
    {
        await using SqliteCommand update = connection.CreateCommand();
        update.CommandText =
            """
            UPDATE Documents
            SET Id = $id,
                StoredFileName = $storedFile,
                OriginalExtension = $extension,
                CreatedUtc = $createdUtc,
                UpdatedUtc = $updatedUtc,
                IsDeleted = 0
            WHERE DisplayName = $name
              AND IsDeleted = 1;
            """;

        AddTextParameter(update, "$id", entry.Id.ToString("D", CultureInfo.InvariantCulture));
        AddTextParameter(update, "$name", NormalizeText(entry.DisplayName));
        AddTextParameter(update, "$storedFile", NormalizeText(entry.StoredFileName));
        AddTextParameter(update, "$extension", NormalizeText(entry.OriginalExtension));
        AddTextParameter(update, "$createdUtc", entry.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
        AddTextParameter(update, "$updatedUtc", entry.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture));

        int rows = await update.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    private static void AddTextParameter(SqliteCommand command, string name, string value)
    {
        SqliteParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.SqliteType = SqliteType.Text;
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
}