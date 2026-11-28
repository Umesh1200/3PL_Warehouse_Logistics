# MDM-2.2 — Client, Customer & Supplier Master Data Implementation Report

**Workstream:** Nexus3PL Enterprise Master Data Management (MDM-2)  
**Stage:** MDM-2.2 — Client, Customer & Supplier Master Data  
**Branch:** `feature/phase-7-enterprise-frontend`  
**Accepted Baseline:** `3c1a03ba7c1fc8cf67ca6ca244be75c5475d45d6`  
**Migration:** `20261128061719_MDM2_2_ClientCustomerSupplierAndPreferences`

---

## 1. Implementation Summary

All MDM-2.2 objectives were implemented additively without modifying any previously accepted migrations or breaking existing API/frontend contracts:

1. **Client Master (`ClientsController`, `CreateClientCommand`, `UpdateClientCommand`, `DeactivateClientCommand`, `TransitionClientStatusCommand`, `GetClientsQuery`, `GetClientByIdQuery`):**
   - Permanent normalized `AccountCode` uniqueness (`ux_clients_normalized_account_code`) enforced across active and soft-deleted rows; `AccountCode` is immutable after creation (`CLIENT_CODE_IMMUTABLE`).
   - Active `CurrencyMaster` validation via `PartnerMasterAuthorization.ValidateRequiredCurrencyAsync`.
   - Structured billing address + `BusinessClassification` + `TaxRegistrationNumber` validated against MDM-1 `CountryProfile`, `AdministrativeDivision`, and postal regex via `PartnerAddressValidationHelper`.
   - Safe deactivation/suspension/termination guarded by `PartnerMasterDependencyGuard` inside PostgreSQL advisory-locked `ExecuteWithClientLifecycleLockAsync` (all 6 `StockOnHand` buckets, present HUs, open outbound orders, open POs/ASNs, open tasks/packing, active allocations, active facility accesses).
   - `Terminated` enforced as an immutable terminal state (`CLIENT_TERMINATED_IMMUTABLE`).

2. **Client Contacts (`ClientContactsController`, `ClientContactsHandlers.cs`):**
   - Full post-create CRUD (`GET`, `POST`, `PUT`, `DELETE`) with `RoleTitle`, `IsPrimary`, `IsActive`, and `ConfigRevision` optimistic concurrency.

3. **Client-Facility Access (`ClientFacilityAccessController`, `ClientFacilityAccessHandlers.cs`):**
   - Full `Active` / `Suspended` / `Revoked` lifecycle management and atomic `PrimaryOperatingFacility` switching under `ExecuteWithClientFacilityAccessLockAsync` backed by partial unique index `ux_client_facility_access_primary_active`.
   - Validates active `Client` and active `Facility` on grant/reactivation; blocks suspension/revocation when operational commitments exist in that `(ClientId, FacilityId)`.

4. **Client Operational Preferences (`ClientPreferencesController`, `ClientOperationalPreferencesHandlers.cs`):**
   - Structured `ClientOperationalPreference` entity supporting explicit `ClientDefault` (`FacilityId = null`) and `FacilityOverride` (`FacilityId != null`) with effective resolution (`GET /effective/{facilityId}`).
   - Validates active `ClientFacilityAccess` and active same-facility `WarehouseZone` references; zero generic settings bags and zero commercial rate-card/SLA duplication.

5. **Customer Master & Structured Addresses (`CustomersController`, `CustomerHandlers.cs`, `CustomerAddress`):**
   - Tenant-scoped CRUD, lifecycle status management, and structured billing/delivery address management.
   - Permanent tenant-scoped normalized `CustomerCode` uniqueness (`ux_customers_client_normalized_code`) and code immutability (`CUSTOMER_CODE_IMMUTABLE`).
   - Historical `OutboundOrder` / `ShippingAddress` snapshots remain untouched on Customer updates or deactivation; `CreateOutboundOrderCommand` blocks inactive customers (`CUSTOMER_INACTIVE`).

6. **Supplier Master & Contacts (`SuppliersController`, `CreateSupplierCommand`, `GetSuppliersQuery`, `SupplierLifecycleAndContactHandlers.cs`, `SupplierContact`):**
   - Permanently resolved prior case-sensitivity defect via `NormalizedSupplierCode` + `ux_suppliers_client_normalized_code` across all rows within tenant.
   - Added `GET /{id}`, `PUT /{id}`, `PATCH /{id}/status`, and structured `SupplierContact` CRUD.
   - Historical `PurchaseOrder` and `AdvanceShippingNotice` relationships preserved; `CreatePurchaseOrderCommand` and `CreateAsnCommand` block inactive suppliers (`SUPPLIER_INACTIVE`).

7. **Security, Tenant Isolation & MDM-1.5 Consistency:**
   - Centralized DB-authoritative `PartnerMasterAuthorization` verifying `Users`, `UserRoles`, `RolePermissions`, `ClientId`, and `AssignedFacilityId`.
   - Rejects unauthenticated (`401`), `Driver` (`403`), missing tenant (`403`), forged SuperAdmin claims (`403`), out-of-facility Staff (`403`), cross-client reads/writes (`403`), and conflicting `X-Tenant-Id` / `X-Facility-Id` headers (`400`).
   - Wired all MDM-2.2 entities into `ConfigurationScopeMapper` for atomic `ConfigurationScopeEpoch` increments and transactional `ConfigurationInvalidationEvent` outbox emission.

---

## 2. Verification Evidence

- **Local .NET Unit & HTTP JWT Security Suite:** `1,488 / 1,488` passed (`0` failed, `0` skipped), including `Mdm22ClientCustomerSupplierTests` (end-to-end Client/Contact/Access/Preference/Customer/Supplier lifecycle, dependency guards, operational consumer enforcement, and real ASP.NET Core `TestServer` JWT HTTP security matrix).
- **Local Frontend Verification:** `tsc --noEmit` (`0` errors), `Vitest` (`111 / 111` passed), `Vite build` (clean production bundle).
- **Live PostgreSQL 16 + Redis 7 Verification (`Phase73PostgresCheck` — `PG-1` through `PG-41`):**
  - `PG-37`: Pre-MDM-2.2 populated database preflight collision detection (fail-closed on case-insensitive `supplier_code`), rollback, remediation, retry, and deterministic backfill.
  - `PG-38`: Permanent DB-level normalized uniqueness indexes (`clients`, `customers`, `suppliers`).
  - `PG-39`: Concurrent primary operating facility switching serialized cleanly under PostgreSQL advisory locks (`ExecuteWithClientFacilityAccessLockAsync`).
  - `PG-40`: Multi-context MDM-1.5 `ConfigurationScopeEpoch` & `ConfigurationInvalidationEvent` outbox verification.
  - `PG-41`: Reversible `Down()` to MDM-2.1 and re-`Up()` to MDM-2.2 with full data preservation.
- **GitHub Actions CI:** Pending push verification (updated upon CI completion).

---

## 3. Remaining Limitations & Deferred Verification Items

- **MDM-2.2 Scope Boundaries (By Design):**
  - Metadata-driven admin UI screens for Clients/Customers/Suppliers are deferred to **MDM-2.6** per approved architecture; existing frontend API contracts remain 100% compatible.
  - Commercial rate cards, client billing agreements, and SLA policies remain in their dedicated Phase 5 / Billing domain and are intentionally not duplicated in `ClientOperationalPreference`.
  - No `MDM-2.3` (Item Master) or subsequent work was started.
- **MDM-2.1 Deferred Operational Verification Items (Tracked Forward):**
  - End-to-end Playwright UI workflows for MDM-2.1 Warehouse Master / Location Hierarchy and MDM-2.2 Partner Master management screens remain deferred until the **MDM-2.6** metadata-driven UI stage.
  - Multi-instance Redis pub/sub live failover testing under network partition remains covered by PostgreSQL-authoritative epoch polling (MDM-1.5) and will be re-validated during final MDM-2 staging hardening.
