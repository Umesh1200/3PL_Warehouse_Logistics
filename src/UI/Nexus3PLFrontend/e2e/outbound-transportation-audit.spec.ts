import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const AUDIT_REPO_DIR = path.resolve(
  __dirname,
  '../../../../docs/audits/outbound-transportation'
);

if (!fs.existsSync(AUDIT_REPO_DIR)) {
  fs.mkdirSync(AUDIT_REPO_DIR, { recursive: true });
}

const TINY_PNG_BASE64 =
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=';

async function shot(page: Page, filename: string) {
  await page.screenshot({
    path: path.join(AUDIT_REPO_DIR, filename),
    fullPage: true,
    animations: 'disabled',
  });
}

async function assertNoOverflow(page: Page, width: number, height: number) {
  await page.setViewportSize({ width, height });
  await page.waitForTimeout(120);
  const metrics = await page.evaluate(() => {
    const scrollW = document.documentElement.scrollWidth;
    const innerW = window.innerWidth;
    const maxCardRight = Array.from(document.querySelectorAll('.card, .outbound-card')).reduce(
      (max, el) => Math.max(max, el.getBoundingClientRect().right),
      0
    );
    return { scrollW, innerW, maxCardRight };
  });
  expect(metrics.scrollW).toBeLessThanOrEqual(metrics.innerW + 1);
  expect(metrics.maxCardRight).toBeLessThanOrEqual(metrics.innerW + 1);
}

async function assertVisibleFocusOutlineOnTab(page: Page) {
  for (let i = 0; i < 4; i++) {
    await page.keyboard.press('Tab');
    const style = await page.evaluate(() => {
      const el = document.activeElement;
      if (!el || el === document.body) return null;
      const cs = window.getComputedStyle(el);
      return {
        outlineWidth: parseFloat(cs.outlineWidth) || 0,
        boxShadow: cs.boxShadow,
      };
    });
    if (style) {
      expect(style.outlineWidth > 0 || style.boxShadow !== 'none').toBe(true);
    }
  }
}

async function assertDialogFocusTrapAndEscape(page: Page, dialogSelector: string) {
  const dialog = page.locator(dialogSelector);
  await expect(dialog).toBeVisible();
  await expect(dialog).toHaveAttribute('role', 'dialog');
  await expect(dialog).toHaveAttribute('aria-modal', 'true');

  // Verify Tab keeps focus confined inside the dialog
  for (let i = 0; i < 6; i++) {
    await page.keyboard.press('Tab');
    const insideDialog = await dialog.evaluate(
      (el) => el === document.activeElement || el.contains(document.activeElement)
    );
    expect(insideDialog).toBe(true);
  }

  await page.keyboard.press('Escape');
  await expect(dialog).toHaveCount(0);
}

test.describe('Phase 7.4.6: Consolidated Outbound & Transportation Visual, A11y & Reflow Audit', () => {
  test('1. Outbound Orders & Pick Waves: error/retry/empty states, drawer a11y/Escape/Tab trap, and 4 viewports', async ({
    page,
  }) => {
    const consoleErrors: string[] = [];
    page.on('pageerror', (err) => consoleErrors.push(err.message));

    const facility = {
      id: 'fac-audit-1',
      facilityCode: 'FAC-CHI1',
      name: 'Chicago Central Hub',
      city: 'Chicago',
      timezone: 'America/Chicago',
      isActive: true,
    };

    let ordersMode: 'error' | 'empty' | 'loaded' = 'error';
    const orderRecord: any = {
      id: 'ord-audit-1',
      clientId: 'cli-audit-1',
      facilityId: facility.id,
      facilityName: facility.name,
      customerName: 'Apex Industrial Corp',
      orderNumber: 'SO-2026-AUDIT-01',
      externalOrderReference: 'PO-746-ALPHA',
      orderType: 'B2BSalesOrder',
      status: 'Released',
      priority: 8,
      serviceLevel: 'StandardGround',
      carrierCode: 'SIM-FEDEX',
      orderDateUtc: '2026-11-24T08:00:00Z',
      requiredShipDateUtc: '2026-11-26T17:00:00Z',
      version: 1,
      createdAt: '2026-11-24T08:00:00Z',
      shippingAddress: {
        recipientName: 'Morgan Stark',
        companyName: 'Apex Industrial Corp',
        addressLine1: '500 Industrial Pkwy',
        city: 'Chicago',
        stateOrProvince: 'IL',
        postalCode: '60607',
        countryCode: 'USA',
      },
      lines: [
        {
          id: 'ol-audit-1',
          outboundOrderId: 'ord-audit-1',
          itemId: 'item-audit-1',
          sku: 'SKU-PUMP-741',
          itemDescription: 'Hydraulic Valve Assembly',
          lineNumber: 1,
          quantityOrdered: 12,
          quantityAllocated: 0,
          quantityShipped: 0,
          remainingDemand: 12,
          status: 'Pending',
        },
      ],
      statusHistories: [],
    };

    const waves: any[] = [
      {
        id: 'wave-audit-1',
        clientId: 'cli-audit-1',
        facilityId: facility.id,
        waveNumber: 'WAVE-20261124-01',
        waveType: 'DiscreteOrder',
        status: 'Released',
        priority: 8,
        ordersCount: 1,
        tasksCount: 1,
        totalQuantityToPick: 12,
        totalQuantityPicked: 0,
        totalOrdersCount: 1,
        totalLinesCount: 1,
        totalUnitsCount: 12,
        pickedUnitsCount: 0,
        completionPercentage: 0,
        estimatedTravelDistanceMeters: 84.5,
        assignedPickerCount: 1,
        releasedAtUtc: '2026-11-24T09:15:00Z',
        createdAt: '2026-11-24T09:00:00Z',
        waveOrders: [
          {
            id: 'wo-audit-1',
            outboundOrderId: 'ord-audit-1',
            orderNumber: 'SO-2026-AUDIT-01',
            customerName: 'Apex Industrial Corp',
            orderStatus: 'Allocated',
            priority: 8,
          },
        ],
        tasks: [
          {
            id: 'pt-audit-w1',
            pickWaveId: 'wave-audit-1',
            outboundOrderId: 'ord-audit-1',
            orderNumber: 'SO-2026-AUDIT-01',
            outboundOrderLineId: 'ol-audit-1',
            inventoryAllocationId: 'alloc-audit-1',
            stockOnHandId: 'stock-audit-1',
            itemId: 'item-audit-1',
            sku: 'SKU-PUMP-741',
            itemDescription: 'Hydraulic Valve Assembly',
            sourceLocationId: 'loc-audit-1',
            sourceLocationCode: 'A-04-02',
            quantityToPick: 12,
            quantityPicked: 0,
            shortPickQuantity: 0,
            pickSequence: 1,
            status: 'Pending',
            version: 1,
          },
        ],
      },
    ];

    await page.addInitScript(() => {
      localStorage.setItem('nexus_access_token', 'audit-tok-orders');
      localStorage.setItem('nexus_refresh_token', 'audit-ref-orders');
      localStorage.setItem('nexus_active_facility_id', 'fac-audit-1');
    });

    await page.route('**/api/v1/**', async (route) => {
      const pathName = new URL(route.request().url()).pathname;
      const method = route.request().method();

      if (pathName === '/api/v1/auth/me') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            userId: 'ops-audit-1',
            email: 'ops@nexus3pl.local',
            firstName: 'Ops',
            lastName: 'Lead',
            userType: 'Staff',
            assignedFacilityId: facility.id,
            roles: ['OperationsManager'],
            permissions: [
              'outbound:orders:view',
              'outbound:orders:create',
              'outbound:orders:import',
              'outbound:orders:allocate',
              'outbound:orders:manage',
              'outbound:waves:view',
              'outbound:waves:manage',
              'facilities:view',
            ],
          }),
        });
      }

      if (pathName === '/api/v1/facilities') {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([facility]) });
      }

      if (pathName === '/api/v1/outbound/orders' && method === 'GET') {
        if (ordersMode === 'error') {
          return route.fulfill({
            status: 500,
            contentType: 'application/json',
            body: JSON.stringify({ detail: 'Transient orders API failure' }),
          });
        }
        if (ordersMode === 'empty') {
          return route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({ items: [], totalCount: 0, pageNumber: 1, pageSize: 15 }),
          });
        }
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ items: [orderRecord], totalCount: 1, pageNumber: 1, pageSize: 15 }),
        });
      }

      if (pathName === '/api/v1/outbound/orders/ord-audit-1' && method === 'GET') {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(orderRecord) });
      }

      if (pathName === '/api/v1/outbound/orders/ord-audit-1/allocate' && method === 'POST') {
        orderRecord.status = 'Allocated';
        orderRecord.lines[0].quantityAllocated = 12;
        orderRecord.lines[0].remainingDemand = 0;
        orderRecord.lines[0].status = 'Allocated';
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            outboundOrderId: orderRecord.id,
            orderNumber: orderRecord.orderNumber,
            status: 'Allocated',
            totalQuantityOrdered: 12,
            totalQuantityAllocated: 12,
            totalShortageQuantity: 0,
            isFullyAllocated: true,
            isDuplicateRequest: false,
            attemptCount: 1,
            lineSummaries: [
              {
                lineId: orderRecord.lines[0].id,
                lineNumber: 1,
                itemId: 'item-audit-1',
                sku: 'SKU-PUMP-741',
                quantityOrdered: 12,
                quantityAllocated: 12,
                remainingDemand: 0,
                status: 'Allocated',
                allocations: [
                  {
                    allocationId: 'alloc-audit-1',
                    stockOnHandId: 'stock-audit-1',
                    locationId: 'loc-audit-1',
                    locationCode: 'A-04-02',
                    quantityAllocated: 12,
                    status: 'Allocated',
                  },
                ],
              },
            ],
            shortages: [],
          }),
        });
      }

      if (pathName === '/api/v1/outbound/pick-waves' && method === 'GET') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ items: waves, totalCount: 1, pageNumber: 1, pageSize: 15 }),
        });
      }

      if (pathName === '/api/v1/outbound/pick-waves/wave-audit-1' && method === 'GET') {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(waves[0]) });
      }

      return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ items: [], totalCount: 0 }) });
    });

    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('/app/outbound/orders');

    // Error state + Retry -> Empty state -> Refresh -> Loaded state
    await expect(page.getByTestId('orders-error-banner')).toContainText('Transient orders API failure');
    await page.waitForLoadState('networkidle');
    ordersMode = 'empty';
    await page.getByTestId('orders-error-banner').getByRole('button', { name: 'Retry' }).click();
    await expect(page.getByTestId('orders-empty')).toBeVisible();
    ordersMode = 'loaded';
    await page.getByRole('button', { name: 'Refresh' }).click();
    await expect(page.getByTestId('outbound-orders-table')).toBeVisible();

    await assertVisibleFocusOutlineOnTab(page);

    // Open OrderDetailDrawer, allocate, test Tab focus trap & Escape close
    await page.getByTestId('inspect-order-ord-audit-1').click();
    await page.getByTestId('btn-allocate-order').click();
    await expect(page.getByTestId('allocation-result-panel')).toBeVisible();
    await assertDialogFocusTrapAndEscape(page, '[data-testid="order-detail-drawer"]');

    // P746-N01: Ensure screenshot destination is portable and repository-relative
    expect(path.isAbsolute(AUDIT_REPO_DIR)).toBe(true);
    expect(AUDIT_REPO_DIR.replace(/\\/g, '/')).toMatch(/\/docs\/audits\/outbound-transportation$/);

    // P746-N02: Desktop layout hides the mobile-only scroll hint cleanly
    await expect(page.getByTestId('outbound-orders-scroll-hint')).toBeHidden();
    await shot(page, 'desktop-01-outbound-orders.png');
    await assertNoOverflow(page, 768, 1024);
    await shot(page, 'tablet-01-outbound-orders.png');
    await assertNoOverflow(page, 375, 812);
    await shot(page, 'mobile-01-outbound-orders.png');
    await assertNoOverflow(page, 320, 568);

    // P746-N02: At 320px, OutboundOrdersPage exposes a visible, accessible, keyboard-focusable horizontal scroll hint without page overflow
    const ordersHint = page.getByTestId('outbound-orders-scroll-hint');
    const ordersRegion = page.getByTestId('outbound-orders-scroll-region');
    await expect(ordersHint).toBeVisible();
    await expect(ordersRegion).toHaveAttribute('role', 'region');
    await expect(ordersRegion).toHaveAttribute('tabindex', '0');
    await expect(ordersRegion).toHaveAttribute('aria-label', 'Outbound orders table');
    const hintId = await ordersHint.getAttribute('id');
    expect(hintId).toBeTruthy();
    await expect(ordersRegion).toHaveAttribute('aria-describedby', hintId!);
    const hintBox = await ordersHint.boundingBox();
    expect(hintBox).not.toBeNull();
    expect(hintBox!.width).toBeGreaterThan(0);
    expect(hintBox!.x + hintBox!.width).toBeLessThanOrEqual(320);

    await ordersRegion.focus();
    await expect(ordersRegion).toBeFocused();
    const regionScrollMetrics = await ordersRegion.evaluate((el) => {
      const cs = window.getComputedStyle(el);
      return {
        scrollWidth: el.scrollWidth,
        clientWidth: el.clientWidth,
        outlineWidth: parseFloat(cs.outlineWidth) || 0,
        boxShadow: cs.boxShadow,
      };
    });
    expect(regionScrollMetrics.scrollWidth).toBeGreaterThan(regionScrollMetrics.clientWidth);
    expect(regionScrollMetrics.outlineWidth > 0 || regionScrollMetrics.boxShadow !== 'none').toBe(true);
    await page.keyboard.press('ArrowRight');
    await expect.poll(() => ordersRegion.evaluate((el) => el.scrollLeft)).toBeGreaterThan(0);
    await ordersRegion.evaluate((el) => {
      el.scrollLeft = 0;
    });

    await shot(page, 'mobile-320-01-outbound-orders.png');

    // Pick Waves Desktop + 320px scroll hint verification
    await assertNoOverflow(page, 1440, 900);
    await page.goto('/app/outbound/waves');
    await page.getByTestId('inspect-wave-wave-audit-1').click();
    await expect(page.getByRole('heading', { name: 'Wave WAVE-20261124-01' })).toBeVisible();
    await expect(page.getByTestId('pick-waves-scroll-hint')).toBeHidden();
    await shot(page, 'desktop-02-pick-waves.png');
    await assertNoOverflow(page, 768, 1024);
    await assertNoOverflow(page, 375, 812);
    await assertNoOverflow(page, 320, 568);
    const wavesHint = page.getByTestId('pick-waves-scroll-hint');
    const wavesRegion = page.getByTestId('pick-waves-scroll-region');
    await expect(wavesHint).toBeVisible();
    const wavesHintId = await wavesHint.getAttribute('id');
    expect(wavesHintId).toBeTruthy();
    await expect(wavesRegion).toHaveAttribute('role', 'region');
    await expect(wavesRegion).toHaveAttribute('tabindex', '0');
    await expect(wavesRegion).toHaveAttribute('aria-describedby', wavesHintId!);

    expect(consoleErrors).toEqual([]);
  });

  test('2. RF Picking Workstation: error/retry/claim/short-pick dialog trap & Escape + 4 viewports', async ({ page }) => {
    const consoleErrors: string[] = [];
    page.on('pageerror', (err) => consoleErrors.push(err.message));

    const facility = {
      id: 'fac-audit-2',
      facilityCode: 'FAC-CHI1',
      name: 'Chicago Central Hub',
      city: 'Chicago',
      timezone: 'America/Chicago',
      isActive: true,
    };

    let nextShouldError = true;
    let taskState: any = {
      id: 'pt-audit-1',
      pickWaveId: 'wave-audit-1',
      outboundOrderId: 'ord-audit-1',
      orderNumber: 'SO-2026-AUDIT-01',
      outboundOrderLineId: 'line-audit-1',
      inventoryAllocationId: 'alloc-audit-1',
      stockOnHandId: 'stock-audit-1',
      itemId: 'item-audit-1',
      sku: 'SKU-MOTOR-742',
      itemDescription: 'Servo Drive Motor Assembly',
      sourceLocationId: 'loc-audit-1',
      sourceLocationCode: 'A-04-02',
      lotId: 'lot-audit-1',
      lotNumber: 'LOT-742-A',
      expectedLpnCode: 'LPN-PAL-742',
      targetToteBarcode: 'TOTE-742',
      quantityToPick: 10,
      quantityPicked: 0,
      shortPickQuantity: 0,
      pickSequence: 1,
      status: 'Pending',
      assignedUserId: null,
      version: 1,
    };

    await page.addInitScript(() => {
      localStorage.setItem('nexus_access_token', 'audit-tok-pick');
      localStorage.setItem('nexus_refresh_token', 'audit-ref-pick');
      localStorage.setItem('nexus_active_facility_id', 'fac-audit-2');
    });

    await page.route('**/api/v1/**', async (route) => {
      const pathName = new URL(route.request().url()).pathname;
      const method = route.request().method();

      if (pathName === '/api/v1/auth/me') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            userId: 'picker-audit-1',
            email: 'picker@nexus3pl.local',
            firstName: 'Pat',
            lastName: 'Picker',
            userType: 'Staff',
            assignedFacilityId: facility.id,
            roles: ['WarehouseOperator'],
            permissions: ['outbound:pick'],
          }),
        });
      }

      if (pathName === '/api/v1/facilities') {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([facility]) });
      }

      if (pathName === '/api/v1/outbound/pick-tasks/next' && method === 'GET') {
        if (nextShouldError) {
          return route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ detail: 'RF engine timeout' }) });
        }
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(taskState) });
      }

      if (pathName === '/api/v1/outbound/pick-tasks/pt-audit-1/claim' && method === 'POST') {
        taskState = { ...taskState, status: 'Assigned', assignedUserId: 'picker-audit-1', version: 2 };
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(taskState) });
      }

      return route.fulfill({ status: 404, body: '{}' });
    });

    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('/app/outbound/picking');

    await expect(page.getByTestId('rf-error-banner')).toBeVisible();
    nextShouldError = false;
    await page.getByTestId('rf-retry-next-btn').click();
    await expect(page.getByTestId('rf-claim-gate')).toBeVisible();
    await page.getByTestId('claim-pick-task-btn').click();
    await expect(page.getByText('Claimed by You')).toBeVisible();

    await page.fill('[data-testid="rf-scan-location"]', 'A-04-02');
    await page.fill('[data-testid="rf-scan-item"]', 'SKU-MOTOR-742');
    await page.fill('[data-testid="rf-scan-tote"]', 'TOTE-742');
    await shot(page, 'desktop-03-picking-rf.png');

    // Open Short-Pick Modal, capture screenshot, verify focus trap + Escape
    await page.getByTestId('open-short-pick-btn').click();
    await expect(page.getByTestId('short-pick-form')).toBeVisible();
    await shot(page, 'desktop-04-short-pick-modal.png');
    await assertDialogFocusTrapAndEscape(page, '.modal-container');

    await assertNoOverflow(page, 768, 1024);
    await shot(page, 'tablet-02-picking-rf.png');

    await assertNoOverflow(page, 375, 812);
    const confirmBox = await page.getByTestId('confirm-pick-btn').boundingBox();
    expect(confirmBox).not.toBeNull();
    expect(confirmBox!.height).toBeGreaterThanOrEqual(44);
    await shot(page, 'mobile-02-picking-rf.png');

    await assertNoOverflow(page, 320, 568);
    await shot(page, 'mobile-320-02-picking-rf.png');

    expect(consoleErrors).toEqual([]);
  });

  test('3. Packing Workbench, 3D Cartonization & Carrier Label Preview across 4 viewports', async ({ page }) => {
    const consoleErrors: string[] = [];
    page.on('pageerror', (err) => consoleErrors.push(err.message));

    const facility = {
      id: 'fac-audit-3',
      facilityCode: 'FAC-CHI1',
      name: 'Chicago Packing Hub',
      city: 'Chicago',
      timezone: 'America/Chicago',
      isActive: true,
    };

    const pkg = {
      id: 'pkg-audit-3',
      clientId: 'cli-audit-3',
      facilityId: facility.id,
      outboundOrderId: 'ord-audit-3',
      orderNumber: 'SO-2026-PACK-743',
      shipmentId: null,
      handlingUnitId: 'hu-audit-3',
      handlingUnitLpn: 'CTN-SO-2026-PACK-743-01',
      packagingMaterialId: 'mat-med',
      packageNumber: 'PKG-SO-2026-PACK-743-01',
      cartonSequence: 1,
      grossWeightKg: 6.45,
      tareWeightKg: 0.45,
      netWeightKg: 6.0,
      lengthCm: 40,
      widthCm: 30,
      heightCm: 25,
      volumetricWeightKg: 6.0,
      carrierCode: 'SIM-UPS',
      trackingNumber: '1Z-SIM-743-9901',
      status: 'LabelPrinted',
      hasActiveLabel: true,
      latestLabelId: 'lbl-audit-3',
    };

    const sessionDetail = {
      id: 'sess-audit-3',
      clientId: 'cli-audit-3',
      facilityId: facility.id,
      packingStationLocationId: 'stn-1',
      packingStationLocationCode: 'PACK-STN-01',
      outboundOrderId: 'ord-audit-3',
      orderNumber: 'SO-2026-PACK-743',
      operatorUserId: 'packer-audit-1',
      operatorEmail: 'packer@nexus3pl.local',
      status: 'Active',
      startedAtUtc: '2026-11-24T05:00:00Z',
      completedAtUtc: null,
      activePackagesCount: 1,
      orderLines: [
        {
          orderLineId: 'ol-1',
          itemId: 'item-1',
          sku: 'SKU-ACTUATOR-743',
          description: 'Precision Linear Actuator',
          quantityOrdered: 4,
          quantityAllocated: 4,
          quantityPicked: 4,
          quantityPacked: 4,
          remainingToPack: 0,
          lineStatus: 'Completed',
        },
      ],
      packages: [pkg],
      pickedTotes: [{ handlingUnitId: 'hu-t1', lpnCode: 'TOTE-743-E2E', status: 'Available', locationCode: 'PACK-STN-01', totalQuantity: 0 }],
    };

    await page.addInitScript(() => {
      localStorage.setItem('nexus_access_token', 'audit-tok-pack');
      localStorage.setItem('nexus_refresh_token', 'audit-ref-pack');
      localStorage.setItem('nexus_active_facility_id', 'fac-audit-3');
    });

    await page.route('**/api/v1/**', async (route) => {
      const pathName = new URL(route.request().url()).pathname;
      if (pathName === '/api/v1/auth/me') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            userId: 'packer-audit-1',
            email: 'packer@nexus3pl.local',
            firstName: 'Pat',
            lastName: 'PackLead',
            userType: 'Staff',
            assignedFacilityId: facility.id,
            roles: ['PackAndShipLead'],
            permissions: ['outbound:pack', 'outbound:ship'],
          }),
        });
      }
      if (pathName === '/api/v1/facilities') {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([facility]) });
      }
      if (pathName === '/api/v1/outbound/packing/stations') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify([{ id: 'stn-1', locationCode: 'PACK-STN-01', barcode: 'PACK-STN-01', aisleCode: 'PK', isActive: true }]),
        });
      }
      if (pathName === '/api/v1/outbound/packing/packaging-materials') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify([
            {
              id: 'mat-med',
              facilityId: facility.id,
              code: 'BOX-MED-01',
              name: 'Medium Corrugated Box',
              materialType: 'CorrugatedBox',
              lengthCm: 40,
              widthCm: 30,
              heightCm: 25,
              maxWeightKg: 20,
              tareWeightKg: 0.45,
              costPerUnit: 1.25,
            },
          ]),
        });
      }
      if (pathName === '/api/v1/outbound/packing/packable-orders') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ items: [], totalCount: 0, pageNumber: 1, pageSize: 50 }),
        });
      }
      if (pathName === '/api/v1/outbound/packing/sessions') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ items: [sessionDetail], totalCount: 1, pageNumber: 1, pageSize: 25 }),
        });
      }
      if (pathName === '/api/v1/outbound/packing/sessions/sess-audit-3') {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(sessionDetail) });
      }
      if (pathName === '/api/v1/outbound/packing/cartonization/recommend') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            isSuccess: true,
            recommendedPackageCount: 1,
            totalEstimatedGrossWeightKg: 6.45,
            totalEstimatedVolumeCm3: 4800,
            message: "Recommended 1 carton: 'BOX-MED-01'.",
            warnings: [],
            recommendedPackages: [
              {
                packageIndex: 1,
                packagingMaterialId: 'mat-med',
                packagingMaterialCode: 'BOX-MED-01',
                packagingMaterialName: 'Medium Corrugated Box',
                materialType: 'CorrugatedBox',
                boxLengthCm: 40,
                boxWidthCm: 30,
                boxHeightCm: 25,
                boxMaxWeightKg: 20,
                tareWeightKg: 0.45,
                estimatedNetWeightKg: 6,
                estimatedGrossWeightKg: 6.45,
                estimatedVolumeCm3: 4800,
                boxCapacityVolumeCm3: 30000,
                volumetricWeightKg: 6,
                volumeUtilizationPercentage: 42.5,
                items: [{ itemId: 'item-1', sku: 'SKU-ACTUATOR-743', quantity: 4, totalWeightKg: 6, totalVolumeCm3: 4800 }],
              },
            ],
          }),
        });
      }
      if (pathName === '/api/v1/outbound/shipping/rates') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify([
            { carrierCode: 'SIM-UPS', carrierName: 'UPS', serviceLevel: 'SecondDayAir', rateAmount: 24.5, currency: 'USD', estimatedTransitDays: 2 },
            { carrierCode: 'SIM-FEDEX', carrierName: 'FedEx', serviceLevel: 'StandardGround', rateAmount: 14.2, currency: 'USD', estimatedTransitDays: 4 },
          ]),
        });
      }
      if (pathName === '/api/v1/outbound/shipping/labels/lbl-audit-3') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'lbl-audit-3',
            packageId: pkg.id,
            packageNumber: pkg.packageNumber,
            carrierCode: 'SIM-UPS',
            serviceLevel: 'SecondDayAir',
            trackingNumber: '1Z-SIM-743-9901',
            labelFormat: 'ZPL',
            routingCode: 'UPS-2DA',
            labelData: '^XA^FO50,50^ADN,36,20^FD1Z-SIM-743-9901^FS^XZ',
            status: 'Printed',
            shippingCostAmount: 24.5,
            currency: 'USD',
            generatedAtUtc: '2026-11-24T05:12:00Z',
          }),
        });
      }
      return route.fulfill({ status: 404, body: '{}' });
    });

    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('/app/outbound/packing');
    await expect(page.getByTestId('packing-workbench-home')).toBeVisible();
    await page.getByTestId('open-session-btn-sess-audit-3').click();
    await expect(page.getByTestId('packing-session-workbench')).toBeVisible();
    await page.getByTestId('recommend-cartonization-btn').click();
    await expect(page.getByTestId('carton-rec-card-1')).toBeVisible();
    await page.getByTestId('quote-rates-btn').click();
    await expect(page.getByTestId('rate-quotes-list')).toBeVisible();
    await page.getByTestId('preview-label-btn').click();
    await expect(page.getByTestId('label-payload-preview')).toContainText('1Z-SIM-743-9901');

    await shot(page, 'desktop-05-packing-labels.png');
    await assertNoOverflow(page, 768, 1024);
    await shot(page, 'tablet-03-packing-labels.png');
    await assertNoOverflow(page, 375, 812);
    await shot(page, 'mobile-03-packing-labels.png');
    await assertNoOverflow(page, 320, 568);
    await shot(page, 'mobile-320-03-packing-labels.png');

    expect(consoleErrors).toEqual([]);
  });

  test('4. Shipment Consolidation & Immutable BOL Modal (Tab trap + Escape) across 4 viewports', async ({ page }) => {
    const consoleErrors: string[] = [];
    page.on('pageerror', (err) => consoleErrors.push(err.message));

    const facility = {
      id: 'fac-audit-4',
      facilityCode: 'FAC-CHI1',
      name: 'Chicago Freight Hub',
      city: 'Chicago',
      timezone: 'America/Chicago',
      isActive: true,
    };

    const shipment = {
      id: 'shp-audit-4',
      clientId: 'cli-audit-4',
      clientName: 'Apex Industrial Corp',
      facilityId: facility.id,
      shipmentNumber: 'SHP-FEDEX-20261124-001',
      carrierCode: 'SIM-FEDEX',
      carrierServiceLevel: 'StandardGround',
      status: 'Dispatched',
      stagingLocationId: 'loc-stg-01',
      stagingLocationCode: 'STG-OUT-01',
      assignedDriverUserId: 'drv-audit-4',
      custodyStatus: 'Confirmed',
      trailerNumber: 'TRL-744-01',
      sealNumber: 'SEAL-744-01',
      billOfLadingId: 'bol-audit-4',
      billOfLadingNumber: 'BOL-SHP-FEDEX-20261124-001',
      totalPackagesCount: 2,
      totalWeightKg: 13.0,
      totalVolumeCm3: 42000,
      dispatchedAtUtc: '2026-11-24T10:00:00Z',
      packages: [
        { packageId: 'p1', packageNumber: 'PKG-744-001', trackingNumber: 'FDX-744-9901', carrierCode: 'SIM-FEDEX', grossWeightKg: 8.4, status: 'Loaded', outboundOrderId: 'o1', orderNumber: 'SO-2026-744-1', currentLocationCode: 'DOCK-DOOR-02' },
        { packageId: 'p2', packageNumber: 'PKG-744-002', trackingNumber: 'FDX-744-9902', carrierCode: 'SIM-FEDEX', grossWeightKg: 4.6, status: 'Loaded', outboundOrderId: 'o1', orderNumber: 'SO-2026-744-1', currentLocationCode: 'DOCK-DOOR-02' },
      ],
    };

    const bol = {
      id: 'bol-audit-4',
      shipmentId: shipment.id,
      bolNumber: shipment.billOfLadingNumber,
      carrierCode: 'SIM-FEDEX',
      carrierName: 'SIM-FEDEX',
      serviceLevel: 'StandardGround',
      totalPackageCount: 2,
      totalGrossWeightKg: 13.0,
      totalVolumeCm3: 42000,
      shipperName: facility.name,
      shipperAddressSnapshot: '1000 Freight Way, Chicago, IL 60609',
      consigneeName: 'Apex Industrial Corp',
      consigneeAddressSnapshot: '500 Industrial Pkwy, Chicago, IL 60607',
      trailerNumber: 'TRL-744-01',
      sealNumber: 'SEAL-744-01',
      specialInstructions: 'Dock check-in required.',
      packageTrackingNumbers: 'FDX-744-9901, FDX-744-9902',
      documentSnapshotJson: JSON.stringify({ packages: shipment.packages }),
      issuedAtUtc: '2026-11-24T09:50:00Z',
      isVoided: false,
    };

    await page.addInitScript(() => {
      localStorage.setItem('nexus_access_token', 'audit-tok-ship');
      localStorage.setItem('nexus_refresh_token', 'audit-ref-ship');
      localStorage.setItem('nexus_active_facility_id', 'fac-audit-4');
    });

    await page.route('**/api/v1/**', async (route) => {
      const pathName = new URL(route.request().url()).pathname;
      if (pathName === '/api/v1/auth/me') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            userId: 'ship-audit-1',
            email: 'ship@nexus3pl.local',
            firstName: 'Sam',
            lastName: 'ShipLead',
            userType: 'Staff',
            assignedFacilityId: facility.id,
            roles: ['ShippingCoordinator'],
            permissions: ['outbound:ship'],
          }),
        });
      }
      if (pathName === '/api/v1/facilities') {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([facility]) });
      }
      if (pathName === '/api/v1/outbound/shipments/staging-locations') {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([{ id: 'loc-stg-01', locationCode: 'STG-OUT-01', barcode: 'STG-OUT-01', locationType: 'StagingLane', isActive: true }]) });
      }
      if (pathName === '/api/v1/outbound/shipments/eligible-drivers') {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
      }
      if (pathName === '/api/v1/outbound/shipments/reference-clients') {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ items: [{ id: 'cli-audit-4', companyName: 'Apex Industrial Corp', accountCode: 'APX' }], totalCount: 1 }) });
      }
      if (pathName === '/api/v1/outbound/shipments/eligible-packages') {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ items: [], totalCount: 0, pageNumber: 1, pageSize: 40 }) });
      }
      if (pathName === '/api/v1/outbound/shipments') {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ items: [shipment], totalCount: 1, pageNumber: 1, pageSize: 30 }) });
      }
      if (pathName === `/api/v1/outbound/shipments/${shipment.id}`) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(shipment) });
      }
      if (pathName === `/api/v1/outbound/shipments/bol/${bol.id}`) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(bol) });
      }
      return route.fulfill({ status: 404, body: '{}' });
    });

    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('/app/outbound/shipping');
    await expect(page.getByTestId('shipment-dispatched-banner')).toBeVisible();

    await page.getByTestId('open-bol-modal-btn').click();
    await expect(page.getByTestId('bol-snapshot-view')).toBeVisible();
    await shot(page, 'desktop-06-shipments-bol.png');
    await assertDialogFocusTrapAndEscape(page, '.modal-container');

    await assertNoOverflow(page, 768, 1024);
    await shot(page, 'tablet-04-shipments-bol.png');
    await assertNoOverflow(page, 375, 812);
    await shot(page, 'mobile-04-shipments-bol.png');
    await assertNoOverflow(page, 320, 568);
    await shot(page, 'mobile-320-04-shipments-bol.png');

    expect(consoleErrors).toEqual([]);
  });

  test('5. Dispatch Board, Unsigned POD Integrity & Courier Handheld across 4 viewports', async ({ page }) => {
    const consoleErrors: string[] = [];
    page.on('pageerror', (err) => consoleErrors.push(err.message));

    const facility = {
      id: 'fac-audit-5',
      facilityCode: 'FAC-CHI1',
      name: 'Chicago Freight Hub',
      city: 'Chicago',
      timezone: 'America/Chicago',
      isActive: true,
    };

    let persona: 'Staff' | 'Driver' = 'Staff';
    let boardShouldError = true;

    const shipment = {
      shipmentId: 'shp-audit-5',
      shipmentNumber: 'SHP-FDX-20261124-745',
      clientId: 'cli-audit-5',
      clientName: 'Apex Industrial Corp',
      facilityId: facility.id,
      facilityName: facility.name,
      carrierCode: 'SIM-FEDEX',
      carrierServiceLevel: 'StandardGround',
      status: 'Dispatched',
      custodyStatus: 'Confirmed',
      deliveryStatus: 'OutForDelivery',
      billOfLadingNumber: 'BOL-SHP-FDX-20261124-745',
      totalPackagesCount: 2,
      deliveredPackagesCount: 1,
      openExceptionsCount: 0,
      totalWeightKg: 13.0,
      version: 2,
      assignedDriverUserId: 'drv-audit-5',
      assignedDriverName: 'Dan Driver',
      assignedDriverEmail: 'dan.driver@nexus3pl.local',
      packages: [
        {
          packageId: 'pkg-audit-5a',
          packageNumber: 'PKG-745-E2E-1',
          trackingNumber: 'FDX-745-1001',
          carrierCode: 'SIM-FEDEX',
          packageStatus: 'Loaded',
          trackingStatus: 'Delivered',
          outboundOrderId: 'ord-audit-5',
          orderNumber: 'SO-2026-745-1',
          hasActivePod: true,
        },
        {
          packageId: 'pkg-audit-5b',
          packageNumber: 'PKG-745-E2E-2',
          trackingNumber: 'FDX-745-1002',
          carrierCode: 'SIM-FEDEX',
          packageStatus: 'Loaded',
          trackingStatus: 'OutForDelivery',
          outboundOrderId: 'ord-audit-5',
          orderNumber: 'SO-2026-745-1',
          hasActivePod: false,
        },
      ],
    };

    await page.addInitScript(() => {
      localStorage.setItem('nexus_access_token', 'audit-tok-disp');
      localStorage.setItem('nexus_refresh_token', 'audit-ref-disp');
      localStorage.setItem('nexus_active_facility_id', 'fac-audit-5');
    });

    await page.route('**/api/v1/**', async (route) => {
      const url = route.request().url();
      const method = route.request().method();

      if (url.endsWith('/api/v1/auth/me')) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(
            persona === 'Staff'
              ? {
                  userId: 'sup-audit-5',
                  email: 'dispatch@nexus3pl.local',
                  firstName: 'Sam',
                  lastName: 'Dispatcher',
                  userType: 'Staff',
                  roles: ['OperationsManager'],
                  permissions: ['dispatch:view', 'dispatch:manage-runs', 'facilities:view'],
                }
              : {
                  userId: 'drv-audit-5',
                  email: 'dan.driver@nexus3pl.local',
                  firstName: 'Dan',
                  lastName: 'Driver',
                  userType: 'Driver',
                  roles: ['Driver'],
                  permissions: ['dispatch:execute-delivery'],
                }
          ),
        });
      }

      if (url.endsWith('/api/v1/facilities')) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([facility]) });
      }

      if (url.includes('/api/v1/outbound/dispatch/board')) {
        if (boardShouldError) {
          return route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ detail: 'Dispatch board unavailable' }) });
        }
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ items: [shipment], totalCount: 1, pageNumber: 1, pageSize: 25 }),
        });
      }

      if (url.endsWith('/api/v1/outbound/dispatch/courier/shipments')) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([shipment]) });
      }

      if (url.includes('/api/v1/outbound/shipments/eligible-drivers')) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify([{ id: 'drv-audit-5', firstName: 'Dan', lastName: 'Driver', email: 'dan.driver@nexus3pl.local' }]),
        });
      }

      if (url.includes('/api/v1/outbound/dispatch/exceptions')) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ items: [], totalCount: 0, pageNumber: 1, pageSize: 25 }),
        });
      }

      if (url.endsWith('/api/v1/outbound/dispatch/packages/pkg-audit-5b/pod') && method === 'POST') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            isSuccess: true,
            isDuplicate: false,
            proofOfDeliveryId: 'pod-audit-5b',
            packageId: 'pkg-audit-5b',
            packageNumber: 'PKG-745-E2E-2',
            trackingNumber: 'FDX-745-1002',
            carrierCode: 'SIM-FEDEX',
            recipientName: 'Sarah Receiver',
            isSigned: false,
            evidenceSource: 'DriverHandheld',
            capturedAtUtc: '2026-11-24T11:00:00Z',
            deliveredAtUtc: '2026-11-24T11:00:00Z',
            isStorageFinalized: true,
            message: 'Unsigned POD recorded',
          }),
        });
      }

      if (url.endsWith('/api/v1/outbound/dispatch/packages/pkg-audit-5a/pod') && method === 'GET') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'pod-audit-5a',
            clientId: 'cli-audit-5',
            facilityId: facility.id,
            packageId: 'pkg-audit-5a',
            packageNumber: 'PKG-745-E2E-1',
            trackingNumber: 'FDX-745-1001',
            carrierCode: 'SIM-FEDEX',
            recipientName: 'Dock Clerk',
            deliveryLocationDescription: 'Bay 2',
            isSigned: true,
            isStorageFinalized: true,
            preSignedSignatureUrl: `data:image/png;base64,${TINY_PNG_BASE64}`,
            evidenceSource: 'DriverHandheld',
            capturedAtUtc: '2026-11-24T10:14:00Z',
            deliveredAtUtc: '2026-11-24T10:14:00Z',
            isSuperseded: false,
          }),
        });
      }

      return route.fulfill({ status: 404, body: '{}' });
    });

    // Staff Dispatch Board: error -> retry -> loaded -> inspect signed POD modal -> Escape
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('/app/transportation');
    await expect(page.getByRole('alert')).toContainText('Dispatch board unavailable');
    boardShouldError = false;
    await page.getByRole('button', { name: 'Retry' }).click();
    await expect(page.getByTestId(`board-shipment-${shipment.shipmentId}`)).toBeVisible();
    await shot(page, 'desktop-07-dispatch-board.png');

    await page.getByTestId('open-pod-btn-pkg-audit-5a').click();
    await expect(page.getByTestId('pod-signature-preview')).toBeVisible();
    await expect(page.getByTestId('pod-signed-status')).toContainText('Signed (Signature Artifact Verified)');
    await assertDialogFocusTrapAndEscape(page, '.modal-container');

    await assertNoOverflow(page, 768, 1024);
    await shot(page, 'tablet-05-dispatch-board.png');

    // Driver Courier Handheld across 1440 (modal capture), 375, and 320
    persona = 'Driver';
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.reload();
    await expect(page.getByTestId('courier-handheld-panel')).toBeVisible();
    await page.getByTestId('courier-pod-btn-pkg-audit-5b').click();
    await page.getByTestId('pod-recipient-input').fill('Sarah Receiver');
    await page.getByTestId('pod-submit-btn').click();
    await expect(page.getByTestId('pod-signed-status')).toContainText('Recipient Name Acknowledgment (Unsigned)');
    await shot(page, 'desktop-08-pod-modal.png');
    await assertDialogFocusTrapAndEscape(page, '.modal-container');

    await assertNoOverflow(page, 375, 812);
    const podBtnBox = await page.getByTestId('courier-pod-btn-pkg-audit-5b').boundingBox();
    expect(podBtnBox).not.toBeNull();
    expect(podBtnBox!.height).toBeGreaterThanOrEqual(40);
    await shot(page, 'mobile-05-courier-handheld.png');

    await assertNoOverflow(page, 320, 568);
    await shot(page, 'mobile-320-05-courier-handheld.png');

    expect(consoleErrors).toEqual([]);
  });
});
