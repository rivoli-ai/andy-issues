// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

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
/// indexes) the model declares that the live DB lacks — using the exact
/// DDL EnsureCreated would emit via
/// <see cref="DatabaseFacade.GenerateCreateScript"/> — and adds any
/// column the model expects but the table is missing. We never drop,
/// rename, or alter, because those would risk data loss and the
/// migrations system is the right tool for those cases.
/// </summary>
public static class SqliteSchemaBootstrapper
{
    /// <summary>
    /// Heals additive schema drift: first creates any tables the EF
    /// model declares that the live SQLite DB lacks (using
    /// EnsureCreated's own generated DDL), then adds any missing
    /// columns on existing tables. On a completely empty DB (zero user
    /// tables) it no-ops — materialising the full schema there is
    /// EnsureCreated's job. Returns the number of healed objects
    /// (tables + columns).
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
        if (existingTables.Count == 0)
        {
            // Fresh DB — EnsureCreated materialises the full schema;
            // nothing to heal.
            return 0;
        }

        int healed = 0;
        healed += await HealMissingTablesAsync(db, conn, existingTables, logger, cancellationToken);
        healed += await HealMissingColumnsAsync(db, logger, cancellationToken);

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

            var actualColumns = await ReadColumnsAsync(conn, tableName, cancellationToken);
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

                using var cmd = conn.CreateCommand();
                cmd.CommandText = alterSql;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
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
    /// Creates any table (plus its indexes) the EF model declares that
    /// the live DB lacks. The DDL comes from
    /// <see cref="DatabaseFacade.GenerateCreateScript"/> — the exact SQL
    /// EnsureCreated would emit — never hand-written statements. This
    /// closes the blind spot where a NEW entity landed after the DB was
    /// created: EnsureCreated no-ops (some tables exist) and the column
    /// heal skips the table (zero live columns), so every query against
    /// it 500s with "no such table" forever.
    /// </summary>
    private static async Task<int> HealMissingTablesAsync(
        AppDbContext db,
        System.Data.Common.DbConnection conn,
        HashSet<string> existingTables,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Which model tables are absent from the live DB?
        var missing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entityType in db.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (string.IsNullOrEmpty(tableName)) continue;
            if (!IsSafeIdentifier(tableName)) continue;
            if (!existingTables.Contains(tableName)) missing.Add(tableName);
        }
        if (missing.Count == 0) return 0;

        // EnsureCreated's own DDL — statements separated by ";" at line end.
        var script = db.Database.GenerateCreateScript();
        var statements = SplitSqlStatements(script);

        int created = 0;
        foreach (var table in missing)
        {
            // CREATE TABLE first, then its indexes.
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
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = createTable;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            created++;

            foreach (var index in statements.Where(s =>
                         s.StartsWith("CREATE INDEX", StringComparison.Ordinal) ||
                         s.StartsWith("CREATE UNIQUE INDEX", StringComparison.Ordinal)))
            {
                if (!index.Contains($" ON \"{table}\" ", StringComparison.Ordinal)) continue;
                using var cmd = conn.CreateCommand();
                cmd.CommandText = index;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        return created;
    }

    private static async Task<HashSet<string>> ReadTableNamesAsync(
        System.Data.Common.DbConnection conn,
        CancellationToken cancellationToken)
    {
        var tables = new HashSet<string>(StringComparer.Ordinal);
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
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.Ordinal);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{tableName}\");";
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            // PRAGMA table_info columns: cid (0), name (1), type (2), ...
            columns.Add(reader.GetString(1));
        }
        return columns;
    }

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
