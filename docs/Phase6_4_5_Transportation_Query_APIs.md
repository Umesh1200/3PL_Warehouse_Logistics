# Phase 6.4.5 — Transportation query APIs and multi-tenant security

Phase 6.4.4.2 was approved for continuation by the user on 2026-11-16. This milestone adds REST queries over the existing transportation and freight analytics. It does not implement Phase 6.4.6 dashboards or mutate operational/Phase 5 financial records.

## HTTP contract

All routes are GET under `/api/v1/analytics/transportation`, use the existing JWT authentication scheme in the application, and are described by the generated OpenAPI document. Permission checks precede execution. Permissions are independently registered in the permission catalog and must be explicitly granted; existing client roles do not automatically gain access to commercial margins or claims.

| Relative route | Permission | Response |
| --- | --- | --- |
| `carriers/{carrierCode}/scorecard` | `analytics:transportation:view` | CarrierScorecardDto |
| `carriers/{carrierCode}/transit-time-distribution` | `analytics:transportation:view` | TransitTimeDistributionDto |
| `carriers/{carrierCode}/exceptions` | `analytics:transportation:view` | DeliveryExceptionIntelligenceDto |
| `shipments/{shipmentId}/claims-readiness` | `analytics:transportation:claims:view` | ClaimsReadinessAssessmentDto |
| `facilities/{facilityId}/claims-readiness` | `analytics:transportation:claims:view` | PagedResult of ClaimsReadinessAssessmentDto |
| `shipments/{shipmentId}/financial-reconciliation` | `analytics:transportation:financial:view` | ReconciledShipmentFinancialsDto |
| `carriers/{carrierCode}/freight/variance` | `analytics:transportation:financial:view` | FreightCostVarianceSummaryDto |
| `carriers/{carrierCode}/freight/profitability` | `analytics:transportation:financial:view` | FreightProfitabilitySummaryDto |
| `carriers/{carrierCode}/freight/accessorials` | `analytics:transportation:financial:view` | AccessorialLeakageAnalysisDto |
| `carriers/{carrierCode}/freight/discrepancies` | `analytics:transportation:financial:view` | PagedResult of CarrierInvoiceDiscrepancyDto |
| `carriers/{carrierCode}/freight/rate-card-compliance` | `analytics:transportation:financial:view` | RateCardComplianceAnalysisDto |

Carrier queries require `startDateUtc` and `endDateUtc`, interpreted as inclusive UTC calendar dates. Date-only inputs are UTC; timestamp inputs with offsets are normalized to UTC. Maximum range is 366 inclusive days; reversed dates and dates in year 9999 are rejected. Carrier codes contain 1–50 letters, digits, dots, underscores or hyphens and begin with a letter/digit. Missing/malformed values, empty client/facility UUID filters, invalid enum values and invalid pagination yield HTTP 400 validation problems.

Optional `clientId` and `facilityId` filters are supported by all carrier routes, including rate-card compliance. They narrow the authenticated scope. `serviceLevel` is supported by scorecard, transit distribution and profitability. Transit distribution additionally supports `destinationCountry` and `destinationState`. Freight queries and shipment financial reconciliation accept an optional `asOfUtc` timestamp with an offset; it is forwarded as UTC to the existing historical valuation logic.

Shipment claims readiness accepts `claimType` (Lost, Damaged, Delayed, Other; default Lost). Facility claims pages accept `clientId`, `pageNumber` (default 1) and `pageSize` (default 50, maximum 200). A query facility, if supplied, must equal the route facility. Only active alerts with an existing, consistently scoped shipment qualify. Candidate shipment IDs are deduplicated, ordered and paged in the database before assessments run.

Freight discrepancy pages also accept `statusFilter`, non-negative `minimumDiscrepancyUsd`, `pageNumber` and `pageSize`. They preserve the existing calculation across the complete authorized date window, then order by shipment, carrier cost record and invoice-line identity for response pagination. This bounds response size, not the entire calculation workload. Maximum page number is 1,000,000 for both lists. Empty/out-of-range pages return an empty `items` array with the authorized `totalCount`.

Examples:

```http
GET /api/v1/analytics/transportation/carriers/FEDEX/scorecard?startDateUtc=2026-11-01&endDateUtc=2026-11-16
GET /api/v1/analytics/transportation/carriers/FEDEX/freight/discrepancies?startDateUtc=2026-11-01&endDateUtc=2026-11-16&pageSize=50&minimumDiscrepancyUsd=5
```

No matching shipment and a shipment outside the caller's scope both return HTTP 404. Explicit requests for another client or an unauthorized facility return HTTP 403. Anonymous requests return HTTP 401; missing endpoint permissions return HTTP 403. Empty authorized aggregate queries return HTTP 200 with the established empty/null-denominator analytics semantics.

## Scope enforcement

`TransportationAnalyticsAuthorization` derives client scope from authenticated identity claims, never from a caller-controlled tenant provider/header. Clients require active ClientFacilityAccess assignments, are confined to their tenant, and honor an assigned user facility when present. Missing identity, inactive assignments and conflicting filters fail closed. Staff without multi-facility permission are confined to their assigned facility; unassigned staff fail closed. `analytics:multi-facility:view` permits explicitly authorized staff aggregation. Existing internal legacy cross-facility permissions remain compatible. SuperAdmin retains its established platform scope.

The scorecard service applies the scope before materializing compliance, daily-performance and exception records. Client-null aggregate daily records cannot contribute to a client's scorecard. Claims readiness first locates a shipment within the authorized scope, then confines all evidence queries to that shipment's client and facility. Freight controller boundaries validate the scope before invoking the existing financial services; those services retain their own checks. The canonical multi-facility permission is recognized by those financial services as well.

`UseAuthenticatedTenantResolution` is shared by production startup and HTTP tests: authentication runs before TenantResolutionMiddleware, and authorization follows it. The middleware can therefore reject a forged X-Tenant-Id using verified principal claims.

## Verification and handoff

Final verification on 2026-11-16: **1,207 solution tests passed, zero failed, zero skipped**. The focused transportation/API run passed 77 tests. EF reports no pending model changes and `git diff --check` is clean. Final TRX evidence is `%TEMP%\phase645-results\phase645-final.trx`; detailed logs are `%TEMP%\phase645-final.log` and `%TEMP%\phase645-model.log`.

HTTP tests run the real MVC routes, model binding/validation, shared authentication/tenant middleware ordering, permission policy provider/handler and analytics services on ASP.NET TestServer. A test-only authentication handler supplies controlled principals; production authentication is unchanged. EF tenant-filter bypass is enabled deliberately. A materialization interceptor rejects foreign tenant/inactive-facility records in client requests, and a save interceptor refuses both synchronous and asynchronous saves after fixture seeding. Full-window GETs also compare operational/financial fingerprints before and after execution.

Coverage includes every endpoint's 200/401/403 behavior, independent commercial/claims permissions, client and facility confinement, assigned and cross-facility staff, SuperAdmin, missing tenant identity, tenant-header spoofing, indistinguishable missing/foreign shipment responses, malformed dates/enums/IDs, bounded claims pagination, explicit staff client filters in rate-card compliance and existing financial/analytics regressions.

Validation commands:

```powershell
dotnet test Warehouse3PL.slnx --no-restore
dotnet ef migrations has-pending-model-changes --no-build --project src/Infrastructure/Warehouse3PL.Infrastructure
git diff --check
```

This milestone changes no EF model/schema and requires no migration. The Phase 6.4.4.2 target-database history reconciliation prerequisite remains applicable to deployment. Existing dependency advisories and the unrelated nullable warning remain outside this change. The original untracked `test_script.sql` is preserved and excluded from commits.

Next milestone: **Phase 6.4.6 — Frontend Dashboard**, followed by **Phase 6.4.7 — Comprehensive Audit**, as confirmed by the roadmap reconciliation addendum. This work is committed locally for review; no push or merge is authorized.
