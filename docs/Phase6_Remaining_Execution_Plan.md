# Remaining Phase 6 execution plan

Source: user-supplied Complete 3PL Warehouse & Logistics Project Roadmap (2026-11-17). Phases 6.5–6.8 are authorized for implementation, verification, commits and pushes after each phase. Phase 7 frontend integration and Phase 8 deployment are separate. Preserve the user's untracked `test_script.sql`.

## Discovery and reusable contracts

- Profitability: `IAnalyticsQueryService.GetClientProfitabilityAsync`, `DailyClientProfitabilitySummary`, `AnalyticsProjectionService.ProcessCommercialActivityRatedAsync`; shipment reconciliation/FX/package allocation from `IFreightReconciliationService`. Existing projection/query, freight reconciliation and transportation HTTP tests establish accounting, isolation and read-only patterns.
- Monitoring: `CarrierLostShipmentAlert` and `TransportationProjectionService` provide alert/dedup examples; `TransportationAnalyticsAuthorization.ResolveAsync` and `TransportationQueryScope.Apply` provide authenticated scope. Inventory risk, capacity, carrier scorecard and freight variance queries provide evidence; general rules and acknowledgement are missing.
- Decisions: `ISlottingOptimizationService`, `IRelocationRecommendationService` and existing bottleneck/workforce queries already cover warehouse/inventory optimization. Missing labor and carrier advisory decisions must use actual scoped evidence.
- Governance: dedicated transportation and inventory-aging durable replay exist. Generic `AnalyticsProjectionService.ReplayProjectionsAsync` needs review: clearing projections without reconstructing them is not replay, and durable movement logs must never be erased as a reset side effect.

## 6.5 — Profitability and cost-to-serve

Implement independent permission-gated client/facility summaries and comparisons, period comparison, and shipment reconciliation/allocation access. Copy explicit scoped querying from transportation analytics. Use facility-specific projection rows for comparisons to avoid aggregate double counting. Keep currencies separate unless authoritative FX is available. Label projected/estimated costs and unavailable cost components; never call partial contribution net profit. Nullable denominators and missing evidence remain explicit.

Verify arithmetic, negative/zero revenue, currencies, facility/tenant isolation under bypass, periods, package conservation, HTTP validation/permissions and no operational/financial saves. Review anti-patterns and changes independently before commit/push.

## 6.6 — Alerts and intelligent monitoring

Implement analytics-owned configurable monitoring rules, evidence-driven detectors for inventory thresholds, SLA/delay, cost variance and operational anomalies, stable deduplication, severity ordering, acknowledgement/resolution and permission-gated API/background integration. Copy existing persistence/configuration, lifecycle, projection and authorization conventions. Missing evidence must never become fabricated zero observations. Rule evaluation may write monitoring state only; it may not alter underlying operational or financial records.

Verify scope/ownership, threshold boundaries, missing evidence, repeated/concurrent evaluation, lifecycle, pagination, background evaluation confinement and unchanged financial evidence. Generate/test migration if schema changes. Review before commit/push.

## 6.7 — Optimization and decision intelligence

Reuse established slotting/relocation/inventory optimization. Add evidence-based labor/workload and carrier/transportation recommendations and bottleneck decision summaries, deterministic ranking, explanation and insufficient-evidence states. Forecasts must disclose historical sample/window and simple method rather than claim an untrained predictive model. Recommendations are advisory; no operational mutations or unsupported invented cost rates.

Verify ranking/ties, zero/missing data, scoped comparisons, deterministic decisions, explanation fidelity, permissions and read-only behavior. Review before commit/push.

## 6.8 — Governance and enterprise verification

Repair generic replay: preserve durable sources and support true reconstruction only where source evidence exists; reject unsupported destructive rebuilds before changes. Verify durable replay equivalence, repeat idempotency, scope/window behavior and transactional safety. Add cross-module authorization and read-only/materialization governance tests. Run reproducible local PostgreSQL checks and performance/scalability experiment with recorded dataset, timings, query counts and limits; do not imply a production capacity guarantee.

Run complete solution/frontend regression, EF model consistency, migration verification for introduced schema, dependency audit, and final anti-pattern/code-quality review. Publish a closure matrix distinguishing verified capabilities, unavailable inputs and any unpassed gate. Do not assert enterprise closure for unverified or failing gates. Commit/push verified phase work and evidence; report real blockers precisely.

## Global guards

- Reuse exact available APIs and established repository patterns. Verify unfamiliar library APIs in primary documentation before use.
- Authenticated identity determines scope; filters may narrow it only. Test with ambient tenant filters bypassed.
- Do not mix original currencies, duplicate aggregate/facility rows, infer goods values from freight charges, or infer unknown costs as zero.
- No financial/operational mutation from analytical queries or advisory decisions. Monitoring writes belong to analytics-owned state.
- No production database access, force pushes, main merges, deployment, or unrelated SQL staging.
- Each phase: implementation → independent verification/anti-pattern/code review → explicit-path commit → push current feature branch. Keep checks sequential where .NET outputs are shared.
