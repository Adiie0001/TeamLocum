namespace TeamLocum.Web.Services.Schema;

public sealed record SchemaDiscoveryResponseDto(
    string Engine,
    string DatabaseName,
    int TotalTables,
    int TotalViews,
    IReadOnlyList<DiscoveredTableDto> Tables,
    DateTimeOffset DiscoveredAt
);

public sealed record DiscoveredTableDto(
    string Name,
    string Type,
    long RowCount,
    IReadOnlyList<DiscoveredColumnDto> Columns,
    IReadOnlyList<DiscoveredForeignKeyDto> ForeignKeys,
    IReadOnlyList<DiscoveredIndexDto> Indexes
);

public sealed record DiscoveredColumnDto(
    int OrdinalPosition,
    string Name,
    string DataType,
    bool IsNullable,
    bool IsPrimaryKey,
    string? DefaultValue
);

public sealed record DiscoveredForeignKeyDto(
    string FromColumn,
    string TargetTable,
    string TargetColumn
);

public sealed record DiscoveredIndexDto(
    string Name,
    bool IsUnique,
    IReadOnlyList<string> Columns
);
