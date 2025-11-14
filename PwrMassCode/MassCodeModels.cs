using System.Text.Json.Serialization;

namespace Community.PowerToys.Run.Plugin.PwrMassCode;

internal sealed class Snippet
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("tags")] public List<Tag> Tags { get; set; } = [];
    [JsonPropertyName("folder")] public Folder? Folder { get; set; }
    [JsonPropertyName("contents")] public List<Content> Contents { get; set; } = [];
    // API field name is 'isFavorites' (plural)
    [JsonPropertyName("isFavorites")][JsonConverter(typeof(FlexibleBoolConverter))] public bool IsFavorite { get; set; }
    [JsonPropertyName("isDeleted")][JsonConverter(typeof(FlexibleBoolConverter))] public bool IsDeleted { get; set; }
    [JsonPropertyName("createdAt")] public long CreatedAt { get; set; }
    [JsonPropertyName("updatedAt")] public long UpdatedAt { get; set; }
}

internal sealed class Tag
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

internal sealed class Folder
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

internal sealed class Content
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
    [JsonPropertyName("language")] public string? Language { get; set; }
}

internal sealed class CreateSnippetRequest
{
    [JsonPropertyName("name")] public required string Name { get; set; }
    [JsonPropertyName("folderId")] public int? FolderId { get; set; }
}

internal sealed class CreateSnippetResponse
{
    [JsonPropertyName("id")] public int Id { get; set; }
}

internal sealed class CreateContentRequest
{
    [JsonPropertyName("label")] public required string Label { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
    [JsonPropertyName("language")] public required string Language { get; set; }
}
