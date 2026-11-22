# Phase 7.4 — Outbound, Transportation & Courier Operations: Pre-Implementation Specification & Execution Plan

**Repository:** `Umesh1200/3PL_Warehouse_Logistics`  
**Branch:** `feature/phase-7-enterprise-frontend`  
**Baseline HEAD:** [`dab06108bd3d933749281c5b0c53a33c426047c5`](https://github.com/Umesh1200/3PL_Warehouse_Logistics/commit/dab06108bd3d933749281c5b0c53a33c426047c5)  
**Accepted Phase 7.3 Implementation Baseline:** [`1a67a71640df28dfbd1c5cdd5363f2a0e77a45c5`](https://github.com/Umesh1200/3PL_Warehouse_Logistics/commit/1a67a71640df28dfbd1c5cdd5363f2a0e77a45c5)  
**Status:** **PRE-IMPLEMENTATION SPECIFICATION GATE (PLANNING AUTHORIZED — IMPLEMENTATION HELD)**  
**Spec Date:** November 22, 2026  

---

> [!IMPORTANT]
> **Implementation Boundary Notice:**  
> This specification is a **read-only planning artifact** derived directly from codebase inspection at commit `dab0610`. No application code, database migrations, or frontend routes for Phase 7.4 have been modified. Existing implementation (`[EXISTING]`) is strictly distinguished from proposed additions (`[PROPOSED ADDITION]`) and proposed hardening fixes (`[PROPOSED HARDENING]`). Accepted Phase 7.3 deliverables remain untouched.

---

## 1. End-to-End Outbound, Transportation & Courier Workflow Map

The domain engine spans **13 linked operational sub-workflows** across three lifecycle planes: **Warehouse Fulfillment (Phase 4.1–4.4)**, **Dispatch & Carrier Handover (Phase 4.4)**, and **Courier Transit, POD & Exceptions (Phase 4.5)**.

```
[1. ORDERS] Create / Bulk Import / Hold / Release / Cancel / Stock Check
     │  (Draft -> Released -> OnHold <-> PreHold -> Cancelled)
     ▼
[2. ALLOCATION] Hard Inventory Reservation (FIFO / FEFO / Manual) + Deallocate / Release
     │  (PartiallyAllocated / Allocated; creates InventoryAllocation + AllocationShortage)
     ▼
[3. PICK WAVES] Generate Pick Wave (DiscreteOrder / BatchPick / ZonePick / BulkPick) & Release
     │  (Planning -> Released -> InProgress -> Completed; Serpentine Route Optimization)
     ▼
[4. RF PICKING] Operator Task Claim / Execution-Only Next Task -> Scan Location, SKU, Lot, Tote LPN
     │  (Pending -> Assigned -> InProgress -> Completed; StockOnHand Bin -> HandlingUnit Tote)
     ├──► [5. SHORT PICKS] Partial/Zero Pick + ShortPickReason -> PickException + Auto CycleCount + Location Lock
     ▼
[6. PACKING] Start/Resume PackingSession at PackingStation -> Scan Tote LPN -> Carton LPN -> Close Weight -> Release Tote -> Complete Session
     │  (PackingSession: Active -> Completed; Package: Open -> Packed)
     ├──► [7. CARTONIZATION] 3D Bounding-Box & Max-Weight Box Recommendation (GetCartonizationRecommendationQuery)
     ▼
[8. CARRIER RATES & LABELS] Quote Rates -> Generate Shipping Label (ZPL/PDF, decoupled outside DB tx) / Void Label
     │  (Package: Packed -> LabelPrinted -> [Void] -> Packed)
     ▼
[9. SHIPMENT CONSOLIDATION] Create Shipment (Client + Facility + Carrier + ServiceLevel) -> Add/Remove Packages -> Stage -> Trailer Load
     │  (Package: LabelPrinted -> Staged -> Loaded; Shipment: Draft -> ReadyForDispatch)
     ▼
[10. BILL OF LADING (BOL)] Generate Immutable BOL Snapshot (Trailer, Seal, Package Count, Weight, Tracking #s)
     │  (Idempotent retrieval if already issued for non-voided shipment)
     ▼
[11. DISPATCH CUSTODY] Confirm Driver Handover (DriverName, License, Signature, Trailer, Seal, IdempotencyKey)
     │  (Shipment -> Dispatched; Completed Orders -> Completed; 0 stock mutation; records ShipmentCustodyTransfer)
     ▼
[12. CARRIER TRACKING & COURIER POD] Polling Sync / Webhook Ingestion -> Milestone Rollup -> Driver/Courier POD (Signature/Photo/GPS) -> Delivery Confirmation
     │  (TrackingMilestoneStatus: PendingHandover -> Accepted -> InTransit -> OutForDelivery -> Delivered; OrderDeliveryStatus rollup)
     └──► [13. DELIVERY EXCEPTIONS & RTS] Driver/Carrier Exception -> Customer Contact / Reattempt / Address Fix / Depot Redirect / Resolve / Void / RTS Webhook
```

### Key State-Machine Separation (`OutboundOrderStatus` vs. `OrderDeliveryStatus`)
As verified in the domain enums:
- **Warehouse Fulfillment Plane (`OutboundOrderStatus`):** Tracks `Draft (1) -> Released (2) -> Allocated (3) / PartiallyAllocated (8) -> InProgress (4) -> Completed (5)` (or `OnHold (7)` / `Cancelled (6)`). Once `ConfirmDispatch` completes, warehouse stock custody terminates (`OutboundOrderStatus.Completed`).
- **Carrier Transit Plane (`OrderDeliveryStatus`):** Tracks post-dispatch last-mile progression across all child packages: `PendingShipment (1) -> Dispatched (2) -> InTransit (3) -> PartiallyDelivered (4) -> Delivered (5)` or `DeliveryException (6)`.

---

## 2. Authoritative API, DTO, Enum & Idempotency Inventory

### 2.1 Existing WebApi Endpoints (`[EXISTING]`)

All existing outbound endpoints live across **six** controllers in `src/Presentation/Warehouse3PL.WebApi/Controllers/`.

#### A. `OutboundOrdersController` (`/api/v1/outbound/orders`)
| Method & Route | Request Contract | Response Contract | Status |
| :--- | :--- | :--- | :---: |
| `POST /` | `CreateOutboundOrderCommand` | `201`: `OutboundOrderDto` | `[EXISTING]` |
| `POST /bulk-import` | `ImportOutboundOrdersBulkCommand` | `200`: `OrderImportBatchDto` | `[EXISTING]` |
| `GET /` | `GetOutboundOrdersQuery` (`FacilityId?`, `Status?`, `OrderType?`, `Priority?`, `SearchTerm?`, `FromDateUtc?`, `ToDateUtc?`, `PageNumber`, `PageSize`) | `200`: `PagedResult<OutboundOrderDto>` | `[EXISTING]` |
| `GET /{id:guid}` | Route `Guid id` | `200`: `OutboundOrderDto` | `[EXISTING]` |
| `GET /{id:guid}/stock-availability` | Route `Guid id` | `200`: `StockAvailabilityAssessmentDto` | `[EXISTING]` |
| `PATCH /{id:guid}/hold` | `HoldOrderRequest(Reason)` | `200`: `OutboundOrderDto` | `[EXISTING]` |
| `PATCH /{id:guid}/release` | `ReleaseOrderRequest(Notes?)` | `200`: `OutboundOrderDto` | `[EXISTING]` |
| `PATCH /{id:guid}/cancel` | `CancelOrderRequest(Reason)` | `200`: `OutboundOrderDto` | `[EXISTING]` |
| `POST /{id:guid}/allocate` | `AllocateOrderRequest(StrategyOverride?, IdempotencyKey?)` | `200`: `AllocationExecutionResultDto` | `[EXISTING]` |
| `POST /{id:guid}/deallocate` | `DeallocateOrderRequest(Reason)` | `200`: `DeallocationExecutionResultDto` | `[EXISTING]` |
| `POST /allocations/{allocationId:guid}/release` | `ReleaseSpecificAllocationRequest(QuantityToRelease?, Reason)` | `200`: `ReleaseAllocationResultDto` | `[EXISTING]` |

#### B. `PickWavesController` (`/api/v1/outbound/pick-waves`)
| Method & Route | Request Contract | Response Contract | Status |
| :--- | :--- | :--- | :---: |
| `POST /` | `GeneratePickWaveCommand(PickWaveGenerationRequest Request)` *(note nested `Request` wrapper)* | `201`: `PickWaveDto` | `[EXISTING]` |
| `GET /` | `GetPickWavesQuery(FacilityId, Status?, WaveType?, PageNumber, PageSize)` | `200`: `PagedResult<PickWaveDto>` | `[EXISTING]` |
| `GET /{id:guid}` | Route `Guid id` | `200`: `PickWaveDto` | `[EXISTING]` |
| `POST /{id:guid}/release` | Route `Guid id` | `200`: `PickWaveDto` | `[EXISTING]` |

#### C. `PickTasksController` (`/api/v1/outbound/pick-tasks`)
| Method & Route | Request Contract | Response Contract | Status |
| :--- | :--- | :--- | :---: |
| `POST /{id:guid}/assign` | `AssignPickTaskRequest(OperatorUserId)` | `200`: `PickTaskDto` | `[EXISTING]` |
| `POST /{id:guid}/confirm` | `ConfirmPickApiRequest(ScannedLocationBarcode, ScannedItemBarcode, ScannedLotNumber?, ScannedToteLpn, Quantity, IdempotencyKey?)` | `200`: `PickConfirmationResult` | `[EXISTING]` |
| `POST /{id:guid}/short-pick` | `ReportShortPickApiRequest(QuantityPicked, Reason, Notes?, ScannedLocationBarcode?, ScannedItemBarcode?, ScannedToteLpn?, IdempotencyKey?)` | `200`: `ShortPickExecutionResult` | `[EXISTING]` |
| `GET /{id:guid}` | Route `Guid id` *(queries `DbContext` directly in controller)* | `200`: `PickTaskDto` | `[EXISTING]` |

#### D. `PackingController` (`/api/v1/outbound/packing`)
| Method & Route | Request Contract | Response Contract | Status |
| :--- | :--- | :--- | :---: |
| `POST /sessions/start` | `StartPackingSessionApiRequest(FacilityId, PackingStationLocationId, OutboundOrderId)` | `200`: `PackingSessionResult` | `[EXISTING]` |
| `POST /cartonization/recommend` | `CartonizationApiRequest(ClientId, FacilityId, OutboundOrderId?, Items?, PreferredPackagingMaterialId?, VolumeUtilizationFactor)` | `200`: `CartonizationRecommendation` | `[EXISTING]` |
| `POST /packages/create` | `CreatePackageApiRequest(PackingSessionId, PackagingMaterialId?, OverrideReason?)` | `200`: `CreatePackageResult` | `[EXISTING]` |
| `POST /items/pack` | `PackItemApiRequest(PackingSessionId, PackageId, ScannedToteLpn, ScannedItemBarcode, ScannedLotNumber?, Quantity, IdempotencyKey?)` | `200`: `PackItemResult` | `[EXISTING]` |
| `POST /packages/{packageId:guid}/close` | `ClosePackageApiRequest(ScaleGrossWeightKg)` | `200`: `ClosePackageResult` | `[EXISTING]` |
| `POST /totes/release` | `ReleaseToteApiRequest(ToteLpn, FacilityId)` | `200`: `ReleaseToteResult` | `[EXISTING]` |
| `POST /sessions/{sessionId:guid}/complete` | Route `Guid sessionId` | `200`: `CompletePackingSessionResult` | `[EXISTING]` |

#### E. `ShipmentsController` (`/api/v1/outbound/shipments`)
| Method & Route | Request Contract | Response Contract | Status |
| :--- | :--- | :--- | :---: |
| `POST /` | `CreateShipmentApiRequest(ClientId, FacilityId, CarrierCode, ServiceLevel, StagingLocationId?)` | `200`: `CreateShipmentResult` | `[EXISTING]` |
| `GET /{id:guid}` | Route `Guid id` | `200`: `ShipmentDto` | `[EXISTING]` |
| `POST /{id:guid}/packages` | `AddPackageToShipmentApiRequest(PackageId)` | `200`: `AddPackageToShipmentResult` | `[EXISTING]` |
| `DELETE /{id:guid}/packages/{packageId:guid}` | `RemovePackageFromShipmentApiRequest(Reason)` *(note DELETE with body)* | `200`: `RemovePackageFromShipmentResult` | `[EXISTING]` |
| `POST /packages/{packageId:guid}/stage` | `StagePackageApiRequest(StagingLocationId)` | `200`: `StagePackageResult` | `[EXISTING]` |
| `POST /packages/{packageId:guid}/load` | `LoadPackageApiRequest(ShipmentId?, TrailerNumber?, DockLocationId?)` | `200`: `LoadPackageResult` | `[EXISTING]` |
| `POST /{id:guid}/bol` | `GenerateBolApiRequest(TrailerNumber?, SealNumber?, SpecialInstructions?)` | `200`: `GenerateBolResult` | `[EXISTING]` |
| `GET /bol/{id:guid}` | Route `Guid id` | `200`: `BillOfLadingDto` | `[EXISTING]` |
| `POST /{id:guid}/dispatch` | `ConfirmDispatchCustodyApiRequest(DriverName, DriverLicenseNumber?, DriverSignature, TrailerNumber?, SealNumber?, Notes?, IdempotencyKey?)` | `200`: `ConfirmDispatchCustodyResult` | `[EXISTING]` |

#### F. `ShippingController` (`/api/v1/outbound/shipping`)
| Method & Route | Request Contract | Response Contract | Status |
| :--- | :--- | :--- | :---: |
| `POST /rates` | `GetPackageRatesApiRequest(PackageId, PreferredCarrierCode?, PreferredServiceLevel?)` | `200`: `IReadOnlyList<CarrierRateQuote>` | `[EXISTING]` |
| `POST /labels/generate` | `GenerateShippingLabelApiRequest(PackageId, CarrierCode, ServiceLevel, LabelFormat?, IdempotencyKey?, SimulateFailure)` | `200`: `GenerateShippingLabelResult` | `[EXISTING]` |
| `POST /labels/{labelId:guid}/void` | `VoidShippingLabelApiRequest(Reason, IdempotencyKey?, SimulateFailure)` | `200`: `VoidShippingLabelResult` | `[EXISTING]` |
| `GET /labels/{labelId:guid}` | Route `Guid labelId` | `200`: `ShippingLabelDto` / `404` | `[EXISTING]` |
| `GET /packages/{packageId:guid}/track` | Route `Guid packageId` | `200`: `CarrierTrackingResponse` | `[EXISTING]` |
| `POST /returns/tracking/webhook` | `IngestReturnTrackingRequest` (`[AllowAnonymous]`) | `200`: `IngestReturnTrackingResult` | `[EXISTING]` |

---

### 2.2 Existing Application CQRS Handlers Lacking HTTP Controller Routes (`[EXISTING HANDLER — NO HTTP ROUTE]`)

Inspection of `src/Core/Warehouse3PL.Application/Features/Outbound/Tracking/` confirms **Phase 4.5 Courier, Tracking, POD, Delivery Exception, and RTS Application handlers and Infrastructure services are already implemented and unit-tested**, yet no WebApi controller exposes them over HTTP:

| Category | Existing MediatR Commands / Queries (`Application/Features/Outbound/Tracking/`) | Underlying DTO / Result | Status |
| :--- | :--- | :--- | :---: |
| **Tracking Timeline & Sync** | `GetPackageTrackingTimelineQuery(PackageId)`, `GetOrderDeliveryStatusQuery(OrderId)`, `SyncShipmentTrackingCommand(ShipmentId)`, `IngestCarrierTrackingEventCommand(IngestTrackingEventRequest)` | `PackageTrackingTimelineDto`, `EvaluateOrderDeliveryRollupResult`, `SyncShipmentTrackingResult`, `IngestTrackingEventResult` | `[EXISTING HANDLER — NO HTTP ROUTE]` |
| **Proof of Delivery (POD)** | `SubmitProofOfDeliveryCommand`, `SupersedeProofOfDeliveryCommand`, `GetProofOfDeliveryQuery(PackageId?, ProofOfDeliveryId?, UrlExpiry?)`, `ConfirmPackageDeliveryCommand` | `SubmitProofOfDeliveryResult`, `ProofOfDeliveryDto`, `DeliveryConfirmationResult` | `[EXISTING HANDLER — NO HTTP ROUTE]` |
| **Delivery Exceptions & Courier Workflow** | `ReportDriverDeliveryExceptionCommand`, `IngestCarrierDeliveryExceptionCommand`, `ResolveDeliveryExceptionCommand`, `ReopenDeliveryExceptionCommand`, `VoidDeliveryExceptionCommand`, `ScheduleDeliveryReattemptCommand`, `RescheduleDeliveryReattemptCommand`, `CancelDeliveryReattemptCommand`, `RecordCustomerContactCommand`, `UpdateDeliveryAddressCommand`, `RedirectToCarrierDepotCommand` | `DeliveryExceptionResult` | `[EXISTING HANDLER — NO HTTP ROUTE]` |

---

### 2.3 Missing Read / Queue Endpoints Needed for Frontend Workstations (`[PROPOSED ADDITION]`)

| Proposed Route | Purpose | Detailed Design Ref | Status |
| :--- | :--- | :--- | :---: |
| `GET /api/v1/outbound/pick-tasks` | Supervisor/viewer paginated pick-task queue filtered by facility, wave, order, status, assigned operator, search term | Section 5.1 | `[PROPOSED ADDITION]` |
| `GET /api/v1/outbound/pick-tasks/next` | Execution-only RF floor next-task query (prioritized by assigned-to-caller, order/wave priority, pick sequence) returning `200` or `204` | Section 5.2 | `[PROPOSED ADDITION]` |
| `GET /api/v1/outbound/shipments` | Paginated shipment list & consolidation workspace query filtered by facility, status, carrier, search term | Section 5.3 | `[PROPOSED ADDITION]` |
| `GET /api/v1/outbound/packing/sessions` | Active/suspended packing sessions list for station resumption & supervisor visibility | Section 5.4 | `[PROPOSED ADDITION]` |
| `GET /api/v1/outbound/packing/sessions/{id:guid}` | Full packing session detail including packed packages, line progress, and remaining order demand | Section 5.4 | `[PROPOSED ADDITION]` |
| `GET /api/v1/outbound/packing/packages` | Paginated packed/labeled/staged packages lookup for packing bench, label reprint/void, and shipment consolidation picker | Section 5.4 | `[PROPOSED ADDITION]` |
| `GET /api/v1/outbound/dispatch/board` | Unified dock dispatch board summarizing `ReadyForDispatch` / `Dispatched` shipments, BOL status, and dock staging status | Section 5.5 | `[PROPOSED ADDITION]` |
| `GET /api/v1/outbound/dispatch/packages/{packageId:guid}/timeline` & courier POD / Exception controller routes | Expose existing Phase 4.5 `Tracking`, `POD`, `DeliveryConfirmation`, and `DeliveryException` CQRS handlers on dedicated `DispatchController` | Section 5.5 | `[PROPOSED ADDITION]` |

---

### 2.4 Authoritative Enum Contracts (Wire Serialization: `JsonStringEnumConverter`)

All WebApi endpoints serialize/deserialize enums via `JsonStringEnumConverter` (string names on the wire, integer backing values in EF Core):

- **`OutboundOrderType`:** `B2BSalesOrder (1)`, `StockTransferOrder (2)`, `EcommerceFulfillment (3)`
- **`OutboundOrderStatus`:** `Draft (1)`, `Released (2)`, `Allocated (3)`, `InProgress (4)`, `Completed (5)`, `Cancelled (6)`, `OnHold (7)`, `PartiallyAllocated (8)`
- **`OutboundOrderLineStatus`:** `Pending (1)`, `PartiallyAllocated (2)`, `Allocated (3)`, `Completed (4)`, `Cancelled (5)`
- **`AllocationStrategyType`:** `FIFO (1)`, `FEFO (2)`, `Manual (3)`
- **`AllocationStatus`:** `Allocated (1)`, `PartiallyPicked (2)`, `Picked (3)`, `Released (4)`, `Cancelled (5)`
- **`PickWaveType`:** `DiscreteOrder (1)`, `BatchPick (2)`, `ZonePick (3)`, `BulkPick (4)`
- **`PickWaveStatus`:** `Planning (1)`, `Released (2)`, `InProgress (3)`, `Completed (4)`, `Cancelled (5)`
- **`PickTaskStatus`:** `Pending (1)`, `Assigned (2)`, `InProgress (3)`, `Completed (4)`, `ShortPicked (5)`, `Cancelled (6)`
- **`ShortPickReason`:** `BinEmpty (1)`, `DamagedStock (2)`, `BarcodeUnreadable (3)`, `WrongItemInBin (4)`, `LotExpired (5)`, `BinInaccessible (6)`
- **`PackingSessionStatus`:** `Active (1)`, `Completed (2)`, `Suspended (3)`, `Aborted (4)`
- **`PackageStatus`:** `Open (1)`, `Packed (2)`, `LabelPrinted (3)`, `Staged (4)`, `Loaded (5)`, `Cancelled (6)`
- **`ShipmentStatus`:** `Draft (1)`, `ReadyForDispatch (2)`, `Dispatched (3)`, `Cancelled (4)`
- **`ShippingLabelStatus`:** `Generated (1)`, `Printed (2)`, `Voided (3)`
- **`CarrierServiceLevel`:** `StandardGround (1)`, `NextDayAir (2)`, `SecondDayAir (3)`, `PriorityOvernight (4)`, `FreightLTL (5)`, `FreightFTL (6)`, `CustomerPickup (7)`
- **`TrackingMilestoneStatus`:** `PendingHandover (1)`, `Accepted (2)`, `InTransit (3)`, `OutForDelivery (4)`, `Delivered (5)`, `DeliveryException (6)`, `ReturnedToSender (7)`
- **`OrderDeliveryStatus`:** `PendingShipment (1)`, `Dispatched (2)`, `InTransit (3)`, `PartiallyDelivered (4)`, `Delivered (5)`, `DeliveryException (6)`

---

### 2.5 Idempotency Key Matrix

| Operation | Idempotency Mechanism Observed in Code | Frontend Requirement |
| :--- | :--- | :--- |
| `POST /api/v1/outbound/orders/{id}/allocate` | Explicit `string? IdempotencyKey` checked against `order.AllocationIdempotencyKey` (`IsDuplicateRequest = true` on match) | Generate UUIDv4 per user click; disable button while in-flight; preserve key on network retry. |
| `POST /api/v1/outbound/orders/bulk-import` | Order-level `ExternalOrderReference` deduplication recorded via `IsDuplicate` per `OrderImportBatchRowDto` | Surface duplicate row flags distinctly in import summary table. |
| `POST /api/v1/outbound/pick-tasks/{id}/confirm` | `string? IdempotencyKey` + terminal state check matching `QuantityPicked` & `TargetToteBarcode` | Generate per-scan `IdempotencyKey` ref (`inFlightRef`) to protect keyboard-wedge double-scans. |
| `POST /api/v1/outbound/pick-tasks/{id}/short-pick` | `string? IdempotencyKey` on `ReportShortPickCommand` | Single-flight submit guard + key preserved across retries. |
| `POST /api/v1/outbound/packing/items/pack` | `string? IdempotencyKey` on `PackItemCommand` | Single-flight submit guard + per-scan `IdempotencyKey`. |
| `POST /api/v1/outbound/packing/sessions/start` | Single active/suspended session check on `OutboundOrderId` + `OperatorUserId`; resumes if same operator | Display session lock banner if another operator holds an active session. |
| `POST /api/v1/outbound/shipping/labels/generate` & `/void` | `string? IdempotencyKey` forwarded into `CarrierOperationLog` / carrier request | Single-flight lock; preserve key on retry after simulated/transient failure (`POST` outside DB tx). |
| `POST /api/v1/outbound/shipments/packages/{id}/load` | Natural idempotency when package is already `Loaded` into target shipment | Safe on duplicate dock door scanner beep. |
| `POST /api/v1/outbound/shipments/{id}/bol` | Returns existing active `BillOfLading` snapshot if already issued | Render existing BOL badge + download/print view instead of erroring. |
| `POST /api/v1/outbound/shipments/{id}/dispatch` | `string? IdempotencyKey` on `ConfirmDispatchCustodyCommand`; idempotent when already `Dispatched` | Lock custody modal submission; support safe retry. |
| Tracking Ingestion / POD Submit (`IShipmentTrackingService`, `IProofOfDeliveryService`) | Event SHA-256 fingerprint (`ClientId + CarrierCode + EventFingerprint`) & explicit `IdempotencyKey` / `CarrierEventId` | Pass deterministic `IdempotencyKey` on handheld POD capture. |

---

## 3. Authoritative Security Matrix (`Permissions.cs`, `SystemRoles.cs`, Scope)

### 3.1 Role Baseline Notice (`DbInitializer.cs` & `CurrentUserContext.cs`)
Inspection of `DbInitializer.cs`, `SystemRoles.cs`, and `PermissionAuthorizationHandler.cs` establishes two strict rules:
1. **`SuperAdmin` vs. All Other Roles:** `PermissionAuthorizationHandler` grants all `[HasPermission]` checks **only** when `ICurrentUserContext.IsSuperAdmin` (`IsInRole(SystemRoles.SuperAdmin)`) is true. Any other role (`OperationsManager`, `WarehouseOperator`, `Driver`, `ClientAdmin`, `ClientUser`, or custom `Admin`) **must hold the exact explicit `PermissionKey` grant**.
2. **Current Seeding Reality in `DbInitializer.cs`:** Only `SuperAdmin` (all permissions) and `ClientAdmin` (`clients:view`, `outbound:view`, `outbound:create`, `outbound:pick`, `outbound:ship`, `dispatch:view`, etc.) currently receive seeded `RolePermission` rows during `SeedRolesAndPermissionsAsync()`. `OperationsManager` is defined in `SystemRoles.cs` without default seeded `RolePermission` rows in `DbInitializer.cs`, and `ClientUser`, `WarehouseOperator`, and `Driver` are created as empty role shells unless permissions are assigned explicitly. **Do not assume any permission implies another.**

### 3.2 Complete Endpoint-to-Permission & Scope Matrix

| Workflow Area | Endpoint | Status | Exact `Permissions.cs` Constant | Permitted Roles (When Explicitly Granted) | Tenant Scope | Facility Scope |
| :--- | :--- | :---: | :--- | :--- | :--- | :--- |
| **Orders** | `GET /api/v1/outbound/orders`, `GET /{id}`, `GET /{id}/stock-availability` | `[EXISTING]` | `Permissions.Outbound.OrdersView` (`outbound:orders:view`) | `SuperAdmin`, `OperationsManager`, `ClientAdmin`, `ClientUser` | Staff: Requested/All authorized; Client: Own `TenantId` only | Staff: Assigned facility (unless CrossFacility); Client: Authorized `ClientFacilityAccess` |
| **Orders** | `POST /api/v1/outbound/orders` | `[EXISTING]` | `Permissions.Outbound.OrdersCreate` (`outbound:orders:create`) | `SuperAdmin`, `OperationsManager`, `ClientAdmin`, `ClientUser` | Same as above | Same as above (`DestinationFacilityId` too on Stock Transfer) |
| **Orders** | `POST /api/v1/outbound/orders/bulk-import` | `[EXISTING]` | `Permissions.Outbound.OrdersImport` (`outbound:orders:import`) | `SuperAdmin`, `OperationsManager`, `ClientAdmin` | Same as above | Same as above |
| **Orders** | `PATCH /api/v1/outbound/orders/{id}/hold`, `/release` | `[EXISTING]` | `Permissions.Outbound.OrdersManage` (`outbound:orders:manage`) | `SuperAdmin`, `OperationsManager` *(Client only if explicitly granted & owns order)* | Order `ClientId == TenantId` if Client | Order `FacilityId == Staff FacilityId` |
| **Orders** | `PATCH /api/v1/outbound/orders/{id}/cancel` | `[EXISTING]` | `Permissions.Outbound.OrdersCancel` (`outbound:orders:cancel`) | `SuperAdmin`, `OperationsManager`, `ClientAdmin` | Same as above | Same as above |
| **Allocation** | `POST /api/v1/outbound/orders/{id}/allocate`, `/deallocate`, `POST /allocations/{id}/release` | `[EXISTING]` | `Permissions.Outbound.OrdersAllocate` (`outbound:orders:allocate`) | `SuperAdmin`, `OperationsManager` | Staff only *(unless Client explicitly granted)* | Order `FacilityId == Staff FacilityId` |
| **Pick Waves** | `GET /api/v1/outbound/pick-waves`, `GET /{id}` | `[EXISTING]` | `Permissions.Outbound.WavesView` (`outbound:waves:view`) | `SuperAdmin`, `OperationsManager`, `WarehouseOperator` | Staff: Requested/All; Client: Own `TenantId` | Wave `FacilityId == Staff FacilityId` |
| **Pick Waves** | `POST /api/v1/outbound/pick-waves`, `POST /{id}/release` | `[EXISTING]` | `Permissions.Outbound.WavesManage` (`outbound:waves:manage`) | `SuperAdmin`, `OperationsManager` | Warehouse Staff only | Wave `FacilityId == Staff FacilityId` |
| **Pick Tasks (Queue)** | `GET /api/v1/outbound/pick-tasks/{id}` | `[EXISTING]` | `Permissions.Outbound.TasksView` (`outbound:tasks:view`) | `SuperAdmin`, `OperationsManager`, `WarehouseOperator` | Staff: All/Requested; Client: Own `TenantId` | Task `FacilityId == Staff FacilityId` |
| **Pick Tasks (Queue)** | `GET /api/v1/outbound/pick-tasks` | `[PROPOSED ADDITION]` | `Permissions.Outbound.TasksView` (`outbound:tasks:view`) | `SuperAdmin`, `OperationsManager`, `WarehouseOperator` (if granted queue view) | Same as above | Same as above |
| **Pick Tasks (Manage)** | `POST /api/v1/outbound/pick-tasks/{id}/assign` | `[EXISTING]` | `Permissions.Outbound.TasksManage` (`outbound:tasks:manage`) | `SuperAdmin`, `OperationsManager` | Warehouse Staff only | Task `FacilityId == Staff FacilityId` |
| **Pick Tasks (RF)** | `GET /api/v1/outbound/pick-tasks/next` | `[PROPOSED ADDITION]` | `Permissions.Outbound.Pick` (`outbound:pick`) | `SuperAdmin`, `WarehouseOperator` | Warehouse Staff (`UserType.Staff`) | Strictly requires valid `FacilityId` claim matching query/header |
| **Pick Tasks (RF)** | `POST /api/v1/outbound/pick-tasks/{id}/confirm`, `/short-pick` | `[EXISTING]` | `Permissions.Outbound.Pick` (`outbound:pick`) | `SuperAdmin`, `WarehouseOperator` | Warehouse Staff (`UserType.Staff`) | Task `FacilityId == Staff FacilityId` |
| **Packing** | `POST /api/v1/outbound/packing/...` (all 7 mutations) & `GET /sessions`, `/sessions/{id}`, `/packages` | `[EXISTING]` + `[PROPOSED]` | `Permissions.Outbound.Pack` (`outbound:pack`) *(list views may allow `Outbound.View` \| `Outbound.Pack` \| `Outbound.Ship` — see Gap Note below)* | `SuperAdmin`, `WarehouseOperator`, `OperationsManager` | Staff (`UserType.Staff`) for bench execution | Session/Package `FacilityId == Staff FacilityId` |
| **Consolidation & Dispatch** | `POST /api/v1/outbound/shipments`, `GET /{id}`, `POST / DELETE packages`, `stage`, `load`, `bol`, `dispatch` | `[EXISTING]` | `Permissions.Outbound.Ship` (`outbound:ship`) | `SuperAdmin`, `OperationsManager`, `WarehouseOperator` | Staff (`UserType.Staff`) for dock handover | Shipment/Package `FacilityId == Staff FacilityId` |
| **Consolidation List & Board** | `GET /api/v1/outbound/shipments`, `GET /api/v1/outbound/dispatch/board` | `[PROPOSED ADDITION]` | `Permissions.Outbound.Ship` or `Permissions.Dispatch.View` *(requires alignment — see Section 3.3)* | `SuperAdmin`, `OperationsManager`, `ClientAdmin` (read-only own tenant) | Staff: Requested/All; Client: Own `TenantId` | Staff: Assigned facility; Client: Authorized facilities |
| **Carrier Rates & Labels** | `POST /rates`, `POST /labels/generate`, `POST /labels/{id}/void`, `GET /labels/{id}` | `[EXISTING]` | `Permissions.Outbound.Ship` (`outbound:ship`) | `SuperAdmin`, `OperationsManager`, `WarehouseOperator` | Warehouse Staff (`UserType.Staff`) | Package `FacilityId == Staff FacilityId` |
| **Package Tracking** | `GET /api/v1/outbound/shipping/packages/{id}/track` | `[EXISTING]` | `Permissions.Outbound.Ship` (`outbound:ship`) *(consider adding `Dispatch.View` policy OR keep `Outbound.Ship` + client-scoped timeline endpoint)* | `SuperAdmin`, `OperationsManager`, `WarehouseOperator`, `ClientAdmin` | Staff or Owning Client (`Package.ClientId`) | Staff: `Package.FacilityId`; Client: `ClientFacilityAccess` |
| **RTS Webhook** | `POST /api/v1/outbound/shipping/returns/tracking/webhook` | `[EXISTING]` | `[AllowAnonymous]` *(Carrier webhook — **requires HMAC/shared-secret header validation before prod**)* | Carrier Webhook Callback | Derived from tracking # (`TenantIdOverride` guard if provided) | Derived from package |
| **Courier Tracking / POD / Exceptions** | `GET timeline`, `GET order-delivery`, `POST sync`, `POST delivery/confirm`, `POST pod/submit`, `POST pod/supersede`, `GET pod`, `POST exceptions/driver-report`, `PATCH exceptions/{id}/...` | `[PROPOSED ADDITION]` (exposing existing CQRS handlers) | Split explicitly:<br>• Read: `Permissions.Dispatch.View`<br>• Courier delivery/POD/driver-exception: `Permissions.Dispatch.ExecuteDelivery`<br>• Dispatch run / exception resolution / contact / reattempt: `Permissions.Dispatch.ManageRuns` | `SuperAdmin`, `OperationsManager` (`ManageRuns`/`View`), `Driver` (`ExecuteDelivery` + custody check), `ClientAdmin` (`View` read-only own tenant) | Staff: Assigned facility; Client: Own `TenantId` only; Driver: Assigned/custody packages only | Facility check for Staff; Custody/Driver check for `UserType.Driver` |

### 3.3 Non-Equivalence Warning (Strict RBAC Boundary)
- Holding `outbound:orders:view` does **NOT** grant `outbound:orders:allocate`, `outbound:waves:manage`, `outbound:pick`, `outbound:pack`, or `outbound:ship`.
- Holding `outbound:tasks:view` allows **queue inspection only** and must **NOT** authorize `/pick-tasks/next`, `/confirm`, or `/short-pick` (`outbound:pick` only).
- Holding `outbound:tasks:manage` or `outbound:waves:manage` does **NOT** imply RF floor pick execution (`outbound:pick`).
- Holding `analytics:transportation:view` grants read-only Phase 6.4 carrier analytics only — it does **NOT** grant `dispatch:view` or `outbound:ship`.
- Note existing mismatch in `Sidebar.tsx`: `/app/outbound` currently only checks `['outbound:orders:view', 'outbound:ship']` (blocking pick/wave/pack operators), and `/app/transportation` checks `['dispatch:view', 'analytics:transportation:view']` (while `TrackPackage` checks `outbound:ship`). Section 6 specifies the exact corrected frontend permission gating.

---

## 4. Security & Authorization Gap Assessment (`WarehouseOperationalAuthorization`, EF Filters, Outbound Handlers)

### 4.1 Critical Audit Findings Across Existing Codebase

| Gap ID | Severity | Component / Exact Location | Verified Behavior in Current Code | Required Phase 7.4 Remediation (`[PROPOSED HARDENING]`) |
| :--- | :---: | :--- | :--- | :--- |
| **GAP-74-01** | **HIGH** | `WarehouseOperationalAuthorization` usage across **all** Outbound handlers (`Application/Features/Outbound/`) | Searching all `.cs` files under `Features/Outbound` shows **zero references** to `WarehouseOperationalAuthorization`. Staff facility confinement (`ValidateFacilityAccessAsync` / `ResolveQueryScopeAsync`) is completely absent across Order queries/mutations, Allocation, Wave generation/query, PickTask query/assign/confirm/short-pick, Packing, Shipments, BOL, and Shipping Labels. | Inject `WarehouseOperationalAuthorization` into all Outbound query and command handlers (and `PickTasksController.GetTaskById`, which should move into a MediatR `GetPickTaskByIdQueryHandler`) to enforce fail-closed Staff facility confinement and Client tenant/facility checks. |
| **GAP-74-02** | **HIGH** | `ApplicationDbContext.BypassTenantFilter` (`ApplicationDbContext.cs:40`) + Child Entities Missing `ITenantEntity` | `BypassTenantFilter => _tenantProvider.BypassTenantFilter \|\| _currentUser.IsStaff;` bypasses EF tenant filters for all `Staff` users (by design for multi-tenant 3PL operations, requiring explicit handler facility checks). Additionally, child entities **`OutboundOrderLine`**, **`PackageLine`**, **`PickWaveOrder`**, **`OrderImportBatchRow`**, **`OrderStatusHistory`**, **`PickTaskStatusHistory`**, **`ShippingAddress`**, **`ShippingLabel`**, and **`PackagingMaterial`** do not implement `ITenantEntity` directly. | Never rely solely on EF global query filters. Every query/mutation against parent or child entities must resolve scope via `WarehouseOperationalAuthorization` and join/filter by parent `ClientId` and `FacilityId` (note `PackagingMaterial` has `FacilityId`). |
| **GAP-74-03** | **HIGH** | Unscoped Query Handlers (`GetPickWavesQueryHandler`, `GetPickWaveByIdQueryHandler`, `GetShipmentByIdQueryHandler`, `GetBillOfLadingByIdQueryHandler`, `PickTasksController.GetTaskById`, `TrackPackageShipmentQueryHandler`, `GetShippingLabelByIdQueryHandler`) | These handlers inject only `IApplicationDbContext` or `IShippingLabelService` with **zero tenant and zero facility validation**. Even for `UserType.Client`, `ShippingLabel` lacks `ITenantEntity`, and in `PickExecutionService`/`PackingExecutionService`/`ShipmentConsolidationService`/`ShippingLabelService` a `Staff` user from Facility A can mutate/view Facility B records (`BypassTenantFilter == true`). | Enforce `ValidateClientFacilityAccessAsync(entity.ClientId, entity.FacilityId)` on every single detail query (`GetById`) and every mutation command before invoking Infrastructure services. |
| **GAP-74-04** | **HIGH** | `AllocationExecutionService.cs:72` & `DeliveryConfirmationService.cs:76` Wrong Exception Type (`500` / `401` instead of `403`) | `AllocationExecutionService` throws `System.UnauthorizedAccessException`, which is **unhandled** by `ExceptionHandlingMiddleware.cs` and returns **HTTP 500 Internal Server Error**. Meanwhile `DeliveryConfirmationService`, `DeliveryExceptionService`, and `ProofOfDeliveryService` throw `UnauthorizedException` (**401**) for cross-tenant attempts instead of `ForbiddenException` (**403**). | Replace cross-tenant / unauthorized scope exceptions with `ForbiddenException` (mapped to `403 Forbidden` in `ExceptionHandlingMiddleware`). Keep `UnauthorizedException` (`401`) strictly for missing/invalid authentication. |
| **GAP-74-05** | **HIGH** | `CancelOutboundOrderCommandHandler`, `HoldOutboundOrderCommandHandler`, `ReleaseOutboundOrderCommandHandler` | Inject `_tenantProvider` and `_currentUser` only to read `UserId`. If `UserType.Client` spoofs or omits tenant context, or if `UserType.Staff` targets an order in a foreign facility (`order.FacilityId != staff.FacilityId`), the handler does not fail closed via `WarehouseOperationalAuthorization`. Also `CreateOutboundOrderCommandHandler` validates `ClientFacilityAccess` for Clients, but does **not** check Staff facility assignment (`staffFacility == request.FacilityId`). | Enforce `ValidateClientFacilityAccessAsync(order.ClientId, order.FacilityId)` (and check `DestinationFacilityId` on Stock Transfers if staff facility-scoped). |
| **GAP-74-06** | **MEDIUM** | `DeliveryConfirmationService.cs:80` Nullable `FacilityId` Gap & `UserType.Driver` in `WarehouseOperationalAuthorization.cs:122` | 1) `DeliveryConfirmationService` only checks `if (_currentUserContext.FacilityId.HasValue ...)` — an unassigned Staff user (`FacilityId == null`) bypasses facility isolation.<br>2) `WarehouseOperationalAuthorization` fails closed on **all** `UserType.Driver` requests (`line 122-123`). Once courier POD/delivery endpoints are exposed, authenticated `Driver` identities need an explicit courier-scope validation path. | Fail closed for unassigned Staff without cross-facility permission. Add an explicit `ValidateCourierPackageAccessAsync(package)` method in `WarehouseOperationalAuthorization` for `UserType.Driver` (verifying active assignment / carrier dispatch state and facility scope). |
| **GAP-74-07** | **MEDIUM** | `ShippingController.IngestReturnTrackingWebhook` (`[AllowAnonymous]`) & Anonymous Webhook Verification | Currently `[AllowAnonymous]` with no webhook signature/API-key verification header check; exposes direct package status mutation to unauthenticated HTTP callers. | Require signed HMAC / carrier webhook shared-secret verification middleware or header policy before production enabling. |

### 4.2 Proposed Negative HTTP Security Tests (Unit / TestServer & Live PostgreSQL CI)

Mirroring `WarehouseOperationalAuthorizationHttpTests.cs` and Section 5 of `Phase73PostgresCheck`:
1. `OutboundOrders_ClientCannotQueryOrCreateForForeignTenant_Returns403` (`GET /orders?clientId=Foreign`, `GET /orders/{foreignOrderId}` -> fail closed `403`/`404`).
2. `OutboundOrders_StaffWithoutCrossFacilityCannotQueryOrAllocateForeignFacility_Returns403` (`GET /orders?facilityId=FacilityB`, `POST /orders/{facilityBOrderId}/allocate`, `PATCH /orders/{facilityBOrderId}/hold|cancel` -> `403`).
3. `OutboundOrders_StaffWithoutAssignedFacilityClaim_Returns403` (Staff without `facility_id` and without cross-facility permission -> `403`).
4. `PickTasks_ViewOnlyUserCannotInvokeNextConfirmOrShortPick_Returns403` (`outbound:tasks:view` calling `/next`, `/confirm`, `/short-pick` -> `403`).
5. `PickTasks_ExecutionOnlyOperatorCannotListQueueOrGenerateWave_Returns403` (`outbound:pick` calling `GET /pick-tasks` or `POST /pick-waves` -> `403`).
6. `PickTasks_OperatorTargetingForeignOrMissingFacilityOnNext_Returns403` (`GET /pick-tasks/next?facilityId=FacilityB` or missing `facility_id` -> `403`).
7. `Packing_NonPackUserAndForeignFacilityStaff_Returns403` (`outbound:orders:view` calling `POST /packing/sessions/start`; Facility A operator calling Facility B `POST /packing/items/pack` -> `403`).
8. `ShipmentsAndShipping_CrossFacilityAndCrossTenantAccess_Returns403` (Facility A staff reading Facility B `GET /shipments/{id}`, `GET /bol/{id}`, `POST /labels/generate`, `POST /dispatch` -> `403`; not `500` or `200`).
9. `CourierDelivery_DriverCannotAccessWarehousePickingPackingOrOtherFacilityEndpoints_Returns403` (`UserType.Driver` attempting `/outbound/orders`, `/pick-tasks`, `/packing`, `/inventory/stock` -> `403`).

---

## 5. Design for Missing Read / Query & Dispatch Board Data Access (`[PROPOSED ADDITION]`)

All proposed query handlers **must** use `WarehouseOperationalAuthorization` (matching `GetPutawayTasksQueryHandler` and `GetNextPutawayTaskQueryHandler`) and return `.AsNoTracking()` DTO projections.

### 5.1 Pick Task Supervisor / Queue Listing
- **Endpoint:** `GET /api/v1/outbound/pick-tasks`
- **Permission:** `[HasPermission(Permissions.Outbound.TasksView)]`
- **Query Params (`GetPickTasksQuery : PagedQuery`):** `Guid? FacilityId`, `Guid? PickWaveId`, `Guid? OutboundOrderId`, `PickTaskStatus? Status`, `List<PickTaskStatus>? Statuses`, `Guid? AssignedUserId`, `string? SearchTerm` (matches `OrderNumber`, `Sku`, `SourceLocationCode`, `TargetToteBarcode`), `PageNumber`, `PageSize`.
- **Response:** `PagedResult<PickTaskDto>`
- **Authorization:** Calls `_operationalAuth.ResolveQueryScopeAsync(null, request.FacilityId, ct)` so facility isolation and client authorized-facility lists apply cleanly.

### 5.2 Execution-Only Next Pick Task Query (RF Terminal)
- **Endpoint:** `GET /api/v1/outbound/pick-tasks/next`
- **Permission:** `[HasPermission(Permissions.Outbound.Pick)]`
- **Query Params (`GetNextPickTaskQuery`):** `Guid FacilityId`, `Guid? PickWaveId`, `Guid? OperatorUserId` *(validated against `_currentUser.UserId` unless caller holds `TasksManage`)*
- **Response:** `200 OK` (`PickTaskDto`) or `204 No Content` when queue is empty (matching `PutawayTasksController.GetNextTask`).
- **Selection Logic:**
  1. Resolve scope via `_operationalAuth.ResolveQueryScopeAsync(null, request.FacilityId, ct)` — fails closed (`403`) if `FacilityId` is unauthorized or staff has no assigned facility.
  2. Candidate filter: `FacilityId == scope.FacilityId`, parent `PickWave.Status` is `Released` or `InProgress`, and task status is `Pending` **or** (`(Assigned || InProgress)` and `AssignedUserId == currentOperatorId`).
  3. Ordering: `(AssignedUserId == currentOperatorId) DESC`, `PickWave.Priority DESC`, `PickTask.PickSequence ASC`, `CreatedAtUtc ASC`.

### 5.3 Shipment Consolidation List Query
- **Endpoint:** `GET /api/v1/outbound/shipments`
- **Permission:** `[HasPermission(Permissions.Outbound.Ship)]` *(or dual-policy `Outbound.Ship` / `Dispatch.View` if Dispatch Board shares listing)*
- **Query Params (`GetShipmentsQuery : PagedQuery`):** `Guid? FacilityId`, `Guid? ClientId`, `ShipmentStatus? Status`, `List<ShipmentStatus>? Statuses`, `string? CarrierCode`, `string? SearchTerm` (`ShipmentNumber`, `BillOfLadingNumber`, `TrailerNumber`), `DateTime? FromDateUtc`, `DateTime? ToDateUtc`.
- **Response:** `PagedResult<ShipmentDto>` (ordered by `CreatedAt DESC`).

### 5.4 Packing Station & Package Workbench Queries
- **`GET /api/v1/outbound/packing/sessions`** (`Permissions.Outbound.Pack` or `Permissions.Outbound.View`)
  - Query: `Guid? FacilityId`, `PackingSessionStatus? Status`, `Guid? OutboundOrderId`, `Guid? PackingStationLocationId`, `PageNumber`, `PageSize`.
  - Returns `PagedResult<PackingSessionSummaryDto>` (`SessionId`, `OrderNumber`, `OrderId`, `StationLocationCode`, `OperatorUserId`, `Status`, `StartedAtUtc`, `ActivePackagesCount`, `TotalItemsRemaining`).
- **`GET /api/v1/outbound/packing/sessions/{id:guid}`** (`Permissions.Outbound.Pack` or `Permissions.Outbound.View`)
  - Returns **`PackingSessionDetailDto`** containing session metadata, all order lines (`QuantityOrdered`, `QuantityPicked`, `QuantityPacked`, `RemainingToPack`), open/sealed packages (`PackageDto` with `PackageLines`), and available picked totes awaiting packing so the Packing Bench UI can hydrate completely after page refresh.
- **`GET /api/v1/outbound/packing/packages`** (`Permissions.Outbound.Pack` or `Permissions.Outbound.Ship`)
  - Query: `Guid? FacilityId`, `Guid? OutboundOrderId`, `Guid? ShipmentId`, `PackageStatus? Status`, `List<PackageStatus>? Statuses`, `bool? UnassignedToShipmentOnly`, `string? SearchTerm` (`PackageNumber`, `TrackingNumber`).
  - Returns `PagedResult<PackageSummaryDto>` powering both the Label Print/Void desk and the Shipment Consolidation "Add Packed/Labeled Cartons" selector.

### 5.5 Dispatch Board & Courier Endpoint Exposure
- **`GET /api/v1/outbound/dispatch/board`** (`DispatchController` `[PROPOSED ADDITION]`, `Permissions.Dispatch.View` or `Permissions.Outbound.Ship`)
  - Returns **`DispatchBoardDto`**: active dock-door/staging shipments grouped by `Draft`, `ReadyForDispatch`, and recent `Dispatched` (today), BOL readiness flags, carrier cut-off summary, and loaded vs. staged package counts.
- **Courier Tracking, POD & Exception Routes (`DispatchController` `[PROPOSED ADDITION]`):**
  - Wrap existing tested Phase 4.5 MediatR commands/queries:
    - `GET /api/v1/outbound/dispatch/packages/{packageId:guid}/timeline` -> `GetPackageTrackingTimelineQuery` (`Dispatch.View` / `Outbound.Ship`)
    - `GET /api/v1/outbound/dispatch/orders/{orderId:guid}/delivery-status` -> `GetOrderDeliveryStatusQuery` (`Dispatch.View` / `Outbound.OrdersView`)
    - `POST /api/v1/outbound/dispatch/shipments/{shipmentId:guid}/sync-tracking` -> `SyncShipmentTrackingCommand` (`Dispatch.ManageRuns`)
    - `POST /api/v1/outbound/dispatch/packages/{packageId:guid}/pod` -> `SubmitProofOfDeliveryCommand` (`Dispatch.ExecuteDelivery`, multipart/form-data for signature + photo streams staged outside DB tx)
    - `POST /api/v1/outbound/dispatch/pod/{podId:guid}/supersede` -> `SupersedeProofOfDeliveryCommand` (`Dispatch.ManageRuns`)
    - `GET /api/v1/outbound/dispatch/packages/{packageId:guid}/pod` -> `GetProofOfDeliveryQuery` (`Dispatch.View`)
    - `POST /api/v1/outbound/dispatch/packages/{packageId:guid}/confirm-delivery` -> `ConfirmPackageDeliveryCommand` (`Dispatch.ExecuteDelivery` or `Dispatch.ManageRuns`)
    - `POST /api/v1/outbound/dispatch/exceptions/driver-report` -> `ReportDriverDeliveryExceptionCommand` (`Dispatch.ExecuteDelivery`)
    - `POST /api/v1/outbound/dispatch/exceptions/{id:guid}/contact|reattempt|reschedule|cancel-reattempt|update-address|redirect-depot|resolve|reopen|void` -> respective existing CQRS commands (`Dispatch.ManageRuns`) + `GET /api/v1/outbound/dispatch/exceptions` paged list query (`Dispatch.View`).

---

## 6. ADR-007 Frontend Architecture Split (Metadata vs. Dedicated Operational Workstations)

In strict compliance with [ADR-007: Metadata-Driven UI Architecture](adr/ADR-007-Metadata-Driven-UI-Architecture.md):
- **Rule:** Standard administrative CRUD tables and read-heavy directories use **Metadata-Driven Schemas (`DynamicEntityPage` + `SchemasController`)**; high-velocity scanning, multi-step state machines, cartonization calculators, BOL/custody capture, and courier POD workflows remain **Dedicated React Workstation Components**.

### 6.1 Metadata-Driven Candidates (Phase 7.4 Schema Additions — `[PROPOSED ADDITION]`)
- **Outbound Orders Directory (`/app/outbound/orders` -> `OutboundOrder` schema optional hybrid):** Filterable list (`OrderNumber`, `CustomerName`, `OrderType`, `Status`, `Priority`, `ServiceLevel`, `RequiredShipDateUtc`) with row actions (`View Detail`, `Check Availability`, `Allocate`, `Hold`/`Release`, `Cancel`) triggering typed action drawers/modals. *(Note: Complex multi-line + nested `ShippingAddress` creation stays a dedicated structured form/modal or custom widget.)*
- **Packaging Materials Master (`/app/admin/packaging-materials` -> `PackagingMaterial` schema):** Standard CRUD grid (`MaterialCode`, `Name`, `MaterialType`, `LengthCm`, `WidthCm`, `HeightCm`, `MaxWeightKg`, `TareWeightKg`, `IsActive`) rendered cleanly via `DynamicEntityPage`.

### 6.2 Dedicated Operational Workstations (`src/pages/outbound/` & `src/pages/transportation/`)
| Workstation Route | Component Scope | UX & Scanning Ergonomics |
| :--- | :--- | :--- |
| **`/app/outbound` (`/app/outbound/orders`)** | `OutboundOrdersPage.tsx` | Order intake, bulk JSON/CSV import audit modal, real-time stock availability drawer, FIFO/FEFO/Manual allocation trigger with idempotency lock, Hold/Release/Cancel dialogs. |
| **`/app/outbound/waves`** | `PickWavesPage.tsx` | Wave planner (Discrete/Batch/Zone/Bulk), serpentine travel estimate preview, wave release action, and wave task progress list. |
| **`/app/outbound/picking`** | `PickingWorkstationPage.tsx` | **Dual-mode shell (mirroring `PutawayPage.tsx`)**:<br>• **Queue Tab (`outbound:tasks:view`)**: Filterable task table + operator assignment (`outbound:tasks:manage`).<br>• **RF Pick Mode (`outbound:pick`)**: Fetches `/pick-tasks/next`, high-contrast bin/SKU/lot/tote inputs (`>= 52px`, monospace, single-flight ref guard), explicit loading/error/empty/task states, and **Short-Pick Exception Modal** (`ShortPickReason`, picked qty, lock indicator). |
| **`/app/outbound/packing`** | `PackingWorkstationPage.tsx` | Packing station selector, order lock/resume indicator, **3D Cartonization Recommendation Card** (`PackageRecommendation` utilization %), tote-to-carton barcode scanner, gross weight input & close package, empty tote release, and carrier rate quote / ZPL-PDF label print & void trigger. |
| **`/app/outbound/shipping`** | `ShipmentConsolidationPage.tsx` | Shipment builder (`CreateShipment`), add/remove packed+labeled cartons, dock staging scan, trailer load scan, **Immutable BOL Preview & Print Snapshot**, and **Driver Handover Custody Modal** (driver name, license, signature input, trailer/seal, idempotency key). |
| **`/app/transportation` (`/app/transportation/dispatch`)** | `DispatchAndCourierPage.tsx` | Dock dispatch board, package tracking timeline stepper (`PendingHandover -> Delivered`), **POD Capture/View Modal** (signature/photo evidence preview + supersession), and **Delivery Exception Resolution Desk** (contact log, reattempt scheduler, address correction, depot redirect, RTS status). |

### 6.3 Required Shell Navigation Updates (`Sidebar.tsx` & `LaunchpadGrid.tsx`)
Update permission visibility arrays (only when Phase 7.4 development is authorized):
- **`Outbound & Fulfillment` (`/app/outbound`)** + child/sub-routes: `permissions: ['outbound:orders:view', 'outbound:waves:view', 'outbound:tasks:view', 'outbound:pick', 'outbound:pack', 'outbound:ship']` — ensuring role-specific deep links or landing tab defaults match `Pick` / `Pack` / `Ship` grants cleanly (never rendering an unauthorized tab to an execute-only operator).
- **`Transportation & Carriers` (`/app/transportation`)**: `permissions: ['dispatch:view', 'dispatch:manage-runs', 'dispatch:execute-delivery', 'outbound:ship']` (separating Phase 7.4 operational dispatch/tracking from Phase 7.6 read-only `analytics:transportation:view` dashboards, or keeping analytics distinct).

---

## 7. Phased Execution Sequence & Independent Acceptance Checkpoints

| Sub-Stage | Scope | Prerequisite | Independent Acceptance Checkpoint |
| :--- | :--- | :--- | :--- |
| **Phase 7.4.0** *(Security & Missing Read Contracts)* | • Wire `WarehouseOperationalAuthorization` (incl. `Driver` custody helper) across Outbound handlers & fix `403` `ForbiddenException` mapping.<br>• Implement missing `GET` endpoints (`/pick-tasks`, `/pick-tasks/next`, `/shipments`, `/packing/sessions`, `/packing/sessions/{id}`, `/packing/packages`, `/dispatch/board`) and `DispatchController` exposing Phase 4.5 tracking/POD/exception handlers.<br>• Add negative HTTP authorization tests. | Approved Pre-Implementation Spec | All existing **1,316+** `.NET` unit/HTTP tests pass + **9+ new negative 403/401 HTTP tests** pass on `TestServer`; zero cross-facility or cross-tenant leaks. |
| **Phase 7.4.1** *(Orders, Allocation & Pick Waves UI)* | Implement `outboundApi.ts`, `OutboundOrdersPage.tsx`, `PickWavesPage.tsx`, bulk import modal, allocation/deallocation with idempotency key, and wave release UI. | Phase 7.4.0 Green | Live HTTP/Postgres test confirms Create -> Stock Check -> Allocate -> Wave Generate -> Wave Release; Vitest RBAC + modal tests pass. |
| **Phase 7.4.2** *(RF Directed Pick & Short-Pick UI)* | Implement `PickingWorkstationPage.tsx` (strict `tasks:view` vs `tasks:manage` vs `pick` RF separation mirroring `PutawayPage.tsx`), short-pick modal, and malformed DTO guards. | Phase 7.4.1 Green | Vitest permission-matrix tests verify view-only/manage-only/pick-only operators; Playwright verifies 320px/375px/768px/1440px reflow, error vs empty states, and double-scan protection. |
| **Phase 7.4.3** *(Packing & Cartonization Workbench UI)* | Implement `PackingWorkstationPage.tsx`, cartonization recommendation panel, tote-to-carton scan, scale weight close, tote release, and carrier rate/label generate + void panel. | Phase 7.4.2 Green | E2E flow confirms Session Start -> Recommend -> Create Carton -> Pack Item -> Close -> Rate Quote -> Label Generate/Void -> Session Complete; idempotency verified. |
| **Phase 7.4.4** *(Shipment Consolidation, BOL & Dispatch Custody UI)* | Implement `ShipmentConsolidationPage.tsx`, package consolidation/removal, staging/loading scans, immutable BOL viewer, and Driver Custody Handover modal. | Phase 7.4.3 Green | PostgreSQL verifier confirms `Draft -> ReadyForDispatch -> Dispatched` with 0 secondary stock mutation, immutable BOL snapshot, and `OutboundOrderStatus.Completed`. |
| **Phase 7.4.5** *(Dispatch Board, Tracking, POD & Courier Exceptions)* | Implement `DispatchAndCourierPage.tsx` (dispatch board, timeline stepper, Driver POD submission/preview/supersede, delivery exception workflow). | Phase 7.4.4 Green | Live integration confirms POD submission -> Delivery Rollup -> `OrderDeliveryStatus.Delivered`, plus driver/carrier exception resolution & negative `Driver` RBAC tests. |
| **Phase 7.4.6** *(Consolidated Visual Audit & Closure Gate)* | Run full `.NET` unit suite, PostgreSQL CI extended verifier, Vitest, production Vite build, Playwright multi-viewport capture, targeted automated a11y checks, and commit visual evidence to `docs/audits/outbound-transportation/`. | Phase 7.4.5 Green | CI green on HEAD; zero TypeScript errors; all 4 viewports (`1440x900`, `768x1024`, `375x812`, `320x568`) screenshots verified on GitHub; consolidated Phase 7.4 closure report submitted. |

---

## 8. Verification Matrix Requirements (Definition of Done for Phase 7.4)

1. **Real PostgreSQL & WebApi `TestServer` Verifier (`Phase74PostgresCheck` or extended CI workflow):**
   - Full outbound lifecycle in live Docker PostgreSQL: `OutboundOrder Create -> Allocate (FIFO/FEFO + 5x concurrent allocation collision test) -> Wave Generate/Release -> Pick Confirm & Short-Pick (verify CycleCount + LocationLock + StockOnHand conservation) -> Packing Session -> Cartonization -> Label Generate (decoupled outside DB tx + failure/void recovery) -> Consolidate -> Stage -> Load -> BOL -> Dispatch Custody -> Tracking Ingest -> POD Submit & Confirm Delivery`.
   - Comprehensive negative `403` / `401` matrix verifying Staff facility isolation, unassigned Staff fail-closed, Client tenant isolation, `Driver` isolation, and view/manage/execute permission separation.
2. **Backend `.NET` Suite (`Warehouse3PL.UnitTests`):** All existing **1,316** tests preserved + new `WarehouseOutboundAuthorizationHttpTests.cs` and query handler unit tests.
3. **Frontend Vitest Suite (`src/UI/Nexus3PLFrontend/src/test/`):** Dedicated permission-matrix tests (`outbound-permissions.test.tsx`), dual-scan RF picking & packing idempotency tests, shipment/BOL/dispatch state machine tests, and zero regressions on existing **72** Phase 7.1–7.3 tests.
4. **Playwright E2E, Targeted Accessibility & Visual Audit:**
   - Verified production preview (`vite build && vite preview`).
   - Exact route matching (avoiding glob shadowing like `/pick-tasks**` over `/pick-tasks/next`), payload contract assertions on every mocked/live card, explicit loading/error/retry/empty states, modal Escape + focus-trap checks, targeted automated accessibility checks, and `scrollWidth <= innerWidth` containment down to `320x568`.
   - Multi-viewport screenshots published to `docs/audits/outbound-transportation/` with audit `README.md`.
