using TeamLocum.Web.Infrastructure.Startup;

var builder = WebApplication.CreateBuilder(args);
builder.AddTeamLocum();

var app = builder.Build();
app.UseTeamLocum();
await app.InitializeAsync();
await app.RunAsync();

// Makes the entry point visible to WebApplicationFactory in the integration tests.
public partial class Program;
