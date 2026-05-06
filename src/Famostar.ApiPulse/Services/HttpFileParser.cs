using System.Text.Json;
using System.Text.RegularExpressions;
using Famostar.ApiPulse.Models;

namespace Famostar.ApiPulse.Services;

/// <summary>
/// Parses .http files and extracts HTTP requests.
/// Supports basic HTTP request format with headers and JSON bodies.
/// </summary>
public class HttpFileParser
{
    private readonly ILogger<HttpFileParser> _logger;

    public HttpFileParser(ILogger<HttpFileParser> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parses an HTTP file and returns a list of HTTP requests.
    /// </summary>
    public List<HttpRequestModel> Parse(string filePath, Dictionary<string, string> variables)
    {
        var requests = new List<HttpRequestModel>();

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("HTTP file not found: {FilePath}", filePath);
            return requests;
        }

        try
        {
            var content = File.ReadAllText(filePath);
            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            HttpRequestModel currentRequest = null;
            var bodyLines = new List<string>();
            var inBody = false;
            var requestOrder = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // Skip empty lines and comments (except ### which separates requests)
                if (string.IsNullOrWhiteSpace(line) || (line.StartsWith("#") && !line.StartsWith("###")))
                    continue;

                // Request separator
                if (line.StartsWith("###"))
                {
                    // Save previous request
                    if (currentRequest != null)
                    {
                        if (bodyLines.Count > 0)
                        {
                            currentRequest.Body = string.Join("\r\n", bodyLines).Trim();
                            bodyLines.Clear();
                        }
                        requests.Add(currentRequest);
                        inBody = false;
                    }

                    // Check if there's a name after ###
                    var namePart = line.Substring(3).Trim();
                    currentRequest = new HttpRequestModel { Order = requestOrder++, Name = namePart };
                    continue;
                }

                // Skip if we haven't started a request yet
                if (currentRequest == null)
                    continue;

                // Parse request line (METHOD URL)
                var requestLineMatch = Regex.Match(line, @"^(GET|POST|PUT|DELETE|PATCH|HEAD|OPTIONS)\s+(.+)$", RegexOptions.IgnoreCase);
                if (requestLineMatch.Success && !inBody)
                {
                    currentRequest.Method = requestLineMatch.Groups[1].Value.ToUpper();
                    currentRequest.Url = ReplaceVariables(requestLineMatch.Groups[2].Value.Trim(), variables);
                    continue;
                }

                // Parse headers (until empty line)
                if (!inBody && !string.IsNullOrWhiteSpace(line) && line.Contains(":"))
                {
                    var headerParts = line.Split(new[] { ":" }, 2, StringSplitOptions.None);
                    if (headerParts.Length == 2)
                    {
                        var headerName = headerParts[0].Trim();
                        var headerValue = ReplaceVariables(headerParts[1].Trim(), variables);
                        currentRequest.Headers[headerName] = headerValue;
                    }
                    continue;
                }

                // Empty line signals start of body
                if (string.IsNullOrWhiteSpace(line) && !inBody && currentRequest.Method != null)
                {
                    inBody = true;
                    continue;
                }

                // Collect body lines
                if (inBody)
                {
                    bodyLines.Add(line);
                }
            }

            // Add last request if exists
            if (currentRequest != null)
            {
                if (bodyLines.Count > 0)
                {
                    currentRequest.Body = string.Join("\r\n", bodyLines).Trim();
                }
                requests.Add(currentRequest);
            }

            _logger.LogInformation("Parsed {Count} HTTP requests from {FilePath}", requests.Count, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing HTTP file: {FilePath}", filePath);
        }

        return requests;
    }

    /// <summary>
    /// Replaces {{variableName}} placeholders with values from the variables dictionary.
    /// </summary>
    private string ReplaceVariables(string input, Dictionary<string, string> variables)
    {
        if (string.IsNullOrEmpty(input) || variables == null || variables.Count == 0)
            return input;

        var result = input;
        foreach (var kvp in variables)
        {
            result = result.Replace($"{{{{{kvp.Key}}}}}", kvp.Value);
        }

        return result;
    }
}
