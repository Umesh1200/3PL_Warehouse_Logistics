using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Items.Commands.CreateItem;

public record CreateItemCommand : IRequest<ItemDto>
{
    public Guid? ClientId { get; init; } // Nullable: resolved from tenant context if Client user
    public string Sku { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? ShortName { get; init; }
    
    public decimal UnitPrice { get; init; }
    public decimal WeightKg { get; init; }
    public decimal LengthCm { get; init; }
    public decimal WidthCm { get; init; }
    public decimal HeightCm { get; init; }
    
    public AbcVelocity AbcVelocity { get; init; } = AbcVelocity.B;
    public StorageTempClass StorageTempClass { get; init; } = StorageTempClass.Ambient;
    public string? HazardClass { get; init; }
    public int StackabilityFactor { get; init; } = 3;
    
    public bool IsLotTracked { get; init; } = false;
    public bool IsSerialized { get; init; } = false;
    public bool RequiresExpiryDate { get; init; } = false;
    public int ShelfLifeDays { get; init; } = 0;
    
    public string? PrimaryBarcode { get; init; }
    public BarcodeSymbology PrimaryBarcodeSymbology { get; init; } = BarcodeSymbology.EAN_13;
}

public class CreateItemCommandValidator : AbstractValidator<CreateItemCommand>
{
    public CreateItemCommandValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
        RuleFor(x => x.WeightKg).GreaterThanOrEqualTo(0);
        RuleFor(x => x.LengthCm).GreaterThanOrEqualTo(0);
        RuleFor(x => x.WidthCm).GreaterThanOrEqualTo(0);
        RuleFor(x => x.HeightCm).GreaterThanOrEqualTo(0);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
    }
}

public class CreateItemCommandHandler : IRequestHandler<CreateItemCommand, ItemDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ICurrentUserContext _currentUser;

    public CreateItemCommandHandler(
        IApplicationDbContext context,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUser)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _currentUser = currentUser;
    }

    public async Task<ItemDto> Handle(CreateItemCommand request, CancellationToken cancellationToken)
    {
        var targetClientId = request.ClientId ?? _tenantProvider.TenantId ?? _currentUser.TenantId;
        if (!targetClientId.HasValue)
        {
            throw new ValidationException(nameof(request.ClientId), "A valid ClientId tenant must be specified.");
        }

        var clientExists = await _context.Clients.AnyAsync(c => c.Id == targetClientId.Value, cancellationToken);
        if (!clientExists)
        {
            throw new EntityNotFoundException(nameof(Client), targetClientId.Value);
        }

        // Verify tenant-aware SKU uniqueness
        var skuExists = await _context.Items.AnyAsync(
            i => i.ClientId == targetClientId.Value && i.Sku == request.Sku, cancellationToken);
        if (skuExists)
        {
            throw new ValidationException(nameof(request.Sku), $"An item with SKU '{request.Sku}' already exists for this client.");
        }

        var item = new Item
        {
            ClientId = targetClientId.Value,
            Sku = request.Sku.Trim().ToUpperInvariant(),
            Description = request.Description,
            ShortName = request.ShortName,
            UnitPrice = request.UnitPrice,
            WeightKg = request.WeightKg,
            LengthCm = request.LengthCm,
            WidthCm = request.WidthCm,
            HeightCm = request.HeightCm,
            VolumeCm3 = request.LengthCm * request.WidthCm * request.HeightCm,
            AbcVelocity = request.AbcVelocity,
            StorageTempClass = request.StorageTempClass,
            HazardClass = request.HazardClass,
            StackabilityFactor = request.StackabilityFactor,
            IsLotTracked = request.IsLotTracked,
            IsSerialized = request.IsSerialized,
            RequiresExpiryDate = request.RequiresExpiryDate,
            ShelfLifeDays = request.ShelfLifeDays,
            Status = ItemStatus.Active
        };

        if (!string.IsNullOrWhiteSpace(request.PrimaryBarcode))
        {
            item.Barcodes.Add(new ItemBarcode
            {
                BarcodeValue = request.PrimaryBarcode.Trim(),
                Symbology = request.PrimaryBarcodeSymbology,
                IsPrimary = true
            });
        }

        // Standard each packaging unit
        item.PackagingUnits.Add(new PackagingUnit
        {
            UnitLevel = PackagingUnitLevel.Each,
            UnitName = "Each",
            ConversionQty = 1m,
            WeightKg = item.WeightKg,
            LengthCm = item.LengthCm,
            WidthCm = item.WidthCm,
            HeightCm = item.HeightCm,
            VolumeCm3 = item.VolumeCm3,
            Barcode = request.PrimaryBarcode
        });

        _context.Items.Add(item);
        await _context.SaveChangesAsync(cancellationToken);

        return new ItemDto
        {
            Id = item.Id,
            ClientId = item.ClientId,
            Sku = item.Sku,
            Description = item.Description,
            ShortName = item.ShortName,
            UnitPrice = item.UnitPrice,
            WeightKg = item.WeightKg,
            LengthCm = item.LengthCm,
            WidthCm = item.WidthCm,
            HeightCm = item.HeightCm,
            VolumeCm3 = item.VolumeCm3,
            AbcVelocity = item.AbcVelocity,
            StorageTempClass = item.StorageTempClass,
            HazardClass = item.HazardClass,
            IsLotTracked = item.IsLotTracked,
            IsSerialized = item.IsSerialized,
            RequiresExpiryDate = item.RequiresExpiryDate,
            ShelfLifeDays = item.ShelfLifeDays,
            Status = item.Status,
            Barcodes = item.Barcodes.Select(b => b.BarcodeValue).ToList(),
            PackagingUnits = item.PackagingUnits.Select(p => new PackagingUnitDto
            {
                Id = p.Id,
                UnitLevel = p.UnitLevel,
                UnitName = p.UnitName,
                ConversionQty = p.ConversionQty,
                WeightKg = p.WeightKg,
                VolumeCm3 = p.VolumeCm3,
                Barcode = p.Barcode
            }).ToList()
        };
    }
}
