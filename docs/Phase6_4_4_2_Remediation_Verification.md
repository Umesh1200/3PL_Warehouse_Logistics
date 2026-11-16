# Phase 6.4.4.2 remediation verification

Date: 2026-11-16. Review baseline: `a198f020`. Scope remains Phase 6.4.4.2; Phase 6.4.5 requires approval.

## Changes

- Reviewed the external partial patch before applying it. Retained its package-line loading, UOM quantities, dimensional recalculation, and explicit query confinement; completed the remaining cases below.
- Carrier zone/lane names are labels, not states or postal codes. Agreements now carry optional destination country, state/province, postal-prefix and facility selectors. All populated selectors must match every package's authoritative order routing. Agreement facility scope establishes the origin for facility-to-facility lanes. Missing mappings, missing order evidence and mixed destinations return indeterminate and cannot fall through to a generic or client fixed-rate contract. A known non-match can use a generic agreement. Existing zone labels require explicit mapping; no mapping is guessed or backfilled.
- Dimensional weight is recomputed from physical dimensions. Contractual KG divisors use cm³/kg; LB divisors use in³/lb, converting volume by 16.387064 cm³/in³. Scale conversion precedes increment rounding, without prematurely rounding the scale value. Stored quote dimensional weights cannot override contract terms.
- Weight-dependent rating rejects missing dimensions, missing scale weights, empty packages and declared package-count mismatches. An incomplete shipment cannot rate only its known subset. UNIT and PIECE quantities sum positive packed line quantities; SHIPMENT quantity is one. Count contracts do not require carrier billed weight unless a weight adjustment itself is being audited. Quantity-based discrepancy allocations conserve the shipment baseline across invoice records.
- Carrier agreement facility deletion uses RESTRICT, preventing a scoped contract from becoming global through SET NULL. Client and facility predicates constrain shipment and agreement queries before materialization, even with ambient tenant-filter bypass enabled. Routing lookups also constrain client/facility identity.
- The baseline already creates carrier agreements and tiers. Repaired the subsequent migration to upgrade those tables in place, preserving rows and tolerating an already-provisioned carrier schema. Repaired missing OrderStatusHistory metadata in its committed designer from the baseline commit's model snapshot. Added a forward migration for routing selectors and the restrictive facility FK.
- Analytics remains read-only. No financial service write path or Phase 5 financial schema was changed.

## Verification

- Full solution regression: **1,156 passed, zero failed, zero skipped** (final run recorded in `phase6442-complete.trx`). This includes the original ledger-invariance test, explicit mapping and unknown/mixed-routing cases, imperial divisors and increment boundaries, missing evidence and UOM cases, and a materialization interceptor rejecting other-client shipments/contracts with tenant-filter bypass enabled.
- `dotnet ef migrations has-pending-model-changes`: no pending model changes.
- Generated the full idempotent PostgreSQL migration script successfully.
- Executed migrations against disposable local PostgreSQL 18.6 databases: fresh chain; populated InitialCreate baseline; baseline with complete carrier columns/FKs/indexes but no carrier-migration stamp; already-stamped carrier migration; full generated idempotent SQL executed twice. All five scenarios passed. Re-running the completed EF chain is also a no-op.
- Seeded upgrades retain carrier agreement/tier identities and rates. Serialized row comparisons preserve clients, facilities, shipments, carrier costs, client freight agreements, invoices, invoice lines and tiers. Carrier costs and commercial agreements contain seeded rows; invoice tables are empty in this migration fixture. The final facility FK is verified as RESTRICT in PostgreSQL metadata.
- `git diff --check`: clean.

Run the reproducible migration check against a disposable local server:

```powershell
$env:FREIGHT_MIGRATION_TEST_CONNECTION = 'Host=127.0.0.1;Port=55442;Username=postgres'
dotnet ef migrations script --idempotent --project src/Infrastructure/Warehouse3PL.Infrastructure --output "$env:TEMP\freight-migrations.sql"
dotnet run --project tests/verification/FreightMigrationCheck -- "$env:TEMP\freight-migrations.sql"
```

The checker creates uniquely named `phase6442_*` databases and retains them for inspection. It refuses non-local hosts. It does not connect to a production database.

## Review and deployment prerequisites

- No code/test blocker remains in the verified scenarios. Obtain review approval before proceeding to Phase 6.4.5. Nothing has been pushed or merged.
- Target deployment schema/history was not supplied or accessed. A manually provisioned database with no InitialCreate migration stamp still needs schema reconciliation and an explicitly reviewed baseline adoption; this patch does not blindly stamp an unknown schema. Existing invalid foreign-key references cause migration failure rather than being deleted or silently broadened.
- The compatibility repair is intentionally forward-only: its Down refuses to drop potentially pre-existing evidence. Downgrade across it requires a verified backup/restore plan. The later routing migration has its generated Down, but reversal removes routing selectors and restores the old SET NULL rule, so it should not be used to roll back contractual evidence casually.
- Build output retains existing dependency advisories for OpenTelemetry.Api 1.11.2 and, in the standalone migration checker dependency graph, Newtonsoft.Json 11.0.1. Existing unrelated nullable warning also remains. Dependency remediation is outside this milestone patch.

## Preserved local work

No applicable AGENTS.md was present in the repository or its parent directories. The initial untracked `test_script.sql` remains untouched and uncommitted. Its contents and the original locally truncated migration designer were copied before repair to `C:\Users\DELL\Documents\Codex\2026-11-16\phase6442-preserved`. The designer's literal truncation marker prevented compilation; the working version was restored and repaired for verification. The original local version remains available in that backup.

Local execution evidence is under `%TEMP%\phase6442-postgres` (server logs, isolated database files and regression TRX files), with additional `%TEMP%\phase6442-*.log` logs and `%TEMP%\phase6442-verified.sql`.
