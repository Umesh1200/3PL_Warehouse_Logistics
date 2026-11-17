# Phase 6.5 — Profitability and cost-to-serve

The read-only API adds `GET /api/v1/analytics/profitability/contributions` and `GET /api/v1/analytics/profitability/shipments/{shipmentId}`. Both require the independent `analytics:profitability:view` grant and authenticated tenant/facility scope. Contributions accepts required inclusive UTC `startDateUtc`/`endDateUtc` (maximum 366 days), optional `clientId`/`facilityId`; shipment reconciliation accepts optional offset-bearing `asOfUtc`.

Contributions returns per-client, per-facility and per-currency rows, making client/warehouse comparisons possible without mixing currencies. It includes an equal-length immediately preceding period for comparisons, revenue streams, observed carrier cost, estimated direct labor, observed cost-to-revenue ratio and contribution margin. Ratios are unavailable when revenue is not positive. Only daily concrete-facility projections contribute; client-wide null-facility rows are excluded to prevent double counting. Observation-day counts distinguish sparse projections from a fully populated interval.

These are projected commercial contributions, not fully absorbed net profit. Overhead, storage operating expenses and unattributed costs have no authoritative allocation basis in the available model. They remain explicitly unavailable rather than invented or presumed zero. Ratios describe observed costs only. Currency partitions remain separate and are never summed into a platform amount. Prior/current rows are matched by client/facility/currency for comparisons; a missing group is no observation, not zero.

Shipment results reuse authoritative freight reconciliation, point-in-time FX evidence, carrier/sell-side baselines and package weight allocation with cent conservation. This supplies shipment transportation cost-to-serve and allocation; it does not attribute unsupported warehouse overhead to a shipment. Existing detailed financial evidence and completeness flags are retained.

Authorization resolves identity scope before querying. Facility/client predicates are explicit even with ambient tenant bypass. Queries use no tracking and never save operational or financial records. No schema change or migration is required.

Verification results are recorded after independent tests/review. This phase does not introduce production deployment, universal accounting closure, or a full frontend integration gate.
`Comparisons` includes matched-group revenue, observed cost and contribution deltas, plus revenue change percentage. Missing baseline remains null. Initial focused verification passed five tests (service and actual HTTP pipeline); independent final evidence follows.

Independent verification: 7 profitability tests passed, zero failed/skipped, including existing regressions and the new HTTP permission/isolation/read-only case. Build passed. git diff --check passed. TRX: %TEMP%\phase65-results\phase65-independent-final.trx. Known inherited OpenTelemetry and nullable warnings remain; the new test also emits a nonblocking Assert.Single style analyzer warning.
