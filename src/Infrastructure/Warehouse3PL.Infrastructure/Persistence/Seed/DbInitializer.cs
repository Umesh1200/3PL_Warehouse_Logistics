using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.CrossDock;
using Warehouse3PL.Domain.Entities.Dock;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Persistence.Seed;

public class DbInitializer
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<DbInitializer> _logger;

    public DbInitializer(
        ApplicationDbContext context,
        IPasswordHasher passwordHasher,
        ILogger<DbInitializer> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        try
        {
            if (_context.Database.IsRelational())
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                if (!await _context.Database.CanConnectAsync(cts.Token))
                {
                    _logger.LogWarning("PostgreSQL database is currently unreachable. Seeding bypassed.");
                    return;
                }
            }

            await _context.Database.EnsureCreatedAsync();

            await SeedRolesAndPermissionsAsync();
            var facility = await SeedFacilitiesAsync();
            var client = await SeedClientsAsync();
            await SeedUsersAsync(client);
            await SeedClientFacilityAccessAsync(client, facility);
            await SeedWmsEntitiesAsync(facility, client);
            await SeedPutawayStrategyRulesAndTasksAsync(facility, client);
            await SeedCrossDockEntitiesAsync(facility, client);

            await _context.SaveChangesAsync();
            _logger.LogInformation("Database seeding completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }

    private async Task SeedRolesAndPermissionsAsync()
    {
        var allPermissions = Permissions.GetAllPermissions();

        // 1. SuperAdmin Role
        var superAdminRole = await _context.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.RoleName == SystemRoles.SuperAdmin);

        if (superAdminRole == null)
        {
            superAdminRole = new Role
            {
                RoleName = SystemRoles.SuperAdmin,
                Description = "System administrator with full platform access across all tenants.",
                IsSystemRole = true
            };

            foreach (var permission in allPermissions)
            {
                superAdminRole.RolePermissions.Add(new RolePermission
                {
                    Role = superAdminRole,
                    PermissionKey = permission,
                    AccessLevel = AccessLevel.Admin
                });
            }

            _context.Roles.Add(superAdminRole);
        }

        // 2. ClientAdmin Role (reconcile idempotently — never leave legacy floor execution grants)
        var clientAdminRole = await _context.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.RoleName == SystemRoles.ClientAdmin);

        var allowedClientAdminPermissions = new[]
        {
            Permissions.Clients.View,
            Permissions.Users.View, Permissions.Users.Create, Permissions.Users.Edit, Permissions.Users.AssignRoles,
            Permissions.Facilities.View,
            Permissions.Inventory.View, Permissions.Inventory.Adjust, Permissions.Inventory.Count,
            Permissions.Inbound.View, Permissions.Inbound.Receive,
            Permissions.Outbound.View, Permissions.Outbound.Create,
            Permissions.Outbound.OrdersView, Permissions.Outbound.OrdersCreate,
            Permissions.Outbound.OrdersImport, Permissions.Outbound.OrdersCancel,
            Permissions.Billing.View,
            Permissions.Dispatch.View
        };

        var obsoleteClientExecutionPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Permissions.Outbound.Pick,
            Permissions.Outbound.Pack,
            Permissions.Outbound.Ship,
            Permissions.Outbound.TasksManage,
            Permissions.Outbound.WavesManage,
            Permissions.Outbound.OrdersAllocate
        };

        if (clientAdminRole == null)
        {
            clientAdminRole = new Role
            {
                RoleName = SystemRoles.ClientAdmin,
                Description = "Client administrator managing tenant users, orders, and inventory.",
                IsSystemRole = true
            };

            foreach (var permission in allowedClientAdminPermissions)
            {
                clientAdminRole.RolePermissions.Add(new RolePermission
                {
                    Role = clientAdminRole,
                    PermissionKey = permission,
                    AccessLevel = AccessLevel.Admin
                });
            }

            _context.Roles.Add(clientAdminRole);
        }
        else if (clientAdminRole.IsSystemRole)
        {
            var toRemove = clientAdminRole.RolePermissions
                .Where(rp => obsoleteClientExecutionPermissions.Contains(rp.PermissionKey))
                .ToList();

            if (toRemove.Count > 0)
            {
                _context.RolePermissions.RemoveRange(toRemove);
            }

            foreach (var permission in allowedClientAdminPermissions)
            {
                if (!clientAdminRole.RolePermissions.Any(rp =>
                    string.Equals(rp.PermissionKey, permission, StringComparison.OrdinalIgnoreCase)))
                {
                    clientAdminRole.RolePermissions.Add(new RolePermission
                    {
                        Role = clientAdminRole,
                        PermissionKey = permission,
                        AccessLevel = AccessLevel.Admin
                    });
                }
            }
        }

        // 3. ClientUser Role
        var clientUserRole = await _context.Roles
            .FirstOrDefaultAsync(r => r.RoleName == SystemRoles.ClientUser);

        if (clientUserRole == null)
        {
            clientUserRole = new Role
            {
                RoleName = SystemRoles.ClientUser,
                Description = "Standard client user with read and order creation access.",
                IsSystemRole = true
            };
            _context.Roles.Add(clientUserRole);
        }

        // 4. WarehouseOperator Role
        var operatorRole = await _context.Roles
            .FirstOrDefaultAsync(r => r.RoleName == SystemRoles.WarehouseOperator);

        if (operatorRole == null)
        {
            operatorRole = new Role
            {
                RoleName = SystemRoles.WarehouseOperator,
                Description = "Floor operator performing pick, pack, putaway, and cycle counts.",
                IsSystemRole = true
            };
            _context.Roles.Add(operatorRole);
        }

        // 5. Driver Role
        var driverRole = await _context.Roles
            .FirstOrDefaultAsync(r => r.RoleName == SystemRoles.Driver);

        if (driverRole == null)
        {
            driverRole = new Role
            {
                RoleName = SystemRoles.Driver,
                Description = "Courier and last-mile delivery driver.",
                IsSystemRole = true
            };
            _context.Roles.Add(driverRole);
        }

        await _context.SaveChangesAsync();
    }

    private async Task<Facility> SeedFacilitiesAsync()
    {
        var facility = await _context.Facilities
            .FirstOrDefaultAsync(f => f.FacilityCode == "FAC-ORD1");

        if (facility == null)
        {
            facility = new Facility
            {
                FacilityCode = "FAC-ORD1",
                Name = "Chicago Central Fulfillment Hub",
                AddressLine1 = "1000 Logistics Parkway",
                City = "Chicago",
                Timezone = "America/Chicago",
                IsActive = true
            };
            _context.Facilities.Add(facility);
            await _context.SaveChangesAsync();
        }

        return facility;
    }

    private async Task<Client> SeedClientsAsync()
    {
        var client = await _context.Clients
            .Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.AccountCode == "APEX-001");

        if (client == null)
        {
            client = new Client
            {
                CompanyName = "Apex Retail Distribution Ltd",
                AccountCode = "APEX-001",
                Status = ClientStatus.Active,
                SlaTier = SlaTier.Gold,
                Currency = "USD"
            };

            client.Contacts.Add(new ClientContact
            {
                Client = client,
                FirstName = "David",
                LastName = "Miller",
                Email = "david.miller@apexretail.com",
                Phone = "+1-555-0199",
                IsBillingContact = true
            });

            _context.Clients.Add(client);
            await _context.SaveChangesAsync();
        }

        return client;
    }

    private async Task SeedUsersAsync(Client client)
    {
        var superAdminRole = await _context.Roles.FirstAsync(r => r.RoleName == SystemRoles.SuperAdmin);
        var clientAdminRole = await _context.Roles.FirstAsync(r => r.RoleName == SystemRoles.ClientAdmin);

        // 1. Platform SuperAdmin User
        var superAdmin = await _context.Users
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Email == "admin@3pllogistics.com");

        if (superAdmin == null)
        {
            superAdmin = new User
            {
                Email = "admin@3pllogistics.com",
                PasswordHash = _passwordHasher.HashPassword("Admin123!"),
                FirstName = "System",
                LastName = "Administrator",
                PhoneNumber = "+1-555-0100",
                UserType = UserType.Staff,
                Status = UserStatus.Active,
                ClientId = null // Staff user has no fixed tenant boundary
            };

            superAdmin.UserRoles.Add(new UserRole
            {
                User = superAdmin,
                Role = superAdminRole,
                AssignedAt = DateTime.UtcNow
            });

            _context.Users.Add(superAdmin);
        }

        // 2. Client Administrator User (Apex Tenant)
        var clientAdmin = await _context.Users
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Email == "client.admin@apexretail.com");

        if (clientAdmin == null)
        {
            clientAdmin = new User
            {
                Email = "client.admin@apexretail.com",
                PasswordHash = _passwordHasher.HashPassword("Client123!"),
                FirstName = "David",
                LastName = "Miller",
                PhoneNumber = "+1-555-0199",
                UserType = UserType.Client,
                Status = UserStatus.Active,
                ClientId = client.Id
            };

            clientAdmin.UserRoles.Add(new UserRole
            {
                User = clientAdmin,
                Role = clientAdminRole,
                AssignedAt = DateTime.UtcNow
            });

            _context.Users.Add(clientAdmin);
        }

        await _context.SaveChangesAsync();
    }

    private async Task SeedClientFacilityAccessAsync(Client client, Facility facility)
    {
        var exists = await _context.ClientFacilityAccesses
            .AnyAsync(a => a.ClientId == client.Id && a.FacilityId == facility.Id);

        if (!exists)
        {
            _context.ClientFacilityAccesses.Add(new ClientFacilityAccess
            {
                ClientId = client.Id,
                FacilityId = facility.Id,
                EffectiveDate = DateTime.UtcNow,
                Status = FacilityAccessStatus.Active,
                PrimaryOperatingFacility = true
            });

            await _context.SaveChangesAsync();
        }
    }

    private async Task SeedWmsEntitiesAsync(Facility facility, Client client)
    {
        // 1. Warehouse Zones
        var receivingZone = await _context.WarehouseZones
            .FirstOrDefaultAsync(z => z.FacilityId == facility.Id && z.ZoneCode == "ZN-RECEIVING");
        if (receivingZone == null)
        {
            receivingZone = new WarehouseZone
            {
                FacilityId = facility.Id,
                ZoneCode = "ZN-RECEIVING",
                Name = "Receiving Inbound Dock",
                ZoneType = WarehouseZoneType.Receiving,
                IsActive = true
            };
            _context.WarehouseZones.Add(receivingZone);
        }

        var bulkZone = await _context.WarehouseZones
            .FirstOrDefaultAsync(z => z.FacilityId == facility.Id && z.ZoneCode == "ZN-BULK-A");
        if (bulkZone == null)
        {
            bulkZone = new WarehouseZone
            {
                FacilityId = facility.Id,
                ZoneCode = "ZN-BULK-A",
                Name = "Bulk High-Bay Storage",
                ZoneType = WarehouseZoneType.BulkStorage,
                IsActive = true
            };
            _context.WarehouseZones.Add(bulkZone);
        }

        var coldZone = await _context.WarehouseZones
            .FirstOrDefaultAsync(z => z.FacilityId == facility.Id && z.ZoneCode == "ZN-COLD");
        if (coldZone == null)
        {
            coldZone = new WarehouseZone
            {
                FacilityId = facility.Id,
                ZoneCode = "ZN-COLD",
                Name = "Cold Storage Freezer",
                ZoneType = WarehouseZoneType.ColdStorage,
                TempMinC = -25.0m,
                TempMaxC = -18.0m,
                IsActive = true
            };
            _context.WarehouseZones.Add(coldZone);
        }

        await _context.SaveChangesAsync();

        // 2. Locations Hierarchy
        var dockLoc = await _context.Locations
            .FirstOrDefaultAsync(l => l.FacilityId == facility.Id && l.LocationCode == "LOC-RCV-DOCK-01");
        if (dockLoc == null)
        {
            dockLoc = new Location
            {
                FacilityId = facility.Id,
                ZoneId = receivingZone.Id,
                LocationCode = "LOC-RCV-DOCK-01",
                Barcode = "LOC-RCV-DOCK-01",
                LocationType = LocationType.StagingLane,
                MaxWeightKg = 50000m,
                IsActive = true,
                IsReceiving = true,
                IsPickable = false
            };
            _context.Locations.Add(dockLoc);
        }

        var aisleA1 = await _context.Locations
            .FirstOrDefaultAsync(l => l.FacilityId == facility.Id && l.LocationCode == "AISLE-A1");
        if (aisleA1 == null)
        {
            aisleA1 = new Location
            {
                FacilityId = facility.Id,
                ZoneId = bulkZone.Id,
                LocationCode = "AISLE-A1",
                Barcode = "LOC-AISLE-A1",
                LocationType = LocationType.Aisle,
                AisleCode = "A1",
                IsActive = true
            };
            _context.Locations.Add(aisleA1);
            await _context.SaveChangesAsync();
        }

        var rackA101 = await _context.Locations
            .FirstOrDefaultAsync(l => l.FacilityId == facility.Id && l.LocationCode == "RACK-A1-01");
        if (rackA101 == null)
        {
            rackA101 = new Location
            {
                FacilityId = facility.Id,
                ZoneId = bulkZone.Id,
                ParentLocationId = aisleA1.Id,
                LocationCode = "RACK-A1-01",
                Barcode = "LOC-RACK-A1-01",
                LocationType = LocationType.Rack,
                AisleCode = "A1",
                RackCode = "01",
                IsActive = true
            };
            _context.Locations.Add(rackA101);
            await _context.SaveChangesAsync();
        }

        var shelfA101L1 = await _context.Locations
            .FirstOrDefaultAsync(l => l.FacilityId == facility.Id && l.LocationCode == "SHELF-A1-01-L1");
        if (shelfA101L1 == null)
        {
            shelfA101L1 = new Location
            {
                FacilityId = facility.Id,
                ZoneId = bulkZone.Id,
                ParentLocationId = rackA101.Id,
                LocationCode = "SHELF-A1-01-L1",
                Barcode = "LOC-SHELF-A1-01-L1",
                LocationType = LocationType.ShelfLevel,
                AisleCode = "A1",
                RackCode = "01",
                ShelfLevel = 1,
                IsActive = true
            };
            _context.Locations.Add(shelfA101L1);
            await _context.SaveChangesAsync();
        }

        var binA10101 = await _context.Locations
            .FirstOrDefaultAsync(l => l.FacilityId == facility.Id && l.LocationCode == "BIN-A1-01-01");
        if (binA10101 == null)
        {
            binA10101 = new Location
            {
                FacilityId = facility.Id,
                ZoneId = bulkZone.Id,
                ParentLocationId = shelfA101L1.Id,
                LocationCode = "BIN-A1-01-01",
                Barcode = "LOC-BIN-A1-01-01",
                LocationType = LocationType.Bin,
                AisleCode = "A1",
                RackCode = "01",
                ShelfLevel = 1,
                BinNumber = 1,
                PickSequence = 10,
                MaxWeightKg = 1500m,
                MaxVolumeCm3 = 2500000m,
                IsActive = true
            };
            _context.Locations.Add(binA10101);
        }

        var coldBin = await _context.Locations
            .FirstOrDefaultAsync(l => l.FacilityId == facility.Id && l.LocationCode == "BIN-COLD-01");
        if (coldBin == null)
        {
            coldBin = new Location
            {
                FacilityId = facility.Id,
                ZoneId = coldZone.Id,
                LocationCode = "BIN-COLD-01",
                Barcode = "LOC-BIN-COLD-01",
                LocationType = LocationType.Bin,
                PickSequence = 90,
                MaxWeightKg = 1000m,
                MaxVolumeCm3 = 1800000m,
                IsActive = true
            };
            _context.Locations.Add(coldBin);
        }

        await _context.SaveChangesAsync();

        // 3. Catalog Items (Apex Retail)
        var oliveOil = await _context.Items
            .FirstOrDefaultAsync(i => i.ClientId == client.Id && i.Sku == "APX-PREM-OLIVE-OIL");
        if (oliveOil == null)
        {
            oliveOil = new Item
            {
                ClientId = client.Id,
                Sku = "APX-PREM-OLIVE-OIL",
                Description = "Extra Virgin Olive Oil 500ml",
                StorageTempClass = StorageTempClass.Ambient,
                AbcVelocity = AbcVelocity.A,
                WeightKg = 0.85m,
                VolumeCm3 = 750m,
                LengthCm = 7.0m,
                WidthCm = 7.0m,
                HeightCm = 25.0m,
                UnitPrice = 14.50m,
                Status = ItemStatus.Active
            };

            oliveOil.PackagingUnits.Add(new PackagingUnit
            {
                Item = oliveOil,
                UnitLevel = PackagingUnitLevel.Each,
                UnitName = "EACH",
                ConversionQty = 1m
            });

            oliveOil.PackagingUnits.Add(new PackagingUnit
            {
                Item = oliveOil,
                UnitLevel = PackagingUnitLevel.Case,
                UnitName = "CASE-12",
                ConversionQty = 12m
            });

            oliveOil.Barcodes.Add(new ItemBarcode
            {
                Item = oliveOil,
                BarcodeValue = "8901234567890",
                Symbology = BarcodeSymbology.EAN_13,
                IsPrimary = true
            });

            _context.Items.Add(oliveOil);
        }

        var gelato = await _context.Items
            .FirstOrDefaultAsync(i => i.ClientId == client.Id && i.Sku == "APX-GELATO-VANILLA");
        if (gelato == null)
        {
            gelato = new Item
            {
                ClientId = client.Id,
                Sku = "APX-GELATO-VANILLA",
                Description = "Artisan Vanilla Bean Gelato 1L",
                StorageTempClass = StorageTempClass.Frozen,
                AbcVelocity = AbcVelocity.B,
                IsLotTracked = true,
                RequiresExpiryDate = true,
                WeightKg = 1.10m,
                VolumeCm3 = 1200m,
                LengthCm = 12.0m,
                WidthCm = 12.0m,
                HeightCm = 15.0m,
                UnitPrice = 8.95m,
                Status = ItemStatus.Active
            };

            gelato.PackagingUnits.Add(new PackagingUnit
            {
                Item = gelato,
                UnitLevel = PackagingUnitLevel.Each,
                UnitName = "EACH",
                ConversionQty = 1m
            });

            gelato.Barcodes.Add(new ItemBarcode
            {
                Item = gelato,
                BarcodeValue = "8909876543210",
                Symbology = BarcodeSymbology.EAN_13,
                IsPrimary = true
            });

            _context.Items.Add(gelato);
        }

        await _context.SaveChangesAsync();

        // 4. Inventory Lot
        var gelatoLot = await _context.InventoryLots
            .FirstOrDefaultAsync(l => l.ClientId == client.Id && l.LotNumber == "LOT-APX-GEL2026");
        if (gelatoLot == null)
        {
            gelatoLot = new InventoryLot
            {
                ClientId = client.Id,
                ItemId = gelato.Id,
                LotNumber = "LOT-APX-GEL2026",
                ManufactureDate = DateTime.UtcNow.AddDays(-10),
                ExpirationDate = DateTime.UtcNow.AddMonths(12),
                QaStatus = LotQaStatus.Released
            };
            _context.InventoryLots.Add(gelatoLot);
            await _context.SaveChangesAsync();
        }

        // 5. Stock on Hand & Ledger Transactions
        var dockStock = await _context.StockOnHand
            .FirstOrDefaultAsync(s => s.FacilityId == facility.Id && s.LocationId == dockLoc.Id && s.ItemId == oliveOil.Id);
        if (dockStock == null)
        {
            dockStock = new StockOnHand
            {
                ClientId = client.Id,
                FacilityId = facility.Id,
                LocationId = dockLoc.Id,
                ItemId = oliveOil.Id,
                QtyAvailable = 200m,
                Version = 1
            };
            _context.StockOnHand.Add(dockStock);

            _context.InventoryTransactions.Add(new InventoryTransaction
            {
                ClientId = client.Id,
                FacilityId = facility.Id,
                ItemId = oliveOil.Id,
                TxType = InventoryTxType.Receive,
                FromLocationId = null,
                ToLocationId = dockLoc.Id,
                Quantity = 200m,
                ReferenceType = "ASN",
                Notes = "Initial inbound receiving dock receipt"
            });
        }

        var bulkStock = await _context.StockOnHand
            .FirstOrDefaultAsync(s => s.FacilityId == facility.Id && s.LocationId == binA10101.Id && s.ItemId == oliveOil.Id);
        if (bulkStock == null)
        {
            bulkStock = new StockOnHand
            {
                ClientId = client.Id,
                FacilityId = facility.Id,
                LocationId = binA10101.Id,
                ItemId = oliveOil.Id,
                QtyAvailable = 50m,
                Version = 1
            };
            _context.StockOnHand.Add(bulkStock);

            _context.InventoryTransactions.Add(new InventoryTransaction
            {
                ClientId = client.Id,
                FacilityId = facility.Id,
                ItemId = oliveOil.Id,
                TxType = InventoryTxType.Putaway,
                FromLocationId = dockLoc.Id,
                ToLocationId = binA10101.Id,
                Quantity = 50m,
                ReferenceType = "PUTAWAY_TASK",
                Notes = "Stock putaway into high-bay bin"
            });
        }

        var coldStock = await _context.StockOnHand
            .FirstOrDefaultAsync(s => s.FacilityId == facility.Id && s.LocationId == coldBin.Id && s.ItemId == gelato.Id);
        if (coldStock == null)
        {
            coldStock = new StockOnHand
            {
                ClientId = client.Id,
                FacilityId = facility.Id,
                LocationId = coldBin.Id,
                ItemId = gelato.Id,
                LotId = gelatoLot.Id,
                QtyAvailable = 60m,
                Version = 1
            };
            _context.StockOnHand.Add(coldStock);

            _context.InventoryTransactions.Add(new InventoryTransaction
            {
                ClientId = client.Id,
                FacilityId = facility.Id,
                ItemId = gelato.Id,
                LotId = gelatoLot.Id,
                TxType = InventoryTxType.Receive,
                FromLocationId = null,
                ToLocationId = coldBin.Id,
                Quantity = 60m,
                ReferenceType = "ASN",
                Notes = "Cold chain verified intake into freezer bin"
            });
        }

        await _context.SaveChangesAsync();

        await SeedInboundOrdersAndAsnsAsync(facility, client, oliveOil, gelato);
    }

    private async Task SeedInboundOrdersAndAsnsAsync(Facility facility, Client client, Item oliveOil, Item gelato)
    {
        // 1. Supplier
        var supplier = await _context.Suppliers
            .FirstOrDefaultAsync(s => s.ClientId == client.Id && s.SupplierCode == "SUP-MED");
        if (supplier == null)
        {
            supplier = new Supplier
            {
                ClientId = client.Id,
                SupplierCode = "SUP-MED",
                SupplierName = "Mediterranean Olive Groves Inc",
                ContactName = "Marco Rossi",
                Email = "supply@med-olives.com",
                Phone = "+39-06-6987-1234",
                AddressLine1 = "Via Appia Nuova 450",
                City = "Rome",
                Country = "Italy",
                IsActive = true
            };
            _context.Suppliers.Add(supplier);
            await _context.SaveChangesAsync();
        }

        // 2. Purchase Order
        var po = await _context.PurchaseOrders
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.ClientId == client.Id && p.PoNumber == "PO-2026-0010");
        if (po == null)
        {
            po = new PurchaseOrder
            {
                ClientId = client.Id,
                FacilityId = facility.Id,
                SupplierId = supplier.Id,
                PoNumber = "PO-2026-0010",
                OrderDate = DateTime.UtcNow.AddDays(-3),
                ExpectedDeliveryDate = DateTime.UtcNow.AddDays(2),
                Status = PurchaseOrderStatus.Submitted,
                Currency = "USD",
                TotalEstimatedCost = 7250m,
                Notes = "Bi-weekly olive oil bulk procurement",
                Version = 2
            };

            po.Lines.Add(new PurchaseOrderLine
            {
                LineNumber = 1,
                ItemId = oliveOil.Id,
                ExpectedQty = 500m,
                ReceivedQty = 0m,
                UnitPrice = 14.50m,
                Currency = "USD",
                Status = PurchaseOrderLineStatus.Open,
                Notes = "Primary shipment line"
            });

            _context.PurchaseOrders.Add(po);
            await _context.SaveChangesAsync();
        }

        var poLine = po.Lines.First();

        // 3. PO-Backed Advance Shipping Notice (Partial shipment: 300 units out of 500 ordered)
        var asnPoBacked = await _context.AdvanceShippingNotices
            .FirstOrDefaultAsync(a => a.ClientId == client.Id && a.AsnNumber == "ASN-2026-0010");
        if (asnPoBacked == null)
        {
            asnPoBacked = new AdvanceShippingNotice
            {
                ClientId = client.Id,
                FacilityId = facility.Id,
                PurchaseOrderId = po.Id,
                SupplierId = supplier.Id,
                SupplierName = supplier.SupplierName,
                AsnNumber = "ASN-2026-0010",
                CarrierName = "FedEx Freight",
                TrackingReference = "FX-2026-INB-01",
                TrailerOrContainerNumber = "TRL-9921",
                EstimatedArrival = DateTime.UtcNow.AddDays(1),
                Status = AsnStatus.InTransit,
                Notes = "Container 1 of 2: 300 cases of olive oil in transit",
                Version = 2
            };

            asnPoBacked.Lines.Add(new AsnLineItem
            {
                PurchaseOrderLineId = poLine.Id,
                ItemId = oliveOil.Id,
                ExpectedQty = 300m,
                ReceivedQty = 0m,
                Notes = "Container 1 shipment line"
            });

            _context.AdvanceShippingNotices.Add(asnPoBacked);
        }

        // 4. Standalone Advance Shipping Notice (No PO link)
        var asnStandalone = await _context.AdvanceShippingNotices
            .FirstOrDefaultAsync(a => a.ClientId == client.Id && a.AsnNumber == "ASN-2026-0020");
        if (asnStandalone == null)
        {
            asnStandalone = new AdvanceShippingNotice
            {
                ClientId = client.Id,
                FacilityId = facility.Id,
                PurchaseOrderId = null,
                AsnNumber = "ASN-2026-0020",
                SupplierName = "Artisan Creamery SRL",
                CarrierName = "ThermoKing Cold Logistics",
                TrackingReference = "TK-COLD-900",
                TrailerOrContainerNumber = "REEFER-110",
                EstimatedArrival = DateTime.UtcNow.AddHours(12),
                Status = AsnStatus.Draft,
                Notes = "Direct manufacturer shipment of specialty frozen gelato",
                Version = 1
            };

            asnStandalone.Lines.Add(new AsnLineItem
            {
                ItemId = gelato.Id,
                ExpectedQty = 120m,
                ReceivedQty = 0m,
                ExpectedLotNumber = "LOT-APX-GEL2026-B",
                ManufactureDate = DateTime.UtcNow.AddDays(-5),
                ExpiryDate = DateTime.UtcNow.AddMonths(14),
                Notes = "Direct inbound frozen lot"
            });

            _context.AdvanceShippingNotices.Add(asnStandalone);
        }

        await _context.SaveChangesAsync();

        await SeedDockAndYardOperationsAsync(facility, client, asnPoBacked, asnStandalone);
    }

    private async Task SeedDockAndYardOperationsAsync(
        Facility facility,
        Client client,
        AdvanceShippingNotice asnPoBacked,
        AdvanceShippingNotice asnCold)
    {
        // 1. Dock Doors
        var door01 = await _context.DockDoors
            .FirstOrDefaultAsync(d => d.FacilityId == facility.Id && d.DoorNumber == "DOOR-01");
        if (door01 == null)
        {
            door01 = new DockDoor
            {
                FacilityId = facility.Id,
                DoorNumber = "DOOR-01",
                DoorType = DockDoorType.Inbound,
                Status = DockDoorStatus.Active,
                SupportsRefrigerated = true,
                MaxTrailerLengthFeet = 53,
                Notes = "Primary refrigerated inbound dock door"
            };
            _context.DockDoors.Add(door01);
        }

        var door02 = await _context.DockDoors
            .FirstOrDefaultAsync(d => d.FacilityId == facility.Id && d.DoorNumber == "DOOR-02");
        if (door02 == null)
        {
            door02 = new DockDoor
            {
                FacilityId = facility.Id,
                DoorNumber = "DOOR-02",
                DoorType = DockDoorType.Inbound,
                Status = DockDoorStatus.Active,
                SupportsRefrigerated = false,
                MaxTrailerLengthFeet = 53,
                Notes = "Standard ambient dry inbound dock door"
            };
            _context.DockDoors.Add(door02);
        }

        var door03 = await _context.DockDoors
            .FirstOrDefaultAsync(d => d.FacilityId == facility.Id && d.DoorNumber == "DOOR-03");
        if (door03 == null)
        {
            door03 = new DockDoor
            {
                FacilityId = facility.Id,
                DoorNumber = "DOOR-03",
                DoorType = DockDoorType.Combination,
                Status = DockDoorStatus.Active,
                SupportsRefrigerated = true,
                MaxTrailerLengthFeet = 53,
                Notes = "Combination inbound/outbound cross-dock door"
            };
            _context.DockDoors.Add(door03);
        }

        var door04 = await _context.DockDoors
            .FirstOrDefaultAsync(d => d.FacilityId == facility.Id && d.DoorNumber == "DOOR-04");
        if (door04 == null)
        {
            door04 = new DockDoor
            {
                FacilityId = facility.Id,
                DoorNumber = "DOOR-04",
                DoorType = DockDoorType.Inbound,
                Status = DockDoorStatus.Maintenance,
                SupportsRefrigerated = false,
                MaxTrailerLengthFeet = 48,
                Notes = "Leveler hydraulic cylinder undergoing maintenance"
            };
            _context.DockDoors.Add(door04);
        }

        await _context.SaveChangesAsync();

        // 2. Scheduled Dock Appointment
        var appointment = await _context.DockAppointments
            .FirstOrDefaultAsync(a => a.FacilityId == facility.Id && a.AppointmentNumber == "APT-FAC-ORD1-20261102-0001");
        if (appointment == null)
        {
            appointment = new DockAppointment
            {
                FacilityId = facility.Id,
                ClientId = client.Id,
                AsnId = asnPoBacked.Id,
                DockDoorId = door02.Id,
                AppointmentNumber = "APT-FAC-ORD1-20261102-0001",
                StartTime = DateTime.UtcNow.AddHours(2),
                EndTime = DateTime.UtcNow.AddHours(3),
                Status = DockAppointmentStatus.Scheduled,
                CarrierName = "Apex Fleet Express",
                TrailerNumber = "TR-APX-8800",
                DriverName = "Marcus Vance",
                DriverPhone = "312-555-8899",
                Notes = "Standard scheduled morning arrival"
            };
            _context.DockAppointments.Add(appointment);
            await _context.SaveChangesAsync();
        }

        // 3. Active Yard Visit (Checked-in trailer waiting in yard)
        var yardVisit = await _context.YardVisits
            .FirstOrDefaultAsync(y => y.FacilityId == facility.Id && y.VisitNumber == "YV-FAC-ORD1-20261102-0001");
        if (yardVisit == null)
        {
            yardVisit = new YardVisit
            {
                FacilityId = facility.Id,
                ClientId = client.Id,
                AsnId = asnPoBacked.Id,
                DockAppointmentId = appointment.Id,
                VisitNumber = "YV-FAC-ORD1-20261102-0001",
                CarrierName = "Apex Fleet Express",
                TrailerNumber = "TR-APX-8800",
                LicensePlate = "IL-8877-TR",
                DriverName = "Marcus Vance",
                DriverPhone = "312-555-8899",
                CheckInTime = DateTime.UtcNow.AddMinutes(-45),
                Status = YardVisitStatus.WaitingInYard,
                ParkingLocation = "Yard-Lane-03",
                Notes = "Trailer staged at Lane 3 awaiting dock assignment"
            };
            _context.YardVisits.Add(yardVisit);
            await _context.SaveChangesAsync();
        }

        // 4. Sample Receiving Session & Inspection Discrepancy
        var receivingSession = await _context.ReceivingSessions
            .FirstOrDefaultAsync(s => s.FacilityId == facility.Id && s.SessionNumber == "RCV-FAC-ORD1-20261102-0001");
        if (receivingSession == null)
        {
            var adminUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == "admin@3pllogistics.com");
            var adminUserId = adminUser?.Id ?? Guid.NewGuid();

            var stagingLoc = await _context.Locations
                .FirstOrDefaultAsync(l => l.FacilityId == facility.Id && l.IsReceiving);

            receivingSession = new ReceivingSession
            {
                FacilityId = facility.Id,
                DockDoorId = door01.Id,
                YardVisitId = yardVisit.Id,
                ClientId = client.Id,
                AsnId = asnPoBacked.Id,
                PurchaseOrderId = asnPoBacked.PurchaseOrderId,
                SessionNumber = "RCV-FAC-ORD1-20261102-0001",
                Status = ReceivingStatus.InProgress,
                StartedAtUtc = DateTime.UtcNow.AddMinutes(-30),
                ReceivedByUserId = adminUserId,
                InitialProbeTemperature = 4.2m,
                SealNumber = "SEAL-ORD-8811",
                SealIntact = true,
                Notes = "Morning scheduled intake for Mediterranean Olive Oil",
                IsBlindReceiving = false,
                RequiresSupervisorApproval = false
            };

            var line1 = new ReceiptLine
            {
                ReceivingSessionId = receivingSession.Id,
                ItemId = asnPoBacked.Lines.First().ItemId,
                PurchaseOrderLineId = asnPoBacked.Lines.First().PurchaseOrderLineId,
                AsnLineItemId = asnPoBacked.Lines.First().Id,
                Lpn = "LPN-20261102-0001",
                QuantityReceived = 140m,
                QuantityExpected = 300m,
                LotNumber = "LOT-MED-2026-A1",
                ManufactureDate = DateTime.UtcNow.AddDays(-10),
                ExpirationDate = DateTime.UtcNow.AddYears(2),
                Condition = ReceiptConditionStatus.Sound,
                StagingLocationId = stagingLoc?.Id,
                Notes = "Pallet 1 - 140 units verified sound"
            };

            var line2 = new ReceiptLine
            {
                ReceivingSessionId = receivingSession.Id,
                ItemId = asnPoBacked.Lines.First().ItemId,
                PurchaseOrderLineId = asnPoBacked.Lines.First().PurchaseOrderLineId,
                AsnLineItemId = asnPoBacked.Lines.First().Id,
                Lpn = "LPN-20261102-0002",
                QuantityReceived = 10m,
                QuantityExpected = 10m,
                LotNumber = "LOT-MED-2026-A1",
                ManufactureDate = DateTime.UtcNow.AddDays(-10),
                ExpirationDate = DateTime.UtcNow.AddYears(2),
                Condition = ReceiptConditionStatus.Damaged,
                StagingLocationId = stagingLoc?.Id,
                Notes = "Pallet 2 - 10 units damaged in transit"
            };

            receivingSession.Lines.Add(line1);
            receivingSession.Lines.Add(line2);

            var discrepancy = new ReceivingDiscrepancy
            {
                ReceivingSessionId = receivingSession.Id,
                ReceiptLineId = line2.Id,
                Type = DiscrepancyType.DamagedGoods,
                DiscrepantQuantity = 10m,
                Notes = "Corner crush on pallet carton #2 during transit. Routed to quarantine.",
                Status = DiscrepancyResolutionStatus.Quarantined,
                ReportedAtUtc = DateTime.UtcNow.AddMinutes(-20)
            };
            receivingSession.Discrepancies.Add(discrepancy);

            _context.ReceivingSessions.Add(receivingSession);
            await _context.SaveChangesAsync();
        }
    }

    private async Task SeedPutawayStrategyRulesAndTasksAsync(Facility facility, Client client)
    {
        if (!await _context.PutawayStrategyRules.AnyAsync())
        {
            var rule1 = new PutawayStrategyRule
            {
                ClientId = client.Id,
                FacilityId = facility.Id,
                Sequence = 1,
                RuleName = "Refrigerated Direct Cold Room",
                TempClassRequirement = StorageTempClass.Refrigerated,
                TargetVelocity = VelocityClass.A,
                PreferConsolidation = true,
                AllowDynamicOverflow = false,
                IsActive = true
            };

            var rule2 = new PutawayStrategyRule
            {
                ClientId = client.Id,
                FacilityId = facility.Id,
                Sequence = 2,
                RuleName = "High Velocity Pick Face Allocation",
                TempClassRequirement = StorageTempClass.Ambient,
                TargetVelocity = VelocityClass.A,
                PreferConsolidation = true,
                AllowDynamicOverflow = false,
                IsActive = true
            };

            var rule3 = new PutawayStrategyRule
            {
                ClientId = client.Id,
                FacilityId = facility.Id,
                Sequence = 3,
                RuleName = "Bulk Reserve High Bay Storage",
                TempClassRequirement = StorageTempClass.Ambient,
                TargetVelocity = VelocityClass.C,
                PreferConsolidation = true,
                AllowDynamicOverflow = true,
                IsActive = true
            };

            _context.PutawayStrategyRules.AddRange(rule1, rule2, rule3);
            await _context.SaveChangesAsync();
        }
    }

    private async Task SeedCrossDockEntitiesAsync(Facility facility, Client client)
    {
        if (!await _context.OutboundOrders.AnyAsync())
        {
            var item = await _context.Items.FirstOrDefaultAsync(i => i.ClientId == client.Id);
            if (item != null)
            {
                var customer = await _context.Customers.FirstOrDefaultAsync(c => c.ClientId == client.Id && c.CustomerCode == "CUST-METRO-01");
                if (customer == null)
                {
                    customer = new Customer
                    {
                        ClientId = client.Id,
                        CustomerCode = "CUST-METRO-01",
                        CustomerName = "Metro Hospital Network",
                        CustomerType = CustomerType.WholesaleDistributor,
                        ContactName = "Dr. Robert Vance",
                        Email = "logistics@metrohospital.org",
                        Phone = "312-555-0199",
                        DefaultAddressLine1 = "1200 Health Parkway",
                        DefaultCity = "Chicago",
                        DefaultStateOrProvince = "IL",
                        DefaultPostalCode = "60601",
                        DefaultCountryCode = "USA",
                        IsActive = true
                    };
                    _context.Customers.Add(customer);
                    await _context.SaveChangesAsync();
                }

                var outboundOrder = new OutboundOrder
                {
                    ClientId = client.Id,
                    FacilityId = facility.Id,
                    CustomerId = customer.Id,
                    OrderNumber = "ORD-OUT-20261102-001",
                    ExternalOrderReference = "PO-METRO-9871",
                    OrderType = OutboundOrderType.B2BSalesOrder,
                    ServiceLevel = CarrierServiceLevel.StandardGround,
                    CarrierCode = "UPS_FREIGHT",
                    CustomerName = "Metro Hospital Network",
                    Status = OutboundOrderStatus.Released,
                    Priority = 2,
                    RequiredShipDateUtc = DateTime.UtcNow.AddDays(1)
                };

                var address = new ShippingAddress
                {
                    OutboundOrderId = outboundOrder.Id,
                    RecipientName = "Receiving Bay 4",
                    CompanyName = "Metro Hospital Logistics Hub",
                    AddressLine1 = "1200 Health Parkway",
                    AddressLine2 = "Dock B",
                    City = "Chicago",
                    StateOrProvince = "IL",
                    PostalCode = "60601",
                    CountryCode = "USA",
                    PhoneNumber = "312-555-0199",
                    DeliveryInstructions = "Call dock coordinator 30 min before arrival"
                };
                outboundOrder.SetShippingAddress(address);

                var line = new OutboundOrderLine
                {
                    OutboundOrderId = outboundOrder.Id,
                    ItemId = item.Id,
                    LineNumber = 1,
                    QuantityOrdered = 25m,
                    QuantityAllocated = 0m,
                    Status = OutboundOrderLineStatus.Pending
                };

                outboundOrder.Lines.Add(line);
                _context.OutboundOrders.Add(outboundOrder);
                await _context.SaveChangesAsync();
            }
        }
    }
}

