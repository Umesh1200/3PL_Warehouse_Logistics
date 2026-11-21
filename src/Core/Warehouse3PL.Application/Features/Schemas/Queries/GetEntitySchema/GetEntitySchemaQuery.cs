using System.Reflection;
using System.Text.Json;
using MediatR;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Schemas.Queries.GetEntitySchema;

public record GetEntitySchemaQuery(string EntityName) : IRequest<EntitySchemaDto>;

public class GetEntitySchemaQueryHandler : IRequestHandler<GetEntitySchemaQuery, EntitySchemaDto>
{
    private readonly ICurrentUserContext _currentUser;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GetEntitySchemaQueryHandler(ICurrentUserContext currentUser)
    {
        _currentUser = currentUser;
    }

    public Task<EntitySchemaDto> Handle(GetEntitySchemaQuery request, CancellationToken cancellationToken)
    {
        var normalizedName = request.EntityName.Trim().ToLowerInvariant();

        // 1. Load schema JSON from embedded resource or physical file
        string? jsonContent = LoadSchemaJson(normalizedName);
        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            throw new EntityNotFoundException("EntitySchema", request.EntityName);
        }

        var schema = JsonSerializer.Deserialize<EntitySchemaDto>(jsonContent, JsonOptions);
        if (schema == null)
        {
            throw new InvalidOperationException($"Failed to deserialize schema for '{request.EntityName}'.");
        }

        // 2. Authorization check: user must possess the View permission for this schema
        if (!string.IsNullOrWhiteSpace(schema.Permissions.View) && !_currentUser.HasPermission(schema.Permissions.View))
        {
            throw new ForbiddenException($"Caller does not have required permission '{schema.Permissions.View}' to view schema for '{schema.Name}'.");
        }

        // 3. Server-side authoritative pruning of actions based on caller's effective permissions
        var resolvedActions = schema.Actions.Where(action =>
        {
            if (string.IsNullOrWhiteSpace(action.Permission))
            {
                return true;
            }
            return _currentUser.HasPermission(action.Permission);
        }).ToList();

        // 4. Server-side authoritative pruning/masking of fields
        var resolvedFields = schema.Fields.Select(field =>
        {
            bool isReadOnly = field.ReadOnly;
            bool isHidden = field.Hidden;

            // If field has write permission and user lacks it, mark readOnly
            if (!string.IsNullOrWhiteSpace(field.WritePermission) && !_currentUser.HasPermission(field.WritePermission))
            {
                isReadOnly = true;
            }

            // If field has read permission and user lacks it, mark hidden
            if (!string.IsNullOrWhiteSpace(field.ReadPermission) && !_currentUser.HasPermission(field.ReadPermission))
            {
                isHidden = true;
            }

            return field with { ReadOnly = isReadOnly, Hidden = isHidden };
        }).ToList();

        var effectiveSchema = schema with
        {
            Fields = resolvedFields,
            Actions = resolvedActions
        };

        return Task.FromResult(effectiveSchema);
    }

    private static string? LoadSchemaJson(string entityName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(r => r.EndsWith($"Schemas.{entityName}.json", StringComparison.OrdinalIgnoreCase));

        if (resourceName != null)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
        }

        // Fallback: check relative file paths
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "Schemas", $"{entityName}.json"),
            Path.Combine(baseDir, "..", "..", "..", "..", "Core", "Warehouse3PL.Application", "Schemas", $"{entityName}.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "Core", "Warehouse3PL.Application", "Schemas", $"{entityName}.json"),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        return null;
    }
}
