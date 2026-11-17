import { describe, expect, it, vi } from 'vitest';
import { reportUrl, request } from './transportationApi';
import { permitted, permissions, type Filters } from '../types/transportation';
const filters: Filters = { carrierCode: 'A/B', startDateUtc: '2026-11-01', endDateUtc: '2026-11-17', clientId: 'client', facilityId: 'facility', serviceLevel: 'FreightLTL', destinationCountry: 'US', destinationState: 'NY', asOfUtc: '2026-11-01T12:00', statusFilter: 'Disputed', minimumDiscrepancyUsd: '5', shipmentId: 'shipment', claimType: 'Damaged' };
describe('transportation contracts', () => {
  it.each(['scorecard', 'transit-time-distribution', 'exceptions'] as const)('maps performance %s', report => {
    const url = new URL(reportUrl(report, filters), 'http://localhost');
    expect(url.pathname).toBe(`/api/v1/analytics/transportation/carriers/A%2FB/${report}`);
    expect(url.searchParams.get('clientId')).toBe('client');
    expect(url.searchParams.get('startDateUtc')).toBe('2026-11-01');
    expect(url.searchParams.has('asOfUtc')).toBe(false);
    expect(url.searchParams.has('serviceLevel')).toBe(report !== 'exceptions');
    expect(url.searchParams.has('destinationCountry')).toBe(report === 'transit-time-distribution');
  });
  it.each(['variance', 'profitability', 'accessorials', 'discrepancies', 'rate-card-compliance'] as const)('maps freight %s', report => {
    const url = new URL(reportUrl(report, filters, 2), 'http://localhost');
    expect(url.pathname).toBe(`/api/v1/analytics/transportation/carriers/A%2FB/freight/${report}`);
    expect(url.searchParams.get('asOfUtc')).toBe(new Date(filters.asOfUtc).toISOString());
    expect(url.searchParams.has('serviceLevel')).toBe(report === 'profitability');
    expect(url.searchParams.has('pageNumber')).toBe(report === 'discrepancies');
    if (report === 'discrepancies') { expect(url.searchParams.get('pageNumber')).toBe('2'); expect(url.searchParams.get('statusFilter')).toBe('Disputed'); }
  });
  it('facility claims excludes unrelated filters and pages', () => {
    const url = new URL(reportUrl('facility-claims', filters, 3), 'http://localhost');
    expect(url.pathname).toBe('/api/v1/analytics/transportation/facilities/facility/claims-readiness');
    expect([...url.searchParams.keys()]).toEqual(['clientId', 'pageNumber', 'pageSize']);
    expect(url.searchParams.get('pageNumber')).toBe('3');
  });
  it('shipment endpoints only send their applicable filters', () => {
    expect(reportUrl('shipment-claims', filters)).toBe('/api/v1/analytics/transportation/shipments/shipment/claims-readiness?claimType=Damaged');
    const url = new URL(reportUrl('financial-reconciliation', filters), 'http://localhost');
    expect(url.pathname).toBe('/api/v1/analytics/transportation/shipments/shipment/financial-reconciliation');
    expect([...url.searchParams.keys()]).toEqual(['asOfUtc']);
  });
  it('keeps claims and financial permissions independent', () => {
    const user = { email: '', firstName: '', roles: [], permissions: [permissions.performance] };
    expect(permitted(user, permissions.performance)).toBe(true);
    expect(permitted(user, permissions.financial)).toBe(false);
    expect(permitted(user, permissions.claims)).toBe(false);
    expect(permitted({ ...user, roles: ['SuperAdmin'] }, permissions.claims)).toBe(true);
  });
  it.each([401, 403, 404])('preserves HTTP %s as actionable error', async status => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status, json: async () => ({}) }));
    await expect(request('/api', 'secret')).rejects.toMatchObject({ status });
  });
  it('sends bearer only in header and never persists it', async () => {
    const fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => ({}) }); vi.stubGlobal('fetch', fetch);
    const storage = vi.spyOn(Storage.prototype, 'setItem');
    await request('/api', 'secret');
    expect(fetch.mock.calls[0][1].headers).toEqual({ Authorization: 'Bearer secret' });
    expect(storage).not.toHaveBeenCalled();
  });
});
