import type { Filters } from '../types/transportation';
export class ApiError extends Error { constructor(public status: number, message: string) { super(message); } }
export async function request<T>(path: string, token: string, signal?: AbortSignal, body?: unknown): Promise<T> {
  const response = await fetch(path, { signal, headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(body ? { 'Content-Type': 'application/json' } : {}) }, ...(body ? { method: 'POST', body: JSON.stringify(body) } : {}) });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    const validation = Object.values(problem.errors ?? {}).flat().join(' ');
    throw new ApiError(response.status, response.status === 401 ? 'Your session has expired. Sign in again.' : response.status === 403 ? 'You do not have permission for this scope or report.' : response.status === 404 ? 'No shipment was found in your authorized scope.' : validation || problem.detail || problem.title || `Request failed (${response.status}).`);
  }
  return response.json();
}
export type Report = 'scorecard' | 'transit-time-distribution' | 'exceptions' | 'variance' | 'profitability' | 'accessorials' | 'discrepancies' | 'rate-card-compliance' | 'facility-claims' | 'shipment-claims' | 'financial-reconciliation';
export function reportUrl(report: Report, filters: Filters, page = 1): string {
  const query = new URLSearchParams();
  const base = '/api/v1/analytics/transportation';
  if (report === 'shipment-claims') return `${base}/shipments/${encodeURIComponent(filters.shipmentId)}/claims-readiness?claimType=${filters.claimType}`;
  if (report === 'financial-reconciliation') {
    if (filters.asOfUtc) query.set('asOfUtc', new Date(filters.asOfUtc).toISOString());
    return `${base}/shipments/${encodeURIComponent(filters.shipmentId)}/financial-reconciliation?${query}`;
  }
  if (report === 'facility-claims') {
    if (filters.clientId) query.set('clientId', filters.clientId);
    query.set('pageNumber', String(page)); query.set('pageSize', '25');
    return `${base}/facilities/${encodeURIComponent(filters.facilityId)}/claims-readiness?${query}`;
  }
  for (const key of ['startDateUtc', 'endDateUtc', 'clientId', 'facilityId'] as const) if (filters[key]) query.set(key, filters[key]);
  if (['scorecard', 'transit-time-distribution', 'profitability'].includes(report) && filters.serviceLevel) query.set('serviceLevel', filters.serviceLevel);
  if (report === 'transit-time-distribution') for (const key of ['destinationCountry', 'destinationState'] as const) if (filters[key]) query.set(key, filters[key]);
  const freight = !['scorecard', 'transit-time-distribution', 'exceptions'].includes(report);
  if (freight && filters.asOfUtc) query.set('asOfUtc', new Date(filters.asOfUtc).toISOString());
  if (report === 'discrepancies') {
    query.set('pageNumber', String(page)); query.set('pageSize', '25');
    if (filters.statusFilter) query.set('statusFilter', filters.statusFilter);
    if (filters.minimumDiscrepancyUsd) query.set('minimumDiscrepancyUsd', filters.minimumDiscrepancyUsd);
  }
  return `${base}/carriers/${encodeURIComponent(filters.carrierCode)}/${freight ? 'freight/' : ''}${report}?${query}`;
}
