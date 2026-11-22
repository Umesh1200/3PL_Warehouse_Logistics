# Phase 7.4 — Outbound, Transportation & Courier Operations: Pre-Implementation Specification & Execution Plan

**Repository:** `Umesh1200/3PL_Warehouse_Logistics`  
**Branch:** `feature/phase-7-enterprise-frontend`  
**Baseline HEAD:** [`dab06108bd3d933749281c5b0c53a33c426047c5`](https://github.com/Umesh1200/3PL_Warehouse_Logistics/commit/dab06108bd3d933749281c5b0c53a33c426047c5) (Audit Revision over `d82d8f80588454c0ccc4ab70e7e62f8ea23dce12`)  
**Accepted Phase 7.3 Implementation Baseline:** [`1a67a71640df28dfbd1c5cdd5363f2a0e77a45c5`](https://github.com/Umesh1200/3PL_Warehouse_Logistics/commit/1a67a71640df28dfbd1c5cdd5363f2a0e77a45c5)  
**Status:** **PRE-IMPLEMENTATION SPECIFICATION GATE (PLANNING AUTHORIZED — IMPLEMENTATION HELD)**  
**Spec Date:** November 22, 2026 (Revision 2 — Auditor Remediation Addendum Applied)  

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

### 2.3 Missing Read / Queue & Claim Endpoints Needed for Frontend Workstations (`[PROPOSED ADDITION]`)

| Proposed Route | Exact Permission | Purpose | Detailed Design Ref | Status |
| :--- | :--- | :--- | :---: | :---: |
| `GET /api/v1/outbound/pick-tasks` | `Permissions.Outbound.TasksView` | Supervisor/viewer paginated pick-task queue filtered by facility, wave, order, status, assigned operator, search term | Section 5.1 | `[PROPOSED ADDITION]` |
| `GET /api/v1/outbound/pick-tasks/next` | `Permissions.Outbound.Pick` | **Strictly read-only** execution RF floor next-task query (assigned-to-caller first, then priority/sequence) returning `200` or `204` | Section 5.2 | `[PROPOSED ADDITION]` |
| `POST /api/v1/outbound/pick-tasks/{id:guid}/claim` | `Permissions.Outbound.Pick` | **Atomic optimistic-concurrency self-claim** mutation binding `PickTask.AssignedUserId` to trusted `_currentUser.UserId` | Section 5.2 | `[PROPOSED ADDITION]` |
| `GET /api/v1/outbound/shipments` | `Permissions.Outbound.Ship` | Paginated shipment list & consolidation workspace query filtered by facility, status, carrier, search term | Section 5.3 | `[PROPOSED ADDITION]` |
| `GET /api/v1/outbound/shipments/eligible-packages` | `Permissions.Outbound.Ship` | Paginated cartons (`Packed` / `LabelPrinted`, unassigned to shipment) for shipment dock consolidation builder | Section 5.3 | `[PROPOSED ADDITION]` |
| `GET /api/v1/outbound/packing/sessions` | `Permissions.Outbound.Pack` | Active/suspended packing sessions list for station resumption & packing supervisor visibility | Section 5.4 | `[PROPOSED ADDITION]` |
| `GET /api/v1/outbound/packing/sessions/{id:guid}` | `Permissions.Outbound.Pack` | Full packing session detail including packed packages, line progress, and remaining order demand | Section 5.4 | `[PROPOSED ADDITION]` |
| `GET /api/v1/outbound/packing/packages` | `Permissions.Outbound.Pack` | Paginated packages lookup scoped to packing workbench hydration (`Open`, `Packed`, `LabelPrinted`) | Section 5.4 | `[PROPOSED ADDITION]` |
| `GET /api/v1/outbound/dispatch/board` | `Permissions.Dispatch.View` | Read-only dock dispatch board summarizing `ReadyForDispatch` / `Dispatched` shipments, BOL readiness, and dock staging counts | Section 5.5 | `[PROPOSED ADDITION]` |
| `DispatchController` (`/api/v1/outbound/dispatch/...`) | Tiered (`Dispatch.View`, `Dispatch.ManageRuns`, `Dispatch.ExecuteDelivery`) | Expose existing Phase 4.5 `Tracking`, `POD`, `DeliveryConfirmation`, and `DeliveryException` CQRS handlers + signed webhook ingestion | Section 5.5 | `[PROPOSED ADDITION]` |

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

## 3. Authoritative Security Matrix (`Permissions.cs`, `SystemRoles.cs`, Identity & Scope)

### 3.1 Role Baseline & ClientAdmin Reconciliation (`DbInitializer.cs` & `CurrentUserContext.cs`)
Inspection of `DbInitializer.cs`, `SystemRoles.cs`, and `PermissionAuthorizationHandler.cs` establishes critical identity rules:
1. **`SuperAdmin` vs. All Other Roles:** `PermissionAuthorizationHandler` grants all `[HasPermission]` checks **only** when `ICurrentUserContext.IsSuperAdmin` (`IsInRole(SystemRoles.SuperAdmin)`) is true. Every other role (`OperationsManager`, `WarehouseOperator`, `Driver`, `ClientAdmin`, `ClientUser`, or custom `Admin`) **must hold the exact explicit `PermissionKey` grant**.
2. **Reconciliation of `ClientAdmin` Seeded Permissions (`DbInitializer.cs:113-123`):** Currently `DbInitializer.cs` seeds `ClientAdmin` with `Permissions.Outbound.Pick` (`outbound:pick`) and `Permissions.Outbound.Ship` (`outbound:ship`) alongside `Outbound.View`, `Outbound.Create`, and `Dispatch.View`. Because physical warehouse execution (RF picking, packing, dock staging/loading, BOL issuance, carrier dispatch custody) is strictly restricted to trusted `UserType.Staff` (or `SuperAdmin`), Phase 7.4 enforces **defense-in-depth**:
   - **Code-level Identity Gate (`[PROPOSED HARDENING]`):** All physical warehouse execution handlers enforce `UserType == UserType.Staff || IsSuperAdmin` inside `WarehouseOperationalAuthorization`, **rejecting `UserType.Client` and `UserType.Driver` with `403 Forbidden` even if the JWT contains `outbound:pick`, `outbound:pack`, or `outbound:ship` claims**.
   - **Seed Reconciliation (`[PROPOSED HARDENING]`):** Remove `Permissions.Outbound.Pick` and `Permissions.Outbound.Ship` from `ClientAdmin` default seeding in `DbInitializer.cs` (and apply idempotent permission cleanup/migration for existing `ClientAdmin` role rows), granting `ClientAdmin` only `Outbound.OrdersView`, `Outbound.OrdersCreate`, `Outbound.OrdersImport`, `Outbound.OrdersCancel`, `Outbound.WavesView`, and `Dispatch.View`.
3. **Permitted Operations for `UserType.Client` (`ClientAdmin` / `ClientUser`):**
   - **Allowed (Own `TenantId` + Active `ClientFacilityAccess` only):**
     - `GET /api/v1/outbound/orders`, `/orders/{id}`, `/orders/{id}/stock-availability` (`Outbound.OrdersView`)
     - `POST /api/v1/outbound/orders` (`Outbound.OrdersCreate`), `POST /orders/bulk-import` (`Outbound.OrdersImport`)
     - `PATCH /api/v1/outbound/orders/{id}/cancel` (`Outbound.OrdersCancel`, pre-pick/ship only)
     - `GET /api/v1/outbound/dispatch/board`, `/dispatch/packages/{packageId}/timeline`, `/dispatch/orders/{orderId}/delivery-status`, `/dispatch/packages/{packageId}/pod`, `/dispatch/exceptions` (`Dispatch.View`, strictly filtered to caller `ClientId`)
   - **Strictly Forbidden for `UserType.Client` (`403 Forbidden`):** Order allocation/deallocation, wave generation/release, pick queue listing (`GET /pick-tasks`), RF `/next`/`/claim`/`/confirm`/`/short-pick`, packing sessions/packages, rate/label generation/voiding, shipment creation/consolidation/BOL/dispatch, courier POD submission, and delivery exception mutations.

### 3.2 Complete Endpoint-to-Permission & Scope Matrix (Finalized — Zero Unresolved Alternatives)

Every endpoint belongs to exactly one authorization tier: **(1) Warehouse Execution**, **(2) Supervisor Management**, **(3) Read-Only Client & Shared Dispatch Views**, **(4) Courier / Dispatch Execution**, or **(5) Transportation Analytics (Phase 6.4/7.6)**.

| Tier | Endpoint | Status | Exact `Permissions.cs` Constant | Required Identity Type & Task/Custody Check | Tenant Scope | Facility Scope |
| :--- | :--- | :---: | :--- | :--- | :--- | :--- |
| **Client & Order Read** | `GET /api/v1/outbound/orders`, `GET /{id}`, `GET /{id}/stock-availability` | `[EXISTING]` | `Permissions.Outbound.OrdersView` (`outbound:orders:view`) | `Staff` (`OperationsManager`) or `Client` (`ClientAdmin`, `ClientUser`) | Staff: Authorized query scope; Client: Own `TenantId` | Staff: Assigned facility (unless CrossFacility Read); Client: Active `ClientFacilityAccess` |
| **Order Entry** | `POST /api/v1/outbound/orders` | `[EXISTING]` | `Permissions.Outbound.OrdersCreate` (`outbound:orders:create`) | `Staff` or `Client` | Same as above | Staff: `AssignedFacility == Request.FacilityId`; Client: `ClientFacilityAccess` (source + dest on transfer) |
| **Order Import** | `POST /api/v1/outbound/orders/bulk-import` | `[EXISTING]` | `Permissions.Outbound.OrdersImport` (`outbound:orders:import`) | `Staff` or `Client` | Same as above | Same as above |
| **Order Cancel** | `PATCH /api/v1/outbound/orders/{id}/cancel` | `[EXISTING]` | `Permissions.Outbound.OrdersCancel` (`outbound:orders:cancel`) | `Staff` (`OperationsManager`) or `Client` (own tenant) | Order `ClientId == TenantId` if Client | Order `FacilityId == Staff FacilityId` (or Client active facility) |
| **Supervisor Management** | `PATCH /api/v1/outbound/orders/{id}/hold`, `/release` | `[EXISTING]` | `Permissions.Outbound.OrdersManage` (`outbound:orders:manage`) | `SuperAdmin` or `UserType.Staff` only | Staff: Any authorized client in facility | Staff: Single assigned facility (`Order.FacilityId == Staff.FacilityId`) |
| **Supervisor Management** | `POST /api/v1/outbound/orders/{id}/allocate`, `/deallocate`, `POST /allocations/{id}/release` | `[EXISTING]` | `Permissions.Outbound.OrdersAllocate` (`outbound:orders:allocate`) | `SuperAdmin` or `UserType.Staff` only | Staff only | Staff: `Order.FacilityId == Staff.FacilityId` |
| **Supervisor / Queue Read** | `GET /api/v1/outbound/pick-waves`, `GET /{id}` | `[EXISTING]` | `Permissions.Outbound.WavesView` (`outbound:waves:view`) | `SuperAdmin` or `UserType.Staff` only | Staff only | Staff: `Wave.FacilityId == Staff.FacilityId` (or CrossFacility Read) |
| **Supervisor Management** | `POST /api/v1/outbound/pick-waves`, `POST /{id}/release` | `[EXISTING]` | `Permissions.Outbound.WavesManage` (`outbound:waves:manage`) | `SuperAdmin` or `UserType.Staff` only | Staff only | Staff: `Wave.FacilityId == Staff.FacilityId` |
| **Supervisor / Queue Read** | `GET /api/v1/outbound/pick-tasks`, `GET /{id}` | `[PROPOSED]` / `[EXISTING]` | `Permissions.Outbound.TasksView` (`outbound:tasks:view`) | `SuperAdmin` or `UserType.Staff` only | Staff only | Staff: `Task.FacilityId == Staff.FacilityId` (or CrossFacility Read) |
| **Supervisor Management** | `POST /api/v1/outbound/pick-tasks/{id}/assign` | `[EXISTING]` | `Permissions.Outbound.TasksManage` (`outbound:tasks:manage`) | `SuperAdmin` or `UserType.Staff` only; validates assignee is active `Staff` in facility | Staff only | Staff: `Task.FacilityId == Staff.FacilityId` |
| **Warehouse Execution** | `GET /api/v1/outbound/pick-tasks/next` | `[PROPOSED ADDITION]` | `Permissions.Outbound.Pick` (`outbound:pick`) | **Strictly `UserType.Staff` or `SuperAdmin`**. Uses `_currentUser.UserId` only; strictly read-only | Staff only | **Mandatory** `Staff.FacilityId == Query.FacilityId` (fail closed if null) |
| **Warehouse Execution** | `POST /api/v1/outbound/pick-tasks/{id}/claim` | `[PROPOSED ADDITION]` | `Permissions.Outbound.Pick` (`outbound:pick`) | **Strictly `UserType.Staff` or `SuperAdmin`**. Ignores payload user; binds `AssignedUserId = _currentUser.UserId` | Staff only | Staff: `Task.FacilityId == Staff.FacilityId` |
| **Warehouse Execution** | `POST /api/v1/outbound/pick-tasks/{id}/confirm`, `/short-pick` | `[EXISTING]` | `Permissions.Outbound.Pick` (`outbound:pick`) | **Strictly `UserType.Staff` or `SuperAdmin`**. Task must be unassigned OR `AssignedUserId == _currentUser.UserId` (unless caller also holds `TasksManage`) | Staff only | Staff: `Task.FacilityId == Staff.FacilityId` |
| **Warehouse Execution** | `POST /api/v1/outbound/packing/...` (all 7 mutations) & `GET /sessions`, `/sessions/{id}`, `/packages` | `[EXISTING]` + `[PROPOSED]` | `Permissions.Outbound.Pack` (`outbound:pack`) | **Strictly `UserType.Staff` or `SuperAdmin`**. Station session locked to `OperatorUserId == _currentUser.UserId` | Staff only | Staff: `Session/Package.FacilityId == Staff.FacilityId` |
| **Warehouse Execution** | `ShipmentsController` (`POST /`, `GET /`, `GET /{id}`, `GET /eligible-packages`, `packages`, `stage`, `load`, `bol`, `dispatch`) & `ShippingController` (`rates`, `labels/generate`, `labels/{id}/void`, `GET /labels/{id}`, `GET /packages/{id}/track`) | `[EXISTING]` + `[PROPOSED]` | `Permissions.Outbound.Ship` (`outbound:ship`) | **Strictly `UserType.Staff` or `SuperAdmin`** | Staff only | Staff: `Shipment/Package.FacilityId == Staff.FacilityId` |
| **Signed Carrier Webhook** | `POST /api/v1/outbound/shipping/returns/tracking/webhook` & Dispatch webhooks | `[EXISTING]` / `[PROPOSED]` | Carrier HMAC-SHA256 Signature Auth (`RequireCarrierWebhookSignature`, zero anonymous open mutation) | Carrier Identity via `X-Carrier-Id` + dual-secret HMAC verification | Derived from tracking # (`TenantIdOverride` match check) | Derived from package/return shipment |
| **Read-Only Tracking & Dispatch** | `GET /dispatch/board`, `GET /dispatch/packages/{id}/timeline`, `GET /dispatch/orders/{id}/delivery-status`, `GET /dispatch/packages/{id}/pod`, `GET /dispatch/exceptions` | `[PROPOSED ADDITION]` | `Permissions.Dispatch.View` (`dispatch:view`) | `Staff` (`OperationsManager`) or `Client` (`ClientAdmin`, own tenant only) | Staff: Authorized query scope; Client: Own `TenantId` | Staff: Assigned facility (or CrossFacility Read); Client: Active `ClientFacilityAccess` |
| **Dispatch Management** | `POST /dispatch/shipments/{id}/assign-driver`, `/sync-tracking`, `POST /dispatch/pod/{id}/supersede`, `POST /dispatch/exceptions/{id}/contact\|reattempt\|reschedule\|cancel-reattempt\|update-address\|redirect-depot\|resolve\|reopen\|void` | `[PROPOSED ADDITION]` | `Permissions.Dispatch.ManageRuns` (`dispatch:manage-runs`) | **Strictly `UserType.Staff` or `SuperAdmin`** | Staff only | Staff: `Shipment/Exception.FacilityId == Staff.FacilityId` |
| **Courier Execution** | `POST /dispatch/packages/{id}/pod`, `/confirm-delivery`, `POST /dispatch/exceptions/driver-report` | `[PROPOSED ADDITION]` | `Permissions.Dispatch.ExecuteDelivery` (`dispatch:execute-delivery`) | `UserType.Driver` (verified `Shipment.AssignedDriverUserId == _currentUser.UserId` custody) OR `SuperAdmin` / `Staff` (manual verify only where allowed) | Package `ClientId` validated internally | Driver: Validated by explicit Shipment/Driver custody assignment (`ValidateCourierPackageAccessAsync`); Staff: Single facility match |
| **Transportation Analytics** | `GET /api/v1/analytics/transportation/...` (Phase 6.4 read-only) | `[EXISTING]` | `Permissions.Analytics.TransportationView`, `TransportationClaimsView`, `TransportationFinancialView` | `SuperAdmin`, `OperationsManager`, `ClientAdmin` (strictly read-only financial/SLA analytics) | Enforced by `TransportationAnalyticsAuthorization` | Separate from Phase 7.4 operational dispatch |

### 3.3 Non-Equivalence Rules & `401 / 403 / 404` HTTP Response Policy
- **Strict Permission Non-Equivalence:**
  - `outbound:orders:view` != `outbound:orders:allocate`, `waves:manage`, `pick`, `pack`, or `ship`.
  - `outbound:tasks:view` != `outbound:pick` (view-only users can never call `/next`, `/claim`, `/confirm`, `/short-pick`).
  - `outbound:tasks:manage` != `outbound:pick` (task managers cannot confirm physical picks unless they also hold `outbound:pick`).
  - `analytics:transportation:view` != `dispatch:view` and `analytics:multi-facility:view` must **never** authorize cross-facility mutations.
  - Holding `outbound:pick` or `outbound:ship` on a `UserType.Client` identity never grants warehouse execution access.
- **Exact Status Code Policy (`ExceptionHandlingMiddleware.cs`):**
  - **`401 Unauthorized` (`UnauthorizedException`):** Missing, expired, or invalid JWT bearer token, or missing/invalid/expired Carrier Webhook HMAC signature.
  - **`403 Forbidden` (`ForbiddenException`):** Authenticated caller lacks `[HasPermission]`, fails `UserType.Staff` operational gate (e.g., `ClientAdmin` with `outbound:pick`/`ship`), supplies foreign `clientId`/`X-Tenant-Id` or foreign `facilityId`/`X-Facility-Id`, lacks `facility_id` claim (`null`), tries to confirm/short-pick a task assigned to another operator, tries to mutate across facilities using read-only multi-facility permission, or `UserType.Driver` lacks active shipment custody.
  - **`404 Not Found` (`EntityNotFoundException`):** Resource UUID does not exist, OR a non-staff tenant-filtered lookup conceals a foreign tenant's record ID when global EF tenant filter excludes it (`Order`/`Shipment` opaque ID concealment when tenant filter applies without explicit foreign parameter). Note: When an authorized lookup finds an entity in a foreign facility (`order.FacilityId != staff.FacilityId`), fail explicitly with **`403 Forbidden`**.

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
| **GAP-74-06** | **MEDIUM** | `DeliveryConfirmationService.cs:80` Nullable `FacilityId`, Cross-Facility Read vs. Write Conflation (`WarehouseOperationalAuthorization.cs:96-100,163-168`), & `UserType.Driver` (`:122`) | 1) `DeliveryConfirmationService` skips facility checks if `FacilityId == null`.<br>2) `WarehouseOperationalAuthorization` currently treats `Permissions.Analytics.MultiFacilityView` as cross-facility authorization for **both** `ResolveQueryScopeAsync` (reads) and `ValidateFacilityAccessAsync` (mutations), allowing read-only analytics grants to unlock multi-facility mutations.<br>3) `UserType.Driver` unconditionally fails at `:122` and `ShipmentCustodyTransfer` tracks string `DriverName` rather than a `Guid? AssignedDriverUserId`. | 1) Fail closed (`403`) whenever non-`SuperAdmin` Staff lack `FacilityId`.<br>2) Separate **cross-facility read scope** (`Analytics.MultiFacilityView` on read-only queries) from **cross-facility mutation scope** (strict single-facility lock for all operational writes unless `SuperAdmin`, preserving Phase 7.3 endpoints identically).<br>3) Add `Guid? AssignedDriverUserId` on `Shipment`/`ShipmentCustodyTransfer` and `ValidateCourierPackageAccessAsync(package)` enforcing driver custody verification (`403` otherwise). |
| **GAP-74-07** | **HIGH** | `ShippingController.IngestReturnTrackingWebhook` (`[AllowAnonymous]`) & Proposed Carrier Webhooks | Currently `[AllowAnonymous]` with no signature verification, timestamp skew check, or nonce replay table; allows unauthenticated callers to mutate `ReturnShipment` / `Package` state if tracking numbers are guessed or brute-forced. | Replace open anonymous ingestion with `RequireCarrierWebhookSignature` filter verifying `X-Carrier-Id`, `X-Webhook-Timestamp` (`±300s`), `X-Webhook-Nonce`, and `X-Webhook-Signature` (`HMAC-SHA256` constant-time compare against dual active secrets for zero-downtime rotation), backed by replay table + `CarrierOperationLog` audit records. |

### 4.2 Proposed Negative HTTP Security Tests (Unit / TestServer & Live PostgreSQL CI)

Mirroring `WarehouseOperationalAuthorizationHttpTests.cs` and Section 5 of `Phase73PostgresCheck`:
1. `OutboundOrders_ClientCannotQueryOrCreateForForeignTenant_Returns403` (`GET /orders?clientId=Foreign` -> `403`; opaque foreign `GET /orders/{foreignOrderId}` concealed as `404` under non-bypassed EF filter).
2. `OutboundOrders_ClientAdminHoldingSeededPickOrShipClaim_RejectedWith403OnPhysicalExecution` (`ClientAdmin` with `outbound:pick` or `outbound:ship` calling `GET /pick-tasks/next`, `POST /pick-tasks/{id}/claim|confirm`, `POST /packing/sessions/start`, `POST /shipments`, `POST /shipping/labels/generate`, `POST /shipments/{id}/dispatch` -> `403 Forbidden`).
3. `OutboundOrders_StaffWithMultiFacilityViewCannotMutateForeignFacility_Returns403` (Staff holding `analytics:multi-facility:view` trying `POST /orders/{facilityBOrderId}/allocate`, `PATCH /hold`, `POST /pick-waves` -> `403`).
4. `OutboundOrders_StaffWithoutAssignedFacilityClaim_Returns403` (Staff without `facility_id` -> `403` on all queries & writes).
5. `PickTasks_ViewOnlyUserCannotInvokeNextClaimConfirmOrShortPick_Returns403` (`outbound:tasks:view` calling `/next`, `/claim`, `/confirm`, `/short-pick` -> `403`).
6. `PickTasks_ExecutionOnlyOperatorCannotListQueueAssignOrConfirmOtherOperatorsTask_Returns403` (`outbound:pick` calling `GET /pick-tasks`, `POST /assign`, or confirming/short-picking a task `AssignedUserId == OperatorA` while authenticated as `OperatorB` -> `403`).
7. `PickTasks_ConcurrentClaimRace_OneWinner200AndOneLoser409Conflict` (Parallel PostgreSQL `POST /pick-tasks/{id}/claim` from OperatorA & OperatorB on single `Pending` task -> 1× `200 OK`, 1× `409 Conflict`).
8. `PickTasks_ClientProvidedOperatorUserIdSpoofing_IgnoredOrRejected403` (`POST /pick-tasks/{id}/claim` or `/next?operatorUserId=Other` cannot claim/execute as another operator without `outbound:tasks:manage`).
9. `Packing_NonPackUserAndForeignFacilityStaff_Returns403` (`outbound:orders:view` calling `POST /packing/sessions/start`; Facility A operator calling Facility B `POST /packing/items/pack` -> `403`).
10. `ShipmentsAndShipping_CrossFacilityAndCrossTenantAccess_Returns403` (Facility A staff accessing Facility B `GET /shipments/{id}`, `GET /bol/{id}`, `POST /labels/generate`, `POST /dispatch` -> `403`, never `500` or `200`).
11. `CourierDelivery_UnassignedDriverOrWarehouseEndpointAccess_Returns403` (`UserType.Driver` attempting warehouse `/pick-tasks`, `/packing`, `/shipments`, or attempting `/dispatch/packages/{id}/pod` for another driver's shipment -> `403`).
12. `CarrierWebhook_UnsignedForgedExpiredAndReplayedRequests_Rejected401Or403WithNoMutation` (`POST /shipping/returns/tracking/webhook` and `/dispatch/webhooks/...` with missing signature -> `401`, wrong HMAC -> `401`, expired timestamp > 5 min -> `401`, mutated payload with reused nonce -> `403`; verify DB unchanged and audit log written).

---

## 5. Design for Missing Read / Query, RF Ownership & Dispatch Board Access (`[PROPOSED ADDITION]`)

All proposed query & command handlers **must** resolve scope through `WarehouseOperationalAuthorization` (matching `GetPutawayTasksQueryHandler` and `GetNextPutawayTaskQueryHandler`) and use `.AsNoTracking()` for reads.

### 5.1 Pick Task Supervisor / Queue Listing
- **Endpoint:** `GET /api/v1/outbound/pick-tasks`
- **Permission:** `[HasPermission(Permissions.Outbound.TasksView)]` (`UserType.Staff` or `IsSuperAdmin` only)
- **Query Params (`GetPickTasksQuery : PagedQuery`):** `Guid? FacilityId`, `Guid? PickWaveId`, `Guid? OutboundOrderId`, `PickTaskStatus? Status`, `List<PickTaskStatus>? Statuses`, `Guid? AssignedUserId`, `string? SearchTerm` (matches `OrderNumber`, `Sku`, `SourceLocationCode`, `TargetToteBarcode`), `PageNumber`, `PageSize`.
- **Response:** `PagedResult<PickTaskDto>`
- **Authorization:** Calls `_operationalAuth.ResolveQueryScopeAsync(null, request.FacilityId, ct)` so facility confinement applies fail-closed.

### 5.2 RF Task Ownership, Claim & Execution-Only Next Pick Query
1. **Strict Read/Write Separation:**
   - **`GET /api/v1/outbound/pick-tasks/next`** (`[HasPermission(Permissions.Outbound.Pick)]`, `UserType.Staff` only) is **strictly read-only**: it never mutates `PickTask.Status` or `AssignedUserId`. Caller identity is derived exclusively from `_currentUser.UserId` (any client-supplied `operatorUserId` query param is ignored unless caller holds `TasksManage`). Returns `200 OK` (`PickTaskDto`) or `204 No Content`.
     - *Candidate Selection Order:* (a) Task already `Assigned` or `InProgress` with `AssignedUserId == callerUserId`, (b) Highest `PickWave.Priority DESC`, `PickTask.PickSequence ASC`, `CreatedAtUtc ASC` among `Pending` (`AssignedUserId == null`) tasks in `Released`/`InProgress` waves at `scope.FacilityId`.
   - **`POST /api/v1/outbound/pick-tasks/{id:guid}/claim`** (`[HasPermission(Permissions.Outbound.Pick)]`, `UserType.Staff` only) performs **atomic optimistic-concurrency claim** with zero `OperatorUserId` body parameter (always binds `AssignedUserId = _currentUser.UserId`):
     - If already `Assigned` to `_currentUser.UserId`, returns `200 OK` (`PickTaskDto`, idempotent).
     - Executes conditional concurrency-guarded update (`PickTask.Version` / `WHERE status = 'Pending' AND assigned_user_id IS NULL AND version = @expectedVersion`):
       - Rows affected == 1 -> transitions to `Assigned`, records `PickTaskStatusHistory`, returns `200 OK`.
       - Rows affected == 0 & now owned by another operator or non-`Pending` -> returns **`409 Conflict`** (`DbUpdateConcurrencyException` / `ConcurrencyConflictException`).
   - **`POST /api/v1/outbound/pick-tasks/{id:guid}/assign`** (`[HasPermission(Permissions.Outbound.TasksManage)]`, supervisor-only reassignment) validates target user is an active `UserType.Staff` member in `task.FacilityId` and refuses reassignment once `Status == InProgress` / terminal.
   - **Execution Authorization Gate on `/confirm` & `/short-pick` (`Permissions.Outbound.Pick`):**
     - `Pending` unassigned task (`AssignedUserId == null`): operator claims atomically inside the confirmation transaction or via prior `/claim`.
     - `Assigned` / `InProgress` task (`AssignedUserId != null`): **ONLY `task.AssignedUserId == _currentUser.UserId` OR a supervisor holding BOTH `Permissions.Outbound.Pick` AND `Permissions.Outbound.TasksManage` may execute `/confirm` or `/short-pick`**; any other operator is rejected with **`403 Forbidden`**.

### 5.3 Shipment Consolidation & Eligible Packages Queries
- **`GET /api/v1/outbound/shipments`** — `[HasPermission(Permissions.Outbound.Ship)]` (`UserType.Staff` only)
  - Query: `Guid? FacilityId`, `Guid? ClientId`, `ShipmentStatus? Status`, `List<ShipmentStatus>? Statuses`, `string? CarrierCode`, `string? SearchTerm`, `DateTime? FromDateUtc`, `DateTime? ToDateUtc`. Returns `PagedResult<ShipmentDto>`.
- **`GET /api/v1/outbound/shipments/eligible-packages`** — `[HasPermission(Permissions.Outbound.Ship)]` (`UserType.Staff` only)
  - Query: `Guid FacilityId`, `Guid? ClientId`, `string? CarrierCode`, `string? SearchTerm`. Returns `PagedResult<PackageSummaryDto>` (`Status` in `{Packed, LabelPrinted}` and `ShipmentId == null`) for adding packages to a `Draft` shipment without needing `outbound:pack` permission.

### 5.4 Packing Station & Package Workbench Queries
All packing workbench read endpoints use one exact permission — **`[HasPermission(Permissions.Outbound.Pack)]`** (`UserType.Staff` only, fail-closed `FacilityId`):
- **`GET /api/v1/outbound/packing/sessions`**: Filtered by `FacilityId`, `Status`, `OutboundOrderId`, `PackingStationLocationId`; returns `PagedResult<PackingSessionSummaryDto>`.
- **`GET /api/v1/outbound/packing/sessions/{id:guid}`**: Returns **`PackingSessionDetailDto`** (order lines, packed vs. remaining quantities, active packages, and picked totes ready to pack).
- **`GET /api/v1/outbound/packing/packages`**: Filtered by `FacilityId`, `OutboundOrderId`, `PackageStatus`, `SearchTerm`; returns `PagedResult<PackageSummaryDto>` for bench packaging & label reprint/void workflows.

### 5.5 Dispatch Board & Courier Endpoint Exposure (`DispatchController`)
Split cleanly across exact non-overlapping permissions (zero `OR` combinations):
- **Read-Only Dispatch & Tracking (`[HasPermission(Permissions.Dispatch.View)]`, Staff + Own-Tenant Client with `ClientFacilityAccess`):**
  - `GET /api/v1/outbound/dispatch/board`: Returns **`DispatchBoardDto`** (`Draft`, `ReadyForDispatch`, `Dispatched` summaries, BOL status, carrier cutoff readiness).
  - `GET /api/v1/outbound/dispatch/packages/{packageId:guid}/timeline` -> `GetPackageTrackingTimelineQuery`
  - `GET /api/v1/outbound/dispatch/orders/{orderId:guid}/delivery-status` -> `GetOrderDeliveryStatusQuery`
  - `GET /api/v1/outbound/dispatch/packages/{packageId:guid}/pod` -> `GetProofOfDeliveryQuery`
  - `GET /api/v1/outbound/dispatch/exceptions` -> Paged open/resolved delivery exceptions queue
- **Dispatch Supervisor Management (`[HasPermission(Permissions.Dispatch.ManageRuns)]`, `UserType.Staff` only, single facility):**
  - `POST /api/v1/outbound/dispatch/shipments/{id:guid}/assign-driver` (`Guid DriverUserId`) -> binds `Shipment.AssignedDriverUserId`
  - `POST /api/v1/outbound/dispatch/shipments/{shipmentId:guid}/sync-tracking` -> `SyncShipmentTrackingCommand`
  - `POST /api/v1/outbound/dispatch/pod/{podId:guid}/supersede` -> `SupersedeProofOfDeliveryCommand`
  - `POST /api/v1/outbound/dispatch/exceptions/{id:guid}/contact|reattempt|reschedule|cancel-reattempt|update-address|redirect-depot|resolve|reopen|void` -> existing Phase 4.5 CQRS commands
- **Courier Execution (`[HasPermission(Permissions.Dispatch.ExecuteDelivery)]`, `UserType.Driver` with custody verification OR `UserType.Staff`):**
  - `POST /api/v1/outbound/dispatch/packages/{packageId:guid}/pod` -> `SubmitProofOfDeliveryCommand` (multipart signature/photo outside DB tx)
  - `POST /api/v1/outbound/dispatch/packages/{packageId:guid}/confirm-delivery` -> `ConfirmPackageDeliveryCommand`
  - `POST /api/v1/outbound/dispatch/exceptions/driver-report` -> `ReportDriverDeliveryExceptionCommand`
- **Signed Carrier Tracking & Exception Webhooks (`[RequireCarrierWebhookSignature]`):**
  - `POST /api/v1/outbound/dispatch/webhooks/carrier-tracking` -> `IngestCarrierTrackingEventCommand`
  - `POST /api/v1/outbound/dispatch/webhooks/carrier-exception` -> `IngestCarrierDeliveryExceptionCommand`
  - Existing `POST /api/v1/outbound/shipping/returns/tracking/webhook` upgraded to `[RequireCarrierWebhookSignature]`.

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
Update permission visibility arrays (only when Phase 7.4 development is authorized), preserving strict role & identity isolation:
- **Client vs. Staff Guard Helper (`canOperateWarehouse`):** Frontend shell hides warehouse execution launchpad items (`RF Picking`, `Packing Bench`, `Dock Consolidation/Dispatch`) when `user.userType === 'Client' || user.userType === 'Driver'`, regardless of any legacy claims.
- **`Outbound & Fulfillment` (`/app/outbound`):** Visible to `outbound:orders:view`, `outbound:orders:create`, `outbound:waves:view`, `outbound:tasks:view`, `outbound:pick`, `outbound:pack`, `outbound:ship` (with sub-tabs gated strictly to each individual grant; `Client` sees Orders & Stock Check only).
- **`Transportation & Dispatch` (`/app/transportation`):** Visible to `dispatch:view`, `dispatch:manage-runs`, `dispatch:execute-delivery` (`Driver` sees Courier POD/Exception handheld view only; `Client` sees read-only Tracking/POD timeline only; `analytics:transportation:view` stays cleanly separated for Phase 7.6 Analytics).

---

## 7. Phased Execution Sequence & Independent Acceptance Checkpoints

| Sub-Stage | Scope | Prerequisite | Independent Acceptance Checkpoint |
| :--- | :--- | :--- | :--- |
| **Phase 7.4.0A** *(Security Hardening, Webhook Auth & ClientAdmin Cleanup)* | • Wire `WarehouseOperationalAuthorization` across all Outbound handlers, separate cross-facility Read vs. Mutation checks, enforce `UserType.Staff` on physical execution, and remove `Outbound.Pick`/`Outbound.Ship` from `ClientAdmin` seeding.<br>• Fix `403 ForbiddenException` mapping (`AllocationExecutionService`, `Delivery*Service`, `ExceptionHandlingMiddleware`).<br>• Secure `ReturnTrackingWebhook` (`HMAC-SHA256`, `±300s` timestamp skew, replay deduplication store, dual-secret rotation, audit logs). | Approved Pre-Implementation Spec | Independent security review + `.NET` `TestServer` suite confirms **ClientAdmin negative 403**, **multi-facility-read negative 403 on mutations**, **missing-facility 403**, and **unsigned/forged/replayed webhook 401/403** (zero regressions on existing 1,316 tests). |
| **Phase 7.4.0B** *(Missing Read Queries & RF Task Ownership/Concurrency)* | • Implement `GET /pick-tasks` (`TasksView`), read-only `GET /pick-tasks/next` (`Pick`), atomic `POST /pick-tasks/{id}/claim` (`Pick` + `PickTask.Version` concurrency check -> `409 Conflict`), `GET /shipments`, `GET /shipments/eligible-packages`, and `GET /packing/sessions\|{id}\|packages` (`Pack`).<br>• Enforce `AssignedUserId == _currentUser.UserId` ownership on `/confirm` & `/short-pick` and ignore client-supplied `OperatorUserId`. | Phase 7.4.0A Reviewed & Passed | Live PostgreSQL parallel test verifies **2 concurrent claims -> 1× `200 OK` + 1× `409 Conflict`**; unauthorized operator `/confirm` -> `403`; view-only vs. execute-only RBAC isolation tests pass. |
| **Phase 7.4.0C** *(Courier HTTP Exposure & Driver Custody Authorization)* | • Add `AssignedDriverUserId` custody binding (`Shipment`/`ShipmentCustodyTransfer`) and `ValidateCourierPackageAccessAsync` in `WarehouseOperationalAuthorization`.<br>• Create `DispatchController` exposing `GET /board`, timeline/order-delivery/POD reads (`Dispatch.View`), run/exception resolution (`Dispatch.ManageRuns`), courier POD/confirm/driver-report (`Dispatch.ExecuteDelivery`), and signed carrier webhooks. | Phase 7.4.0B Reviewed & Passed | Independent Postgres/HTTP test suite passes **Driver custody positive `200` / non-custody `403` / warehouse-endpoint `403`**, POD upload + `OrderDeliveryStatus.Delivered` rollup, and exception resolution. |
| **Phase 7.4.1** *(Orders, Allocation & Pick Waves UI)* | Implement `outboundApi.ts`, `OutboundOrdersPage.tsx`, `PickWavesPage.tsx`, bulk import modal, allocation/deallocation with idempotency key, and wave release UI. | Phase 7.4.0C Green | Live HTTP/Postgres test confirms Create -> Stock Check -> Allocate -> Wave Generate -> Wave Release; Vitest RBAC + modal tests pass. |
| **Phase 7.4.2** *(RF Directed Pick & Short-Pick UI)* | Implement `PickingWorkstationPage.tsx` (strict `tasks:view` vs `tasks:manage` vs `pick` RF separation + atomic `/claim` step), short-pick modal, and malformed DTO guards. | Phase 7.4.1 Green | Vitest permission-matrix tests verify view-only/manage-only/pick-only/ClientAdmin boundaries; Playwright verifies 320px/375px/768px/1440px reflow, error vs empty states, and double-scan protection. |
| **Phase 7.4.3** *(Packing & Cartonization Workbench UI)* | Implement `PackingWorkstationPage.tsx`, cartonization recommendation panel, tote-to-carton scan, scale weight close, tote release, and carrier rate/label generate + void panel. | Phase 7.4.2 Green | E2E flow confirms Session Start -> Recommend -> Create Carton -> Pack Item -> Close -> Rate Quote -> Label Generate/Void -> Session Complete; idempotency verified. |
| **Phase 7.4.4** *(Shipment Consolidation, BOL & Dispatch Custody UI)* | Implement `ShipmentConsolidationPage.tsx`, eligible package picker, staging/loading scans, immutable BOL viewer, and Driver Custody Handover modal. | Phase 7.4.3 Green | PostgreSQL verifier confirms `Draft -> ReadyForDispatch -> Dispatched` with 0 secondary stock mutation, immutable BOL snapshot, and `OutboundOrderStatus.Completed`. |
| **Phase 7.4.5** *(Dispatch Board, Tracking, POD & Courier Exceptions)* | Implement `DispatchAndCourierPage.tsx` (dispatch board, timeline stepper, Driver POD submission/preview/supersede, delivery exception workflow). | Phase 7.4.4 Green | Live integration confirms POD submission -> Delivery Rollup -> `OrderDeliveryStatus.Delivered`, plus driver/carrier exception resolution & negative `Driver` RBAC tests. |
| **Phase 7.4.6** *(Consolidated Visual Audit & Closure Gate)* | Run full `.NET` unit suite, PostgreSQL CI extended verifier, Vitest, production Vite build, Playwright multi-viewport capture, targeted automated a11y checks, and commit visual evidence to `docs/audits/outbound-transportation/`. | Phase 7.4.5 Green | CI green on HEAD; zero TypeScript errors; all 4 viewports (`1440x900`, `768x1024`, `375x812`, `320x568`) screenshots verified on GitHub; consolidated Phase 7.4 closure report submitted. |

---

## 8. Verification Matrix Requirements (Definition of Done for Phase 7.4)

1. **Real PostgreSQL & WebApi `TestServer` Verifier (`Phase74PostgresCheck` or extended CI workflow):**
   - Full outbound lifecycle in live Docker PostgreSQL: `OutboundOrder Create -> Allocate (FIFO/FEFO + 5x concurrent allocation collision test) -> Wave Generate/Release -> Atomic Claim Race (2 concurrent operators -> 1× 200 + 1× 409) -> Pick Confirm & Short-Pick (verify CycleCount + LocationLock + StockOnHand conservation) -> Packing Session -> Cartonization -> Label Generate (decoupled outside DB tx + failure/void recovery) -> Consolidate -> Stage -> Load -> BOL -> Dispatch Custody -> Tracking Ingest -> POD Submit & Confirm Delivery`.
   - Comprehensive negative `403` / `401` matrix verifying Staff single-facility mutation lock, unassigned Staff fail-closed, `ClientAdmin` holding `outbound:pick`/`ship` rejected `403`, `MultiFacilityView` rejected `403` on writes, `OperatorB` confirming `OperatorA` task rejected `403`, Driver non-custody `403`, and unsigned/forged/expired/replayed carrier webhook rejection (`401`/`403`).
2. **Backend `.NET` Suite (`Warehouse3PL.UnitTests`):** All existing **1,316** tests preserved + new `WarehouseOutboundAuthorizationHttpTests.cs`, `PickTaskClaimConcurrencyTests.cs`, `CarrierWebhookSignatureTests.cs`, and query handler unit tests.
3. **Frontend Vitest Suite (`src/UI/Nexus3PLFrontend/src/test/`):** Dedicated permission-matrix tests (`outbound-permissions.test.tsx`) asserting `ClientAdmin`, `Driver`, `View-Only`, `Manage-Only`, `Pick-Only`, `Pack-Only`, `Ship-Only`, and `Dispatch-Only` isolation, dual-scan RF picking & packing idempotency tests, and zero regressions on existing **72** Phase 7.1–7.3 tests.
4. **Playwright E2E, Targeted Accessibility & Visual Audit:**
   - Verified production preview (`vite build && vite preview`).
   - Exact route matching (avoiding glob shadowing like `/pick-tasks**` over `/pick-tasks/next`), payload contract assertions on every mocked/live card, explicit loading/error/retry/empty states, modal Escape + focus-trap checks, targeted automated accessibility checks, and `scrollWidth <= innerWidth` containment down to `320x568`.
   - Multi-viewport screenshots published to `docs/audits/outbound-transportation/` with audit `README.md`.
