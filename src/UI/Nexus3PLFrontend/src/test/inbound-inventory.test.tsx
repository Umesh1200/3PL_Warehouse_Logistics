import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { AuthProvider } from '../context/AuthContext';
import { FacilityProvider } from '../context/FacilityContext';
import { RouterProvider } from '../context/RouterContext';
import { ReceivingPage } from '../pages/inbound/ReceivingPage';
import { PutawayPage } from '../pages/inbound/PutawayPage';
import { StockOnHandPage } from '../pages/inventory/StockOnHandPage';
import { tokenManager } from '../services/apiClient';
import {
  ReceivingStatus,
  ReceiptConditionStatus,
  PutawayTaskStatus,
  PutawayPriority,
  DiscrepancyType,
  DiscrepancyResolutionStatus,
} from '../types/inbound';

describe('Phase 7.3: Receiving, Directed Putaway & Inventory Ledger Integration Tests', () => {
  const mockFacility = {
    id: 'fac-ord1-guid',
    facilityCode: 'FAC-ORD1',
    name: 'Chicago Central Hub',
    addressLine1: '1000 Logistics Pkwy',
    city: 'Chicago',
    state: 'IL',
    postalCode: '60601',
    country: 'USA',
    timezone: 'America/Chicago',
    isActive: true,
  };

  const mockUserProfile = {
    userId: 'usr-wh-lead-1',
    email: 'lead@3pl.local',
    firstName: 'Warehouse',
    lastName: 'Lead',
    userType: 1, // Staff
    tenantId: null,
    companyName: '3PL Enterprise',
    roles: ['WarehouseOperator', 'OperationsStaff'],
    permissions: [
      'inbound:view',
      'inbound:receive',
      'inbound:receive:approve',
      'inbound:discrepancy:resolve',
      'inbound:putaway',
      'inbound:putaway:execute',
      'inbound:putaway:manage',
      'inventory:view',
      'facilities:view',
    ],
  };

  beforeEach(() => {
    tokenManager.clearSession();
    tokenManager.setTokens('mock-access-token', 'mock-refresh-token');
    tokenManager.setActiveFacilityId(mockFacility.id);
    vi.restoreAllMocks();
  });

  afterEach(() => {
    tokenManager.clearSession();
    vi.restoreAllMocks();
  });

  const setupProviders = (ui: React.ReactElement, initialPath: string = '/app/inbound') => {
    return render(
      <RouterProvider initialPath={initialPath}>
        <AuthProvider>
          <FacilityProvider>{ui}</FacilityProvider>
        </AuthProvider>
      </RouterProvider>
    );
  };

  it('completes end-to-end goods receiving lifecycle with line scanning, duplicate submit prevention, and status transition', async () => {
    const mockSession = {
      id: 'session-rcv-101',
      facilityId: mockFacility.id,
      facilityCode: 'FAC-ORD1',
      dockDoorId: 'door-1',
      doorNumber: 'DOOR-01',
      yardVisitId: 'yv-1',
      visitNumber: 'YV-01',
      trailerNumber: 'TR-101',
      clientId: 'client-1',
      clientName: 'Apex Retail',
      asnId: 'asn-1',
      asnNumber: 'ASN-101',
      sessionNumber: 'RCV-FAC-ORD1-20261119-0001',
      status: ReceivingStatus.InProgress,
      startedAtUtc: '2026-11-19T10:00:00Z',
      completedAtUtc: null,
      receivedByUserId: 'usr-wh-lead-1',
      isBlindReceiving: false,
      requiresSupervisorApproval: false,
      isSupervisorApproved: false,
      version: 1,
      lines: [
        {
          id: 'line-1',
          receivingSessionId: 'session-rcv-101',
          itemId: 'item-100',
          sku: 'SKU-WIDGET-A',
          itemDescription: 'Widget Alpha',
          lpn: 'LPN-PALLET-001',
          quantityReceived: 10,
          quantityExpected: 10,
          lotNumber: 'LOT-2026-A',
          condition: ReceiptConditionStatus.Sound,
          stagingLocationId: 'loc-stg-1',
          stagingLocationCode: 'STAGING-01',
          scannedAtUtc: '2026-11-19T10:05:00Z',
          version: 1,
        },
      ],
      discrepancies: [],
    };

    let completeInvoked = 0;
    const fetchSpy = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();

      if (url.endsWith('/api/v1/auth/me')) {
        return { ok: true, status: 200, json: async () => mockUserProfile } as Response;
      }

      if (url.includes('/api/v1/facilities')) {
        return { ok: true, status: 200, json: async () => [mockFacility] } as Response;
      }

      if (url.includes('/api/v1/receiving/sessions/session-rcv-101/complete')) {
        completeInvoked++;
        // Simulate network delay to verify submission guard
        await new Promise((r) => setTimeout(r, 60));
        return {
          ok: true,
          status: 200,
          json: async () => ({
            ...mockSession,
            status: ReceivingStatus.Completed,
            completedAtUtc: '2026-11-19T10:15:00Z',
          }),
        } as Response;
      }

      if (url.includes('/api/v1/receiving/sessions/session-rcv-101/lines')) {
        const body = JSON.parse(init?.body as string);
        return {
          ok: true,
          status: 201,
          json: async () => ({
            id: 'line-2',
            receivingSessionId: 'session-rcv-101',
            itemId: body.itemId,
            sku: 'SKU-WIDGET-B',
            itemDescription: 'Widget Beta',
            lpn: body.lpn || 'LPN-AUTO-002',
            quantityReceived: body.quantityReceived,
            quantityExpected: 0,
            lotNumber: body.lotNumber,
            condition: body.condition,
            stagingLocationCode: 'STAGING-01',
            scannedAtUtc: '2026-11-19T10:10:00Z',
            version: 1,
          }),
        } as Response;
      }

      if (url.includes('/api/v1/receiving/sessions/session-rcv-101')) {
        return { ok: true, status: 200, json: async () => mockSession } as Response;
      }

      if (url.includes('/api/v1/receiving/sessions')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            items: [mockSession],
            totalCount: 1,
            pageNumber: 1,
            pageSize: 10,
          }),
        } as Response;
      }

      return { ok: false, status: 404 } as Response;
    });
    vi.stubGlobal('fetch', fetchSpy);

    setupProviders(<ReceivingPage />);

    // 1. Verify Sessions table loads
    await waitFor(() => {
      expect(screen.getByText('RCV-FAC-ORD1-20261119-0001')).toBeDefined();
    });

    // 2. Open Session Workspace
    fireEvent.click(screen.getByTestId('open-session-session-rcv-101'));

    await waitFor(() => {
      expect(screen.getByTestId('session-detail-view')).toBeDefined();
      expect(screen.getByTestId('line-scanner-card')).toBeDefined();
      expect(screen.getByText('SKU-WIDGET-A')).toBeDefined();
    });

    // 3. Scan a second line
    fireEvent.change(screen.getByTestId('scan-item-id-input'), { target: { value: 'item-200' } });
    fireEvent.change(screen.getByTestId('scan-qty-input'), { target: { value: '5' } });
    fireEvent.change(screen.getByTestId('scan-lpn-input'), { target: { value: 'LPN-SCAN-2' } });

    const scanBtn = screen.getByTestId('submit-line-btn');
    fireEvent.click(scanBtn);

    // 4. Verify Complete Receiving Session flow with duplicate submission prevention
    await waitFor(() => {
      expect(screen.getByTestId('complete-session-action-btn')).toBeDefined();
    });

    fireEvent.click(screen.getByTestId('complete-session-action-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('complete-confirm-modal')).toBeDefined();
    });

    const confirmCompleteBtn = screen.getByTestId('confirm-complete-btn');

    // Fire two consecutive clicks to verify idempotency / submission guard
    fireEvent.click(confirmCompleteBtn);
    fireEvent.click(confirmCompleteBtn);

    // Button should enter loading/disabled state
    expect(confirmCompleteBtn).toHaveProperty('disabled', true);

    await waitFor(() => {
      expect(screen.getByTestId('feedback-banner')).toBeDefined();
      expect(screen.getByText(/finalized. Stock-on-hand ledger updated/i)).toBeDefined();
    });

    // Exactly one complete call should be dispatched
    expect(completeInvoked).toBe(1);
  });

  it('allows a dedicated-permission supervisor without inbound receive to approve and resolve', async () => {
    const supervisor = {
      ...mockUserProfile,
      permissions: [
        'inbound:view',
        'inbound:receive:approve',
        'inbound:discrepancy:resolve',
        'facilities:view',
      ],
    };
    const discrepancy = {
      id: 'disc-supervisor-1',
      receivingSessionId: 'session-supervisor-1',
      receiptLineId: null,
      type: DiscrepancyType.UnexpectedSKU,
      discrepantQuantity: 1,
      notes: 'Supervisor review required',
      status: DiscrepancyResolutionStatus.PendingReview,
      reportedAtUtc: '2026-11-19T10:05:00Z',
      resolvedAtUtc: null,
      resolvedByUserId: null,
      resolutionNotes: null,
      version: 1,
    };
    const session = {
      id: 'session-supervisor-1',
      facilityId: mockFacility.id,
      facilityCode: mockFacility.facilityCode,
      dockDoorId: 'door-1',
      doorNumber: 'DOOR-01',
      yardVisitId: 'visit-1',
      visitNumber: 'VISIT-01',
      trailerNumber: 'TR-01',
      clientId: 'client-1',
      clientName: 'Client One',
      sessionNumber: 'RCV-SUPERVISOR-1',
      status: ReceivingStatus.Flagged,
      startedAtUtc: '2026-11-19T10:00:00Z',
      completedAtUtc: null,
      receivedByUserId: 'receiver-1',
      isBlindReceiving: true,
      requiresSupervisorApproval: true,
      isSupervisorApproved: false,
      version: 1,
      lines: [],
      discrepancies: [discrepancy],
    };
    let approvalRequests = 0;
    let resolutionRequests = 0;
    const fetchSpy = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      if (url.endsWith('/api/v1/auth/me'))
        return { ok: true, status: 200, json: async () => supervisor } as Response;
      if (url.includes('/api/v1/facilities'))
        return { ok: true, status: 200, json: async () => [mockFacility] } as Response;
      if (url.endsWith(`/api/v1/receiving/sessions/${session.id}/approve`) && init?.method === 'POST') {
        approvalRequests++;
        return { ok: true, status: 200, json: async () => ({ ...session, isSupervisorApproved: true }) } as Response;
      }
      if (url.endsWith(`/api/v1/receiving/sessions/${session.id}/discrepancies/${discrepancy.id}/resolve`) && init?.method === 'POST') {
        resolutionRequests++;
        return { ok: true, status: 200, json: async () => ({ ...discrepancy, status: DiscrepancyResolutionStatus.Approved }) } as Response;
      }
      if (url.endsWith(`/api/v1/receiving/sessions/${session.id}`))
        return { ok: true, status: 200, json: async () => session } as Response;
      if (url.includes('/api/v1/receiving/sessions'))
        return { ok: true, status: 200, json: async () => ({ items: [session], totalCount: 1, pageNumber: 1, pageSize: 15 }) } as Response;
      return { ok: false, status: 404 } as Response;
    });
    vi.stubGlobal('fetch', fetchSpy);
    setupProviders(<ReceivingPage />);

    await waitFor(() => expect(screen.getByTestId(`open-session-${session.id}`)).toBeDefined());
    fireEvent.click(screen.getByTestId(`open-session-${session.id}`));
    await waitFor(() => expect(screen.getByTestId('approve-session-btn')).toBeDefined());

    fireEvent.click(screen.getByTestId('approve-session-btn'));
    await waitFor(() => expect(approvalRequests).toBe(1));
    fireEvent.click(screen.getByTestId(`resolve-approve-${discrepancy.id}`));
    await waitFor(() => expect(resolutionRequests).toBe(1));
  });

  it('generates directed putaway tasks and executes dual-scan verification with ledger relocation', async () => {
    const mockPendingTask = {
      id: 'task-ptw-1',
      clientId: 'client-1',
      facilityId: mockFacility.id,
      itemId: 'item-100',
      sku: 'SKU-PALLET-A',
      itemDescription: 'High Velocity SKU',
      lotId: 'lot-1',
      lotNumber: 'LOT-9988',
      lpnId: 'lpn-rec-1',
      lpnCode: 'LPN-BOX-001',
      sourceLocationId: 'loc-stg-1',
      sourceLocationCode: 'STAGING-RCV-01',
      suggestedLocationId: 'loc-bin-a1',
      suggestedLocationCode: 'BIN-A1-04',
      suggestedLocationBarcode: 'LOC-BIN-A1-04',
      quantity: 20,
      status: PutawayTaskStatus.Pending,
      priority: PutawayPriority.High,
      createdAtUtc: '2026-11-19T10:10:00Z',
      version: 1,
    };

    let confirmInvoked = 0;
    let generateInvoked = 0;

    const fetchSpy = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();

      if (url.endsWith('/api/v1/auth/me')) {
        return { ok: true, status: 200, json: async () => mockUserProfile } as Response;
      }

      if (url.includes('/api/v1/facilities')) {
        return { ok: true, status: 200, json: async () => [mockFacility] } as Response;
      }

      if (url.includes('/api/v1/putaway/tasks/generate')) {
        generateInvoked++;
        return {
          ok: true,
          status: 200,
          json: async () => [mockPendingTask],
        } as Response;
      }

      if (url.includes('/api/v1/putaway/tasks/task-ptw-1/confirm')) {
        confirmInvoked++;
        const body = JSON.parse(init?.body as string);
        return {
          ok: true,
          status: 200,
          json: async () => ({
            ...mockPendingTask,
            status: PutawayTaskStatus.Completed,
            actualLocationId: 'loc-bin-a1',
            actualLocationCode: 'BIN-A1-04',
            scannedLpn: body.scannedLpn,
          }),
        } as Response;
      }

      if (url.includes('/api/v1/putaway/tasks/next')) {
        // Return task on first call, null on subsequent after completion
        if (confirmInvoked === 0) {
          return { ok: true, status: 200, json: async () => mockPendingTask } as Response;
        } else {
          return { ok: true, status: 204, json: async () => null } as Response;
        }
      }

      if (url.includes('/api/v1/putaway/tasks')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            items: [mockPendingTask],
            totalCount: 1,
            pageNumber: 1,
            pageSize: 10,
          }),
        } as Response;
      }

      return { ok: false, status: 404 } as Response;
    });
    vi.stubGlobal('fetch', fetchSpy);

    setupProviders(<PutawayPage />, '/app/inbound/putaway');

    // 1. Task Queue displays pending task
    await waitFor(() => {
      expect(screen.getByTestId('putaway-queue-view')).toBeDefined();
      expect(screen.getByText('SKU-PALLET-A')).toBeDefined();
      expect(screen.getByText('BIN-A1-04')).toBeDefined();
    });

    // 2. Click Generate Tasks
    const genBtn = screen.getByTestId('generate-tasks-btn');
    fireEvent.click(genBtn);

    await waitFor(() => {
      expect(generateInvoked).toBe(1);
    });

    // 3. Switch to RF Mobile Execution Tab
    fireEvent.click(screen.getByTestId('tab-rf'));

    await waitFor(() => {
      expect(screen.getByTestId('rf-execution-view')).toBeDefined();
      expect(screen.getByTestId('dual-scan-form')).toBeDefined();
      expect(screen.getByText('LPN-BOX-001')).toBeDefined();
    });

    // 4. Confirm button disabled initially when inputs are blank
    const confirmBtn = screen.getByTestId('confirm-putaway-btn');
    expect(confirmBtn).toHaveProperty('disabled', true);

    // 5. Fill dual-scans
    fireEvent.change(screen.getByTestId('input-scanned-lpn'), { target: { value: 'LPN-BOX-001' } });
    fireEvent.change(screen.getByTestId('input-scanned-bin'), { target: { value: 'LOC-BIN-A1-04' } });

    expect(confirmBtn).toHaveProperty('disabled', false);

    // 6. Confirm Putaway
    fireEvent.click(confirmBtn);

    await waitFor(() => {
      expect(confirmInvoked).toBe(1);
      expect(screen.getByTestId('putaway-feedback')).toBeDefined();
      expect(screen.getByText(/Putaway confirmed!/i)).toBeDefined();
    });
  });

  it('renders live stock-on-hand ledger scoped to active facility and propagates X-Facility-Id header', async () => {
    const mockStock = [
      {
        id: 'stock-1',
        clientId: 'client-1',
        facilityId: mockFacility.id,
        itemId: 'item-1',
        sku: 'SKU-SOUND-100',
        itemDescription: 'Sound Storage SKU',
        locationId: 'loc-bin-1',
        locationCode: 'BIN-01-A',
        locationBarcode: 'LOC-BIN-01-A',
        lotId: 'lot-1',
        lotNumber: 'LOT-2026-A',
        qtyAvailable: 50,
        qtyAllocated: 10,
        qtyReserved: 5,
        qtyQuarantined: 0,
        qtyDamaged: 0,
        qtyOnHold: 0,
        totalQuantity: 65,
        version: 1,
      },
      {
        id: 'stock-2',
        clientId: 'client-1',
        facilityId: mockFacility.id,
        itemId: 'item-2',
        sku: 'SKU-DMG-200',
        itemDescription: 'Damaged Hold Item',
        locationId: 'loc-stg-1',
        locationCode: 'STAGING-01',
        locationBarcode: 'LOC-STG-01',
        lotId: null,
        lotNumber: null,
        qtyAvailable: 0,
        qtyAllocated: 0,
        qtyReserved: 0,
        qtyQuarantined: 0,
        qtyDamaged: 8,
        qtyOnHold: 0,
        totalQuantity: 8,
        version: 1,
      },
    ];

    let facilityHeaderCaptured: string | null = null;

    const fetchSpy = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      const headers = (init?.headers as Record<string, string>) || {};

      if (url.endsWith('/api/v1/auth/me')) {
        return { ok: true, status: 200, json: async () => mockUserProfile } as Response;
      }

      if (url.includes('/api/v1/facilities')) {
        return { ok: true, status: 200, json: async () => [mockFacility] } as Response;
      }

      if (url.includes('/api/v1/inventory/stock')) {
        facilityHeaderCaptured = headers['X-Facility-Id'] || null;
        return {
          ok: true,
          status: 200,
          json: async () => ({
            items: mockStock,
            totalCount: 2,
            pageNumber: 1,
            pageSize: 15,
          }),
        } as Response;
      }

      return { ok: false, status: 404 } as Response;
    });
    vi.stubGlobal('fetch', fetchSpy);

    setupProviders(<StockOnHandPage />, '/app/inventory');

    await waitFor(() => {
      expect(screen.getByTestId('stock-table')).toBeDefined();
      expect(screen.getByText('SKU-SOUND-100')).toBeDefined();
      expect(screen.getByText('SKU-DMG-200')).toBeDefined();
    });

    // Verify aggregate metric cards
    expect(screen.getByTestId('metric-total-skus').textContent).toBe('2');
    expect(screen.getByTestId('metric-available-qty').textContent).toBe('50');
    expect(screen.getByTestId('metric-quarantine-qty').textContent).toBe('8');

    // Verify location badges
    expect(screen.getByText('Staging')).toBeDefined();
    expect(screen.getByText('Bin')).toBeDefined();

    // Verify X-Facility-Id header was sent
    expect(facilityHeaderCaptured).toBe(mockFacility.id);
  });

  it('verifies receiving intake workstation ergonomics, hardware wedge scanner single-flight guard, and focus restoration', async () => {
    const mockSession = {
      id: 'session-rcv-wedge',
      facilityId: mockFacility.id,
      facilityCode: 'FAC-ORD1',
      dockDoorId: 'door-1',
      doorNumber: 'DOOR-02',
      yardVisitId: 'yv-2',
      visitNumber: 'YV-02',
      trailerNumber: 'TR-202',
      clientId: 'client-1',
      clientName: 'Apex Retail',
      asnId: 'asn-2',
      asnNumber: 'ASN-202',
      sessionNumber: 'RCV-FAC-ORD1-20261119-0002',
      status: ReceivingStatus.InProgress,
      startedAtUtc: '2026-11-19T10:00:00Z',
      completedAtUtc: null,
      receivedByUserId: 'usr-wh-lead-1',
      isBlindReceiving: false,
      requiresSupervisorApproval: false,
      isSupervisorApproved: false,
      version: 1,
      lines: [],
      discrepancies: [],
    };

    let lineScanCallCount = 0;
    const fetchSpy = vi.fn(async (input: RequestInfo | URL) => {
      const url = input.toString();

      if (url.endsWith('/api/v1/auth/me')) {
        return { ok: true, status: 200, json: async () => mockUserProfile } as Response;
      }
      if (url.includes('/api/v1/facilities')) {
        return { ok: true, status: 200, json: async () => [mockFacility] } as Response;
      }
      if (url.includes('/api/v1/receiving/sessions/session-rcv-wedge/lines')) {
        lineScanCallCount++;
        // Simulate network latency to test single-flight / in-flight protection
        await new Promise((r) => setTimeout(r, 80));
        return {
          ok: true,
          status: 200,
          json: async () => ({
            id: 'line-wedge-1',
            receivingSessionId: 'session-rcv-wedge',
            itemId: 'item-wedge-99',
            sku: 'SKU-WEDGE-99',
            itemDescription: 'Hardware Wedge Scanner SKU',
            lpn: 'LPN-WEDGE-01',
            quantityReceived: 5,
            quantityExpected: 5,
            lotNumber: 'LOT-WEDGE',
            condition: ReceiptConditionStatus.Sound,
            stagingLocationId: 'loc-stg-1',
            stagingLocationCode: 'STAGING-01',
            scannedAtUtc: new Date().toISOString(),
            version: 1,
          }),
        } as Response;
      }
      if (url.includes('/api/v1/receiving/sessions/session-rcv-wedge')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            ...mockSession,
            lines: lineScanCallCount > 0 ? [
              {
                id: 'line-wedge-1',
                receivingSessionId: 'session-rcv-wedge',
                itemId: 'item-wedge-99',
                sku: 'SKU-WEDGE-99',
                itemDescription: 'Hardware Wedge Scanner SKU',
                lpn: 'LPN-WEDGE-01',
                quantityReceived: 5,
                quantityExpected: 5,
                lotNumber: 'LOT-WEDGE',
                condition: ReceiptConditionStatus.Sound,
                stagingLocationId: 'loc-stg-1',
                stagingLocationCode: 'STAGING-01',
                scannedAtUtc: new Date().toISOString(),
                version: 1,
              }
            ] : [],
          }),
        } as Response;
      }
      if (url.includes('/api/v1/receiving/sessions')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            items: [mockSession],
            totalCount: 1,
            pageNumber: 1,
            pageSize: 10,
          }),
        } as Response;
      }
      return { ok: false, status: 404 } as Response;
    });
    vi.stubGlobal('fetch', fetchSpy);

    setupProviders(<ReceivingPage />, '/app/inbound');

    await waitFor(() => {
      expect(screen.getByTestId('open-session-session-rcv-wedge')).toBeDefined();
    });

    fireEvent.click(screen.getByTestId('open-session-session-rcv-wedge'));

    await waitFor(() => {
      expect(screen.getByTestId('line-scanner-card')).toBeDefined();
    });

    const itemInput = screen.getByTestId('scan-item-id-input') as HTMLInputElement;
    const lpnInput = screen.getByTestId('scan-lpn-input') as HTMLInputElement;

    // Verify barcode input classes (>=52px height, monospace readable scanner text)
    expect(itemInput.className).toContain('scan-barcode-input');
    expect(lpnInput.className).toContain('scan-barcode-input');

    // Fill form
    fireEvent.change(itemInput, { target: { value: 'item-wedge-99' } });
    fireEvent.change(screen.getByTestId('scan-qty-input'), { target: { value: '5' } });
    fireEvent.change(lpnInput, { target: { value: 'LPN-WEDGE-01' } });

    const submitBtn = screen.getByTestId('submit-line-btn');

    // Simulate rapid hardware scanner double Enter / double submission
    fireEvent.click(submitBtn);
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(lineScanCallCount).toBe(1); // Protected by inFlightScanRef single-flight guard
      expect(screen.getByText('SKU-WEDGE-99')).toBeDefined();
    });

    // Verify focus restoration after scan
    await waitFor(() => {
      expect(document.activeElement).toBe(itemInput);
    });
  });

  it('verifies RF directed putaway two-step guidance, device vibration feedback, and duplicate confirmation prevention', async () => {
    const mockPutawayTask = {
      id: 'ptw-step-1',
      facilityId: mockFacility.id,
      facilityCode: 'FAC-ORD1',
      receiptLineId: 'line-step-1',
      lpnCode: 'LPN-DUAL-01',
      sku: 'SKU-DUAL-55',
      itemDescription: 'Dual-Scan Ergonomics Item',
      quantity: 20,
      sourceLocationCode: 'STAGE-DOCK-1',
      sourceLocationBarcode: 'LOC-STAGE-DOCK-1',
      suggestedLocationCode: 'RACK-BIN-99',
      suggestedLocationBarcode: 'LOC-RACK-BIN-99',
      status: PutawayTaskStatus.Pending,
      priority: PutawayPriority.High,
      createdAtUtc: new Date().toISOString(),
      assignedUserId: null,
      actualLocationCode: null,
      completedAtUtc: null,
      version: 1,
    };

    let confirmTaskCalls = 0;
    const vibrateSpy = vi.fn();
    // Stub navigator.vibrate
    Object.defineProperty(navigator, 'vibrate', {
      value: vibrateSpy,
      configurable: true,
      writable: true,
    });

    const fetchSpy = vi.fn(async (input: RequestInfo | URL) => {
      const url = input.toString();

      if (url.endsWith('/api/v1/auth/me')) {
        return { ok: true, status: 200, json: async () => mockUserProfile } as Response;
      }
      if (url.includes('/api/v1/facilities')) {
        return { ok: true, status: 200, json: async () => [mockFacility] } as Response;
      }
      if (url.includes('/api/v1/putaway/tasks/next')) {
        return { ok: true, status: 200, json: async () => mockPutawayTask } as Response;
      }
      if (url.includes('/api/v1/putaway/tasks/ptw-step-1/confirm')) {
        confirmTaskCalls++;
        await new Promise((r) => setTimeout(r, 80));
        return {
          ok: true,
          status: 200,
          json: async () => ({
            ...mockPutawayTask,
            actualLocationCode: 'RACK-BIN-99',
            status: PutawayTaskStatus.Completed,
          }),
        } as Response;
      }
      if (url.includes('/api/v1/putaway/tasks')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            items: [mockPutawayTask],
            totalCount: 1,
            pageNumber: 1,
            pageSize: 10,
          }),
        } as Response;
      }
      return { ok: false, status: 404 } as Response;
    });
    vi.stubGlobal('fetch', fetchSpy);

    setupProviders(<PutawayPage />, '/app/inbound/putaway');

    await waitFor(() => {
      expect(screen.getByTestId('tab-rf')).toBeDefined();
    });

    fireEvent.click(screen.getByTestId('tab-rf'));

    await waitFor(() => {
      expect(screen.getByTestId('rf-task-card')).toBeDefined();
      expect(screen.getByText('SKU-DUAL-55')).toBeDefined();
    });

    // Verify 2-step scanning guidance labels
    expect(screen.getByText('Step 1')).toBeDefined();
    expect(screen.getByText(/Scan Container LPN Barcode \*/i)).toBeDefined();
    expect(screen.getByText('Step 2')).toBeDefined();
    expect(screen.getByText(/Scan Destination Bin Tag Barcode \*/i)).toBeDefined();

    // Verify inputs have scan-input styling
    const lpnInput = screen.getByTestId('input-scanned-lpn') as HTMLInputElement;
    const binInput = screen.getByTestId('input-scanned-bin') as HTMLInputElement;
    expect(lpnInput.className).toContain('scan-input');
    expect(binInput.className).toContain('scan-input');

    // Fill inputs
    fireEvent.change(lpnInput, { target: { value: 'LPN-DUAL-01' } });
    fireEvent.change(binInput, { target: { value: 'LOC-RACK-BIN-99' } });

    const confirmBtn = screen.getByTestId('confirm-putaway-btn');

    // Simulate rapid double click to test duplicate submission guard
    fireEvent.click(confirmBtn);
    fireEvent.click(confirmBtn);

    await waitFor(() => {
      expect(confirmTaskCalls).toBe(1); // Single-flight ref prevented duplicate call
      expect(vibrateSpy).toHaveBeenCalledWith([100, 50, 100]); // Device vibration feedback verified
      expect(screen.getByText(/Putaway confirmed!/i)).toBeDefined();
    });
  });
});
