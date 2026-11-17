# Phase 6.4.6 — Transportation frontend dashboard

The Phase 6.4.5 handoff names this milestone before Phase 6.4.7 (Comprehensive Audit).
Implemented as a React/TypeScript feature under `src/UI/TransportationDashboard`, following the repository's existing page/components/hooks/services/types organization. An isolated Vite host supplies the previously missing runnable frontend entry point. No backend models, migrations, finance records or API contracts change.

## Run locally

Start the existing WebApi on `http://localhost:5000` with PostgreSQL available. Then:

```powershell
cd src/UI/TransportationDashboard
npm ci
npm run dev
```

Open `http://127.0.0.1:3000`. The development server proxies `/api` to the local WebApi; change `vite.config.ts` if the API uses a different port. Sign in with an existing warehouse account. Access tokens and credentials are never written to local/session storage; reloading requires sign-in. Tokens expire through the normal API policy and a 401 returns the user to sign-in. There is no automatic refresh or durable session.

For production, `npm run build` produces `dist/`; serve that directory behind the same origin as `/api`, or configure an equivalent authenticated API reverse proxy. This milestone is local repository work, with no deployment or source push.

## Delivered experience

- Permission-gated navigation to all eleven Phase 6.4.5 endpoints, including independent performance, financial and claims grants. The authenticated `/auth/me` profile supplies grants; backend authorization remains authoritative.
- Carrier scorecard KPI summaries, transit histogram/sample reliability, delivery exception details, freight variance/profitability/accessorial/rate-card evidence, discrepancy pages, claims readiness pages and shipment reconciliation evidence.
- Report-specific carrier, UTC date, tenant, facility, service, destination, historical valuation, status, discrepancy and claim filters. Optional valuation time is entered in the browser's local timezone and converted to UTC. Facility claims uses active alerts across all dates/carriers, matching the existing API.
- Paged discrepancies and facility claims with 25 rows per page. Every returned financial/evidence field remains available, including invoice-line identity, quarantine flags, package allocations and conversion evidence.
- Explicit loading, permission, missing shipment, validation and empty-page states. Null and omitted KPI denominators display “Not available”. Numeric evidence preserves up to 15 significant digits, including FX rates. Source currency evidence is distinguished from USD summary fields. Realized margin is identified as accrual based.
- Abort-on-change requests, token/scope keyed response state, and immediate removal of report data on sign-out. Draft filters are applied explicitly; the result identifies its applied scope.
- Responsive layout, labeled controls, focus indicators, semantic navigation and live result announcements. Server values render as React text, never injected HTML.

## Verification

```powershell
cd src/UI/TransportationDashboard
npm test
npm run build
```

Automated tests cover every API route/query contract, independent permissions, bearer handling without persistence, 401/403/404 responses, stale-request cancellation, logout clearing, claims pagination, missing denominators and safe evidence rendering. The build performs strict TypeScript checking and bundles the runnable application.

Live API/database and visual browser verification depend on the local services being available and are reported separately. Comprehensive system audit remains Phase 6.4.7. The pre-existing untracked `test_script.sql` is preserved.
