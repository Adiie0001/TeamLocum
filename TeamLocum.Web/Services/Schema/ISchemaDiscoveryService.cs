namespace TeamLocum.Web.Services.Schema;

public interface ISchemaDiscoveryService
{
    Task<SchemaDiscoveryResponseDto> DiscoverSchemaAsync(CancellationToken cancellationToken = default);
    Task<DiscoveredTableDto?> GetTableMetadataAsync(string tableName, CancellationToken cancellationToken = default);
}
