using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using TeamLocum.Web.Data;

namespace TeamLocum.Web.Services.Schema;

public sealed class DynamicSchemaDiscoveryService(
    ApplicationDbContext db,
    ILogger<DynamicSchemaDiscoveryService> logger) : ISchemaDiscoveryService
{
    public async Task<SchemaDiscoveryResponseDto> DiscoverSchemaAsync(CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var tables = new List<DiscoveredTableDto>();
        var databaseName = connection.Database;
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            databaseName = "teamlocum.db";
        }

        const string queryTables = @"
            SELECT name, type 
            FROM sqlite_master 
            WHERE type IN ('table', 'view') 
              AND name NOT LIKE 'sqlite_%' 
            ORDER BY name;";

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = queryTables;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            var tableEntries = new List<(string Name, string Type)>();
            while (await reader.ReadAsync(cancellationToken))
            {
                tableEntries.Add((reader.GetString(0), reader.GetString(1).ToUpperInvariant()));
            }

            foreach (var (tableName, tableType) in tableEntries)
            {
                var tableMeta = await InspectTableAsync(connection, tableName, tableType, cancellationToken);
                tables.Add(tableMeta);
            }
        }

        int totalTables = tables.Count(t => t.Type == "TABLE");
        int totalViews = tables.Count(t => t.Type == "VIEW");

        logger.LogInformation("Discovered {Count} tables and views in database {Db}", tables.Count, databaseName);

        return new SchemaDiscoveryResponseDto(
            Engine: "SQLite",
            DatabaseName: databaseName,
            TotalTables: totalTables,
            TotalViews: totalViews,
            Tables: tables,
            DiscoveredAt: DateTimeOffset.UtcNow
        );
    }

    public async Task<DiscoveredTableDto?> GetTableMetadataAsync(string tableName, CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        const string checkSql = "SELECT type FROM sqlite_master WHERE name = @name AND type IN ('table', 'view');";
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = checkSql;
        var p = cmd.CreateParameter();
        p.ParameterName = "@name";
        p.Value = tableName;
        cmd.Parameters.Add(p);

        var typeObj = await cmd.ExecuteScalarAsync(cancellationToken);
        if (typeObj == null || typeObj == DBNull.Value)
        {
            return null;
        }

        return await InspectTableAsync(connection, tableName, typeObj.ToString()!.ToUpperInvariant(), cancellationToken);
    }

    private static async Task<DiscoveredTableDto> InspectTableAsync(
        DbConnection connection,
        string tableName,
        string tableType,
        CancellationToken ct)
    {
        var columns = new List<DiscoveredColumnDto>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA table_info(\"{tableName}\");";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var cid = reader.GetInt32(0);
                var colName = reader.GetString(1);
                var colType = reader.IsDBNull(2) ? "TEXT" : reader.GetString(2);
                var notNull = reader.GetInt32(3) == 1;
                var defaultValue = reader.IsDBNull(4) ? null : reader.GetString(4);
                var isPk = reader.GetInt32(5) >= 1;

                columns.Add(new DiscoveredColumnDto(
                    OrdinalPosition: cid,
                    Name: colName,
                    DataType: colType,
                    IsNullable: !notNull,
                    IsPrimaryKey: isPk,
                    DefaultValue: defaultValue
                ));
            }
        }

        var foreignKeys = new List<DiscoveredForeignKeyDto>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA foreign_key_list(\"{tableName}\");";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var targetTable = reader.GetString(2);
                var fromCol = reader.GetString(3);
                var targetCol = reader.GetString(4);

                foreignKeys.Add(new DiscoveredForeignKeyDto(
                    FromColumn: fromCol,
                    TargetTable: targetTable,
                    TargetColumn: targetCol
                ));
            }
        }

        var indexes = new List<DiscoveredIndexDto>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA index_list(\"{tableName}\");";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var indexEntries = new List<(string Name, bool Unique)>();
            while (await reader.ReadAsync(ct))
            {
                var indexName = reader.GetString(1);
                var isUnique = reader.GetInt32(2) == 1;
                indexEntries.Add((indexName, isUnique));
            }

            foreach (var (idxName, isUnique) in indexEntries)
            {
                var idxCols = new List<string>();
                await using var idxCmd = connection.CreateCommand();
                idxCmd.CommandText = $"PRAGMA index_info(\"{idxName}\");";
                await using var idxReader = await idxCmd.ExecuteReaderAsync(ct);
                while (await idxReader.ReadAsync(ct))
                {
                    if (!idxReader.IsDBNull(2))
                    {
                        idxCols.Add(idxReader.GetString(2));
                    }
                }

                indexes.Add(new DiscoveredIndexDto(
                    Name: idxName,
                    IsUnique: isUnique,
                    Columns: idxCols
                ));
            }
        }

        long rowCount = 0;
        if (tableType == "TABLE")
        {
            await using var countCmd = connection.CreateCommand();
            countCmd.CommandText = $"SELECT COUNT(*) FROM \"{tableName}\";";
            var countResult = await countCmd.ExecuteScalarAsync(ct);
            if (countResult != null && countResult != DBNull.Value)
            {
                rowCount = Convert.ToInt64(countResult);
            }
        }

        return new DiscoveredTableDto(
            Name: tableName,
            Type: tableType,
            RowCount: rowCount,
            Columns: columns,
            ForeignKeys: foreignKeys,
            Indexes: indexes
        );
    }
}
