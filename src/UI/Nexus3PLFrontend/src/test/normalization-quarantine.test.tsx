import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { AuthProvider } from '../context/AuthContext';
import { FacilityProvider } from '../context/FacilityContext';
import { RouterProvider } from '../context/RouterContext';
import { NormalizationQuarantinePage } from '../pages/admin/NormalizationQuarantinePage';
import { tokenManager } from '../services/apiClient';
import { UserType, UserStatus } from '../types/auth';

describe('MDM-1.6: Country Normalization Quarantine Diagnostics Page', () => {
  beforeEach(() => {
    tokenManager.clearSession();
    vi.restoreAllMocks();
  });

  afterEach(() => {
    tokenManager.clearSession();
    vi.restoreAllMocks();
  });

  it('renders error retry, diagnostics KPI/table, status/entity badges, and empty state for authorized Staff', async () => {
    tokenManager.setTokens('staff-jwt-m16', 'staff-ref-m16');

    const mockProfile = {
      userId: 'staff-1',
      email: 'norm@nexus3pl.local',
      firstName: 'Data',
      lastName: 'Steward',
      userType: UserType.Staff,
      status: UserStatus.Active,
      tenantId: null,
      companyName: null,
      roles: ['SuperAdmin'],
      permissions: ['global:normalization:view', 'facilities:view'],
    };

    let callCount = 0;
    const fetchSpy = vi.fn(async (input: RequestInfo | URL) => {
      const url = input.toString();
      if (url.endsWith('/api/v1/auth/me')) {
        return { ok: true, status: 200, json: async () => mockProfile } as Response;
      }
      if (url.endsWith('/api/v1/facilities')) {
        return {
          ok: true,
          status: 200,
          json: async () => [
            { id: 'fac-us', facilityCode: 'FAC-ORD', name: 'Chicago Hub', countryCode: 'US', timezone: 'America/Chicago', isActive: true },
          ],
        } as Response;
      }
      if (url.includes('/api/v1/reference/normalization-quarantine')) {
        callCount++;
        if (callCount === 1) {
          return {
            ok: false,
            status: 500,
            json: async () => ({ message: 'Temporary diagnostic query failure' }),
          } as Response;
        }
        if (url.includes('entityType=Supplier')) {
          return {
            ok: true,
            status: 200,
            json: async () => ({
              items: [],
              totalCount: 0,
              pageNumber: 1,
              pageSize: 20,
              totalPages: 1,
              pendingCount: 1,
              normalizedCount: 1,
            }),
          } as Response;
        }
        return {
          ok: true,
          status: 200,
          json: async () => ({
            items: [
              {
                auditId: 'aud-1',
                entityType: 'Facility',
                entityId: '11111111-1111-1111-1111-111111111111',
                fieldName: 'CountryCode',
                originalCountryValue: 'ZZZ',
                normalizedCountryCode: null,
                normalizationStatus: 'PendingNormalization',
                preservedHistoricalValue: false,
                wasInferredFromParent: false,
                inferenceSource: null,
                reason: "Unrecognized country value 'ZZZ'",
                facilityId: 'fac-us',
                facilityCode: 'FAC-ORD',
                clientId: null,
                correlationBatchId: 'MDM16-SQL',
                recordedAtUtc: '2026-11-26T10:00:00Z',
              },
              {
                auditId: 'aud-2',
                entityType: 'CarrierSlaComplianceRecord',
                entityId: '22222222-2222-2222-2222-222222222222',
                fieldName: 'DestinationCountry',
                originalCountryValue: 'NPL',
                normalizedCountryCode: 'NP',
                normalizationStatus: 'Normalized',
                preservedHistoricalValue: true,
                wasInferredFromParent: true,
                inferenceSource: 'ClientDomesticAccess+AdministrativeDivision+PostalCode',
                reason: 'Immutable historical record preserved untouched',
                facilityId: 'fac-np',
                facilityCode: 'FAC-KTM',
                clientId: 'cli-1',
                correlationBatchId: 'MDM16-SQL',
                recordedAtUtc: '2026-11-26T10:00:00Z',
              },
            ],
            totalCount: 2,
            pageNumber: 1,
            pageSize: 20,
            totalPages: 1,
            pendingCount: 1,
            normalizedCount: 1,
          }),
        } as Response;
      }
      return { ok: true, status: 200, json: async () => ({}) } as Response;
    });

    vi.stubGlobal('fetch', fetchSpy);

    render(
      <RouterProvider initialPath="/app/admin/normalization-quarantine">
        <AuthProvider>
          <FacilityProvider>
            <NormalizationQuarantinePage />
          </FacilityProvider>
        </AuthProvider>
      </RouterProvider>
    );

    await waitFor(() => {
      expect(screen.getByRole('alert').textContent).toContain('Temporary diagnostic query failure');
    });

    fireEvent.click(screen.getByRole('button', { name: /Retry/i }));

    await waitFor(() => {
      expect(screen.getByText('Country Normalization & Quarantine Diagnostics')).toBeDefined();
      expect(screen.getByText('FAC-ORD')).toBeDefined();
      expect(screen.getByText('Historical Preserved')).toBeDefined();
      expect(screen.getByText('Parent Inferred')).toBeDefined();
    });

    fireEvent.change(screen.getByLabelText('Entity Type'), { target: { value: 'Supplier' } });

    await waitFor(() => {
      expect(screen.getByText('No country normalization diagnostic records match the active scope filters.')).toBeDefined();
    });
  });

  it('blocks Client portal callers with 403 Forbidden guard', async () => {
    tokenManager.setTokens('client-jwt-m16', 'client-ref-m16');

    const mockClientProfile = {
      userId: 'client-user-1',
      email: 'client@tenant.local',
      firstName: 'Client',
      lastName: 'Admin',
      userType: UserType.Client,
      status: UserStatus.Active,
      tenantId: 'cli-99',
      companyName: 'Tenant Corp',
      roles: ['ClientAdmin'],
      permissions: ['global:normalization:view'],
    };

    const fetchSpy = vi.fn(async (input: RequestInfo | URL) => {
      const url = input.toString();
      if (url.endsWith('/api/v1/auth/me')) {
        return { ok: true, status: 200, json: async () => mockClientProfile } as Response;
      }
      if (url.endsWith('/api/v1/facilities')) {
        return { ok: true, status: 200, json: async () => [] } as Response;
      }
      return { ok: true, status: 200, json: async () => ({}) } as Response;
    });

    vi.stubGlobal('fetch', fetchSpy);

    render(
      <RouterProvider initialPath="/app/admin/normalization-quarantine">
        <AuthProvider>
          <FacilityProvider>
            <NormalizationQuarantinePage />
          </FacilityProvider>
        </AuthProvider>
      </RouterProvider>
    );

    await waitFor(() => {
      expect(screen.getByText('403 FORBIDDEN')).toBeDefined();
      expect(screen.getByText('Staff Authorization Required')).toBeDefined();
    });
  });

  it('scopes facilities:manage callers to activeFacility, renders PII redaction and pagination, and blocks unprivileged Staff', async () => {
    tokenManager.setTokens('fac-mgr-jwt', 'fac-mgr-ref');

    const mockFacMgrProfile = {
      userId: 'fac-mgr-1',
      email: 'facmgr@nexus3pl.local',
      firstName: 'Hub',
      lastName: 'Manager',
      userType: UserType.Staff,
      status: UserStatus.Active,
      tenantId: null,
      companyName: null,
      roles: ['OperationsManager'],
      permissions: ['facilities:manage', 'facilities:view'],
    };

    const requestedUrls: string[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = input.toString();
        requestedUrls.push(url);
        if (url.endsWith('/api/v1/auth/me')) {
          return { ok: true, status: 200, json: async () => mockFacMgrProfile } as Response;
        }
        if (url.endsWith('/api/v1/facilities')) {
          return {
            ok: true,
            status: 200,
            json: async () => [
              { id: 'fac-ord-1', facilityCode: 'FAC-ORD', name: 'Chicago Hub', countryCode: 'US', timezone: 'America/Chicago', isActive: true },
            ],
          } as Response;
        }
        if (url.includes('/api/v1/reference/normalization-quarantine')) {
          const isPage2 = url.includes('pageNumber=2');
          return {
            ok: true,
            status: 200,
            json: async () => ({
              items: [
                {
                  auditId: isPage2 ? 'aud-p2' : 'aud-p1',
                  entityType: 'Customer',
                  entityId: isPage2 ? '33333333-3333-3333-3333-333333333333' : '44444444-4444-4444-4444-444444444444',
                  fieldName: 'DefaultCountryCode',
                  originalCountryValue: '[REDACTED_POTENTIAL_PII]',
                  normalizedCountryCode: null,
                  normalizationStatus: 'PendingNormalization',
                  preservedHistoricalValue: false,
                  wasInferredFromParent: false,
                  inferenceSource: null,
                  reason: 'Unrecognized value in Customer.DefaultCountryCode (potential PII redacted).',
                  facilityId: 'fac-ord-1',
                  facilityCode: 'FAC-ORD',
                  clientId: 'cli-1',
                  correlationBatchId: 'R05-BATCH',
                  recordedAtUtc: '2026-11-27T10:00:00Z',
                },
              ],
              totalCount: 25,
              pageNumber: isPage2 ? 2 : 1,
              pageSize: 20,
              totalPages: 2,
              pendingCount: 25,
              normalizedCount: 0,
            }),
          } as Response;
        }
        return { ok: true, status: 200, json: async () => ({}) } as Response;
      })
    );

    const { unmount } = render(
      <RouterProvider initialPath="/app/admin/normalization-quarantine">
        <AuthProvider>
          <FacilityProvider>
            <NormalizationQuarantinePage />
          </FacilityProvider>
        </AuthProvider>
      </RouterProvider>
    );

    await waitFor(() => {
      expect(screen.getByText('[REDACTED_POTENTIAL_PII]')).toBeDefined();
      expect(screen.getAllByText('FAC-ORD').length).toBeGreaterThanOrEqual(1);
      expect(screen.getByText(/Showing page 1 of 2/)).toBeDefined();
    });

    expect(requestedUrls.some((u) => u.includes('/api/v1/reference/normalization-quarantine') && u.includes('facilityId=fac-ord-1'))).toBe(true);

    fireEvent.click(screen.getByRole('button', { name: 'Next' }));
    await waitFor(() => {
      expect(screen.getByText(/Showing page 2 of 2/)).toBeDefined();
    });
    unmount();

    // Unprivileged staff without global:normalization:view or facilities:manage is blocked client-side before querying API
    tokenManager.setTokens('unpriv-staff-jwt', 'unpriv-ref');
    const unprivCalls: string[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = input.toString();
        unprivCalls.push(url);
        if (url.endsWith('/api/v1/auth/me')) {
          return {
            ok: true,
            status: 200,
            json: async () => ({
              ...mockFacMgrProfile,
              roles: ['WarehouseOperator'],
              permissions: ['inventory:view'],
            }),
          } as Response;
        }
        return { ok: true, status: 200, json: async () => [] } as Response;
      })
    );

    render(
      <RouterProvider initialPath="/app/admin/normalization-quarantine">
        <AuthProvider>
          <FacilityProvider>
            <NormalizationQuarantinePage />
          </FacilityProvider>
        </AuthProvider>
      </RouterProvider>
    );

    await waitFor(() => {
      expect(screen.queryByText('Country Normalization & Quarantine Diagnostics')).toBeNull();
    });
    expect(unprivCalls.some((u) => u.includes('/api/v1/reference/normalization-quarantine'))).toBe(false);
  });
});
