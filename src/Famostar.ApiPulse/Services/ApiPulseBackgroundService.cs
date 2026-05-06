using System.Diagnostics;
using System.Text.Json;
using Famostar.ApiPulse.Models;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;

namespace Famostar.ApiPulse.Services;

/// <summary>
/// Background service that executes HTTP requests from .http files at regular intervals.
/// Manages variable state across requests (e.g., auth tokens).
/// </summary>
public class ApiPulseBackgroundService : BackgroundService
{
    private readonly ILogger<ApiPulseBackgroundService> _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpFileParser _parser;
    private readonly HttpClient _httpClient;
    private readonly TelemetryClient _telemetryClient;
    private readonly Dictionary<string, string> _variables;
    private TimeSpan _interval;
    private string _httpFilePath = string.Empty;

    public ApiPulseBackgroundService(
        ILogger<ApiPulseBackgroundService> logger,
        IConfiguration configuration,
        HttpFileParser parser,
        HttpClient httpClient,
        TelemetryClient telemetryClient)
    {
        _logger = logger;
        _configuration = configuration;
        _parser = parser;
        _httpClient = httpClient;
        _telemetryClient = telemetryClient;
        _variables = new Dictionary<string, string>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ApiPulse Background Service is starting");

        // Load configuration
        _interval = TimeSpan.FromSeconds(_configuration.GetValue("ApiPulse:IntervalSeconds", 60));
        _httpFilePath = _configuration.GetValue<string>("ApiPulse:HttpFilePath") 
            ?? "../api-gateways.http";

        // Load environment variables into the dictionary
        LoadEnvironmentVariables();

        // Initial delay to allow app to fully start
        await Task.Delay(2000, stoppingToken);

        // Execute on schedule
        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await ExecuteHttpRequests(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during HTTP request execution cycle");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("ApiPulse Background Service is stopping");
        }
        finally
        {
            timer.Dispose();
        }
    }

    /// <summary>
    /// Parses and executes all HTTP requests from the configured .http file.
    /// </summary>
    private async Task ExecuteHttpRequests(CancellationToken cancellationToken)
    {
        var requests = _parser.Parse(_httpFilePath, _variables);

        if (requests.Count == 0)
        {
            _logger.LogWarning("No HTTP requests parsed from file: {FilePath}", _httpFilePath);
            return;
        }

        _logger.LogInformation("Starting execution of {Count} HTTP requests at {Timestamp:O}", 
            requests.Count, DateTime.UtcNow);

        foreach (var request in requests)
        {
            try
            {
                await ExecuteSingleRequest(request, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing request: {RequestName}", request.Name);
            }
        }

        _logger.LogInformation("Completed execution cycle at {Timestamp:O}", DateTime.UtcNow);
    }

    /// <summary>
    /// Executes a single HTTP request and extracts variables from the response if applicable.
    /// </summary>
    private async Task ExecuteSingleRequest(HttpRequestModel request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing {Method} {Url} - {Name}", 
            request.Method, request.Url, request.Name);

        var startTime = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var success = false;
        var resultCode = "0";
        var target = "unknown";

        using var httpRequest = new HttpRequestMessage(
            new HttpMethod(request.Method),
            request.Url);

        target = httpRequest.RequestUri?.Host ?? "unknown";

        // Add headers
        foreach (var header in request.Headers)
        {
            // Skip Content-Type and Content-Length as they are set automatically
            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                continue;

            httpRequest.Headers.Add(header.Key, header.Value);
        }

        // Add body
        if (!string.IsNullOrEmpty(request.Body))
        {
            httpRequest.Content = new StringContent(
                request.Body,
                System.Text.Encoding.UTF8,
                request.Headers.ContainsKey("Content-Type") 
                    ? request.Headers["Content-Type"] 
                    : "application/json");
        }

        try
        {
            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            resultCode = ((int)response.StatusCode).ToString();
            success = response.IsSuccessStatusCode;

            _logger.LogInformation("Response: {StatusCode} for {Name}\nBody: {ResponseBody}", 
                response.StatusCode, request.Name, responseBody);

            // Extract variables from response if it's JSON
            if (response.Content.Headers.ContentType?.MediaType == "application/json")
            {
                ExtractVariablesFromResponse(request.Name, responseBody);
            }
        }
        catch (Exception ex)
        {
            resultCode = "EXCEPTION";
            _telemetryClient.TrackException(ex, new Dictionary<string, string>
            {
                ["requestName"] = request.Name,
                ["method"] = request.Method,
                ["url"] = request.Url
            });
            _logger.LogError(ex, "HTTP request failed: {Name}", request.Name);
        }
        finally
        {
            stopwatch.Stop();
            var dependencyTelemetry = new DependencyTelemetry(
                "HTTP",
                target,
                $"{request.Method} {request.Name}",
                request.Url,
                startTime,
                stopwatch.Elapsed,
                resultCode,
                success);

            dependencyTelemetry.Properties["requestName"] = request.Name;
            dependencyTelemetry.Properties["method"] = request.Method;

            _telemetryClient.TrackDependency(dependencyTelemetry);
        }
    }

    /// <summary>
    /// Extracts variables from JSON responses for use in subsequent requests.
    /// Currently supports extracting "token" field from responses.
    /// </summary>
    private void ExtractVariablesFromResponse(string requestName, string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            // Extract token if present
            if (root.TryGetProperty("token", out var tokenElement))
            {
                var token = tokenElement.GetString();
                if (!string.IsNullOrEmpty(token))
                {
                    _variables["auth_token"] = token;
                    _logger.LogInformation("Extracted auth_token from {RequestName}", requestName);
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON response from {RequestName}", requestName);
        }
    }

    /// <summary>
    /// Loads environment variables from configuration into the variables dictionary.
    /// </summary>
    private void LoadEnvironmentVariables()
    {
        var envConfig = _configuration.GetSection("Environment");
        foreach (var child in envConfig.GetChildren())
        {
            _variables[child.Key] = child.Value ?? string.Empty;
        }

        _logger.LogInformation("Loaded {Count} environment variables", _variables.Count);
    }
}
