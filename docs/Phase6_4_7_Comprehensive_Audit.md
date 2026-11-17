# Phase 6.4.7 — Comprehensive transportation audit

Audit date: 2026-11-17. This milestone reviews the Phase 6.4 transportation analytics and Phase 6.4.6 dashboard together, fixes confirmed evidence/session defects, and records reproducible verification. Work remains local; no commit, push, deployment or production database access was performed.

## Scope and findings

Reviewed the eleven transportation query routes, MVC validation and permission boundaries, authenticated tenant/facility confinement, claims evidence rules, freight analytics/read-only invariants, and the runnable React dashboard. Existing API integration coverage exercises real MVC/model binding, permission policies and tenant middleware using test authentication and EF fixtures; this is not a live production-JWT/PostgreSQL end-to-end test.

Confirmed issues fixed:

| Finding | Correction | Regression evidence |
| --- | --- | --- |
| Freight charges/insurance premiums incorrectly counted as goods valuation, allowing unsupported claims readiness | Goods valuation stays missing because the model has no authoritative declared/replacement goods valuation source | Dedicated freight/insurance evidence cases; existing complete-operational-evidence test now expects missing valuation |
| Historical/invalid lost alerts could establish a current threshold breach | Only an active alert with a positive threshold and sufficient inactivity establishes the current breach; historical alerts remain historical obstruction evidence | Active/resolved/dismissed/claim-initiated and invalid-threshold cases |
| Superseded or unfinalized POD could count as current delivery evidence | Require current, storage-finalized proof | POD lifecycle cases |
| Any disruption could support a damaged-goods claim | Require a non-voided documented damage exception for damage obstruction evidence | Weather/open/resolved/voided damage cases |
| Freight evidence depended on whichever cost record materialized first | Search the scoped cost records for positive cost evidence deterministically | Reversed record-order cases |
| Financial children linked to an authorized shipment could carry another tenant/facility and contribute when ambient filters were bypassed | Independently constrain packages, carrier costs/adjustments/accessorials, margins, activities and invoice relationships before materialization | Poisoned cross-tenant/facility HTTP fixture with materialization guard and financial-total assertions; PostgreSQL query translation smoke |
| Logout/expiry left the previous account's client/facility/shipment filter drafts in memory | Reset the session, report selection and filter drafts on sign-out and HTTP 401 | Account-switch cases for explicit sign-out and expiry |
| Results did not identify report-specific applied filters, making unsubmitted edits confusing | Show submitted service/destination/valuation/status/claim filters from the actual request URL | Applied-versus-draft integration case |
| Invalid valuation input could throw while constructing a report URL | Validate valuation input before applying the selection | Strict type/build verification and form guard review |

Authoritative goods valuation is deliberately unavailable rather than inferred. Claims assessments consequently remain `InsufficientEvidence` when this mandatory evidence is required. Persisting and validating an authoritative valuation source is future product work, not silently assumed by this audit.

Audit severity: the financial child-scope gap and unsupported mandatory goods valuation were high-priority correctness/security findings. Alert/POD/damage/cost evidence defects were medium-priority evidence findings. The child-scope regression reproduced the pre-fix failure with deliberately inconsistent records under ambient filter bypass; it does not assert that a production breach occurred.

No financial write paths, schema or migrations changed. Backend evidence queries retain explicit authorized shipment client/facility scope. Independent cross-review found no remaining blocking issue in the patches. Frontend response evidence uses generic records, so backend DTO drift is not fully caught by TypeScript; route/query contract tests provide current coverage.

## Verification

Final post-fix solution regression passed **1,227 tests**, zero failed or skipped. Baseline regression passed 1,207 tests; a claims-only intermediate run passed 1,225. The final run includes all claims and financial-scope changes.

The focused final backend run passed **188 tests**, including claims evidence regressions, guarded HTTP financial-scope cases, financial analytics regressions and Npgsql query translation. The translation smoke generates SQL without connecting to PostgreSQL.

Independent frontend verification passed **24 tests across four files**. Strict TypeScript checking and Vite production build passed without warnings. The locked dashboard dependency tree reports **zero vulnerabilities** from `npm audit --json`.

Commands:

```powershell
dotnet test Warehouse3PL.slnx --no-restore --logger "trx;LogFileName=phase647-final.trx" --results-directory "$env:TEMP\phase647-results"
dotnet ef migrations has-pending-model-changes --no-build --project src/Infrastructure/Warehouse3PL.Infrastructure
dotnet list Warehouse3PL.slnx package --vulnerable --include-transitive
git diff --check

cd src/UI/TransportationDashboard
npm test
npm run build
npm audit --json
```

Final EF verification reports **no pending model changes**, and `git diff --check` passes. Final test/build and EF commands ran sequentially to avoid design-time build output locks. An initial concurrent baseline run encountered transient copy retries while EF held assemblies, recovered, and passed; no test failed. Git emits informational LF-to-CRLF conversion notices for several edited files, with no whitespace errors.

Local evidence: `%TEMP%\phase647-results\phase647-final.trx`, `%TEMP%\phase647-final.log`, `%TEMP%\phase647-results\phase647-financial-scope-final.trx`, `%TEMP%\phase647-results\phase647-baseline.trx`, `%TEMP%\phase647-baseline.log`, `%TEMP%\phase647-model.log`, `%TEMP%\phase647-frontend-test.log`, `%TEMP%\phase647-frontend-build.log`, `%TEMP%\phase647-npm-audit.json`, and `%TEMP%\phase647-dotnet-advisories.log`.

## Limits and remaining risks

- Browser visual/interactive QA could not run because the computer-use native pipe was unavailable. DOM-based interaction tests do not establish visual layout quality.
- No live database-backed API round trip, target deployment schema/history reconciliation, production load test or migration replay was performed in this milestone. The Phase 6.4.4.2 deployment prerequisites remain applicable; no production readiness certification is implied.
- Existing full-window freight discrepancy calculations bound response pagination, not total calculation workload. A 366-day authorized query can still be expensive; no load benchmark was run.
- `dotnet list package --vulnerable --include-transitive` found the inherited moderate `OpenTelemetry.Api` 1.11.2 advisory [GHSA-g94r-2vxg-569j](https://github.com/advisories/GHSA-g94r-2vxg-569j) in the WebApi and unit-test dependency graphs. No dependency version changed in this audit. The standalone migration checker is outside the solution dependency scan; its previously recorded Newtonsoft.Json advisory was not reassessed here.
- The unrelated existing nullable warning in `DeliveryExceptionResolutionAndReattemptTests.cs` remains outside these changes.

The user's pre-existing untracked `test_script.sql` remains untouched. Phase 6.4.6 dashboard sources and its handoff document remain local work and are preserved.
