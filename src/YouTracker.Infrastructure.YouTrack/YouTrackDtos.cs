using System.Text.Json;

namespace YouTracker.Infrastructure.YouTrack;

// PascalCase DTOs deserialized case-insensitively from YouTrack's camelCase JSON.
// YouTrack only returns fields explicitly requested via `fields=`.

internal sealed class UserDto
{
    public string? Id { get; set; }
    public string? Login { get; set; }
    public string? FullName { get; set; }
    public bool Banned { get; set; }
}

internal sealed class IssueDto
{
    public string? IdReadable { get; set; }
    public string? Summary { get; set; }
    public long Updated { get; set; }
    public ProjectDto? Project { get; set; }
    public List<CustomFieldDto>? CustomFields { get; set; }
    public List<IssueLinkDto>? Links { get; set; }
}

internal sealed class IssueLinkDto
{
    public string? Direction { get; set; }
    public LinkTypeDto? LinkType { get; set; }
    public List<IssueDto>? Issues { get; set; }
}

internal sealed class LinkTypeDto
{
    public string? Name { get; set; }
}

internal sealed class ProjectDto
{
    public string? ShortName { get; set; }
}

internal sealed class CustomFieldDto
{
    public string? Name { get; set; }

    /// <summary>Polymorphic: object (name / minutes+presentation), array, or null.</summary>
    public JsonElement Value { get; set; }
}

internal sealed class WorkItemDto
{
    public string? Id { get; set; }
    public long Date { get; set; }
    public DurationDto? Duration { get; set; }
    public WorkItemTypeDto? Type { get; set; }
    public string? Text { get; set; }
    public IssueRefDto? Issue { get; set; }
    public AuthorDto? Author { get; set; }
}

internal sealed class DurationDto
{
    public int Minutes { get; set; }
    public string? Presentation { get; set; }
}

internal sealed class WorkItemTypeDto
{
    public string? Id { get; set; }
    public string? Name { get; set; }
}

internal sealed class IssueRefDto
{
    public string? IdReadable { get; set; }
    public string? Summary { get; set; }
}

internal sealed class AuthorDto
{
    public string? Login { get; set; }
}
