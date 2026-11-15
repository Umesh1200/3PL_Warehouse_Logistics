using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Features.CycleCounts.Commands.CreateCycleCount;
using Warehouse3PL.Application.Features.CycleCounts.Commands.RecordCycleCountItems;
using Warehouse3PL.Application.Features.CycleCounts.Commands.ReconcileCycleCount;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.UnitTests.Common;
using Xunit;

namespace Warehouse3PL.UnitTests.Wms;

public class CycleCountingAndAdjustmentTests
{
    [Fact]
    public async Task CompleteCycleCountLifecycle_RecordsObservations_AndReconcilesVariancesWithAuditTrail()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var locId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };

        using (var seedContext = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seedContext.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-AUDIT", Name = "Audit Center" });
            seedContext.Locations.Add(new Location { Id = locId, FacilityId = facilityId, LocationCode = "BIN-CC-01", Barcode = "LOC-CC-01" });
            seedContext.Items.Add(new Item
            {
                Id = itemId,
                ClientId = clientId,
                Sku = "AUDIT-SKU",
                Description = "High value audit SKU",
                UnitPrice = 25.00m,
                AbcVelocity = AbcVelocity.A
            });

            // Initial stock: 100 units
            seedContext.StockOnHand.Add(new StockOnHand
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                LocationId = locId,
                QtyAvailable = 100m,
                Version = 1
            });

            await seedContext.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);

        // Step 1: Create Cycle Count
        var createHandler = new CreateCycleCountCommandHandler(context);
        var countDto = await createHandler.Handle(new CreateCycleCountCommand
        {
            FacilityId = facilityId,
            CountType = CycleCountType.AbcVelocity
        }, CancellationToken.None);

        Assert.NotNull(countDto);
        Assert.Single(countDto.Items);
        var countItemId = countDto.Items[0].Id;
        Assert.Equal(100m, countDto.Items[0].ExpectedQty);

        // Step 2: Record Physical Count Observation (Found 95 units -> 5 units missing)
        var recordHandler = new RecordCycleCountItemsCommandHandler(context, currentUser);
        var recordResult = await recordHandler.Handle(new RecordCycleCountItemsCommand(
            countDto.Id,
            [new CountObservationDto(countItemId, 95m)]), CancellationToken.None);

        Assert.True(recordResult);

        // Step 3: Reconcile Cycle Count with Shrinkage Adjustment
        var reconcileHandler = new ReconcileCycleCountCommandHandler(context, currentUser);
        var reconciledCount = await reconcileHandler.Handle(new ReconcileCycleCountCommand(
            countDto.Id,
            DefaultReasonCode: AdjustmentReasonCode.Shrinkage,
            Notes: "Missing 5 units during monthly ABC cycle count"
        ), CancellationToken.None);

        // Assert - Cycle Count Summary
        Assert.Equal(CycleCountStatus.Completed, reconciledCount.Status);
        Assert.Equal(1, reconciledCount.DiscrepancyCount);
        Assert.Equal(-5m, reconciledCount.Items[0].VarianceQty);
        Assert.True(reconciledCount.Items[0].IsReconciled);

        // Assert - Stock on hand adjusted from 100 -> 95
        var updatedStock = await context.StockOnHand.FirstAsync(s => s.LocationId == locId && s.ItemId == itemId);
        Assert.Equal(95m, updatedStock.QtyAvailable);
        Assert.NotNull(updatedStock.LastCountedAt);

        // Assert - InventoryAdjustment record
        var adjustment = await context.InventoryAdjustments.FirstOrDefaultAsync(a => a.StockId == updatedStock.Id);
        Assert.NotNull(adjustment);
        Assert.Equal(-5m, adjustment.QtyAdjusted);
        Assert.Equal(AdjustmentReasonCode.Shrinkage, adjustment.ReasonCode);
        Assert.Equal(-125.00m, adjustment.AccountingImpactAmount); // -5 * $25.00
        Assert.Equal(currentUser.UserId, adjustment.ApprovedByUserId);

        // Assert - Immutable Ledger Entry
        var ledgerTx = await context.InventoryTransactions
            .FirstOrDefaultAsync(t => t.TxType == InventoryTxType.CycleCount && t.ReferenceId == countDto.Id);
        Assert.NotNull(ledgerTx);
        Assert.Equal(5m, ledgerTx.Quantity);
        Assert.Equal(locId, ledgerTx.FromLocationId);
        Assert.Equal(locId, ledgerTx.ToLocationId);
    }
}

