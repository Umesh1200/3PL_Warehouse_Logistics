using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public record ClientDto
{
    public Guid Id { get; init; }
    public string CompanyName { get; init; } = string.Empty;
    public string AccountCode { get; init; } = string.Empty;
    public ClientStatus Status { get; init; }
    public SlaTier SlaTier { get; init; }
    public string Currency { get; init; } = "USD";
    public DateTime CreatedAt { get; init; }
    public int ActiveUsersCount { get; init; }
    public IReadOnlyList<ClientContactDto> Contacts { get; init; } = [];
}

public record ClientContactDto
{
    public Guid Id { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public bool IsBillingContact { get; init; }
}

