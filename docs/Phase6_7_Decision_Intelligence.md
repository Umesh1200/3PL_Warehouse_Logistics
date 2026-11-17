# Phase 6.7 — Optimization and decision intelligence

The Phase6.7 backend provides advisory evidence, transparent historical estimates and existing simulation workflows. Operational execution remains subject to existing WMS approval/execution paths.

## Capability map

| Roadmap area | Implemented capability |
|---|---|
| Warehouse and inventory optimization | Existing ISlottingOptimizationService compatibility checks, suitability matrices, ABC/velocity/ergonomic/physical slotting simulation; IRelocationRecommendationService disruption-aware prioritization, inspection, review and approval |
| Labor optimization | New facility picking-workload advice identifies unassigned/open tasks and remaining positive units; compares observed positive picking executions in the exact historical window to estimate required labor hours |
| Transportation and carrier selection recommendations | New bounded carrier shortlist ranks statistically reliable historical on-time delivery percentage, then average transit days and carrier code; insufficient samples remain unranked |
| Bottleneck detection | Existing GetFacilityBottlenecksAsync provides receiving/picking/packing/dispatch diagnostics; new picking advice explicitly identifies live backlog and assignment review priorities |
| Predictive and prescriptive analytics | Transparent historical throughput extrapolation estimates labor hours required to clear current picking units, with assignment-review and qualification/availability caveats. Existing slotting simulations provide explicit candidate placement recommendations. This is an evidence-based extrapolation rather than a trained ML forecast |

## HTTP contracts and permissions

GET `/api/v1/analytics/decisions/facilities/{facilityId}/labor?startDateUtc=2026-11-01&endDateUtc=2026-11-16` requires `analytics:decisions:labor:view`, authenticated staff or SuperAdmin, and assigned-facility or multi-facility authorization. Workforce logs have no client identity, so client principals are denied even if granted permission. The endpoint is facility-wide; clientId filters are rejected and a supplied query facility must match the route. Staff's existing explicit facility rules remain authoritative.

GET `/api/v1/analytics/decisions/carriers?carrierCodes=FEDEX&carrierCodes=UPS&startDateUtc=2026-11-01&endDateUtc=2026-11-16` requires `analytics:decisions:transportation:view`. Optional clientId, facilityId and serviceLevel narrow authorized scope. Candidates contain 1..20 valid carrier codes (1..50 alphanumeric/dot/underscore/hyphen, initial alphanumeric), duplicate codes collapse case-insensitively. Both date windows are inclusive UTC calendar days, ordered and at most366 days. Empty identifiers and unknown service levels return400.

All endpoints read data only. Scope authorization runs before source materialization, independently of ambient tenant-filter bypass. New permissions are separate from existing transportation, workforce and financial grants.

## Evidence and ranking semantics

Labor backlog includes nondeleted Pending/Assigned/InProgress pick tasks and max(0, required-picked) units. Closed/cancelled/short-picked tasks are not silently treated as pending work. History uses positive-unit, positive-duration Picking executions whose start and completion are within the requested window and whose completion is not in the future. Historical units divided by summed actual labor hours yields units per labor hour. Remaining units divided by that rate yields estimated labor hours; no historical denominator yields null. Sum of execution labor time can include simultaneous workers; the result does not predict elapsed clearance or infer workers currently available. No wages, worker identifiers or names are returned, and no assignments are changed.

Carrier metrics come from the existing scoped scorecard service for identical dates/scope/service. Rates are percentages0..100. Only statistically reliable samples with positive sample size and a defined on-time percentage receive ranks. Missing transit sorts after known transit within equal on-time rates; ordinal carrier code breaks remaining ties. Insufficient evidence is returned explicitly and never promoted as a zero-quality carrier. Results are historical performance advice, not live quotations, cheapest-price claims, route eligibility or guaranteed future SLA. Caller supplies candidates; contractual routing decisions still require the existing shipment/contract workflows.

## Verification

Focused unit and HTTP test results, full regression and PostgreSQL governance checks are recorded by the phase coordinator. HTTP tests should exercise independent permissions, client/staff facility confinement, invalid inputs, missing evidence, ranking stability and source-write/materialization guards. Generic inventory and warehouse simulation coverage is reused rather than duplicated. No schema migration is required by this phase.
`2026-11-17` independent verification: four focused decision unit/HTTP tests passed. Full solution regression passed 1,243/1,243 tests, zero failed or skipped (TRX `%TEMP%\phase67-results\phase67-full-final.trx`). EF reports no pending model changes (`%TEMP%\phase67-model-final.log`); `git diff --check` is clean. Query HTTP tests enforce independent permission, client/facility confinement under bypass and the operational/financial save guard. Existing warehouse/inventory optimization tests passed in full regression. No new live routing capability or production load guarantee is implied.
