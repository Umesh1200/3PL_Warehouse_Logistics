import { test, expect } from '@playwright/test';

test.describe('Phase 7.3: Real Browser E2E Receiving, Directed Putaway & Stock Ledger Verification', () => {
  const mockFacility = {
    id: 'fac-ord1-guid',
    facilityCode: 'FAC-ORD1',
    name: 'Chicago Central Fulfillment Hub',
    addressLine1: '1000 Logistics Parkway',
    city: 'Chicago',
    state: 'IL',
    postalCode: '60601',
    country: 'USA',
    timezone: 'America/Chicago',
    isActive: true,
  };

  const mockSession = {
    id: 'e2e-session-1',
    facilityId: mockFacility.id,
    facilityCode: 'FAC-ORD1',
    dockDoorId: 'door-1',
    doorNumber: 'DOOR-04',
    yardVisitId: 'yv-1',
    visitNumber: 'YV-8821',
    trailerNumber: 'TR-7721',
    clientId: 'client-1',
    clientName: 'Apex Distribution Ltd',
    asnId: 'asn-1',
    asnNumber: 'ASN-2026-001',
    sessionNumber: 'RCV-FAC-ORD1-20261119-0001',
    status: 'InProgress',
    startedAtUtc: '2026-11-19T09:00:00Z',
    completedAtUtc: null,
    receivedByUserId: 'e2e-user-1',
    isBlindReceiving: false,
    requiresSupervisorApproval: false,
    isSupervisorApproved: false,
    version: 1,
    lines: [
      {
        id: 'line-1',
        receivingSessionId: 'e2e-session-1',
        itemId: 'item-1',
        sku: 'SKU-E2E-AUDIO',
        itemDescription: 'Wireless Noise-Canceling Headset',
        lpn: 'LPN-E2E-001',
        quantityReceived: 10,
        quantityExpected: 10,
        lotNumber: 'LOT-AUDIO-1',
        condition: 'Sound',
        stagingLocationId: 'loc-stg-1',
        stagingLocationCode: 'STAGING-RCV-FAC-ORD1',
        scannedAtUtc: '2026-11-19T09:15:00Z',
        version: 1,
      },
    ],
    discrepancies: [],
  };

  const mockPutawayTask = {
    id: 'task-ptw-e2e',
    clientId: 'client-1',
    facilityId: mockFacility.id,
    itemId: 'item-1',
    sku: 'SKU-E2E-AUDIO',
    itemDescription: 'Wireless Noise-Canceling Headset',
    lotId: 'lot-1',
    lotNumber: 'LOT-AUDIO-1',
    lpnId: 'line-1',
    lpnCode: 'LPN-E2E-001',
    sourceLocationId: 'loc-stg-1',
    sourceLocationCode: 'STAGING-RCV-FAC-ORD1',
    suggestedLocationId: 'loc-bin-401',
    suggestedLocationCode: 'BIN-HIGHBAY-04',
    suggestedLocationBarcode: 'LOC-BIN-HB-04',
    quantity: 10,
    status: 'Pending',
    priority: 'High',
    createdAtUtc: '2026-11-19T09:30:00Z',
    version: 1,
  };

  const mockStock = [
    {
      id: 'stock-stg-1',
      clientId: 'client-1',
      facilityId: mockFacility.id,
      itemId: 'item-1',
      sku: 'SKU-E2E-AUDIO',
      itemDescription: 'Wireless Noise-Canceling Headset',
      locationId: 'loc-stg-1',
      locationCode: 'STAGING-RCV-FAC-ORD1',
      locationBarcode: 'LOC-STG-RCV-ORD1',
      lotId: 'lot-1',
      lotNumber: 'LOT-AUDIO-1',
      qtyAvailable: 10,
      qtyAllocated: 0,
      qtyReserved: 0,
      qtyQuarantined: 0,
      qtyDamaged: 0,
      qtyOnHold: 0,
      totalQuantity: 10,
      version: 1,
    },
  ];

  test.beforeEach(async ({ page }) => {
    // Mock authentication
    await page.route('**/api/v1/auth/login', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          accessToken: 'e2e-access-token-73',
          refreshToken: 'e2e-refresh-token-73',
          tokenType: 'Bearer',
          expiresIn: 3600,
          user: {
            id: 'e2e-user-1',
            email: 'whlead@3pl.local',
            firstName: 'Floor',
            lastName: 'Supervisor',
            userType: 1, // Staff
            clientId: null,
            status: 1,
            roles: ['SuperAdmin'],
          },
        }),
      });
    });

    await page.route('**/api/v1/auth/me', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          userId: 'e2e-user-1',
          email: 'whlead@3pl.local',
          firstName: 'Floor',
          lastName: 'Supervisor',
          userType: 1,
          tenantId: null,
          companyName: 'Nexus3PL Enterprise',
          roles: ['SuperAdmin'],
          permissions: [
            'inbound:view',
            'inbound:receive',
            'inbound:putaway',
            'inbound:putaway:execute',
            'inbound:putaway:manage',
            'inventory:view',
            'facilities:view',
          ],
        }),
      });
    });

    await page.route('**/api/v1/facilities**', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([mockFacility]),
      });
    });

    // Mock receiving endpoints with URL routing
    await page.route('**/api/v1/receiving/sessions**', async (route) => {
      const url = route.request().url();
      if (url.includes('/e2e-session-1/complete')) {
        await new Promise((r) => setTimeout(r, 200));
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            ...mockSession,
            status: 'Completed',
            completedAtUtc: '2026-11-19T09:45:00Z',
          }),
        });
      }
      if (url.includes('/e2e-session-1')) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(mockSession),
        });
      }
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          items: [mockSession],
          totalCount: 1,
          pageNumber: 1,
          pageSize: 10,
        }),
      });
    });

    // Mock putaway endpoints with URL routing
    await page.route('**/api/v1/putaway/tasks**', async (route) => {
      const url = route.request().url();
      if (url.includes('/next')) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(mockPutawayTask),
        });
      }
      if (url.includes('/confirm')) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            ...mockPutawayTask,
            status: 'Completed',
            actualLocationId: 'loc-bin-401',
            actualLocationCode: 'BIN-HIGHBAY-04',
          }),
        });
      }
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          items: [mockPutawayTask],
          totalCount: 1,
          pageNumber: 1,
          pageSize: 10,
        }),
      });
    });

    // Mock stock endpoints
    await page.route('**/api/v1/inventory/stock**', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          items: mockStock,
          totalCount: 1,
          pageNumber: 1,
          pageSize: 15,
        }),
      });
    });
  });

  test('executes end-to-end receiving intake, complete guard, directed putaway dual-scan, and stock query', async ({ page }) => {
    // 1. Login
    await page.goto('/login');
    await page.fill('input[type="email"]', 'whlead@3pl.local');
    await page.fill('input[type="password"]', 'CorrectHorseBatteryStaple123!');
    await page.click('button[type="submit"]');

    // Wait for Dashboard to mount
    await expect(page.getByRole('heading', { name: /Operations Dashboard/i })).toBeVisible();

    // 2. Navigate to Inbound Goods Receiving via Sidebar
    const inboundNavLink = page.getByRole('link', { name: /Inbound & Receiving/i });
    await expect(inboundNavLink).toBeVisible();
    await inboundNavLink.click();

    // Verify Receiving Page loads
    await expect(page.getByRole('heading', { name: /Inbound Goods Receiving/i }).first()).toBeVisible();
    await expect(page.getByText('RCV-FAC-ORD1-20261119-0001')).toBeVisible();

    // 3. Open Intake Session Workspace
    const openIntakeBtn = page.getByTestId('open-session-e2e-session-1');
    await expect(openIntakeBtn).toBeVisible();
    await openIntakeBtn.click();

    // Verify Workspace Elements
    await expect(page.getByTestId('session-detail-view')).toBeVisible();
    await expect(page.getByTestId('line-scanner-card')).toBeVisible();
    await expect(page.getByText('SKU-E2E-AUDIO')).toBeVisible();

    // 4. Test Complete Session Action and Idempotency Guard
    const completeSessionBtn = page.getByTestId('complete-session-action-btn');
    await expect(completeSessionBtn).toBeVisible();
    await completeSessionBtn.click();

    // Modal Confirmation Dialog
    const confirmModal = page.getByTestId('complete-confirm-modal');
    await expect(confirmModal).toBeVisible();

    const confirmBtn = page.getByTestId('confirm-complete-btn');
    await expect(confirmBtn).toBeVisible();

    // Click confirm: button should enter disabled state during in-flight network request
    await confirmBtn.click();
    await expect(confirmBtn).toBeDisabled();

    // Verify success feedback appears
    await expect(page.getByTestId('feedback-banner')).toBeVisible();
    await expect(page.getByText(/finalized. Stock-on-hand ledger updated in staging/i)).toBeVisible();

    // 5. Navigate to Directed Putaway
    const putawayNavLink = page.getByRole('link', { name: /Directed Putaway/i });
    await expect(putawayNavLink).toBeVisible();
    await putawayNavLink.click();

    await expect(page.getByRole('heading', { name: /Directed Putaway & Storage Relocation/i }).first()).toBeVisible();
    await expect(page.getByTestId('putaway-queue-view')).toBeVisible();
    await expect(page.getByText('BIN-HIGHBAY-04')).toBeVisible();

    // 6. Switch to RF Mobile Execution Tab
    const rfTabBtn = page.getByTestId('tab-rf');
    await expect(rfTabBtn).toBeVisible();
    await rfTabBtn.click();

    await expect(page.getByTestId('rf-execution-view')).toBeVisible();
    await expect(page.getByTestId('dual-scan-form')).toBeVisible();

    // Confirm button must be disabled before scanning
    const confirmPutawayBtn = page.getByTestId('confirm-putaway-btn');
    await expect(confirmPutawayBtn).toBeDisabled();

    // Fill dual-scan barcodes
    await page.fill('[data-testid="input-scanned-lpn"]', 'LPN-E2E-001');
    await page.fill('[data-testid="input-scanned-bin"]', 'LOC-BIN-HB-04');

    // Confirm button should become enabled
    await expect(confirmPutawayBtn).toBeEnabled();

    // Click confirm putaway
    await confirmPutawayBtn.click();

    // Success notification should be displayed
    await expect(page.getByTestId('putaway-feedback')).toBeVisible();
    await expect(page.getByText(/Putaway confirmed!/i)).toBeVisible();

    // 7. Navigate to Stock on Hand Ledger
    const stockNavLink = page.getByRole('link', { name: /Inventory Balances/i });
    await expect(stockNavLink).toBeVisible();
    await stockNavLink.click();

    await expect(page.getByRole('heading', { name: /Stock on Hand & Inventory Ledger/i }).first()).toBeVisible();
    await expect(page.getByTestId('stock-table')).toBeVisible();
    await expect(page.getByText('SKU-E2E-AUDIO')).toBeVisible();
    await expect(page.getByTestId('metric-total-skus')).toHaveText('1');
    await expect(page.getByText('Staging', { exact: true })).toBeVisible();
  });
});
