namespace LiveLingo.HfGguf;

public sealed class HfHubException : Exception
{
    public HfHubException(string message, int? statusCode, string? requestUrl, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        RequestUrl = requestUrl;
    }

    public int? StatusCode { get; }
    public string? RequestUrl { get; }
}
