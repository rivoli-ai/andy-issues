// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Andy.Issues.Infrastructure.Data;

/// <summary>
/// Heals additive schema drift on the embedded SQLite database.
///
/// Background: andy-issues' SQLite path uses
/// <see cref="DatabaseFacade.EnsureCreatedAsync"/>, which creates the
/// schema from the live model snapshot on a fresh DB but is a no-op
/// once any table exists. Migrations on the SQLite path are not
/// expected to run (most are written with Postgres-specific SQL), so a
/// user whose DB was created by an older binary is stuck on whatever
/// schema EnsureCreated produced back then. When a new entity property
/// lands (e.g. <c>UserStory.ContentHash</c>), every query that selects
/// it 500s with <c>SQLite Error 1: 'no such column: u.ContentHash'</c>.
///
/// This bootstrapper closes that gap. After
/// <see cref="DatabaseFacade.EnsureCreatedAsync"/> has had a chance to
/// run, it compares the live EF model against the actual SQLite schema
/// and heals ADDITIVE drift only: it creates any entire table (plus its
/// indexes and model seed rows) the model declares that the live DB
/// lacks, restores missing indexes on existing tables, adds missing
/// columns, and reconciles the stateful backlog sequence counters. Table,
/// index, and seed SQL comes from the exact script EnsureCreated would
/// emit via <see cref="DatabaseFacade.GenerateCreateScript"/>. We never
/// drop, rename, or destructively alter existing objects, because those
/// would risk data loss and the migrations system is the right tool for
/// those cases.
/// </summary>
public static class SqliteSchemaBootstrapper
{
    /// <summary>
    /// Heals additive schema drift: creates missing tables with their
    /// model seed rows, restores missing indexes, adds missing columns,
    /// and reconciles stateful backlog sequence counters. On a truly
    /// empty DB it no-ops — materialising the full schema there is
    /// EnsureCreated's job. Returns the number of healed objects or
    /// state rows.
    /// </summary>
    public static async Task<int> HealAsync(
        AppDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsSqlite()) return 0;

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken);
        }

        var existingTables = await ReadTableNamesAsync(conn, cancellationToken);
        if (existingTables.Count == 0 &&
            !await HasAnyTablesAsync(conn, cancellationToken))
        {
            // Truly fresh DB — EnsureCreated materialises the full
            // schema. A DB containing only __EFMigrationsHistory is
            // deliberately NOT considered fresh: EnsureCreated sees
            // that table and no-ops, so the healer must create the
            // application schema.
            return 0;
        }

        // Keep the combined heal atomic. In particular, never commit a
        // newly-created table without its indexes or model seed rows;
        // otherwise a failed startup would see the table on retry and
        // could silently leave uniqueness constraints absent.
        var ownedTransaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        int healed;
        try
        {
            healed = 0;
            healed += await HealMissingTablesAndIndexesAsync(
                db,
                conn,
                existingTables,
                logger,
                cancellationToken);
            healed += await HealMissingColumnsAsync(db, logger, cancellationToken);
            healed += await ReconcileBacklogSequencesAsync(
                db,
                conn,
                logger,
                cancellationToken);

            if (ownedTransaction is not null)
            {
                await ownedTransaction.CommitAsync(cancellationToken);
            }
        }
        finally
        {
            if (ownedTransaction is not null)
            {
                await ownedTransaction.DisposeAsync();
            }
        }

        if (healed > 0)
        {
            logger.LogWarning(
                "andy-issues SQLite schema heal complete: {Healed} object(s) added.",
                healed);
        }

        return healed;
    }

    /// <summary>
    /// Inspects the SQLite database and adds any column declared by the
    /// EF model that doesn't yet exist in the live schema. No-ops on
    /// non-SQLite providers and on tables that haven't been created yet
    /// (a fresh DB on which EnsureCreated hasn't run is left alone).
    /// </summary>
    public static async Task<int> HealMissingColumnsAsync(
        AppDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsSqlite()) return 0;

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken);
        }

        int healed = 0;

        foreach (var entityType in db.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (string.IsNullOrEmpty(tableName)) continue;
            if (!IsSafeIdentifier(tableName)) continue;

            var actualColumns = await ReadColumnsAsync(
                conn,
                tableName,
                db.Database.CurrentTransaction?.GetDbTransaction(),
                cancellationToken);
            if (actualColumns.Count == 0)
            {
                // Table not created yet — fresh DB, or a table that
                // EnsureCreated will create on first run. Leave alone.
                continue;
            }

            var storeObject = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());

            foreach (var property in entityType.GetProperties())
            {
                var columnName = property.GetColumnName(storeObject);
                if (string.IsNullOrEmpty(columnName)) continue;
                if (actualColumns.Contains(columnName)) continue;
                if (!IsSafeIdentifier(columnName)) continue;

                // Only auto-add columns that are safe to add to a
                // populated table: must be nullable OR have a default
                // value OR be backed by a value converter that
                // gracefully handles missing data on read (e.g. the
                // List<string> JSON converters that materialise null →
                // empty list). Anything else needs human attention.
                var isNullable = property.IsColumnNullable();
                var defaultSql = property.GetDefaultValueSql(storeObject);
                var defaultValue = property.GetDefaultValue(storeObject);
                var hasValueConverter = property.GetValueConverter() is not null;
                if (!isNullable && defaultSql is null && defaultValue is null && !hasValueConverter)
                {
                    logger.LogWarning(
                        "andy-issues SQLite schema heal: refusing to add non-nullable column {Table}.{Column} without default.",
                        tableName,
                        columnName);
                    continue;
                }

                var columnType = ResolveSqliteColumnType(property);
                // Converter-backed non-nullable properties (e.g. JSON
                // list serialisation) get added as nullable on the
                // SQLite path — the converter handles null → safe
                // default at read time. Plain non-nullable columns
                // keep their NOT NULL + default.
                var addAsNullable = isNullable || (hasValueConverter && defaultSql is null && defaultValue is null);
                var nullClause = addAsNullable ? "NULL" : "NOT NULL";
                var defaultClause = defaultSql is not null
                    ? $" DEFAULT ({defaultSql})"
                    : (defaultValue is not null ? $" DEFAULT {FormatDefaultLiteral(defaultValue)}" : "");

                var alterSql = $"ALTER TABLE \"{tableName}\" ADD COLUMN \"{columnName}\" {columnType} {nullClause}{defaultClause};";

                logger.LogWarning(
                    "andy-issues SQLite schema heal: adding missing column {Table}.{Column} (\"{Sql}\").",
                    tableName,
                    columnName,
                    alterSql);

                await db.Database.ExecuteSqlRawAsync(alterSql, cancellationToken);
                healed++;
            }
        }

        if (healed > 0)
        {
            logger.LogWarning(
                "andy-issues SQLite schema heal complete: {Healed} column(s) added.",
                healed);
        }

        return healed;
    }

    /// <summary>
    /// Creates any table (plus its model seed rows) the EF model declares
    /// that the live DB lacks and restores missing model indexes on all
    /// existing tables. The SQL comes from
    /// <see cref="DatabaseFacade.GenerateCreateScript"/> — the exact SQL
    /// EnsureCreated would emit — never hand-written statements. This
    /// closes the blind spot where a NEW entity landed after the DB was
    /// created: EnsureCreated no-ops (some tables exist) and the column
    /// heal skips the table (zero live columns), so every query against
    /// it 500s with "no such table" forever.
    /// </summary>
    private static async Task<int> HealMissingTablesAndIndexesAsync(
        AppDbContext db,
        System.Data.Common.DbConnection conn,
        HashSet<string> existingTables,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Which model tables are absent from the live DB?
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entityType in db.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (string.IsNullOrEmpty(tableName)) continue;
            if (!IsSafeIdentifier(tableName)) continue;
            if (!existingTables.Contains(tableName)) missing.Add(tableName);
        }
        // EnsureCreated's own DDL — statements separated by ";" at line end.
        var script = db.Database.GenerateCreateScript();
        var statements = SplitSqlStatements(script);

        int healed = 0;
        var createdTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Create every missing table before inserting seed data so
        // foreign-keyed seed rows can rely on all model tables existing.
        foreach (var table in missing)
        {
            var createTable = statements.FirstOrDefault(s =>
                s.StartsWith($"CREATE TABLE \"{table}\"", StringComparison.Ordinal));
            if (createTable is null)
            {
                logger.LogWarning(
                    "andy-issues SQLite schema heal: model table {Table} missing from DB but no CREATE TABLE found in generated script; skipping.",
                    table);
                continue;
            }

            logger.LogWarning(
                "andy-issues SQLite schema heal: creating missing table {Table}.",
                table);
            await db.Database.ExecuteSqlRawAsync(createTable, cancellationToken);
            createdTables.Add(table);
            healed++;
        }

        // GenerateCreateScript includes HasData inserts. Replay only
        // those targeting tables created above; running them against
        // existing tables could duplicate or overwrite user data.
        foreach (var statement in statements)
        {
            if (!TryGetInsertTarget(statement, out var table) ||
                !createdTables.Contains(table))
            {
                continue;
            }

            await db.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        // Index healing is deliberately independent of table healing.
        // This makes a retry repair an index missed by an interrupted
        // older run where the table itself already exists.
        var existingIndexes = await ReadIndexesAsync(
            conn,
            db.Database.CurrentTransaction?.GetDbTransaction(),
            cancellationToken);
        foreach (var statement in statements)
        {
            if (!TryGetCreateIndexTarget(
                    statement,
                    out var indexName,
                    out var tableName) ||
                (!existingTables.Contains(tableName) &&
                 !createdTables.Contains(tableName)) ||
                existingIndexes.Contains(IndexKey(tableName, indexName)))
            {
                continue;
            }

            logger.LogWarning(
                "andy-issues SQLite schema heal: creating missing index {Index} on {Table}.",
                indexName,
                tableName);
            await db.Database.ExecuteSqlRawAsync(statement, cancellationToken);
            existingIndexes.Add(IndexKey(tableName, indexName));
            healed++;
        }

        return healed;
    }

    private static async Task<HashSet<string>> ReadTableNamesAsync(
        System.Data.Common.DbConnection conn,
        CancellationToken cancellationToken)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsHistory';";
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(reader.GetString(0));
        }
        return tables;
    }

    private static async Task<bool> HasAnyTablesAsync(
        System.Data.Common.DbConnection conn,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type='table' AND rootpage IS NOT NULL);";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) != 0;
    }

    private static async Task<HashSet<string>> ReadIndexesAsync(
        System.Data.Common.DbConnection conn,
        System.Data.Common.DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var indexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText =
            "SELECT tbl_name, name FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%';";
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            indexes.Add(IndexKey(reader.GetString(0), reader.GetString(1)));
        }
        return indexes;
    }

    private static List<string> SplitSqlStatements(string script)
    {
        // EF's SQLite create script separates statements with ";" followed
        // by a newline. No procedural blocks exist on SQLite, so this split
        // is unambiguous.
        return script
            .Split([";\r\n", ";\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }

    private static async Task<HashSet<string>> ReadColumnsAsync(
        System.Data.Common.DbConnection conn,
        string tableName,
        System.Data.Common.DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"PRAGMA table_info(\"{tableName}\");";
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            // PRAGMA table_info columns: cid (0), name (1), type (2), ...
            columns.Add(reader.GetString(1));
        }
        return columns;
    }

    private static async Task<int> ReconcileBacklogSequencesAsync(
        AppDbContext db,
        System.Data.Common.DbConnection conn,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        var sequenceColumns = await ReadColumnsAsync(
            conn,
            "backlog_sequences",
            transaction,
            cancellationToken);
        if (!sequenceColumns.Contains("entity_type") ||
            !sequenceColumns.Contains("next_seq"))
        {
            return 0;
        }

        var sources = new (int EntityType, string TableName)[]
        {
            ((int)BacklogEntityType.Epic, "Epics"),
            ((int)BacklogEntityType.Feature, "Features"),
            ((int)BacklogEntityType.Story, "UserStories"),
            ((int)BacklogEntityType.Issue, "Issues"),
        };

        int healed = 0;
        foreach (var source in sources)
        {
            var sourceColumns = await ReadColumnsAsync(
                conn,
                source.TableName,
                transaction,
                cancellationToken);
            if (!sourceColumns.Contains("Seq"))
            {
                logger.LogWarning(
                    "andy-issues SQLite schema heal: cannot reconcile backlog sequence {EntityType}; {Table}.Seq is missing.",
                    source.EntityType,
                    source.TableName);
                continue;
            }

            var sql = $"""
                INSERT INTO "backlog_sequences" ("entity_type", "next_seq")
                SELECT {source.EntityType}, COALESCE(MAX("Seq"), 0) + 1 FROM "{source.TableName}"
                WHERE true
                ON CONFLICT("entity_type") DO UPDATE SET "next_seq" = excluded."next_seq"
                WHERE "backlog_sequences"."next_seq" < excluded."next_seq";
                """;
            healed += await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }

        if (healed > 0)
        {
            logger.LogWarning(
                "andy-issues SQLite schema heal: reconciled {Count} backlog sequence row(s).",
                healed);
        }

        return healed;
    }

    private static bool TryGetInsertTarget(string statement, out string tableName)
    {
        const string prefix = "INSERT INTO ";
        tableName = string.Empty;
        return statement.StartsWith(prefix, StringComparison.Ordinal) &&
               TryReadQuotedIdentifier(statement, prefix.Length, out tableName, out _);
    }

    private static bool TryGetCreateIndexTarget(
        string statement,
        out string indexName,
        out string tableName)
    {
        const string createIndex = "CREATE INDEX ";
        const string createUniqueIndex = "CREATE UNIQUE INDEX ";

        indexName = string.Empty;
        tableName = string.Empty;
        int offset;
        if (statement.StartsWith(createUniqueIndex, StringComparison.Ordinal))
        {
            offset = createUniqueIndex.Length;
        }
        else if (statement.StartsWith(createIndex, StringComparison.Ordinal))
        {
            offset = createIndex.Length;
        }
        else
        {
            return false;
        }

        if (!TryReadQuotedIdentifier(statement, offset, out indexName, out offset) ||
            !statement.AsSpan(offset).StartsWith(" ON ", StringComparison.Ordinal))
        {
            return false;
        }

        offset += " ON ".Length;
        return TryReadQuotedIdentifier(statement, offset, out tableName, out _);
    }

    private static bool TryReadQuotedIdentifier(
        string statement,
        int offset,
        out string identifier,
        out int nextOffset)
    {
        identifier = string.Empty;
        nextOffset = offset;
        if (offset >= statement.Length || statement[offset] != '"')
        {
            return false;
        }

        var value = new StringBuilder();
        for (int i = offset + 1; i < statement.Length; i++)
        {
            if (statement[i] != '"')
            {
                value.Append(statement[i]);
                continue;
            }

            if (i + 1 < statement.Length && statement[i + 1] == '"')
            {
                value.Append('"');
                i++;
                continue;
            }

            identifier = value.ToString();
            nextOffset = i + 1;
            return true;
        }

        return false;
    }

    private static string IndexKey(string tableName, string indexName) =>
        $"{tableName}\u001f{indexName}";

    private static string ResolveSqliteColumnType(IProperty property)
    {
        // Prefer the explicit relational type mapping (what EF would
        // emit when creating the schema). Falls back to TEXT as the
        // last-resort SQLite-affinity-safe default.
        var typeMapping = property.GetRelationalTypeMapping();
        var storeType = typeMapping?.StoreType;
        if (!string.IsNullOrEmpty(storeType)) return storeType;

        var configured = property.GetColumnType();
        return !string.IsNullOrEmpty(configured) ? configured : "TEXT";
    }

    private static string FormatDefaultLiteral(object value)
    {
        return value switch
        {
            bool b => b ? "1" : "0",
            string s => $"'{s.Replace("'", "''")}'",
            null => "NULL",
            _ => value.ToString() ?? "NULL"
        };
    }

    private static bool IsSafeIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier)) return false;
        foreach (var c in identifier)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
        }
        return true;
    }
}
