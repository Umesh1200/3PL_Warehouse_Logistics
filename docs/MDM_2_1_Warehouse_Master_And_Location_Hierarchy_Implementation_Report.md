# Nexus3PL — MDM-2.1 Warehouse Master & Physical Location Hierarchy Implementation Report

**Workstream:** `MDM-2.1` — Warehouse Master & Physical Location Hierarchy (Option C + Mandatory Architectural Conditions **A01–A07**)  
**Repository:** `Umesh1200/3PL_Warehouse_Logistics`  
**Branch:** `feature/phase-7-enterprise-frontend`  
**Authoritative Design Baseline:** `9e525f762a679504c3387ef911c6b7e240a6562e`  
**Authoritative Application Baseline:** `c1723711be66c8f5e0d882b8d9c5d359d881d039`  
**Main Branch Preserved At:** `a198f02090194ce4d59dec2941c89d519757f4b4`  
**Date:** November 27, 2026  

---

## 1. Executive Summary

`MDM-2.1` implements the approved **Option C (Hybrid Operational `WarehouseZone` + Evolved Self-Referencing Physical `Location` Hierarchy + Separated Companion Profile Tables)** architecture with full database, concurrency, authorization, operational putaway/allocation, and `MDM-1.5` configuration-epoch/outbox integration across all mandatory architectural conditions **A01–A07**.

All **21 existing `locations.location_id` foreign keys** and **3 `warehouse_zones.zone_id` foreign keys** are preserved with zero destructive drops or rewrites of previously applied migrations.

---

## 2. Mandatory Architectural Conditions Compliance Matrix (A01–A07)

| Condition | Requirement | Implementation & Verification Evidence |
| :-: | :--- | :--- |
| **A01** | Separate administrative lifecycle (`LocationStatus`: `Active`, `Inactive`, `Decommissioned`) from operational locks (`LocationLock` / `IsLocked`) and physical damage (`IsDamaged`); reactivation never releases locks or clears damage; independent lock release | `WarehouseMasterEnums.cs`, `Location.SetLifecycleStatus`, `UpdateLocationStatusCommandHandler`, `ReleaseLocationLockCommandHandler` (`POST /api/v1/locations/{id}/locks/{lockId}/release`). Verified in unit & live PostgreSQL 16 tests (`PG-9`). |
| **A02** | Composite same-facility parent & zone FKs, self-cycle `CHECK`, PostgreSQL multi-hop cycle trigger, transactional advisory lock (`pg_advisory_xact_lock`) for concurrent opposite reparenting (`A->B` vs `B->A`), server-derived `HierarchyLevel` (1..10) & ID-based `HierarchyPath` (`/{id:N}/...`) with atomic subtree recalculation, 4 partial unique indexes on `location_hierarchy_rules` | `LocationConfiguration.cs`, `LocationHierarchyRuleConfiguration.cs`, `ApplicationDbContext.ExecuteWithFacilityHierarchyLockAsync`, `MoveLocationCommandHandler`, migration trigger `trg_locations_prevent_cycle` + `fn_locations_prevent_cycle()`. Verified in live PG16 (`PG-3`, `PG-4`, `PG-5`, `PG-6`). |
| **A03** | Permanent facility-scoped normalized uniqueness (`NormalizedLocationCode`, `NormalizedBarcode`, `NormalizedZoneCode`, `NormalizedDoorNumber`, `NormalizedEquipmentCode`) with no reuse of soft-deleted codes/barcodes; immutable codes by default | Permanent unfiltered unique indexes on normalized columns + `IgnoreQueryFilters()` pre-checks in create/update/batch handlers + immutable code rejection (`DomainException`). Verified in unit & live PG16 (`PG-8`). |
| **A04** | Reuse existing `LocationType` enum values; configurable root/transition rules per facility supporting compact Kathmandu (`FAC-KTM1`) and deep Chicago (`FAC-ORD1`) topologies; `HierarchyLevel` = derived tree depth | `LocationHierarchyRuleEvaluator.cs`, `LocationHierarchyRulesHandlers.cs`, seeded global rules in migration `20261127125902_MDM2_1_WarehouseMasterAndLocationHierarchy`. Verified in unit & live PG16 (`PG-7`). |
| **A05** | Same-facility composite FKs across parent locations, zones, storage/operational profiles, `default_staging_location_id`, equipment requirements, and `dock_doors.location_id`; `DedicatedClientId` validated via active `ClientFacilityAccess`; deterministic facility-wide vs client-specific `PackagingMaterial` uniqueness; server-side scope & conflicting `X-Facility-Id` header rejection (`400`) | Composite FKs in EF Core configurations & migration, `WarehouseMasterAuthorization.cs`, `PackagingMaterialConfiguration.cs` partial unique indexes (`WHERE client_id IS NULL` / `WHERE client_id IS NOT NULL`). Verified in real HTTP JWT security tests & live PG16 (`PG-3`, `PG-11`). |
| **A06** | Expose only actively enforced storage profile fields (`DedicatedClientId`, `MaxHandlingUnits`, `AllowMixedSku`, `AllowMixedLot`, `IsPutawayAllowed`) alongside `Location.IsStorable` and `WarehouseZone.DedicatedClientId`; defer thermal/hazmat bin overrides to `MDM-2.4`; preserve `MDM-0.4` temperature safety floors | `LocationStorageProfile.cs`, `PutawayLocationSelector.cs`, `PutawaySafetyValidator.cs`, `ConfirmPutawayCommand.cs`, `ConfirmPutawayTaskCommand.cs`, `AllocationCandidateFilter.cs`. Verified in unit & live PG16 tests. |
| **A07** | Fail-closed pre-cutover migration guard (cycles, cross-facility refs, orphans, normalized collisions, invalid temp bounds, stock in structural nodes); preserve all 21 `location_id` & 3 `zone_id` FKs; transactional lifecycle deactivation guards (`SELECT ... FOR UPDATE`) against concurrent stock/HU/task placement; reversible `Down()` | `20261127125902_MDM2_1_WarehouseMasterAndLocationHierarchy.cs` (`DO $$` guard + deterministic backfill + clean `Down()`), `ApplicationDbContext.ExecuteWithLocationLifecycleLockAsync`. Verified in live PG16 (`PG-1`, `PG-2`, `PG-10`, `PG-13`). |

---

## 3. Domain, Schema & Migration Summary

- **New Enums (`src/Core/Warehouse3PL.Domain/Enums/WarehouseMasterEnums.cs`):** `LocationStatus` (`Active`, `Inactive`, `Decommissioned`), `WarehouseStationRole`, `WarehouseEquipmentStatus`.
- **New Companion Entities (`src/Core/Warehouse3PL.Domain/Entities/Facilities/`):**
  - `LocationHierarchyRule` (`location_hierarchy_rules`)
  - `LocationStorageProfile` (`location_storage_profiles`)
  - `LocationOperationalProfile` (`location_operational_profiles`)
  - `WarehouseEquipment` (`warehouse_equipment`)
  - `LocationEquipmentRequirement` (`location_equipment_requirements`)
- **Evolved Existing Entities:**
  - `Location`: `NormalizedLocationCode`, `LocationName`, `NormalizedBarcode`, `HierarchyLevel`, `HierarchyPath`, `Status`, `IsStorable`, `Remarks`, `ConfigRevision`.
  - `WarehouseZone`: `NormalizedZoneCode`, `DedicatedClientId`, `Remarks`, `ConfigRevision`.
  - `DockDoor`: `LocationId`, `NormalizedDoorNumber`, `MaxTrailerLengthCm` (canonical metric, `30.48 cm/ft`), `ConfigRevision`.
  - `PackagingMaterial`: `NormalizedCode`, `CurrencyCode`, `ConfigRevision`.
- **Additive EF Core Migration:** `20261127125902_MDM2_1_WarehouseMasterAndLocationHierarchy` (no prior migrations modified).
  - Compatible with the `--migrate-db` execution boundary established in `MDM-1.6`.
  - Downgrade compatibility: `Down()` drops the cycle trigger/function, composite FKs, new companion tables, and additive columns, and restores the pre-MDM-2.1 single-column FKs and indexes cleanly.

---

## 4. API Endpoints & Authorization Surface

All Warehouse Master endpoints enforce DB-authoritative Staff RBAC via `WarehouseMasterAuthorization`:
- Anonymous → `401 Unauthorized`
- Client user / unprivileged Staff / forged SuperAdmin claim / cross-facility Staff → `403 Forbidden`
- Conflicting `X-Facility-Id` header vs query/body `FacilityId` → `400 Bad Request`
- Global `LocationHierarchyRule` mutations → strictly DB-verified `SuperAdmin`

| Controller | Route Prefix | Operations |
| :--- | :--- | :--- |
| `WarehouseZonesController` | `/api/v1/warehouse-zones` | `GET` (paged), `GET {id}`, `POST`, `PUT {id}`, `PATCH {id}/status` |
| `LocationsController` | `/api/v1/locations` | `GET` (paged), `GET {id}`, `GET tree`, `POST`, `PUT {id}`, `POST {id}/move`, `PATCH {id}/status`, `POST batch-generate`, `GET {id}/locks`, `POST {id}/lock`, `POST {id}/unlock`, `POST {id}/locks/{lockId}/release`, `PUT {id}/storage-profile`, `PUT {id}/operational-profile`, `POST {id}/equipment-requirements`, `DELETE {id}/equipment-requirements/{requirementId}` |
| `LocationHierarchyRulesController` | `/api/v1/location-hierarchy-rules` | `GET`, `PUT` |
| `DockDoorsController` | `/api/v1/dock-doors` | `GET`, `GET {id}`, `POST`, `PUT {id}`, `PATCH {id}/status` |
| `WarehouseEquipmentController` | `/api/v1/warehouse-equipment` | `GET` (paged), `GET {id}`, `POST`, `PUT {id}`, `PATCH {id}/status` |
| `PackagingMaterialsController` | `/api/v1/packaging-materials` | `GET` (paged), `GET {id}`, `POST`, `PUT {id}`, `PATCH {id}/status` |

Additional `Facility` closure:
- `FacilityReferenceValidationHelper.cs` validates `DefaultCurrencyCode` against active `CurrencyMaster` records.
- `UpdateFacilityCommand.cs` blocks facility deactivation when active stock on hand, open outbound orders, or open putaway tasks exist.

---

## 5. Cache & Configuration Epoch Invalidation (`MDM-1.5` Integration)

`ConfigurationScopeMapper.cs` registers `WarehouseZone`, `Location`, `LocationHierarchyRule`, `LocationStorageProfile`, `LocationOperationalProfile`, `DockDoor`, `WarehouseEquipment`, `LocationEquipmentRequirement`, and `PackagingMaterial`, atomically incrementing `configuration_scope_epochs` and appending `configuration_invalidation_events` outbox rows across `Facility`, `Client`, and `Global` scopes on every write (including both original and destination scopes when scope keys change).

---

## 6. Verification Evidence

- **.NET Unit & Real HTTP Security Suite (`Release`):** `1,454 / 1,454` passed (up from `1,449` baseline; includes 5 comprehensive MDM-2.1 unit & real JWT HTTP security test suites).
- **Frontend Regression Suite:** `111 / 111` Vitest passed, `tsc --noEmit` passed, `vite build` passed.
- **Playwright Chromium E2E Suite:** `34 / 34` passed.
- **Live PostgreSQL 16 & Redis 7 Verification (`Mdm21PostgresCheck` in `Phase73PostgresCheck`):** Executed via GitHub Actions CI (`PG-1` through `PG-13`, covering preflight fail-closed/rollback/retry, FK catalog preservation, composite FKs, multi-hop cycle trigger, concurrent opposite `A->B` vs `B->A` reparenting, atomic subtree recalculation, KTM1/ORD1 rules, permanent normalized uniqueness, A01 lock independence, concurrent deactivation vs putaway race, dock cm/ft conversion, packaging tenant isolation, 2-context `MDM-1.5` epoch/outbox invalidation, and `Down()`/`Up()` reversibility).

---

## 7. Next Gate

`MDM-2.1` implementation is complete and ready for independent acceptance review. `MDM-2.2` and subsequent stages have **not** been started.
