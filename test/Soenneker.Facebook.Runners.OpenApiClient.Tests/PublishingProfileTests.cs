using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Soenneker.OpenApi.Converters.Meta.Models;
using Soenneker.OpenApi.Converters.Meta;
using Soenneker.Facebook.Runners.OpenApiClient.Profiles;

namespace Soenneker.Facebook.Runners.OpenApiClient.Tests;

public sealed class PublishingProfileTests
{
    [Test]
    public void FacebookProfileOwnsFilteringPayloadsPathsAndMetadata()
    {
        var input = Facebook();
        var original = new Dictionary<string, string>(input);
        var result = Convert(input, Options());
        var document = result.Document;
        var paths = document["paths"]!.AsObject();
        Check(paths.ContainsKey("/{node-id}/feed") && !paths.ContainsKey("/{node-id}/ads"), "Publishing paths only");
        Check(!paths.ContainsKey("/{id}"), "Kiota-compatible paths");
        var schema = paths["/{node-id}/feed"]!["post"]!["requestBody"]!["content"]!["application/x-www-form-urlencoded"]!["schema"]!;
        Check(schema["properties"]!["thumbnail"] is null, "Unrelated file parameter excluded");
        Check(schema["properties"]!["attached_media"]!["contentSchema"]!["type"]!.GetValue<string>() == "array", "JSON form payload preserved");
        Check(schema["properties"]!["scheduled_publish_time"]!["type"]!.GetValue<string>() == "integer", "Scheduled time corrected");
        Check(document["components"]!["schemas"]!["Unrelated"] is null, "Unused models pruned");
        Check(paths["/{node-id}"]!["get"]!["responses"]!["200"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>() == "#/components/schemas/PublishingNode", "Shared read response");
        Check(document["x-meta-source"]!["revision"]!.GetValue<string>() == "test-revision", "Source revision");
        Check(input.All(entry => original[entry.Key] == entry.Value) && input.Count == original.Count, "Inputs not mutated");
        Validate(result);
    }

    [Test]
    public void RejectsMissingRequiredOperations()
    {
        var specs = Facebook();
        var page = JsonNode.Parse(specs["Page.json"])!;
        var apis = page["apis"]!.AsArray();
        apis.Remove(apis.Single(api => api!["method"]!.GetValue<string>() == "POST" && api["endpoint"]!.GetValue<string>() == "feed"));
        specs["Page.json"] = page.ToJsonString();
        try { Convert(specs, Options()); }
        catch (InvalidOperationException error)
        {
            Check(error.Message.Contains("Page POST feed", StringComparison.Ordinal), "Missing operation identified");
            return;
        }
        throw new InvalidOperationException("Invalid profile input was accepted");
    }

    [Test]
    public void HonorsOverridesAndRejectsDanglingReferences()
    {
        var response = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["custom"] = new JsonObject { ["type"] = "string" } } };
        var options = new MetaOpenApiConverterOptions
        {
            GraphApiVersion = "v26.0",
            ResponseSchemaOverrides = new Dictionary<string, JsonObject> { ["Page POST feed"] = response }
        };
        var result = Convert(Facebook(), options);
        Check(result.Document["paths"]!["/{node-id}/feed"]!["post"]!["responses"]!["200"]!["content"]!["application/json"]!["schema"]!["properties"]!["custom"] is not null, "Caller override honored");
        response.Clear();
        response["$ref"] = "#/components/schemas/Missing";
        try { Convert(Facebook(), options); }
        catch (InvalidOperationException error) { Check(error.Message.Contains("Missing", StringComparison.Ordinal), "Dangling reference identified"); return; }
        throw new InvalidOperationException("Dangling reference accepted");
    }

    private static MetaOpenApiConverterOptions Options() => new() { GraphApiVersion = "v26.0" };

    private static MetaOpenApiConversionResult Convert(IReadOnlyDictionary<string, string> specifications,
        MetaOpenApiConverterOptions options)
        => FacebookSpecificationProfile.Convert(new MetaOpenApiConverter(), specifications, options, "test-revision", true);

    private static Dictionary<string, string> Facebook() => new()
    {
        ["Page.json"] = Node("Page", "GET", "GET feed", "POST feed", "GET photos", "POST photos", "GET posts", "GET published_posts", "GET scheduled_posts", "GET ads"),
        ["PagePost"] = Node("PagePost", "GET", "POST", "DELETE"),
        ["Photo"] = Node("Photo", "GET", "DELETE"),
        ["User"] = Node("User", "GET accounts"),
        ["Unrelated"] = Node("Unrelated", "GET ads")
    };

    private static Dictionary<string, string> Instagram() => new()
    {
        ["IGUser.json"] = Node("IGUser", "GET", "GET media", "POST media", "POST media_publish", "GET stories", "GET content_publishing_limit", "GET ads"),
        ["IGMedia"] = Node("IGMedia", "GET", "GET children", "GET comments", "POST comments", "POST", "DELETE"),
        ["IGComment"] = Node("IGComment", "GET", "DELETE")
    };

    private static string Node(string name, params string[] operations)
    {
        var apis = new JsonArray();
        foreach (string operation in operations)
        {
            string[] parts = operation.Split(' ', 2);
            var parameters = new JsonArray();
            if (operation == "POST feed") parameters = JsonNode.Parse("""[{"name":"message","type":"string"},{"name":"attached_media","type":"list<Object>"},{"name":"scheduled_publish_time","type":"datetime"},{"name":"thumbnail","type":"file"}]""")!.AsArray();
            if (operation == "POST media_publish") parameters = JsonNode.Parse("""[{"name":"creation_id","type":"unsigned int","required":true}]""")!.AsArray();
            var api = new JsonObject { ["method"] = parts[0], ["return"] = name, ["params"] = parameters };
            if (parts.Length == 2) api["endpoint"] = parts[1];
            apis.Add(api);
        }
        return new JsonObject { ["fields"] = JsonNode.Parse("""[{"name":"id","type":"string"},{"name":"name","type":"string"}]"""), ["apis"] = apis }.ToJsonString();
    }

    private static void Validate(MetaOpenApiConversionResult result)
    {
        var parsed = OpenApiDocument.Parse(result.ToJson(), "json");
        Check(parsed.Document is not null && parsed.Diagnostic?.Errors.Count == 0, "Valid OpenAPI document");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}