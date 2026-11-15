using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Features.Inbound.Asns.Commands.CancelAsn;
using Warehouse3PL.Application.Features.Inbound.Asns.Commands.CreateAsn;
using Warehouse3PL.Application.Features.Inbound.Asns.Commands.MarkAsnArrived;
using Warehouse3PL.Application.Features.Inbound.Asns.Commands.MarkAsnInTransit;
using Warehouse3PL.Application.Features.Inbound.Asns.Queries.GetAsns;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.UnitTests.Common;
using Xunit;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.UnitTests.Inbound;

public class AdvanceShippingNoticeManagementTests
{
    [Fact]
    public async Task CreateAsn_StandaloneWithoutPo_SucceedsForClientItems()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), TenantId = clientId, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Tenant A", AccountCode = "CL-A" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = facilityId, Status = FacilityAccessStatus.Active });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-STANDALONE", Description = "Direct Supplier Item" });
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new CreateAsnCommandHandler(context, currentUser);

        // Act - Standalone ASN (PurchaseOrderId = null)
        var result = await handler.Handle(new CreateAsnCommand
        {
            FacilityId = facilityId,
            PurchaseOrderId = null,
            AsnNumber = "ASN-DIRECT-001",
            CarrierName = "FedEx Freight",
            TrackingReference = "FX-987654321",
            TrailerOrContainerNumber = "CONT-4410",
            EstimatedArrival = DateTime.UtcNow.AddDays(2),
            Lines = [new CreateAsnLineItemDto { ItemId = itemId, ExpectedQty = 500m }]
        }, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Null(result.PurchaseOrderId);
        Assert.Equal("ASN-DIRECT-001", result.AsnNumber);
        Assert.Equal(AsnStatus.Draft, result.Status);
        Assert.Single(result.Lines);
        Assert.Equal(500m, result.Lines[0].ExpectedQty);
        Assert.Equal(0m, result.Lines[0].ReceivedQty); // Received quantity remains 0 in Phase 3.1
    }

    [Fact]
    public async Task CreateAsn_LinkedToPo_ValidatesPoLineAndItemMatching()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var poId = Guid.NewGuid();
        var poLineId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), TenantId = clientId, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Tenant A", AccountCode = "CL-A" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = facilityId, Status = FacilityAccessStatus.Active });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-MATCH", Description = "Matched Item" });

            var po = new PurchaseOrder
            {
                Id = poId,
                ClientId = clientId,
                FacilityId = facilityId,
                PoNumber = "PO-MATCH-01",
                Status = PurchaseOrderStatus.Submitted
            };
            po.Lines.Add(new PurchaseOrderLine
            {
                Id = poLineId,
                LineNumber = 1,
                ItemId = itemId,
                ExpectedQty = 100m,
                UnitPrice = 20m,
                Status = PurchaseOrderLineStatus.Open
            });
            seed.PurchaseOrders.Add(po);
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new CreateAsnCommandHandler(context, currentUser);

        // Act - ASN 1 expecting partial shipment of 40 units against the PO
        var result = await handler.Handle(new CreateAsnCommand
        {
            FacilityId = facilityId,
            PurchaseOrderId = poId,
            AsnNumber = "ASN-PARTIAL-01",
            CarrierName = "DHL Global",
            Lines =
            [
                new CreateAsnLineItemDto
                {
                    PurchaseOrderLineId = poLineId,
                    ItemId = itemId,
                    ExpectedQty = 40m
                }
            ]
        }, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(poId, result.PurchaseOrderId);
        Assert.Equal("PO-MATCH-01", result.PoNumber);
        Assert.Single(result.Lines);
        Assert.Equal(poLineId, result.Lines[0].PurchaseOrderLineId);
        Assert.Equal(1, result.Lines[0].PoLineNumber);
        Assert.Equal(40m, result.Lines[0].ExpectedQty);
    }

    [Fact]
    public async Task CreateAsn_LinkedToPo_WhenPoLineBelongsToDifferentPo_ThrowsValidationException()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var poId1 = Guid.NewGuid();
        var poId2 = Guid.NewGuid();
        var poLineOfPo2 = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), TenantId = clientId, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Tenant A", AccountCode = "CL-A" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = facilityId, Status = FacilityAccessStatus.Active });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-MISMATCH", Description = "Item" });

            seed.PurchaseOrders.Add(new PurchaseOrder
            {
                Id = poId1,
                ClientId = clientId,
                FacilityId = facilityId,
                PoNumber = "PO-01",
                Status = PurchaseOrderStatus.Submitted
            });

            var po2 = new PurchaseOrder
            {
                Id = poId2,
                ClientId = clientId,
                FacilityId = facilityId,
                PoNumber = "PO-02",
                Status = PurchaseOrderStatus.Submitted
            };
            po2.Lines.Add(new PurchaseOrderLine { Id = poLineOfPo2, LineNumber = 1, ItemId = itemId, ExpectedQty = 50m });
            seed.PurchaseOrders.Add(po2);
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new CreateAsnCommandHandler(context, currentUser);

        // Act & Assert - Attempt linking to PO1 while referencing line of PO2
        var ex = await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new CreateAsnCommand
        {
            FacilityId = facilityId,
            PurchaseOrderId = poId1,
            AsnNumber = "ASN-PO-LINE-MISMATCH",
            Lines = [new CreateAsnLineItemDto { PurchaseOrderLineId = poLineOfPo2, ItemId = itemId, ExpectedQty = 10m }]
        }, CancellationToken.None));

        Assert.Contains("does not belong to linked Purchase Order", ex.Errors["PurchaseOrderLineId"].First());
    }

    [Fact]
    public async Task CreateAsn_WhenItemRequiresExpiry_AndExpiryMissing_ThrowsValidationException()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), TenantId = clientId, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Tenant A", AccountCode = "CL-A" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = facilityId, Status = FacilityAccessStatus.Active });
            seed.Items.Add(new Item
            {
                Id = itemId,
                ClientId = clientId,
                Sku = "SKU-PERISHABLE",
                Description = "Fresh Dairy",
                RequiresExpiryDate = true // Mandatory expiry tracking
            });
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new CreateAsnCommandHandler(context, currentUser);

        // Act & Assert - Omits ExpiryDate
        var ex = await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new CreateAsnCommand
        {
            FacilityId = facilityId,
            AsnNumber = "ASN-NO-EXPIRY",
            Lines = [new CreateAsnLineItemDto { ItemId = itemId, ExpectedQty = 100m, ExpiryDate = null }]
        }, CancellationToken.None));

        Assert.Contains("requires an expiration date", ex.Errors["ExpiryDate"].First());
    }

    [Fact]
    public async Task AsnLifecycle_TransitionsDraftToInTransitToArrived_WithTimestampAndVersionBumps()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var asnId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), TenantId = clientId, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Tenant A", AccountCode = "CL-A" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = facilityId, Status = FacilityAccessStatus.Active });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-01", Description = "Item" });

            var asn = new AdvanceShippingNotice
            {
                Id = asnId,
                ClientId = clientId,
                FacilityId = facilityId,
                AsnNumber = "ASN-LIFECYCLE-01",
                Status = AsnStatus.Draft,
                Version = 1
            };
            asn.Lines.Add(new AsnLineItem { ItemId = itemId, ExpectedQty = 100m });
            seed.AdvanceShippingNotices.Add(asn);
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);

        // Step 1: Transition Draft -> InTransit
        var inTransitHandler = new MarkAsnInTransitCommandHandler(context, currentUser);
        var inTransitResult = await inTransitHandler.Handle(new MarkAsnInTransitCommand(asnId), CancellationToken.None);

        Assert.Equal(AsnStatus.InTransit, inTransitResult.Status);
        Assert.Equal(2u, inTransitResult.Version); // Version bumped

        // Step 2: Transition InTransit -> Arrived
        var arrivalTime = DateTime.UtcNow;
        var arrivedHandler = new MarkAsnArrivedCommandHandler(context, currentUser);
        var arrivedResult = await arrivedHandler.Handle(new MarkAsnArrivedCommand(asnId, arrivalTime), CancellationToken.None);

        Assert.Equal(AsnStatus.Arrived, arrivedResult.Status);
        Assert.NotNull(arrivedResult.ActualArrival);
        Assert.Equal(3u, arrivedResult.Version); // Version bumped
    }

    [Fact]
    public async Task AsnLifecycle_DirectDraftToArrived_Succeeds()
    {
        // Arrange: Shipment registered upon physical warehouse arrival
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var asnId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), TenantId = clientId, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Tenant A", AccountCode = "CL-A" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = facilityId, Status = FacilityAccessStatus.Active });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-01", Description = "Item" });

            var asn = new AdvanceShippingNotice
            {
                Id = asnId,
                ClientId = clientId,
                FacilityId = facilityId,
                AsnNumber = "ASN-DIRECT-ARRIVE",
                Status = AsnStatus.Draft,
                Version = 1
            };
            asn.Lines.Add(new AsnLineItem { ItemId = itemId, ExpectedQty = 50m });
            seed.AdvanceShippingNotices.Add(asn);
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var arrivedHandler = new MarkAsnArrivedCommandHandler(context, currentUser);

        // Act - Direct transition from Draft to Arrived
        var arrivedResult = await arrivedHandler.Handle(new MarkAsnArrivedCommand(asnId), CancellationToken.None);

        // Assert
        Assert.Equal(AsnStatus.Arrived, arrivedResult.Status);
        Assert.NotNull(arrivedResult.ActualArrival);
        Assert.Equal(2u, arrivedResult.Version);
    }

    [Fact]
    public async Task CancelAsn_WhenAlreadyReceiving_ThrowsDomainException()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var asnId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), TenantId = clientId, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Tenant A", AccountCode = "CL-A" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = facilityId, Status = FacilityAccessStatus.Active });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-01", Description = "Item" });

            var asn = new AdvanceShippingNotice
            {
                Id = asnId,
                ClientId = clientId,
                FacilityId = facilityId,
                AsnNumber = "ASN-LOCKED-RECEIVING",
                Status = AsnStatus.Receiving // Receiving already in progress
            };
            asn.Lines.Add(new AsnLineItem { ItemId = itemId, ExpectedQty = 100m });
            seed.AdvanceShippingNotices.Add(asn);
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var cancelHandler = new CancelAsnCommandHandler(context, currentUser);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            cancelHandler.Handle(new CancelAsnCommand(asnId, "Cancel attempt"), CancellationToken.None));

        Assert.Contains("receiving execution is already 'Receiving'", ex.Message);
    }

    [Fact]
    public async Task TenantIsolation_AsnCreatedByTenantA_IsInvisibleToTenantB()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemA = Guid.NewGuid();
        var itemB = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientA, CompanyName = "Tenant A", AccountCode = "CL-A" });
            seed.Clients.Add(new Client { Id = clientB, CompanyName = "Tenant B", AccountCode = "CL-B" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.ClientFacilityAccesses.AddRange(
                new ClientFacilityAccess { ClientId = clientA, FacilityId = facilityId, Status = FacilityAccessStatus.Active },
                new ClientFacilityAccess { ClientId = clientB, FacilityId = facilityId, Status = FacilityAccessStatus.Active }
            );
            seed.Items.Add(new Item { Id = itemA, ClientId = clientA, Sku = "SKU-A" });
            seed.Items.Add(new Item { Id = itemB, ClientId = clientB, Sku = "SKU-B" });

            // ASN for Tenant A
            var asnA = new AdvanceShippingNotice
            {
                ClientId = clientA,
                FacilityId = facilityId,
                AsnNumber = "ASN-TENANT-A",
                Status = AsnStatus.InTransit
            };
            asnA.Lines.Add(new AsnLineItem { ItemId = itemA, ExpectedQty = 10m });
            seed.AdvanceShippingNotices.Add(asnA);

            // ASN for Tenant B
            var asnB = new AdvanceShippingNotice
            {
                ClientId = clientB,
                FacilityId = facilityId,
                AsnNumber = "ASN-TENANT-B",
                Status = AsnStatus.InTransit
            };
            asnB.Lines.Add(new AsnLineItem { ItemId = itemB, ExpectedQty = 20m });
            seed.AdvanceShippingNotices.Add(asnB);

            await seed.SaveChangesAsync();
        }

        // Query as Tenant B
        var tenantBProvider = new TestTenantProvider { TenantId = clientB };
        var tenantBUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), TenantId = clientB, UserType = UserType.Client };
        using var clientBContext = TestDbContextFactory.Create(tenantBProvider, tenantBUser, dbName);

        var queryHandler = new GetAsnsQueryHandler(clientBContext, tenantBUser);
        var result = await queryHandler.Handle(new GetAsnsQuery(), CancellationToken.None);

        // Assert: Tenant B sees strictly its own ASN and 0 ASNs of Tenant A
        Assert.Single(result.Items);
        Assert.Equal("ASN-TENANT-B", result.Items[0].AsnNumber);
        Assert.Equal(clientB, result.Items[0].ClientId);
    }
}

