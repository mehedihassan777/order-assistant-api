using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OrderAssistant.Api.Data;
using OrderAssistant.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter())); // "Shipped", not 2

builder.Services.AddDbContext<OmsDb>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Oms")));
builder.Services.AddHealthChecks().AddDbContextCheck<OmsDb>();

var app = builder.Build();

// Unhandled exceptions and bare 4xx/5xx become RFC 7807 problem+json responses.
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<OmsDb>();
    await db.Database.MigrateAsync(); // applies migrations on startup
    await Seeder.SeedAsync(db);
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection(); // local dev runs on plain http (the web app calls it server-side)
}

app.MapHealthChecks("/health");
app.MapOrderEndpoints();
app.MapInventoryEndpoints();

app.Run();

// Lets the integration tests reference Program through WebApplicationFactory<Program>.
public partial class Program;
