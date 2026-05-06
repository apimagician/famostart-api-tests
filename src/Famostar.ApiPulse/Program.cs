using Famostar.ApiPulse.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add services
builder.Services.AddHttpClient();
builder.Services.AddSingleton<HttpFileParser>();
builder.Services.AddHostedService<ApiPulseBackgroundService>();
builder.Services.AddApplicationInsightsTelemetry();

// Configure HTTP client with reasonable timeouts
builder.Services.ConfigureHttpClientDefaults(http =>
{
    http.ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    });
});

var app = builder.Build();

app.Logger.LogInformation("Application starting with configuration:");
app.Logger.LogInformation("- HttpFilePath: {HttpFilePath}", 
    app.Configuration.GetValue<string>("ApiPulse:HttpFilePath") ?? "../api-gateways.http");
app.Logger.LogInformation("- IntervalSeconds: {IntervalSeconds}", 
    app.Configuration.GetValue<int>("ApiPulse:IntervalSeconds", 60));
app.Logger.LogInformation("- ApplicationInsightsConnectionStringConfigured: {IsConfigured}",
    !string.IsNullOrWhiteSpace(app.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]) ||
    !string.IsNullOrWhiteSpace(app.Configuration["ApplicationInsights:ConnectionString"]));

// Simple health check endpoint for Azure App Service
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

app.Run();
