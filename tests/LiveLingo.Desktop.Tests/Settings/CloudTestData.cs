using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.Tests.Settings;

/// <summary>Settings with saved cloud endpoints, for the tests that need somewhere to send text to.</summary>
internal static class CloudTestData
{
    public const string MainUrl = "https://api.example.com/v1";
    public const string OtherUrl = "https://other.example.com/v1";

    public static string SecretId(string id) => "cloud-key-" + id;

    public static CloudEndpoint Endpoint(string id, string name, string url, string model) =>
        new() { Id = id, Name = name, BaseUrl = url, Model = model, SecretId = SecretId(id) };

    /// <summary>The endpoints "main" and "other"; <paramref name="active"/> is the id of the enabled one (null: none).</summary>
    public static SettingsModel WithEndpoints(string? active = "main", bool translate = false, bool clean = false)
    {
        var model = new SettingsModel();
        model.Cloud.Endpoints = [Endpoint("main", "Main", MainUrl, "m"), Endpoint("other", "Other", OtherUrl, "o")];
        model.Cloud.ActiveEndpointId = active ?? "";
        model.Cloud.Translate = translate;
        model.Cloud.Clean = clean;
        return model;
    }
}
