using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TeamLocum.Web.Data;

/// <summary>Lets <c>dotnet ef</c> build the model without starting the web host, so tooling needs no secrets or database.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
