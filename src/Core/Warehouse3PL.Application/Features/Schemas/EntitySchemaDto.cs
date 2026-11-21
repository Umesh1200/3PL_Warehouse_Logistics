using System.Text.Json.Serialization;

namespace Warehouse3PL.Application.Features.Schemas;

public record EntitySchemaDto
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0.0";
    public string Title { get; init; } = string.Empty;
    public string PluralTitle { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Icon { get; init; } = "Boxes";
    public string PrimaryKey { get; init; } = "id";
    public EntityEndpointsDto Endpoints { get; init; } = new();
    public EntityPermissionsDto Permissions { get; init; } = new();
    public IReadOnlyList<FieldDefinitionDto> Fields { get; init; } = [];
    public SchemaViewsDto Views { get; init; } = new();
    public IReadOnlyList<ActionDefinitionDto> Actions { get; init; } = [];
}

public record EntityEndpointsDto
{
    public string List { get; init; } = string.Empty;
    public string? Get { get; init; }
    public string? Create { get; init; }
    public string? Update { get; init; }
    public string? Delete { get; init; }
}

public record EntityPermissionsDto
{
    public string View { get; init; } = string.Empty;
    public string? Create { get; init; }
    public string? Edit { get; init; }
    public string? Delete { get; init; }
}

public record FieldDefinitionDto
{
    public string Name { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Type { get; init; } = "string";
    public bool Required { get; init; }
    public bool ReadOnly { get; init; }
    public bool Hidden { get; init; }
    public object? DefaultValue { get; init; }
    public string? Placeholder { get; init; }
    public IReadOnlyList<SelectOptionDto>? Options { get; init; }
    public FieldValidationDto? Validation { get; init; }
    public string? Icon { get; init; }
    public int? Span { get; init; }
    public string? ReadPermission { get; init; }
    public string? WritePermission { get; init; }
    public string? HelperText { get; init; }
}

public record SelectOptionDto
{
    public object Value { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string? Variant { get; init; }
    public string? Icon { get; init; }
}

public record FieldValidationDto
{
    public double? Min { get; init; }
    public double? Max { get; init; }
    public string? Pattern { get; init; }
    public string? Message { get; init; }
}

public record SchemaViewsDto
{
    public ListViewDefinitionDto List { get; init; } = new();
    public FormViewDefinitionDto? Form { get; init; }
}

public record ListViewDefinitionDto
{
    public IReadOnlyList<ListColumnDefinitionDto> Columns { get; init; } = [];
    public DefaultSortDto? DefaultSort { get; init; }
    public IReadOnlyList<string>? SearchFields { get; init; }
    public EmptyStateDto? EmptyState { get; init; }
}

public record ListColumnDefinitionDto
{
    public string Field { get; init; } = string.Empty;
    public string? Label { get; init; }
    public string? Width { get; init; }
    public string? Align { get; init; }
    public bool? Sortable { get; init; }
    public string? Format { get; init; }
}

public record DefaultSortDto
{
    public string Field { get; init; } = string.Empty;
    public string Direction { get; init; } = "asc";
}

public record EmptyStateDto
{
    public string Icon { get; init; } = "Boxes";
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? ActionLabel { get; init; }
}

public record FormViewDefinitionDto
{
    public IReadOnlyList<FormSectionDefinitionDto> Sections { get; init; } = [];
}

public record FormSectionDefinitionDto
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Icon { get; init; }
    public int? Columns { get; init; }
    public IReadOnlyList<string> Fields { get; init; } = [];
}

public record ActionDefinitionDto
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string? Icon { get; init; }
    public string? Variant { get; init; }
    public string Target { get; init; } = "header";
    public string? Permission { get; init; }
    public string? ConfirmPrompt { get; init; }
}
