using System.Net;
using System.Net.Http.Json;
using TeamLocum.Tests.TestSupport;
using TeamLocum.Web.Services.Schema;

namespace TeamLocum.Tests;

public sealed class SchemaDiscoveryTests(AppFactory factory) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task DiscoverSchema_api_returns_200_with_all_healthcare_tables_and_columns()
    {
        var client = factory.NewClient();

        var response = await client.GetAsync("/api/schema/discovery");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var schema = await response.Content.ReadFromJsonAsync<SchemaDiscoveryResponseDto>();

        Assert.NotNull(schema);
        Assert.Equal("SQLite", schema.Engine);
        Assert.True(schema.TotalTables >= 4, $"Expected at least 4 tables, found {schema.TotalTables}");
        Assert.NotEmpty(schema.Tables);

        var tableNames = schema.Tables.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Bookings", tableNames);
        Assert.Contains("Clients", tableNames);
        Assert.Contains("Locums", tableNames);
        Assert.Contains("Holidays", tableNames);

        var bookingsTable = schema.Tables.First(t => string.Equals(t.Name, "Bookings", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("TABLE", bookingsTable.Type);
        Assert.NotEmpty(bookingsTable.Columns);
        Assert.NotEmpty(bookingsTable.ForeignKeys);

        var idCol = bookingsTable.Columns.FirstOrDefault(c => string.Equals(c.Name, "Id", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(idCol);
        Assert.True(idCol.IsPrimaryKey);
    }

    [Fact]
    public async Task GetTable_api_returns_200_for_existing_healthcare_table()
    {
        var client = factory.NewClient();

        var response = await client.GetAsync("/api/schema/tables/Bookings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var table = await response.Content.ReadFromJsonAsync<DiscoveredTableDto>();

        Assert.NotNull(table);
        Assert.Equal("Bookings", table.Name);
        Assert.Equal("TABLE", table.Type);
        Assert.NotEmpty(table.Columns);
        Assert.True(table.RowCount >= 0);
    }

    [Fact]
    public async Task Admin_schema_page_renders_html_for_authenticated_admin()
    {
        var admin = await factory.AdminAsync();

        var response = await admin.GetAsync("/Admin/Schema");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Database Schema Inspector", html);
        Assert.Contains("Bookings", html);
        Assert.Contains("Clients", html);
    }
}
