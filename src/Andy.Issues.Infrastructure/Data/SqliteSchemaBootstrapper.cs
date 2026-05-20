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
/// and adds any column the model expects but the table is missing. Only
/// additive, nullable column adds are performed — we never drop, rename,
/// or alter, because those would risk data loss and the migrations
/// system is the right tool for those cases.
/// </summary>
public static class SqliteSchemaBootstrapper
{
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
                // value. Anything else needs human attention.
                var isNullable = property.IsColumnNullable();
                var defaultSql = property.GetDefaultValueSql(storeObject);
                var defaultValue = property.GetDefaultValue(storeObject);
                if (!isNullable && defaultSql is null && defaultValue is null)
                {
                    logger.LogWarning(
                        "andy-issues SQLite schema heal: refusing to add non-nullable column {Table}.{Column} without default.",
                        tableName,
                        columnName);
                    continue;
                }

                var columnType = ResolveSqliteColumnType(property);
                var nullClause = isNullable ? "NULL" : "NOT NULL";
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
