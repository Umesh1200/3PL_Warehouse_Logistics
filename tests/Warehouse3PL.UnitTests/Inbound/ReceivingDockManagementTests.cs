using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Inbound.DockManagement.Appointments.Commands.CancelDockAppointment;
using Warehouse3PL.Application.Features.Inbound.DockManagement.Appointments.Commands.RescheduleDockAppointment;
using Warehouse3PL.Application.Features.Inbound.DockManagement.Appointments.Commands.ScheduleDockAppointment;
using Warehouse3PL.Application.Features.Inbound.DockManagement.Appointments.Queries.GetDockAppointments;
using Warehouse3PL.Application.Features.Inbound.DockManagement.DockDoors.Commands.CreateDockDoor;
using Warehouse3PL.Application.Features.Inbound.DockManagement.DockDoors.Commands.UpdateDockDoorStatus;
using Warehouse3PL.Application.Features.Inbound.DockManagement.DockDoors.Queries.GetDockDoors;
using Warehouse3PL.Application.Features.Inbound.DockManagement.Yard.Commands.AssignDockDoor;
using Warehouse3PL.Application.Features.Inbound.DockManagement.Yard.Commands.CheckInVehicle;
using Warehouse3PL.Application.Features.Inbound.DockManagement.Yard.Commands.ConfirmAtDock;
using Warehouse3PL.Application.Features.Inbound.DockManagement.Yard.Commands.RecordVehicleDeparture;
using Warehouse3PL.Application.Features.Inbound.DockManagement.Yard.Commands.ReleaseDockDoor;
using Warehouse3PL.Application.Features.Inbound.DockManagement.Yard.Queries.GetReceivingQueue;
using Warehouse3PL.Application.Features.Inbound.DockManagement.Yard.Queries.GetYardVisits;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Dock;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.UnitTests.Common;
using Xunit;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.UnitTests.Inbound;

public class ReceivingDockManagementTests
{
    private static async Task<TestCurrentUserContext> SeedStaffAdminAsync(ApplicationDbContext db, Guid facilityId)
    {
        var role = new Role { RoleName = SystemRoles.SuperAdmin };
        var userId = Guid.NewGuid();
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        db.Users.Add(new User
        {
            Id = userId,
            Email = $"dock-admin-{userId:N}@nexus3pl.test",
            PasswordHash = "hash",
            FirstName = "Dock",
            LastName = "Admin",
            UserType = UserType.Staff,
            Status = UserStatus.Active,
            AssignedFacilityId = facilityId
        });
        db.UserRoles.Add(new UserRole { UserId = userId, RoleId = role.Id });
        await db.SaveChangesAsync();

        return new TestCurrentUserContext
        {
            UserId = userId,
            UserType = UserType.Staff,
            FacilityId = facilityId,
            RolesList = [SystemRoles.SuperAdmin],
            PermissionsList = Permissions.GetAllPermissions().ToList()
        };
    }

    [Fact]
    public async Task CreateDockDoor_WithUniqueDoorNumber_PersistsActiveDoor()
    {
        var dbName = Guid.NewGuid().ToString();
        var facilityId = Guid.NewGuid();
        TestCurrentUserContext adminUser;

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            adminUser = await SeedStaffAdminAsync(seed, facilityId);
        }

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, adminUser, dbName);
        var handler = new CreateDockDoorCommandHandler(context, new WarehouseMasterAuthorization(context, adminUser));

        var result = await handler.Handle(new CreateDockDoorCommand
        {
            FacilityId = facilityId,
            DoorNumber = "DOOR-01",
            DoorType = DockDoorType.Inbound,
            Status = DockDoorStatus.Active,
            SupportsRefrigerated = true,
            MaxTrailerLengthFeet = 53,
            Notes = "Primary refrigerated inbound door"
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("DOOR-01", result.DoorNumber);
        Assert.True(result.IsOperational);
        Assert.True(result.SupportsRefrigerated);
        Assert.Equal(53, result.MaxTrailerLengthFeet);
    }

    [Fact]
    public async Task CreateDockDoor_DuplicateDoorNumberInSameFacility_ThrowsValidationException()
    {
        var dbName = Guid.NewGuid().ToString();
        var facilityId = Guid.NewGuid();
        TestCurrentUserContext adminUser;

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.DockDoors.Add(new DockDoor
            {
                FacilityId = facilityId,
                DoorNumber = "DOOR-01",
                DoorType = DockDoorType.Inbound,
                Status = DockDoorStatus.Active
            });
            adminUser = await SeedStaffAdminAsync(seed, facilityId);
        }

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, adminUser, dbName);
        var handler = new CreateDockDoorCommandHandler(context, new WarehouseMasterAuthorization(context, adminUser));

        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await handler.Handle(new CreateDockDoorCommand
            {
                FacilityId = facilityId,
                DoorNumber = "door-01", // case-insensitive duplicate check
                DoorType = DockDoorType.Combination
            }, CancellationToken.None);
        });
    }

    [Fact]
    public async Task UpdateDockDoorStatus_ChangesStatusAndOperationalState()
    {
        var dbName = Guid.NewGuid().ToString();
        var facilityId = Guid.NewGuid();
        var doorId = Guid.NewGuid();
        TestCurrentUserContext adminUser;

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.DockDoors.Add(new DockDoor
            {
                Id = doorId,
                FacilityId = facilityId,
                DoorNumber = "DOOR-02",
                DoorType = DockDoorType.Inbound,
                Status = DockDoorStatus.Active
            });
            adminUser = await SeedStaffAdminAsync(seed, facilityId);
        }

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, adminUser, dbName);
        var handler = new UpdateDockDoorStatusCommandHandler(context, new WarehouseMasterAuthorization(context, adminUser));

        var result = await handler.Handle(new UpdateDockDoorStatusCommand
        {
            Id = doorId,
            Status = DockDoorStatus.Maintenance,
            Notes = "Hydraulic leveler undergoing maintenance"
        }, CancellationToken.None);

        Assert.Equal(DockDoorStatus.Maintenance, result.Status);
        Assert.False(result.IsOperational);
        Assert.Equal("Hydraulic leveler undergoing maintenance", result.Notes);
    }

    [Fact]
    public async Task ScheduleDockAppointment_WithValidSlot_CreatesScheduledAppointment()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var asnId = Guid.NewGuid();
        var doorId = Guid.NewGuid();

        var startTime = DateTime.UtcNow.AddHours(2);
        var endTime = startTime.AddHours(1);

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex Retail", AccountCode = "CL-APEX" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = facilityId, Status = FacilityAccessStatus.Active });
            seed.DockDoors.Add(new DockDoor
            {
                Id = doorId,
                FacilityId = facilityId,
                DoorNumber = "DOOR-01",
                Status = DockDoorStatus.Active,
                SupportsRefrigerated = true
            });
            seed.AdvanceShippingNotices.Add(new AdvanceShippingNotice
            {
                Id = asnId,
                ClientId = clientId,
                FacilityId = facilityId,
                AsnNumber = "ASN-2026-01",
                CarrierName = "Apex Fleet",
                Status = AsnStatus.Draft
            });
            await seed.SaveChangesAsync();
        }

        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), TenantId = clientId, UserType = UserType.Client };
        using var context = TestDbContextFactory.Create(new TestTenantProvider { TenantId = clientId }, currentUser, dbName);
        var handler = new ScheduleDockAppointmentCommandHandler(context, currentUser);

        var result = await handler.Handle(new ScheduleDockAppointmentCommand
        {
            FacilityId = facilityId,
            AsnId = asnId,
            DockDoorId = doorId,
            StartTime = startTime,
            EndTime = endTime,
            TrailerNumber = "TR-100",
            DriverName = "John Doe"
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.StartsWith("APT-FAC-ORD1-", result.AppointmentNumber);
        Assert.Equal(DockAppointmentStatus.Scheduled, result.Status);
        Assert.Equal("DOOR-01", result.DoorNumber);
        Assert.Equal("ASN-2026-01", result.AsnNumber);
    }

    [Fact]
    public async Task ScheduleDockAppointment_WhenFacilityMismatchWithAsn_ThrowsValidationException()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityA = Guid.NewGuid();
        var facilityB = Guid.NewGuid();
        var asnId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityA, FacilityCode = "FAC-A", Name = "Facility A" });
            seed.Facilities.Add(new Facility { Id = facilityB, FacilityCode = "FAC-B", Name = "Facility B" });
            seed.AdvanceShippingNotices.Add(new AdvanceShippingNotice
            {
                Id = asnId,
                ClientId = clientId,
                FacilityId = facilityA,
                AsnNumber = "ASN-A-01",
                Status = AsnStatus.Draft
            });
            await seed.SaveChangesAsync();
        }

        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), TenantId = clientId, UserType = UserType.Client };
        using var context = TestDbContextFactory.Create(new TestTenantProvider { TenantId = clientId }, currentUser, dbName);
        var handler = new ScheduleDockAppointmentCommandHandler(context, currentUser);

        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await handler.Handle(new ScheduleDockAppointmentCommand
            {
                FacilityId = facilityB, // Mismatch with ASN facility A
                AsnId = asnId,
                StartTime = DateTime.UtcNow.AddHours(1),
                EndTime = DateTime.UtcNow.AddHours(2)
            }, CancellationToken.None);
        });
    }

    [Fact]
    public async Task ScheduleDockAppointment_OverlappingSlotOnSameDoor_ThrowsValidationException()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var asnId1 = Guid.NewGuid();
        var asnId2 = Guid.NewGuid();
        var doorId = Guid.NewGuid();

        var baseTime = DateTime.UtcNow.AddHours(3);

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.DockDoors.Add(new DockDoor { Id = doorId, FacilityId = facilityId, DoorNumber = "DOOR-01", Status = DockDoorStatus.Active });
            seed.AdvanceShippingNotices.Add(new AdvanceShippingNotice { Id = asnId1, ClientId = clientId, FacilityId = facilityId, AsnNumber = "ASN-1", Status = AsnStatus.Draft });
            seed.AdvanceShippingNotices.Add(new AdvanceShippingNotice { Id = asnId2, ClientId = clientId, FacilityId = facilityId, AsnNumber = "ASN-2", Status = AsnStatus.Draft });

            seed.DockAppointments.Add(new DockAppointment
            {
                FacilityId = facilityId,
                ClientId = clientId,
                AsnId = asnId1,
                DockDoorId = doorId,
                AppointmentNumber = "APT-EXISTING-01",
                StartTime = baseTime,
                EndTime = baseTime.AddHours(2), // 3:00 to 5:00
                Status = DockAppointmentStatus.Scheduled
            });
            await seed.SaveChangesAsync();
        }

        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), TenantId = clientId, UserType = UserType.Client };
        using var context = TestDbContextFactory.Create(new TestTenantProvider { TenantId = clientId }, currentUser, dbName);
        var handler = new ScheduleDockAppointmentCommandHandler(context, currentUser);

        // Attempting overlapping appointment: 4:00 to 6:00 on the same door
        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await handler.Handle(new ScheduleDockAppointmentCommand
            {
                FacilityId = facilityId,
                AsnId = asnId2,
                DockDoorId = doorId,
                StartTime = baseTime.AddHours(1),
                EndTime = baseTime.AddHours(3)
            }, CancellationToken.None);
        });
    }

    [Fact]
    public async Task RescheduleDockAppointment_ValidWindow_UpdatesTimes()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var asnId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        var doorId = Guid.NewGuid();

        var initialStart = DateTime.UtcNow.AddHours(2);

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.DockDoors.Add(new DockDoor { Id = doorId, FacilityId = facilityId, DoorNumber = "DOOR-01", Status = DockDoorStatus.Active });
            seed.AdvanceShippingNotices.Add(new AdvanceShippingNotice { Id = asnId, ClientId = clientId, FacilityId = facilityId, AsnNumber = "ASN-RESCHED", Status = AsnStatus.Draft });
            seed.DockAppointments.Add(new DockAppointment
            {
                Id = appointmentId,
                FacilityId = facilityId,
                ClientId = clientId,
                AsnId = asnId,
                DockDoorId = doorId,
                AppointmentNumber = "APT-RESCHED-01",
                StartTime = initialStart,
                EndTime = initialStart.AddHours(1),
                Status = DockAppointmentStatus.Scheduled,
                Version = 1
            });
            await seed.SaveChangesAsync();
        }

        var newStart = DateTime.UtcNow.AddHours(5);
        var newEnd = newStart.AddHours(1);

        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), TenantId = clientId, UserType = UserType.Client };
        using var context = TestDbContextFactory.Create(new TestTenantProvider { TenantId = clientId }, currentUser, dbName);
        var handler = new RescheduleDockAppointmentCommandHandler(context, currentUser);

        var result = await handler.Handle(new RescheduleDockAppointmentCommand
        {
            Id = appointmentId,
            StartTime = newStart,
            EndTime = newEnd
        }, CancellationToken.None);

        Assert.Equal(newStart, result.StartTime);
        Assert.Equal(newEnd, result.EndTime);
        Assert.Equal(2u, result.Version);
    }

    [Fact]
    public async Task CancelDockAppointment_ScheduledAppointment_CancelsWithReason()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var asnId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.AdvanceShippingNotices.Add(new AdvanceShippingNotice { Id = asnId, ClientId = clientId, FacilityId = facilityId, AsnNumber = "ASN-CANCEL", Status = AsnStatus.Draft });
            seed.DockAppointments.Add(new DockAppointment
            {
                Id = appointmentId,
                FacilityId = facilityId,
                ClientId = clientId,
                AsnId = asnId,
                AppointmentNumber = "APT-CANCEL-01",
                StartTime = DateTime.UtcNow.AddHours(1),
                EndTime = DateTime.UtcNow.AddHours(2),
                Status = DockAppointmentStatus.Scheduled,
                Version = 1
            });
            await seed.SaveChangesAsync();
        }

        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), TenantId = clientId, UserType = UserType.Client };
        using var context = TestDbContextFactory.Create(new TestTenantProvider { TenantId = clientId }, currentUser, dbName);
        var handler = new CancelDockAppointmentCommandHandler(context, currentUser);

        var result = await handler.Handle(new CancelDockAppointmentCommand
        {
            Id = appointmentId,
            Reason = "Carrier truck breakdown on highway"
        }, CancellationToken.None);

        Assert.Equal(DockAppointmentStatus.Cancelled, result.Status);
        Assert.Equal("Carrier truck breakdown on highway", result.CancellationReason);
        Assert.NotNull(result.CancelledAt);
        Assert.Equal(2u, result.Version);
    }

    [Fact]
    public async Task CheckInVehicle_ValidDetails_CreatesYardVisitAndMarksAppointmentCheckedIn()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var asnId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-INBOUND-01", Description = "Inbound Goods" });
            seed.AdvanceShippingNotices.Add(new AdvanceShippingNotice
            {
                Id = asnId,
                ClientId = clientId,
                FacilityId = facilityId,
                AsnNumber = "ASN-CHECKIN",
                Status = AsnStatus.InTransit,
                Lines = [new AsnLineItem { ItemId = itemId, ExpectedQty = 100 }]
            });
            seed.DockAppointments.Add(new DockAppointment
            {
                Id = appointmentId,
                FacilityId = facilityId,
                ClientId = clientId,
                AsnId = asnId,
                AppointmentNumber = "APT-CHECKIN-01",
                StartTime = DateTime.UtcNow.AddMinutes(30),
                EndTime = DateTime.UtcNow.AddHours(1),
                Status = DockAppointmentStatus.Scheduled
            });
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName);
        var handler = new CheckInVehicleCommandHandler(context);

        var result = await handler.Handle(new CheckInVehicleCommand
        {
            FacilityId = facilityId,
            DockAppointmentId = appointmentId,
            CarrierName = "Swift Transportation",
            TrailerNumber = "TR-9988",
            LicensePlate = "IL-77665",
            DriverName = "Michael Scott",
            DriverPhone = "312-555-0199",
            ParkingLocation = "Lane-04"
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.StartsWith("YV-FAC-ORD1-", result.VisitNumber);
        Assert.Equal(YardVisitStatus.CheckedIn, result.Status);
        Assert.Equal("TR-9988", result.TrailerNumber);
        Assert.Equal("Lane-04", result.ParkingLocation);

        // Verify linked appointment marked checked in
        var updatedApt = await context.DockAppointments.FindAsync(appointmentId);
        Assert.Equal(DockAppointmentStatus.CheckedIn, updatedApt!.Status);

        // Verify linked ASN transitioned to Arrived
        var updatedAsn = await context.AdvanceShippingNotices.FindAsync(asnId);
        Assert.Equal(AsnStatus.Arrived, updatedAsn!.Status);
        Assert.NotNull(updatedAsn.ActualArrival);
    }

    [Fact]
    public async Task CheckInVehicle_DuplicateActiveTrailer_ThrowsValidationException()
    {
        var dbName = Guid.NewGuid().ToString();
        var facilityId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.YardVisits.Add(new YardVisit
            {
                FacilityId = facilityId,
                VisitNumber = "YV-ACTIVE-01",
                CarrierName = "FedEx",
                TrailerNumber = "TR-DUPLICATE",
                Status = YardVisitStatus.WaitingInYard
            });
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName);
        var handler = new CheckInVehicleCommandHandler(context);

        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await handler.Handle(new CheckInVehicleCommand
            {
                FacilityId = facilityId,
                CarrierName = "FedEx",
                TrailerNumber = "tr-duplicate" // Case-insensitive duplicate check
            }, CancellationToken.None);
        });
    }

    [Fact]
    public async Task AssignDockDoor_OperationalDoor_CreatesAssignmentAndTransitionsVisitToAssignedToDock()
    {
        var dbName = Guid.NewGuid().ToString();
        var facilityId = Guid.NewGuid();
        var yardVisitId = Guid.NewGuid();
        var doorId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.DockDoors.Add(new DockDoor
            {
                Id = doorId,
                FacilityId = facilityId,
                DoorNumber = "DOOR-01",
                Status = DockDoorStatus.Active
            });
            seed.YardVisits.Add(new YardVisit
            {
                Id = yardVisitId,
                FacilityId = facilityId,
                VisitNumber = "YV-ASSIGN-01",
                CarrierName = "DHL",
                TrailerNumber = "TR-DHL-01",
                Status = YardVisitStatus.CheckedIn
            });
            await seed.SaveChangesAsync();
        }

        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };
        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, currentUser, dbName);
        var handler = new AssignDockDoorCommandHandler(context, currentUser);

        var result = await handler.Handle(new AssignDockDoorCommand
        {
            YardVisitId = yardVisitId,
            DockDoorId = doorId,
            Notes = "Dock door assigned for immediate unloading"
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("DOOR-01", result.DoorNumber);
        Assert.Equal(DockAssignmentStatus.Assigned, result.Status);

        var visit = await context.YardVisits.FindAsync(yardVisitId);
        Assert.Equal(YardVisitStatus.AssignedToDock, visit!.Status);
    }

    [Fact]
    public async Task AssignDockDoor_WhenDoorInactiveOrMaintenance_ThrowsValidationException()
    {
        var dbName = Guid.NewGuid().ToString();
        var facilityId = Guid.NewGuid();
        var yardVisitId = Guid.NewGuid();
        var doorId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.DockDoors.Add(new DockDoor
            {
                Id = doorId,
                FacilityId = facilityId,
                DoorNumber = "DOOR-MAINT",
                Status = DockDoorStatus.Maintenance // Inactive / under maintenance
            });
            seed.YardVisits.Add(new YardVisit
            {
                Id = yardVisitId,
                FacilityId = facilityId,
                VisitNumber = "YV-MAINT-01",
                CarrierName = "DHL",
                TrailerNumber = "TR-DHL-02",
                Status = YardVisitStatus.WaitingInYard
            });
            await seed.SaveChangesAsync();
        }

        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };
        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, currentUser, dbName);
        var handler = new AssignDockDoorCommandHandler(context, currentUser);

        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await handler.Handle(new AssignDockDoorCommand
            {
                YardVisitId = yardVisitId,
                DockDoorId = doorId
            }, CancellationToken.None);
        });
    }

    [Fact]
    public async Task AssignDockDoor_WhenDoorAlreadyOccupied_ThrowsValidationException()
    {
        var dbName = Guid.NewGuid().ToString();
        var facilityId = Guid.NewGuid();
        var doorId = Guid.NewGuid();
        var visitAId = Guid.NewGuid();
        var visitBId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.DockDoors.Add(new DockDoor { Id = doorId, FacilityId = facilityId, DoorNumber = "DOOR-OCCUPIED", Status = DockDoorStatus.Active });

            seed.YardVisits.Add(new YardVisit { Id = visitAId, FacilityId = facilityId, VisitNumber = "YV-OCC-A", TrailerNumber = "TR-A", Status = YardVisitStatus.AssignedToDock });
            seed.YardVisits.Add(new YardVisit { Id = visitBId, FacilityId = facilityId, VisitNumber = "YV-OCC-B", TrailerNumber = "TR-B", Status = YardVisitStatus.WaitingInYard });

            seed.DockAssignments.Add(new DockAssignment
            {
                FacilityId = facilityId,
                DockDoorId = doorId,
                YardVisitId = visitAId,
                Status = DockAssignmentStatus.Assigned // Door is currently assigned to Visit A
            });
            await seed.SaveChangesAsync();
        }

        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };
        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, currentUser, dbName);
        var handler = new AssignDockDoorCommandHandler(context, currentUser);

        // Attempting to assign occupied door to Visit B
        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await handler.Handle(new AssignDockDoorCommand
            {
                YardVisitId = visitBId,
                DockDoorId = doorId
            }, CancellationToken.None);
        });
    }

    [Fact]
    public async Task AssignDockDoor_RefrigeratedAsnToAmbientOnlyDoor_ThrowsValidationException()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var doorId = Guid.NewGuid();
        var yardVisitId = Guid.NewGuid();
        var asnId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.DockDoors.Add(new DockDoor
            {
                Id = doorId,
                FacilityId = facilityId,
                DoorNumber = "DOOR-AMBIENT",
                Status = DockDoorStatus.Active,
                SupportsRefrigerated = false // Ambient only!
            });

            var coldItem = new Item
            {
                Id = itemId,
                ClientId = clientId,
                Sku = "SKU-COLD-VACCINE",
                Description = "Refrigerated Medical Supplies",
                StorageTempClass = StorageTempClass.Refrigerated // Cold chain!
            };
            seed.Items.Add(coldItem);

            var asn = new AdvanceShippingNotice
            {
                Id = asnId,
                ClientId = clientId,
                FacilityId = facilityId,
                AsnNumber = "ASN-COLD-01",
                Status = AsnStatus.Arrived,
                Lines = [new AsnLineItem { ItemId = itemId, ExpectedQty = 50 }]
            };
            seed.AdvanceShippingNotices.Add(asn);

            seed.YardVisits.Add(new YardVisit
            {
                Id = yardVisitId,
                FacilityId = facilityId,
                ClientId = clientId,
                AsnId = asnId,
                VisitNumber = "YV-COLD-01",
                TrailerNumber = "TR-REEFER-01",
                Status = YardVisitStatus.CheckedIn
            });
            await seed.SaveChangesAsync();
        }

        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };
        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, currentUser, dbName);
        var handler = new AssignDockDoorCommandHandler(context, currentUser);

        // Attempting to assign refrigerated shipment to ambient-only door
        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await handler.Handle(new AssignDockDoorCommand
            {
                YardVisitId = yardVisitId,
                DockDoorId = doorId
            }, CancellationToken.None);
        });
    }

    [Fact]
    public async Task DockLifecycle_ConfirmAtDock_Release_AndDepart_ExecutesCleanly()
    {
        var dbName = Guid.NewGuid().ToString();
        var facilityId = Guid.NewGuid();
        var doorId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.DockDoors.Add(new DockDoor { Id = doorId, FacilityId = facilityId, DoorNumber = "DOOR-01", Status = DockDoorStatus.Active });
            seed.YardVisits.Add(new YardVisit
            {
                Id = visitId,
                FacilityId = facilityId,
                VisitNumber = "YV-FLOW-01",
                TrailerNumber = "TR-FLOW-01",
                Status = YardVisitStatus.AssignedToDock
            });
            seed.DockAssignments.Add(new DockAssignment
            {
                Id = assignmentId,
                FacilityId = facilityId,
                DockDoorId = doorId,
                YardVisitId = visitId,
                Status = DockAssignmentStatus.Assigned
            });
            await seed.SaveChangesAsync();
        }

        // 1. Confirm at dock
        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var confirmHandler = new ConfirmAtDockCommandHandler(context);
            var confirmResult = await confirmHandler.Handle(new ConfirmAtDockCommand { DockAssignmentId = assignmentId }, CancellationToken.None);

            Assert.Equal(DockAssignmentStatus.AtDock, confirmResult.Status);
            Assert.NotNull(confirmResult.ArrivedAtDock);

            var visit = await context.YardVisits.FindAsync(visitId);
            Assert.Equal(YardVisitStatus.AtDock, visit!.Status);
        }

        // 2. Release dock door
        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var releaseHandler = new ReleaseDockDoorCommandHandler(context);
            var releaseResult = await releaseHandler.Handle(new ReleaseDockDoorCommand
            {
                DockAssignmentId = assignmentId,
                Notes = "Unloading completed, door free"
            }, CancellationToken.None);

            Assert.Equal(DockAssignmentStatus.Completed, releaseResult.Status);
            Assert.NotNull(releaseResult.ReleasedAt);

            var visit = await context.YardVisits.FindAsync(visitId);
            Assert.Equal(YardVisitStatus.DockReleased, visit!.Status);
        }

        // 3. Depart from facility yard
        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var departHandler = new RecordVehicleDepartureCommandHandler(context);
            var departResult = await departHandler.Handle(new RecordVehicleDepartureCommand { YardVisitId = visitId }, CancellationToken.None);

            Assert.Equal(YardVisitStatus.Departed, departResult.Status);
            Assert.NotNull(departResult.DepartureTime);
        }
    }

    [Fact]
    public async Task RecordVehicleDeparture_WhileStillAtDock_ThrowsDomainException()
    {
        var dbName = Guid.NewGuid().ToString();
        var facilityId = Guid.NewGuid();
        var visitId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.YardVisits.Add(new YardVisit
            {
                Id = visitId,
                FacilityId = facilityId,
                VisitNumber = "YV-AT-DOCK",
                TrailerNumber = "TR-LOCKED",
                Status = YardVisitStatus.AtDock // Truck is still parked at the dock!
            });
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName);
        var handler = new RecordVehicleDepartureCommandHandler(context);

        // Cannot depart while still AtDock
        await Assert.ThrowsAsync<DomainException>(async () =>
        {
            await handler.Handle(new RecordVehicleDepartureCommand { YardVisitId = visitId }, CancellationToken.None);
        });
    }

    [Fact]
    public async Task GetReceivingQueue_ReturnsPrioritizedActiveShipments()
    {
        var dbName = Guid.NewGuid().ToString();
        var facilityId = Guid.NewGuid();
        var doorId = Guid.NewGuid();
        var visit1Id = Guid.NewGuid();
        var visit2Id = Guid.NewGuid();
        var departedVisitId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.DockDoors.Add(new DockDoor { Id = doorId, FacilityId = facilityId, DoorNumber = "DOOR-01", Status = DockDoorStatus.Active });

            // Active visit 1: Checked in 30 mins ago
            seed.YardVisits.Add(new YardVisit
            {
                Id = visit1Id,
                FacilityId = facilityId,
                VisitNumber = "YV-QUEUE-01",
                CarrierName = "Carrier One",
                TrailerNumber = "TR-QUEUE-1",
                CheckInTime = DateTime.UtcNow.AddMinutes(-30),
                Status = YardVisitStatus.CheckedIn
            });

            // Active visit 2: At dock
            seed.YardVisits.Add(new YardVisit
            {
                Id = visit2Id,
                FacilityId = facilityId,
                VisitNumber = "YV-QUEUE-02",
                CarrierName = "Carrier Two",
                TrailerNumber = "TR-QUEUE-2",
                CheckInTime = DateTime.UtcNow.AddMinutes(-60),
                Status = YardVisitStatus.AtDock
            });
            seed.DockAssignments.Add(new DockAssignment
            {
                FacilityId = facilityId,
                DockDoorId = doorId,
                YardVisitId = visit2Id,
                Status = DockAssignmentStatus.AtDock
            });

            // Inactive departed visit: Should be excluded from active queue
            seed.YardVisits.Add(new YardVisit
            {
                Id = departedVisitId,
                FacilityId = facilityId,
                VisitNumber = "YV-DEPARTED",
                CarrierName = "Old Carrier",
                TrailerNumber = "TR-OLD",
                CheckInTime = DateTime.UtcNow.AddHours(-4),
                DepartureTime = DateTime.UtcNow.AddHours(-1),
                Status = YardVisitStatus.Departed
            });

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName);
        var handler = new GetReceivingQueueQueryHandler(context);

        var queue = await handler.Handle(new GetReceivingQueueQuery { FacilityId = facilityId }, CancellationToken.None);

        Assert.Equal(2, queue.Count);
        Assert.DoesNotContain(queue, item => item.VisitNumber == "YV-DEPARTED");

        var atDockItem = queue.First(item => item.VisitNumber == "YV-QUEUE-02");
        Assert.Equal("DOOR-01", atDockItem.AssignedDockDoorNumber);
        Assert.True(atDockItem.WaitDurationMinutes >= 59);

        var checkedInItem = queue.First(item => item.VisitNumber == "YV-QUEUE-01");
        Assert.Null(checkedInItem.AssignedDockDoorNumber);
        Assert.True(checkedInItem.WaitDurationMinutes >= 29);
    }
}
