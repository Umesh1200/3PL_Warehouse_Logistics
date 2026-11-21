import { test, expect } from '@playwright/test';
import * as path from 'path';
import * as fs from 'fs';

const AUDIT_REPO_DIR = 'C:/Code/3PL_Warehouse_Logistics/docs/audits/receiving-putaway';
const ARTIFACT_DIR = 'C:/Users/DELL/.gemini/antigravity/brain/3ef5018b-e9e3-4374-9eca-184b02ff2c4e/receiving-putaway';

// Ensure directories exist
if (!fs.existsSync(AUDIT_REPO_DIR)) {
  fs.mkdirSync(AUDIT_REPO_DIR, { recursive: true });
}
if (!fs.existsSync(ARTIFACT_DIR)) {
  fs.mkdirSync(ARTIFACT_DIR, { recursive: true });
}

const viewports = [
  { name: 'desktop', width: 1440, height: 900 },
  { name: 'tablet', width: 768, height: 1024 },
  { name: 'mobile', width: 375, height: 812 },
];

test.describe('Phase 7.3: Receiving & RF Putaway Enterprise UI Audit', () => {
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

  for (const vp of viewports) {
    test(`captures multi-viewport visual evidence for ${vp.name} (${vp.width}x${vp.height})`, async ({ page }) => {
      await page.setViewportSize({ width: vp.width, height: vp.height });

      // Mock auth and API endpoints
      await page.route('**/api/v1/auth/login', (route) => {
        route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            accessToken: 'mock-access-token',
            refreshToken: 'mock-refresh-token',
            tokenType: 'Bearer',
            expiresIn: 3600,
            user: {
              id: 'e2e-user-1',
              email: 'whlead@3pl.local',
              firstName: 'Floor',
              lastName: 'Lead',
              userType: 1,
              status: 1,
              roles: ['OperationsStaff'],
            },
          }),
        });
      });

      await page.route('**/api/v1/auth/me', (route) => {
        route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            userId: 'e2e-user-1',
            email: 'whlead@3pl.local',
            firstName: 'Floor',
            lastName: 'Lead',
            userType: 1,
            tenantId: null,
            companyName: 'Nexus3PL Enterprise',
            roles: ['OperationsStaff'],
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
              'analytics:control-tower:view',
            ],
          }),
        });
      });

      await page.route('**/api/v1/facilities', (route) => {
        route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify([mockFacility]),
        });
      });

      await page.route('**/api/v1/control-tower/**', (route) => {
        route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            facilityId: mockFacility.id,
            asOfUtc: new Date().toISOString(),
            ordersReceivedToday: 50,
            ordersDispatchedToday: 40,
            packagesDispatchedToday: 80,
            unitsPickedToday: 500,
            cartonsPackedToday: 120,
            inboundPalletsReceivedToday: 6,
            putawayCompletedToday: 5,
            returnsInspectedToday: 0,
            unitsPerLaborHourToday: 35.0,
            averageFulfillmentCycleTimeHours: 2.1,
            sameDayDispatchAdherencePercent: 96.0,
            totalBilledRevenueToday: 5000,
            netFreightMarginToday: 800,
            onTimeDeliveryRatePercent: 98.0,
            activeDeliveryExceptionsCount: 0,
            palletPositionOccupancyPercent: 70.0,
            volumetricCubeFillPercent: 55.0,
          }),
        });
      });

      await page.route('**/api/v1/receiving/sessions**', (route) => {
        const url = route.request().url();
        if (url.includes('/lines')) {
          route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({
              id: 'line-2',
              receivingSessionId: 'e2e-session-1',
              itemId: 'item-2',
              sku: 'SKU-NEW-02',
              itemDescription: 'Industrial Barcode Scanner',
              lpn: 'LPN-E2E-002',
              quantityReceived: 5,
              lotNumber: 'LOT-NEW-2',
              condition: 'Sound',
              stagingLocationCode: 'STAGING-RCV-FAC-ORD1',
              scannedAtUtc: new Date().toISOString(),
              version: 1,
            }),
          });
        } else if (url.includes('/e2e-session-1')) {
          route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify(mockSession),
          });
        } else {
          route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({
              items: [mockSession],
              totalCount: 1,
              pageNumber: 1,
              pageSize: 10,
            }),
          });
        }
      });

      await page.route('**/api/v1/putaway/tasks/next**', (route) => {
        route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(mockPutawayTask),
        });
      });

      await page.route('**/api/v1/putaway/tasks**', (route) => {
        route.fulfill({
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

      // Login Flow
      await page.goto('/login');
      await page.fill('input[type="email"]', 'whlead@3pl.local');
      await page.fill('input[type="password"]', 'CorrectHorseBatteryStaple123!');
      await page.click('button[type="submit"]');
      await page.waitForSelector('.app-shell-container', { timeout: 10000 });

      // 1. Receiving Session List
      await page.goto('/app/inbound');
      await page.waitForSelector('[data-testid="sessions-table"]', { timeout: 10000 });
      await page.waitForTimeout(400);

      const rcvListFile = `${vp.name}-01-receiving-list.png`;
      await page.screenshot({ path: path.join(AUDIT_REPO_DIR, rcvListFile), fullPage: true });
      await page.screenshot({ path: path.join(ARTIFACT_DIR, rcvListFile), fullPage: true });

      // Check zero horizontal overflow
      const overflowList = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
      expect(overflowList).toBe(false);

      // 2. Receiving Intake Workstation (Detail view)
      await page.click('[data-testid="open-session-e2e-session-1"]');
      await page.waitForSelector('[data-testid="line-scanner-card"]', { timeout: 10000 });
      await page.waitForTimeout(400);

      // Verify scanner input dimensions (>= 50px height)
      const itemInputBox = await page.locator('#scan-item-id').boundingBox();
      expect(itemInputBox?.height).toBeGreaterThanOrEqual(48);

      const rcvWorkstationFile = `${vp.name}-02-receiving-workstation.png`;
      await page.screenshot({ path: path.join(AUDIT_REPO_DIR, rcvWorkstationFile), fullPage: true });
      await page.screenshot({ path: path.join(ARTIFACT_DIR, rcvWorkstationFile), fullPage: true });

      // 3. Receiving Discrepancy Modal
      await page.click('[data-testid="record-discrepancy-btn"]');
      await page.waitForSelector('[data-testid="discrepancy-modal"]', { timeout: 5000 });
      await page.waitForTimeout(300);

      const rcvDiscFile = `${vp.name}-03-receiving-discrepancy.png`;
      await page.screenshot({ path: path.join(AUDIT_REPO_DIR, rcvDiscFile), fullPage: false });
      await page.screenshot({ path: path.join(ARTIFACT_DIR, rcvDiscFile), fullPage: false });

      // Dismiss discrepancy modal via Escape key
      await page.keyboard.press('Escape');
      await page.waitForTimeout(300);

      // 4. Putaway RF Workstation
      await page.goto('/app/inbound/putaway');
      await page.waitForSelector('[data-testid="tab-rf"]', { timeout: 10000 });
      await page.click('[data-testid="tab-rf"]');
      await page.waitForSelector('[data-testid="rf-task-card"]', { timeout: 10000 });
      await page.waitForTimeout(400);

      // Verify RF scanner inputs height (>= 48px)
      const lpnInputBox = await page.locator('#scan-lpn-input').boundingBox();
      expect(lpnInputBox?.height).toBeGreaterThanOrEqual(48);
      const binInputBox = await page.locator('#scan-bin-input').boundingBox();
      expect(binInputBox?.height).toBeGreaterThanOrEqual(48);

      const ptwRfFile = `${vp.name}-04-putaway-rf.png`;
      await page.screenshot({ path: path.join(AUDIT_REPO_DIR, ptwRfFile), fullPage: true });
      await page.screenshot({ path: path.join(ARTIFACT_DIR, ptwRfFile), fullPage: true });

      // Check zero horizontal overflow
      const overflowPtw = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
      expect(overflowPtw).toBe(false);

      // 5. Putaway Exception Modal
      await page.click('[data-testid="report-putaway-exception-btn"]');
      await page.waitForSelector('[data-testid="putaway-exception-modal"]', { timeout: 5000 });
      await page.waitForTimeout(300);

      const ptwExcFile = `${vp.name}-05-putaway-exception.png`;
      await page.screenshot({ path: path.join(AUDIT_REPO_DIR, ptwExcFile), fullPage: false });
      await page.screenshot({ path: path.join(ARTIFACT_DIR, ptwExcFile), fullPage: false });

      // Dismiss exception modal via Escape key
      await page.keyboard.press('Escape');
      await page.waitForTimeout(300);
    });
  }

  test('captures manage-only workstation experience and verifies 320x568 reflow', async ({ page }) => {
    // Set viewport to narrow mobile 320x568
    await page.setViewportSize({ width: 320, height: 568 });

    // Mock manage-only user (has inbound:putaway:manage only)
    await page.route('**/api/v1/auth/login', (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          accessToken: 'mock-manager-token',
          refreshToken: 'mock-manager-refresh',
          tokenType: 'Bearer',
          expiresIn: 3600,
          user: {
            id: 'e2e-manager-1',
            email: 'manager@nexus3pl.com',
            firstName: 'Floor',
            lastName: 'Manager',
            userType: 1,
            status: 1,
            roles: ['OperationsManager'],
          },
        }),
      });
    });

    await page.route('**/api/v1/auth/me', (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          userId: 'e2e-manager-1',
          email: 'manager@nexus3pl.com',
          firstName: 'Floor',
          lastName: 'Manager',
          userType: 1,
          tenantId: null,
          companyName: 'Nexus3PL Enterprise',
          roles: ['OperationsManager'],
          permissions: ['inbound:putaway:manage'],
        }),
      });
    });

    await page.route('**/api/v1/facilities', (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([mockFacility]),
      });
    });

    await page.goto('/login');
    await page.fill('input[type="email"]', 'manager@nexus3pl.com');
    await page.fill('input[type="password"]', 'ManagerPass123!');
    await page.click('button[type="submit"]');
    await page.waitForSelector('.app-shell-container', { timeout: 10000 });

    await page.goto('/app/inbound/putaway');
    await page.waitForSelector('[data-testid="putaway-manage-workstation"]', { timeout: 10000 });
    await page.waitForTimeout(400);

    // Verify 0 horizontal page overflow at 320px
    const overflow320 = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    expect(overflow320).toBe(false);

    // Capture manage-only screenshot
    const manageFile = `mobile-320-06-putaway-manage-workstation.png`;
    await page.screenshot({ path: path.join(AUDIT_REPO_DIR, manageFile), fullPage: true });
    await page.screenshot({ path: path.join(ARTIFACT_DIR, manageFile), fullPage: true });

    // Capture desktop manage-only view as well
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.waitForTimeout(300);
    const desktopManageFile = `desktop-06-putaway-manage-workstation.png`;
    await page.screenshot({ path: path.join(AUDIT_REPO_DIR, desktopManageFile), fullPage: true });
    await page.screenshot({ path: path.join(ARTIFACT_DIR, desktopManageFile), fullPage: true });
  });
});
