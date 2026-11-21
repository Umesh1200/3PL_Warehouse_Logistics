import { test } from '@playwright/test';
import * as path from 'path';

const ARTIFACT_DIR = 'C:/Users/DELL/.gemini/antigravity/brain/3ef5018b-e9e3-4374-9eca-184b02ff2c4e/visual-audit';

const viewports = [
  { name: 'desktop', width: 1440, height: 900 },
  { name: 'tablet', width: 768, height: 1024 },
  { name: 'mobile', width: 375, height: 812 },
];

test.describe('Nexus3PL Visual Design Audit - Multi-Viewport Capture', () => {
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

  const mockPutawayTasks = [
    {
      id: 'task-ptw-1',
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
    },
    {
      id: 'task-ptw-2',
      clientId: 'client-1',
      facilityId: mockFacility.id,
      itemId: 'item-2',
      sku: 'SKU-E2E-SMART',
      itemDescription: 'Smart Watch Sensor Pro',
      lotId: null,
      lotNumber: null,
      lpnId: 'line-2',
      lpnCode: 'LPN-E2E-002',
      sourceLocationId: 'loc-stg-1',
      sourceLocationCode: 'STAGING-RCV-FAC-ORD1',
      suggestedLocationId: 'loc-bin-402',
      suggestedLocationCode: 'BIN-ZONE-B-12',
      suggestedLocationBarcode: 'LOC-BIN-ZB-12',
      quantity: 25,
      status: 'Assigned',
      priority: 'Normal',
      createdAtUtc: '2026-11-19T09:35:00Z',
      version: 1,
    },
  ];

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
    {
      id: 'stock-bin-1',
      clientId: 'client-1',
      facilityId: mockFacility.id,
      itemId: 'item-2',
      sku: 'SKU-E2E-SMART',
      itemDescription: 'Smart Watch Sensor Pro',
      locationId: 'loc-bin-402',
      locationCode: 'BIN-ZONE-B-12',
      locationBarcode: 'LOC-BIN-ZB-12',
      lotId: null,
      lotNumber: null,
      qtyAvailable: 50,
      qtyAllocated: 10,
      qtyReserved: 5,
      qtyQuarantined: 0,
      qtyDamaged: 0,
      qtyOnHold: 0,
      totalQuantity: 65,
      version: 1,
    },
  ];

  for (const vp of viewports) {
    test(`capture screenshots for ${vp.name} (${vp.width}x${vp.height})`, async ({ page }) => {
      await page.setViewportSize({ width: vp.width, height: vp.height });

      // Route setup
      await page.route('**/api/v1/auth/login', async (route) => {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            accessToken: 'e2e-token',
            refreshToken: 'e2e-refresh',
            tokenType: 'Bearer',
            expiresIn: 3600,
            user: {
              id: 'e2e-user-1',
              email: 'whlead@3pl.local',
              firstName: 'Floor',
              lastName: 'Supervisor',
              userType: 1,
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
            companyName: 'Nexus3PL Enterprise Logistics',
            roles: ['SuperAdmin'],
            permissions: [
              'inbound:view',
              'inbound:receive',
              'inbound:putaway',
              'inbound:putaway:execute',
              'inbound:putaway:manage',
              'inventory:view',
              'facilities:view',
              'users:view',
              'roles:view',
              'clients:view',
              'clients:create',
              'clients:edit',
              'clients:delete',
              'analytics:control-tower:view',
            ],
          }),
        });
      });

      // Entity Schema route mock for metadata-driven modules
      await page.route('**/api/v1/schemas/**', async (route) => {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            name: 'Client',
            version: '1.0.0',
            title: 'Client Tenant',
            pluralTitle: 'Client Tenants',
            description: 'Multi-tenant accounts, billing currencies, and service level agreements.',
            icon: 'Building',
            primaryKey: 'id',
            endpoints: {
              list: '/api/v1/clients',
              get: '/api/v1/clients/{id}',
              create: '/api/v1/clients',
              update: '/api/v1/clients/{id}',
              delete: '/api/v1/clients/{id}',
            },
            permissions: {
              view: 'clients:view',
              create: 'clients:create',
              edit: 'clients:edit',
              delete: 'clients:delete',
            },
            fields: [
              { name: 'id', label: 'Client ID', type: 'string', readOnly: true, hidden: true },
              { name: 'accountCode', label: 'Client Code', type: 'code', required: true, icon: 'Barcode' },
              { name: 'companyName', label: 'Company Name', type: 'string', required: true, icon: 'Building' },
              {
                name: 'slaTier',
                label: 'SLA Tier',
                type: 'select',
                required: true,
                defaultValue: 1,
                options: [
                  { value: 1, label: 'Standard', variant: 'info' },
                  { value: 2, label: 'Silver', variant: 'info' },
                  { value: 3, label: 'Gold', variant: 'warning' },
                  { value: 4, label: 'Platinum', variant: 'success' },
                ],
              },
              { name: 'currency', label: 'Billing Currency', type: 'currency', required: true, defaultValue: 'USD' },
              {
                name: 'status',
                label: 'Status',
                type: 'select',
                required: true,
                defaultValue: 2,
                options: [
                  { value: 1, label: 'Onboarding', variant: 'info' },
                  { value: 2, label: 'Active', variant: 'success' },
                  { value: 3, label: 'Suspended', variant: 'warning' },
                  { value: 4, label: 'Terminated', variant: 'danger' },
                ],
              },
              { name: 'createdAt', label: 'Created At', type: 'date', readOnly: true, icon: 'Calendar' },
              { name: 'activeUsersCount', label: 'Active Users', type: 'integer', readOnly: true, icon: 'Users' },
              { name: 'contactFirstName', label: 'Contact First Name', type: 'string', icon: 'Users' },
              { name: 'contactLastName', label: 'Contact Last Name', type: 'string', icon: 'Users' },
              { name: 'contactEmail', label: 'Contact Email', type: 'email', icon: 'Mail' },
              { name: 'contactPhone', label: 'Contact Phone', type: 'phone', icon: 'Phone' },
            ],
            views: {
              list: {
                columns: [
                  { field: 'accountCode', label: 'Client Code', width: '150px', sortable: true, format: 'code' },
                  { field: 'companyName', label: 'Company Name', sortable: true, format: 'text' },
                  { field: 'slaTier', label: 'SLA Tier', width: '130px', format: 'badge' },
                  { field: 'currency', label: 'Currency', width: '110px', align: 'center', format: 'currency' },
                  { field: 'status', label: 'Status', width: '120px', align: 'center', format: 'badge' },
                  { field: 'createdAt', label: 'Created', width: '130px', align: 'right', format: 'date' },
                ],
                defaultSort: { field: 'companyName', direction: 'asc' },
                searchFields: ['companyName', 'accountCode'],
              },
              form: {
                sections: [
                  {
                    id: 'core',
                    title: 'Tenant Identity & SLA Configuration',
                    description: 'Primary organization details, account partition identifier, and billing settings.',
                    icon: 'Building',
                    columns: 2,
                    fields: ['companyName', 'accountCode', 'slaTier', 'currency', 'status'],
                  },
                  {
                    id: 'contact',
                    title: 'Primary Contact Information',
                    description: 'Direct communications contact for logistics operations and exceptions.',
                    icon: 'Mail',
                    columns: 2,
                    fields: ['contactFirstName', 'contactLastName', 'contactEmail', 'contactPhone'],
                  },
                ],
              },
            },
            actions: [
              {
                id: 'create',
                label: 'New Client',
                icon: 'Plus',
                variant: 'primary',
                target: 'header',
                permission: 'clients:create',
              },
              {
                id: 'edit',
                label: 'Edit Details',
                icon: 'Edit2',
                variant: 'secondary',
                target: 'row',
                permission: 'clients:edit',
              },
              {
                id: 'deactivate',
                label: 'Deactivate',
                icon: 'Trash2',
                variant: 'danger',
                target: 'row',
                permission: 'clients:delete',
              },
            ],
          }),
        });
      });

      const mockClientsList = [
        {
          id: 'c1111111-1111-1111-1111-111111111111',
          accountCode: 'APEX',
          companyName: 'Apex Distribution Ltd',
          slaTier: 3,
          currency: 'USD',
          status: 2,
          createdAt: '2026-01-15T08:00:00Z',
          activeUsersCount: 14,
          contactFirstName: 'Arthur',
          contactLastName: 'Pendleton',
          contactEmail: 'logistics@apexdist.com',
          contactPhone: '+1 312-555-0144',
          version: 1,
        },
        {
          id: 'c2222222-2222-2222-2222-222222222222',
          accountCode: 'BEACON',
          companyName: 'Beacon Healthcare Supplies',
          slaTier: 4,
          currency: 'USD',
          status: 2,
          createdAt: '2026-02-10T10:30:00Z',
          activeUsersCount: 8,
          contactFirstName: 'Dr. Sarah',
          contactLastName: 'Lin',
          contactEmail: 'inbound@beaconhealth.org',
          contactPhone: '+1 617-555-0182',
          version: 1,
        },
        {
          id: 'c3333333-3333-3333-3333-333333333333',
          accountCode: 'CASCADE',
          companyName: 'Cascade Cold Chain Corp',
          slaTier: 1,
          currency: 'CAD',
          status: 1,
          createdAt: '2026-03-01T14:15:00Z',
          activeUsersCount: 2,
          contactFirstName: 'Marcus',
          contactLastName: 'Vance',
          contactEmail: 'ops@cascadecold.io',
          contactPhone: '+1 604-555-0199',
          version: 1,
        },
      ];

      await page.route('**/api/v1/clients**', async (route) => {
        if (route.request().method() === 'GET') {
          return route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({
              items: mockClientsList,
              totalCount: mockClientsList.length,
              pageNumber: 1,
              pageSize: 15,
              totalPages: 1,
            }),
          });
        }
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ ok: true }),
        });
      });

      await page.route('**/api/v1/facilities**', async (route) => {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify([mockFacility]),
        });
      });

      await page.route('**/api/v1/receiving/sessions**', async (route) => {
        if (route.request().url().includes(mockSession.id)) {
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

      await page.route('**/api/v1/putaway/tasks/next**', async (route) => {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(mockPutawayTasks[0]),
        });
      });

      await page.route('**/api/v1/putaway/tasks**', async (route) => {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            items: mockPutawayTasks,
            totalCount: mockPutawayTasks.length,
            pageNumber: 1,
            pageSize: 10,
          }),
        });
      });

      await page.route('**/api/v1/inventory/stock**', async (route) => {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            items: mockStock,
            totalCount: mockStock.length,
            pageNumber: 1,
            pageSize: 15,
          }),
        });
      });

      await page.route('**/api/v1/control-tower/summary**', async (route) => {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            facilityId: mockFacility.id,
            asOfUtc: '2026-11-21T18:00:00Z',
            ordersReceivedToday: 148,
            ordersDispatchedToday: 122,
            packagesDispatchedToday: 340,
            unitsPickedToday: 2150,
            cartonsPackedToday: 480,
            inboundPalletsReceivedToday: 18,
            putawayCompletedToday: 14,
            returnsInspectedToday: 3,
            unitsPerLaborHourToday: 48.6,
            averageFulfillmentCycleTimeHours: 1.6,
            sameDayDispatchAdherencePercent: 99.1,
            totalBilledRevenueToday: 21800,
            netFreightMarginToday: 3450,
            onTimeDeliveryRatePercent: 99.4,
            activeDeliveryExceptionsCount: 1,
            palletPositionOccupancyPercent: 82.5,
            volumetricCubeFillPercent: 68.4,
          }),
        });
      });

      await page.route('**/api/v1/control-tower/bottlenecks**', async (route) => {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            facilityId: mockFacility.id,
            evaluatedAtUtc: '2026-11-21T18:00:00Z',
            overallHealthScore: 98,
            receivingCongestion: { stage: 'Receiving', severity: 'Normal', queueDepth: 1, metricValue: 18, metricUnit: 'pallets', summaryMessage: 'Inflow clear', recommendedAction: 'None' },
            pickingBacklog: { stage: 'Picking', severity: 'Normal', queueDepth: 3, metricValue: 28, metricUnit: 'orders', summaryMessage: 'Picking pacing balanced', recommendedAction: 'None' },
            packingDelays: { stage: 'Packing', severity: 'Normal', queueDepth: 2, metricValue: 22, metricUnit: 'cartons', summaryMessage: 'Packing throughput optimal', recommendedAction: 'None' },
            dispatchDelays: { stage: 'Dispatch', severity: 'Normal', queueDepth: 0, metricValue: 0, metricUnit: 'pallets', summaryMessage: 'Staging lanes clear', recommendedAction: 'None' },
          }),
        });
      });

      await page.route('**/api/v1/control-tower/dock-yard**', async (route) => {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            totalYardSpotsCount: 24,
            occupiedYardSpotsCount: 5,
            yardOccupancyPercent: 20.8,
            trucksCheckedInCount: 15,
            trucksDepartedCount: 11,
            trucksWaitingInYardCount: 2,
            trucksAtDockCount: 3,
            averageYardWaitTimeMinutes: 12.5,
            averageDockServiceTimeMinutes: 26.0,
            averageTotalTurnTimeMinutes: 38.5,
          }),
        });
      });

      // 1. Landing Page
      await page.goto('/');
      await page.waitForLoadState('networkidle');
      await page.screenshot({
        path: path.join(ARTIFACT_DIR, `${vp.name}-01-landing.png`),
        fullPage: true,
      });

      // 2. Login Page
      await page.goto('/login');
      await page.waitForLoadState('networkidle');
      await page.screenshot({
        path: path.join(ARTIFACT_DIR, `${vp.name}-02-login.png`),
        fullPage: true,
      });

      // Perform Login
      await page.fill('input[type="email"]', 'whlead@3pl.local');
      await page.fill('input[type="password"]', 'CorrectHorseBatteryStaple123!');
      await page.click('button[type="submit"]');
      await page.waitForSelector('.app-shell-container', { timeout: 10000 });

      // 3. Dashboard Page
      await page.waitForLoadState('networkidle');
      await page.screenshot({
        path: path.join(ARTIFACT_DIR, `${vp.name}-03-dashboard.png`),
        fullPage: true,
      });
      await page.screenshot({
        path: path.join('C:/Users/DELL/.gemini/antigravity/brain/3ef5018b-e9e3-4374-9eca-184b02ff2c4e', `${vp.name}-03-dashboard.png`),
        fullPage: true,
      });

      // 4. Inbound / Receiving Page - Sessions List
      await page.goto('/app/inbound');
      await page.waitForSelector('[data-testid="sessions-table"]', { timeout: 10000 });
      await page.screenshot({
        path: path.join(ARTIFACT_DIR, `${vp.name}-04-receiving-list.png`),
        fullPage: true,
      });

      // 4b. Inbound / Receiving Page - Session Detail
      const detailBtn = page.getByTestId('open-session-e2e-session-1').first();
      if (await detailBtn.isVisible()) {
        await detailBtn.click();
        await page.waitForSelector('[data-testid="session-detail-view"]', { timeout: 10000 });
        await page.waitForTimeout(400);
        await page.screenshot({
          path: path.join(ARTIFACT_DIR, `${vp.name}-04b-receiving-detail.png`),
          fullPage: true,
        });
      }

      // 5. Putaway Queue Tab
      await page.goto('/app/inbound/putaway');
      await page.waitForSelector('[data-testid="putaway-queue-view"]', { timeout: 10000 });
      await page.screenshot({
        path: path.join(ARTIFACT_DIR, `${vp.name}-05-putaway-queue.png`),
        fullPage: true,
      });

      // 6. Putaway RF Mode Tab
      const rfTab = page.getByTestId('tab-rf').first();
      if (await rfTab.isVisible()) {
        await rfTab.click();
        await page.waitForSelector('[data-testid="rf-execution-view"]', { timeout: 10000 });
        await page.waitForTimeout(400);
        await page.screenshot({
          path: path.join(ARTIFACT_DIR, `${vp.name}-06-putaway-rf.png`),
          fullPage: true,
        });
      }

      // 7. Inventory Balances Page
      await page.goto('/app/inventory');
      await page.waitForSelector('.stock-page-container', { timeout: 10000 });
      await page.waitForTimeout(400);
      await page.screenshot({
        path: path.join(ARTIFACT_DIR, `${vp.name}-07-inventory.png`),
        fullPage: true,
      });

      // 8. Clients Metadata List Page (Metadata Pilot)
      await page.goto('/app/admin/clients');
      await page.waitForSelector('[data-testid="entity-table"]', { timeout: 10000 });
      await page.waitForTimeout(400);
      await page.screenshot({
        path: path.join(ARTIFACT_DIR, `${vp.name}-08-clients-list.png`),
        fullPage: true,
      });

      // 9. Mobile Nav Drawer (Mobile Viewport Only)
      if (vp.name === 'mobile') {
        const navToggle = page.locator('[data-testid="mobile-nav-toggle"]');
        if (await navToggle.isVisible()) {
          await navToggle.click();
          await page.waitForSelector('.app-sidebar.mobile-open', { timeout: 5000 });
          await page.waitForTimeout(300);
          await page.screenshot({
            path: path.join(ARTIFACT_DIR, `${vp.name}-09-mobile-nav-drawer.png`),
            fullPage: false,
          });
          // Close drawer via Escape key
          await page.keyboard.press('Escape');
          await page.waitForTimeout(300);
        }
      }

      // 10. Clients Create Modal (Metadata-driven Form)
      const createBtn = page.locator('[data-testid="action-create"]');
      if (await createBtn.isVisible()) {
        await createBtn.click();
        await page.waitForSelector('[data-testid="modal-title"]', { timeout: 10000 });
        await page.waitForTimeout(400);
        const modalFileName =
          vp.name === 'mobile'
            ? 'mobile-10-clients-new-modal.png'
            : `${vp.name}-09-clients-new-modal.png`;
        await page.screenshot({
          path: path.join(ARTIFACT_DIR, modalFileName),
          fullPage: false,
        });
        await page.keyboard.press('Escape');
        await page.waitForTimeout(300);
      }
    });
  }
});
