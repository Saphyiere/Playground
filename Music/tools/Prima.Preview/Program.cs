// Preview host: Prima's real screens (Prima.UI) in a browser, as Blazor Server, against sample data in a SQLite file, so a cloud
// session can render and screenshot them (see CLAUDE.md). /seed/{scenario} sets up the data.
using Microsoft.Data.Sqlite;
using Prima.Preview.Components;
using Prima.UI;
using Prima.UI.Storage;

var dbPath = Path.Combine(AppContext.BaseDirectory, "preview.db");
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddPrima(dbPath);

var app = builder.Build();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode().AddAdditionalAssemblies(typeof(Routes).Assembly);

// Clears the data, then fills it for a scenario: "empty" is the first run.
app.MapGet("/seed/{scenario}", async (string scenario, PrimaStore store) =>
{
    await store.GetSettingsAsync(); // ensures the schema exists
    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        foreach (var table in new[] { "Setting" }) new SqliteCommand($"DELETE FROM {table}", connection).ExecuteNonQuery();
    }
    return Results.Text(scenario);
});

app.Run();
