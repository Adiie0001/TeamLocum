using Microsoft.AspNetCore.Mvc;
using TeamLocum.Web.Services.Schema;

namespace TeamLocum.Web.Controllers;

[ApiController]
[Route("api/schema")]
[Produces("application/json")]
public class SchemaApiController(ISchemaDiscoveryService schemaService) : ControllerBase
{
    /// <summary>
    /// Performs zero-model dynamic schema discovery across the SQLite healthcare database catalog.
    /// Returns reflected tables, columns, data types, primary keys, and foreign-key relationships.
    /// </summary>
    [HttpGet("discovery")]
    public async Task<ActionResult<SchemaDiscoveryResponseDto>> Discover(CancellationToken ct)
    {
        var schema = await schemaService.DiscoverSchemaAsync(ct);
        return Ok(schema);
    }

    /// <summary>
    /// Returns dynamic metadata, column types, and live row count for a specific table.
    /// </summary>
    [HttpGet("tables/{tableName}")]
    public async Task<ActionResult<DiscoveredTableDto>> GetTable(string tableName, CancellationToken ct)
    {
        var table = await schemaService.GetTableMetadataAsync(tableName, ct);
        if (table == null)
        {
            return NotFound(new { error = $"Table '{tableName}' not found in database catalog." });
        }
        return Ok(table);
    }
}
