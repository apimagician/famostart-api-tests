namespace Famostar.ApiPulse.Models;

/// <summary>
/// Represents a parsed HTTP request from .http file
/// </summary>
public class HttpRequestModel
{
    public string Name { get; set; }
    public string Method { get; set; }
    public string Url { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public string Body { get; set; }
    public int Order { get; set; }
    
    /// <summary>
    /// Stores extracted variables from responses for use in subsequent requests.
    /// Key: variable name, Value: variable value
    /// </summary>
    public Dictionary<string, string> Variables { get; set; } = new();
}
