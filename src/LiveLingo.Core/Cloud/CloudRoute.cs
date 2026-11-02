namespace LiveLingo.Core.Cloud;

/// <summary>
/// A user-configured OpenAI-compatible endpoint (Microsoft Foundry, OpenAI, any compatible service). Only text is
/// ever sent to it, and only for the roles the user switched to the cloud.
/// </summary>
public sealed record CloudRoute
{
    private CloudRoute(Uri endpoint, string model, string apiKey)
    {
        Endpoint = endpoint;
        Model = model;
        ApiKey = apiKey;
    }

    /// <summary>The full chat-completions URL.</summary>
    public Uri Endpoint { get; }

    /// <summary>Model or deployment name.</summary>
    public string Model { get; }

    public string ApiKey { get; }

    /// <param name="baseUrl">For example <c>https://api.openai.com/v1</c>; plain http is accepted only for loopback.</param>
    /// <exception cref="ArgumentException">The URL, model or key is missing or unusable.</exception>
    public static CloudRoute Create(string baseUrl, string model, string apiKey)
    {
        if (!Uri.TryCreate(baseUrl?.Trim().TrimEnd('/'), UriKind.Absolute, out var uri))
            throw new ArgumentException("The base URL must be an absolute URL.", nameof(baseUrl));

        var secure = uri.Scheme == Uri.UriSchemeHttps;
        if (!secure && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            throw new ArgumentException("The base URL must use https (http is allowed only for localhost).", nameof(baseUrl));
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("A model or deployment name is required.", nameof(model));
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("An API key is required.", nameof(apiKey));

        return new CloudRoute(new Uri(uri.ToString().TrimEnd('/') + "/chat/completions"), model.Trim(), apiKey.Trim());
    }

    public override string ToString() => $"CloudRoute {{ Endpoint = {Endpoint}, Model = {Model} }}";
}
