using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Inbound.DockManagement.DockDoors.Queries.GetDockDoors;

public record GetDockDoorsQuery : PagedQuery, IRequest<PagedResult<DockDoorDto>>
{
    public Guid? FacilityId { get; init; }
    public DockDoorType? DoorType { get; init; }
    public DockDoorStatus? Status { get; init; }
    public bool? IsOperational { get; init; }
}

public class GetDockDoorsQueryHandler : IRequestHandler<GetDockDoorsQuery, PagedResult<DockDoorDto>>
{
    private readonly IApplicationDbContext _context;

    public GetDockDoorsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<DockDoorDto>> Handle(GetDockDoorsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.DockDoors
            .AsNoTracking()
            .AsQueryable();

        if (request.FacilityId.HasValue)
        {
            query = query.Where(d => d.FacilityId == request.FacilityId.Value);
        }

        if (request.DoorType.HasValue)
        {
            query = query.Where(d => d.DoorType == request.DoorType.Value);
        }

        if (request.Status.HasValue)
        {
            query = query.Where(d => d.Status == request.Status.Value);
        }

        if (request.IsOperational.HasValue)
        {
            if (request.IsOperational.Value)
            {
                query = query.Where(d => d.Status == DockDoorStatus.Active);
            }
            else
            {
                query = query.Where(d => d.Status != DockDoorStatus.Active);
            }
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim().ToLower();
            query = query.Where(d =>
                d.DoorNumber.ToLower().Contains(term) ||
                (d.Notes != null && d.Notes.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(d => d.DoorNumber)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(d => new DockDoorDto
            {
                Id = d.Id,
                FacilityId = d.FacilityId,
                DoorNumber = d.DoorNumber,
                DoorType = d.DoorType,
                Status = d.Status,
                SupportsRefrigerated = d.SupportsRefrigerated,
                MaxTrailerLengthFeet = d.MaxTrailerLengthFeet,
                Notes = d.Notes,
                IsOperational = d.Status == DockDoorStatus.Active,
                CreatedAt = d.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return PagedResult<DockDoorDto>.Create(items, totalCount, request.PageNumber, request.PageSize);
    }
}

