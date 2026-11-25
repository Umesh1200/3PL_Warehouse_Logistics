# Nexus3PL — MDM-0 Consolidated Stabilization Closure Report

**Status:** **ACCEPTED & CLOSED**  
**Workstream:** `MDM-0` (Baseline Stabilization & Hardening — Pre-MDM-1 Foundation)  
**Branch:** `feature/phase-7-enterprise-frontend`  
**Prior Accepted Phase 7.4 Baseline SHA:** `8af4474efde676413dae487928ecebcde3f29059`  
**Authoritative `MDM-0` Closure Baseline SHA:** `6ce6165ebdf606b12538371a5f6c82a3ca6d9c8c`  
**Protected `main` / `origin/main`:** Unchanged at `a198f02090194ce4d59dec2941c89d519757f4b4`  
**Date:** November 25, 2026  

---

## 1. Executive Summary

All five stabilization sub-units of **`MDM-0`** (`MDM-0.1` through `MDM-0.5`), together with the accepted Revision 2 architecture specification (`6b5c006`), were independently audited and verified across `.NET` Unit & Security tests, live `PostgreSQL 16` operational/concurrency checks, frontend production builds, `Vitest`, and multi-viewport `Playwright` end-to-end suites.

The repository now holds an independently accepted baseline at **`6ce6165ebdf606b12538371a5f6c82a3ca6d9c8c`** with zero blocking defects, zero schema drift on closed Phase 7.4 invariants, and full CI verification ahead of `MDM-1` planning.

---

## 2. Accepted Sub-Unit Verification Ledger

| Sub-Unit | Accepted Scope & Outcomes | Accepted Commit SHA | GitHub Actions Run | Verified Test & Check Gate |
| :--- | :--- | :--- | :--- | :--- |
| **Specification Baseline** | Revision 2 gap audit & pre-implementation spec (`D1`–`D5`, `MDM-0`–`MDM-6`, `LegalEntity` separation, conditional Nepal VCTS/CBMS, DB-authoritative cache contract). | `6b5c006` | *(Docs only)* | Accepted Revision 2 gate prior to `MDM-0.1`. |
| **`MDM-0.1`** *(Facility Address & DTO)* | `ShippingLabelService` resolves persisted `Facility` origin address (no Columbus, OH hardcoding), fails closed when incomplete, and `FacilityDto` separates privileged `ConfigurationJson` / `OperatingHoursJson`. | `274eaba3f27563832f31f68196e790def2f95c9c` | `45425801146` | `1,387 / 1,387` .NET passed; live PostgreSQL `MDM-0.1` (`NP`/`US` origin, fail-closed missing address, DTO auth) passed; `102 / 102` Vitest & `17 / 17` Playwright local reported. |
| **`MDM-0.2`** *(Storage Download Security)* | Hardened `GET /api/v1/storage/download` endpoint (`StorageController`), non-Dev fail-closed signing key enforcement, short bounded HMAC verification, canonical prefix allowlist, tenant/facility ownership & Staff/POD parity (`MDM02-R01`, `MDM02-R02`), safe download headers. | `f07340d43a4aa8d4447ebb2491e8e0ec22972920` | `45554909791` | `1,397 / 1,397` .NET passed; live PostgreSQL `MDM-0.2` security & cross-tenant/facility isolation passed. |
| **`MDM-0.3`** *(Invoice Sequence Integrity)* | `InvoiceSequence` row-level PostgreSQL locking (`FOR UPDATE`), first-row safe atomic upsert, rollback/retry safety, dry-run sequence isolation, and disjoint billing window checks. | `259fc190b65d954fe8b89ea47daf7dbc0ae201c1` | `45558304345` | `1,401 / 1,401` .NET passed; live PostgreSQL `MDM-0.3` invoice & CRN concurrency/rollback/dry-run verifier passed. |
| **`MDM-0.4`** *(Operations Policy Wiring & Execution Integrity)* | Wired `AllocationPolicy.PreferFullHandlingUnits` (residual-demand-aware `R04`), `AllocationExecutionService.strategyOverride`, `PutawayStrategyRule.PreferConsolidation`, shared strict temperature compatibility (`R01`), unified effective rule precedence (`R02`), client-facility pairing & `Guid.Empty` rejection (`R03`, `R05`), and PostgreSQL allocation/putaway row locking + transaction-scoped lock lifetime. | `76738cba033ead817d0a7a4292223eb85506ae86` | `45584265752` | `1,409 / 1,409` .NET passed; live PostgreSQL `MDM-0.4` concurrent allocation/putaway conservation, `R01`–`R05` negative & positive gates passed. |
| **`MDM-0.5`** *(UI Audit Follow-Ups)* | Replaced hardcoded Windows screenshot output path in `outbound-transportation-audit.spec.ts` with 4-level repository-relative ESM resolution (`P746-N01`); added `OutboundTableRegion` with `320px` discoverable scroll hint, `aria-describedby`, `role="region"`, `tabIndex={0}`, visible `:focus-visible` outline, and zero page overflow across outbound tables (`P746-N02`). | `6ce6165ebdf606b12538371a5f6c82a3ca6d9c8c` | `45588183914` | `1,409 / 1,409` .NET & live PostgreSQL `MDM-0.1`–`MDM-0.4` CI green; local `tsc --noEmit && vite build` green; `103 / 103` Vitest passed; `32 / 32` Playwright passed (with `320x568` visual inspection). |

---

## 3. Cumulative `MDM-0` Commit Chain (`8af4474` -> `6ce6165`)

```text
6ce6165 fix(mdm-0.5): make outbound audit screenshot path portable and add 320px accessible table scroll hints
76738cb fix(mdm-0.4): reject unsupported Guid.Empty putaway strategies safely for MDM04-R05
4b81b55 fix(mdm-0.4): remediate R01-R04 temperature safety, rule precedence, strategy client-facility auth, and residual HU ordering
37df40a test(mdm-0.4): use persisted staff user identity in Mdm04PostgresCheck putaway race
b935e86 fix(mdm-0.4): pre-lock active SKU stock rows across transaction and replan on post-lock ineligibility
58feaa2 test(mdm-0.4): isolate Mdm04PostgresCheck facility and deterministic empty bin naming
de16ffb feat(mdm-0.4): wire PreferFullHandlingUnits, strategyOverride, PreferConsolidation, and policy safety/concurrency guards
259fc19 test(mdm-0.3): use disjoint period windows in Mdm03PostgresCheck
110da56 feat(mdm-0.3): serialize invoice/credit-note sequences with PostgreSQL locking, transactional rollback, and dry-run isolation
f07340d fix(mdm-0.2): remediate MDM02-R01 POD permission/custody parity and MDM02-R02 finalized/hash integrity
f10f5e0 feat(mdm-0.2): add authorized storage download endpoint and fail-closed signing key hardening
274eaba feat(mdm-0.1): resolve persisted facility origin on shipping labels and separate privileged FacilityDto config
6b5c006 docs(mdm): record accepted Revision 2 global master data gap audit and specification
```

---

## 4. Carried-Forward Design Dependencies & Nonblocking Refinements

1. **Facility-Wide Default `PutawayStrategyRule` Persistence (`MDM04-R05` -> `MDM-2`):**  
   `PutawayStrategyRule.ClientId` currently enforces a non-null foreign key to `clients` (`fk_putaway_strategy_rules_clients_client_id`). `CreatePutawayStrategyCommandHandler` cleanly rejects `Guid.Empty` (`400 Bad Request`) until a separately approved schema design and EF Core migration in `MDM-2` enables nullable client scoping or a dedicated facility-default policy table.
2. **Overflow-Aware Mobile Table Scroll Hint (`MDM-0.5` nonblocking observation):**  
   `OutboundTableRegion` currently renders the mobile scroll hint (`<= 768px`) unconditionally inside narrow cards. Adding an optional `ResizeObserver`-backed overflow detector so tables that already fit inside `320px` suppress the swipe hint may be added opportunistically later without affecting `MDM-0` closure.

---

## 5. Closure Status

- **`MDM-0` Consolidated Stabilization:** **ACCEPTED & CLOSED** at **`6ce6165ebdf606b12538371a5f6c82a3ca6d9c8c`**.
- **Authorized Next Step:** `MDM-1` decomposition and detailed design planning ([MDM_1_Implementation_Plan_And_Design.md](MDM_1_Implementation_Plan_And_Design.md)), gated on independent approval before writing migrations or entities.
