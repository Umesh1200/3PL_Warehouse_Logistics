namespace Warehouse3PL.Application.Common.Models;

public record PagedQuery
{
    private const int MaxPageSize = 100;
    private int _pageSize = 20;

    public int PageNumber { get; init; } = 1;

    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value > MaxPageSize ? MaxPageSize : value < 1 ? 1 : value;
    }

    public string? SearchTerm { get; init; }
    public string? SortBy { get; init; }
    public bool SortDescending { get; init; }
}

