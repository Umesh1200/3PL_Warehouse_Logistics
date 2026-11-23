using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Outbound.OrderManagement.Commands.CreateOutboundOrder;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.OrderManagement.Commands.ImportOutboundOrdersBulk;

public record ImportOutboundOrdersBulkCommand : IRequest<OrderImportBatchDto>
{
    public Guid FacilityId { get; init; }
    public string SourceFileName { get; init; } = "bulk_import.json";
    public List<CreateOutboundOrderCommand> Orders { get; init; } = [];
}

public class ImportOutboundOrdersBulkCommandValidator : AbstractValidator<ImportOutboundOrdersBulkCommand>
{
    public ImportOutboundOrdersBulkCommandValidator()
    {
        RuleFor(x => x.FacilityId)
            .NotEmpty().WithMessage("Facility ID is required.");

        RuleFor(x => x.SourceFileName)
            .NotEmpty().WithMessage("Source file name is required.");

        RuleFor(x => x.Orders)
            .NotEmpty().WithMessage("Orders list cannot be empty.");
    }
}

public class ImportOutboundOrdersBulkCommandHandler : IRequestHandler<ImportOutboundOrdersBulkCommand, OrderImportBatchDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public ImportOutboundOrdersBulkCommandHandler(
        IApplicationDbContext context,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUser,
        OutboundOperationalAuthorization? outboundAuth = null)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _currentUser = currentUser;
        _outboundAuth = outboundAuth ?? new OutboundOperationalAuthorization(context, currentUser, tenantProvider);
    }

    public async Task<OrderImportBatchDto> Handle(ImportOutboundOrdersBulkCommand request, CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureAuthenticated();

        if (!_currentUser.IsSuperAdmin &&
            _currentUser.UserType != UserType.Staff &&
            _currentUser.UserType != UserType.Client)
        {
            throw new ForbiddenException("User identity type is not authorized for bulk order import.");
        }

        Guid clientId;
        if (_currentUser.UserType == UserType.Client && !_currentUser.IsSuperAdmin)
        {
            clientId = _outboundAuth.ResolveValidatedClientTenantId(null);
        }
        else
        {
            _outboundAuth.ValidateStaffMutationFacilityAccess(request.FacilityId, nameof(ImportOutboundOrdersBulkCommand));
            clientId = _tenantProvider.TenantId ?? _currentUser.TenantId
                ?? throw new Domain.Exceptions.ValidationException(nameof(request.FacilityId), "Client tenant header (X-Tenant-Id) is required when staff import outbound orders.");
        }

        // Check facility exists
        var facility = await _context.Facilities.FirstOrDefaultAsync(f => f.Id == request.FacilityId, cancellationToken);
        if (facility == null)
        {
            throw new EntityNotFoundException(nameof(Domain.Entities.Facilities.Facility), request.FacilityId);
        }

        if (!_currentUser.IsSuperAdmin)
        {
            await _outboundAuth.EnsureClientHasActiveFacilityAccessAsync(clientId, request.FacilityId, cancellationToken);
        }

        var batchNumber = $"BATCH-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
        var batch = new OrderImportBatch
        {
            ClientId = clientId,
            FacilityId = request.FacilityId,
            BatchNumber = batchNumber,
            SourceFileName = request.SourceFileName,
            TotalRows = request.Orders.Count,
            Status = ImportBatchStatus.Processing,
            StartedAtUtc = DateTime.UtcNow
        };

        _context.OrderImportBatches.Add(batch);
        await _context.SaveChangesAsync(cancellationToken);

        int successfulCount = 0;
        int failedCount = 0;
        int duplicateCount = 0;
        var errorSummaries = new Dictionary<string, string>();

        var existingExtRefs = await _context.OutboundOrders
            .Where(o => o.ClientId == clientId && o.ExternalOrderReference != null)
            .Select(o => o.ExternalOrderReference!)
            .ToHashSetAsync(cancellationToken);

        // Preload client items for fast lookup
        var allClientItems = await _context.Items
            .Where(i => i.ClientId == clientId)
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        int rowIndex = 1;
        foreach (var orderCmd in request.Orders)
        {
            var rowLog = new OrderImportBatchRow
            {
                OrderImportBatchId = batch.Id,
                RowNumber = rowIndex,
                ExternalOrderReference = orderCmd.ExternalOrderReference?.Trim()
            };

            // Check Duplicate Reference
            if (!string.IsNullOrWhiteSpace(rowLog.ExternalOrderReference) && existingExtRefs.Contains(rowLog.ExternalOrderReference))
            {
                rowLog.IsSuccess = false;
                rowLog.IsDuplicate = true;
                rowLog.ErrorMessage = $"Duplicate external reference '{rowLog.ExternalOrderReference}'. Order previously imported.";
                duplicateCount++;
                errorSummaries[$"row_{rowIndex}"] = rowLog.ErrorMessage;
                batch.Rows.Add(rowLog);
                rowIndex++;
                continue;
            }

            // Validate Line SKUs
            bool hasSkuError = false;
            foreach (var line in orderCmd.Lines)
            {
                if (!allClientItems.ContainsKey(line.ItemId))
                {
                    rowLog.IsSuccess = false;
                    rowLog.ErrorMessage = $"Item '{line.ItemId}' does not exist or does not belong to client '{clientId}'.";
                    failedCount++;
                    errorSummaries[$"row_{rowIndex}"] = rowLog.ErrorMessage;
                    hasSkuError = true;
                    break;
                }

                if (line.QuantityOrdered <= 0)
                {
                    rowLog.IsSuccess = false;
                    rowLog.ErrorMessage = $"Line item '{line.ItemId}' has invalid quantity '{line.QuantityOrdered}'. Must be > 0.";
                    failedCount++;
                    errorSummaries[$"row_{rowIndex}"] = rowLog.ErrorMessage;
                    hasSkuError = true;
                    break;
                }
            }

            if (hasSkuError)
            {
                batch.Rows.Add(rowLog);
                rowIndex++;
                continue;
            }

            // Create Order
            try
            {
                var orderNumber = orderCmd.OrderNumber?.Trim();
                if (string.IsNullOrWhiteSpace(orderNumber))
                {
                    orderNumber = $"SO-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
                }

                var order = new OutboundOrder
                {
                    ClientId = clientId,
                    FacilityId = request.FacilityId,
                    DestinationFacilityId = orderCmd.DestinationFacilityId,
                    CustomerId = orderCmd.CustomerId,
                    OrderNumber = orderNumber,
                    ExternalOrderReference = rowLog.ExternalOrderReference,
                    OrderType = orderCmd.OrderType,
                    Status = OutboundOrderStatus.Released,
                    Priority = orderCmd.Priority,
                    ServiceLevel = orderCmd.ServiceLevel,
                    CarrierCode = orderCmd.CarrierCode,
                    CustomerName = orderCmd.ShippingAddress?.CompanyName ?? orderCmd.ShippingAddress?.RecipientName ?? "Bulk Import Recipient",
                    RequiredShipDateUtc = orderCmd.RequiredShipDateUtc,
                    EarliestShipDateUtc = orderCmd.EarliestShipDateUtc,
                    Notes = orderCmd.Notes
                };

                var shippingAddress = new ShippingAddress
                {
                    OutboundOrderId = order.Id,
                    RecipientName = orderCmd.ShippingAddress?.RecipientName ?? "Recipient",
                    CompanyName = orderCmd.ShippingAddress?.CompanyName,
                    AddressLine1 = orderCmd.ShippingAddress?.AddressLine1 ?? "Address Line 1",
                    AddressLine2 = orderCmd.ShippingAddress?.AddressLine2,
                    City = orderCmd.ShippingAddress?.City ?? "City",
                    StateOrProvince = orderCmd.ShippingAddress?.StateOrProvince ?? "State",
                    PostalCode = orderCmd.ShippingAddress?.PostalCode ?? "00000",
                    CountryCode = orderCmd.ShippingAddress?.CountryCode ?? "USA",
                    PhoneNumber = orderCmd.ShippingAddress?.PhoneNumber,
                    DeliveryInstructions = orderCmd.ShippingAddress?.DeliveryInstructions
                };
                order.SetShippingAddress(shippingAddress);

                int lineNum = 1;
                foreach (var lineReq in orderCmd.Lines)
                {
                    order.Lines.Add(new OutboundOrderLine
                    {
                        OutboundOrderId = order.Id,
                        ItemId = lineReq.ItemId,
                        PackagingUnitId = lineReq.PackagingUnitId,
                        LineNumber = lineNum++,
                        QuantityOrdered = lineReq.QuantityOrdered,
                        QuantityAllocated = 0m,
                        QuantityShipped = 0m,
                        RequiredLotNumber = lineReq.RequiredLotNumber?.Trim(),
                        MinExpiryDateUtc = lineReq.MinExpiryDateUtc,
                        Status = OutboundOrderLineStatus.Pending,
                        Notes = lineReq.Notes
                    });
                }

                var operatorId = _currentUser.UserId ?? Guid.Empty;
                order.StatusHistories.Add(new OrderStatusHistory
                {
                    OutboundOrderId = order.Id,
                    FromStatus = OutboundOrderStatus.Draft,
                    ToStatus = OutboundOrderStatus.Released,
                    Reason = $"Bulk imported via batch '{batchNumber}'",
                    ChangedByUserId = operatorId,
                    ChangedAtUtc = DateTime.UtcNow
                });

                _context.OutboundOrders.Add(order);

                if (!string.IsNullOrWhiteSpace(rowLog.ExternalOrderReference))
                {
                    existingExtRefs.Add(rowLog.ExternalOrderReference);
                }

                rowLog.IsSuccess = true;
                rowLog.CreatedOutboundOrderId = order.Id;
                successfulCount++;
            }
            catch (Exception ex)
            {
                rowLog.IsSuccess = false;
                rowLog.ErrorMessage = $"Unexpected processing failure: {ex.Message}";
                failedCount++;
                errorSummaries[$"row_{rowIndex}"] = rowLog.ErrorMessage;
            }

            batch.Rows.Add(rowLog);
            rowIndex++;
        }

        string? errorJson = errorSummaries.Count > 0 ? JsonSerializer.Serialize(errorSummaries) : null;
        batch.Complete(successfulCount, failedCount, duplicateCount, errorJson);

        await _context.SaveChangesAsync(cancellationToken);

        return new OrderImportBatchDto
        {
            BatchId = batch.Id,
            BatchNumber = batch.BatchNumber,
            SourceFileName = batch.SourceFileName,
            TotalRows = batch.TotalRows,
            SuccessfulRows = batch.SuccessfulRows,
            FailedRows = batch.FailedRows,
            DuplicateRows = batch.DuplicateRows,
            Status = batch.Status,
            StartedAtUtc = batch.StartedAtUtc,
            CompletedAtUtc = batch.CompletedAtUtc,
            Rows = batch.Rows.Select(r => new OrderImportBatchRowDto
            {
                RowNumber = r.RowNumber,
                ExternalOrderReference = r.ExternalOrderReference,
                IsSuccess = r.IsSuccess,
                IsDuplicate = r.IsDuplicate,
                ErrorMessage = r.ErrorMessage,
                CreatedOutboundOrderId = r.CreatedOutboundOrderId
            }).ToList()
        };
    }
}

