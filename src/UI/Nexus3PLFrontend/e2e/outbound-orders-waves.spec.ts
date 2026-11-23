import { test, expect } from '@playwright/test';

const FACILITIES = [
  {
    id: 'fac-chi-741',
    facilityCode: 'FAC-CHI1',
    name: 'Chicago Central Hub',
    city: 'Chicago',
    timezone: 'America/Chicago',
    isActive: true,
  },
  {
    id: 'fac-dal-741',
    facilityCode: 'FAC-DAL1',
    name: 'Dallas Distribution Center',
    city: 'Dallas',
    timezone: 'America/Chicago',
    isActive: true,
  },
];

const CLIENT_PAGE = {
  items: [{ id: 'cli-e2e-1', companyName: 'Apex Industrial Corp', accountCode: 'APEX', status: 'Active' }],
  totalCount: 1,
  pageNumber: 1,
  pageSize: 100,
};

const CATALOG_ITEMS_PAGE = {
  items: [
    {
      id: 'item-e2e-1',
      clientId: 'cli-e2e-1',
      sku: 'SKU-PUMP-741',
      description: 'Hydraulic Valve Assembly',
      storageTempClass: 'Ambient',
      isLotTracked: false,
      requiresExpiryDate: false,
    },
  ],
  totalCount: 1,
  pageNumber: 1,
  pageSize: 100,
};

test.describe('Phase 7.4.1: Outbound Orders, Allocation & Pick Waves E2E', () => {
  test('exercises Create Order -> Stock Availability -> Allocate -> Generate Wave -> Release Wave and responsive viewports', async ({
    page,
  }, testInfo) => {
    const orders: any[] = [
      {
        id: 'ord-e2e-1',
        clientId: 'cli-e2e-1',
        facilityId: FACILITIES[0].id,
        facilityName: FACILITIES[0].name,
        customerName: 'Apex Industrial Corp',
        orderNumber: 'SO-2026-E2E-1',
        externalOrderReference: 'PO-ALPHA-99',
        orderType: 'B2BSalesOrder',
        status: 'Released',
        priority: 7,
        serviceLevel: 'StandardGround',
        carrierCode: 'SIM-FEDEX',
        orderDateUtc: '2026-11-23T08:00:00Z',
        requiredShipDateUtc: '2026-11-25T17:00:00Z',
        version: 1,
        createdAt: '2026-11-23T08:00:00Z',
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
            id: 'line-e2e-1',
            outboundOrderId: 'ord-e2e-1',
            itemId: 'item-e2e-1',
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
      },
    ];

    const waves: any[] = [];
    let generatedWavePayload: any = null;

    await page.route('**/api/v1/auth/login', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          accessToken: 'e2e-tok-741',
          refreshToken: 'e2e-ref-741',
          tokenType: 'Bearer',
          expiresIn: 3600,
          user: {
            id: 'staff-741',
            email: 'ops@nexus3pl.local',
            firstName: 'Ops',
            lastName: 'Lead',
            userType: 'Staff',
            clientId: null,
            status: 'Active',
            createdAt: '2026-11-01T00:00:00Z',
            roles: ['OperationsManager'],
          },
        }),
      });
    });

    await page.route('**/api/v1/auth/me', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          userId: 'staff-741',
          email: 'ops@nexus3pl.local',
          firstName: 'Ops',
          lastName: 'Lead',
          userType: 'Staff',
          tenantId: null,
          companyName: 'Nexus3PL',
          roles: ['OperationsManager'],
          permissions: [
            'analytics:control-tower:view',
            'outbound:orders:view',
            'outbound:orders:create',
            'outbound:orders:import',
            'outbound:orders:allocate',
            'outbound:orders:manage',
            'outbound:orders:cancel',
            'outbound:waves:view',
            'outbound:waves:manage',
            'facilities:view',
          ],
        }),
      });
    });

    await page.route('**/api/v1/facilities', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(FACILITIES),
      });
    });

    await page.route('**/api/v1/clients**', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          items: [{ id: 'cli-e2e-1', companyName: 'Apex Industrial Corp', accountCode: 'APEX' }],
          totalCount: 1,
          pageNumber: 1,
          pageSize: 100,
        }),
      });
    });

    await page.route('**/api/v1/items**', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          items: [
            {
              id: 'item-e2e-1',
              clientId: 'cli-e2e-1',
              sku: 'SKU-PUMP-741',
              description: 'Hydraulic Valve Assembly',
              isLotTracked: false,
              requiresExpiryDate: false,
            },
          ],
          totalCount: 1,
          pageNumber: 1,
          pageSize: 100,
        }),
      });
    });

    await page.route('**/api/v1/locations/tree**', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([]),
      });
    });

    await page.route('**/api/v1/control-tower/**', async (route) => {
      const url = route.request().url();
      if (url.includes('/bottlenecks')) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ facilityId: FACILITIES[0].id, facilityHealthScore: 92, stages: [] }),
        });
      }
      if (url.includes('/dock-yard')) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            facilityId: FACILITIES[0].id,
            totalDockDoors: 10,
            occupiedDockDoors: 3,
            doorsInReceiving: 2,
            doorsInShipping: 1,
            averageTurnTimeMinutes: 40,
            activeYardTrailers: 2,
          }),
        });
      }
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          facilityId: FACILITIES[0].id,
          asOfUtc: '2026-11-23T09:00:00Z',
          ordersReceivedToday: 10,
          ordersDispatchedToday: 8,
          packagesDispatchedToday: 12,
          unitsPickedToday: 120,
          cartonsPackedToday: 15,
          inboundPalletsReceivedToday: 4,
          putawayCompletedToday: 4,
          returnsInspectedToday: 1,
          unitsPerLaborHourToday: 38.5,
          laborVelocityUnitsPerHour: 38.5,
          averageFulfillmentCycleTimeHours: 1.6,
          sameDayDispatchAdherencePercent: 98.0,
          totalBilledRevenueToday: 9500,
          netFreightMarginToday: 1800,
          onTimeDeliveryRatePercent: 99.1,
          activeDeliveryExceptionsCount: 0,
          palletPositionOccupancyPercent: 72.5,
          rackOccupancyPercentage: 72.5,
          volumetricCubeFillPercent: 61.0,
        }),
      });
    });

    await page.route('**/api/v1/outbound/orders**', async (route) => {
      const urlObj = new URL(route.request().url());
      const path = urlObj.pathname;
      const method = route.request().method();

      if (path.endsWith('/stock-availability') && method === 'GET') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            orderId: orders[0].id,
            orderNumber: orders[0].orderNumber,
            isFullyAvailable: true,
            overallFulfillmentPercent: 100,
            lines: [
              {
                lineId: orders[0].lines[0].id,
                lineNumber: 1,
                itemId: 'item-e2e-1',
                sku: 'SKU-PUMP-741',
                itemDescription: 'Hydraulic Valve Assembly',
                quantityRequested: 12,
                quantityAvailable: 40,
                shortageQuantity: 0,
                isFullyAvailable: true,
              },
            ],
          }),
        });
      }

      if (path.endsWith('/allocate') && method === 'POST') {
        orders[0] = {
          ...orders[0],
          status: 'Allocated',
          lines: [{ ...orders[0].lines[0], quantityAllocated: 12, remainingDemand: 0, status: 'Allocated' }],
        };
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            outboundOrderId: orders[0].id,
            orderNumber: orders[0].orderNumber,
            status: 'Allocated',
            totalQuantityOrdered: 12,
            totalQuantityAllocated: 12,
            totalShortageQuantity: 0,
            isFullyAllocated: true,
            isDuplicateRequest: false,
            attemptCount: 1,
            lineSummaries: [
              {
                lineId: orders[0].lines[0].id,
                lineNumber: 1,
                itemId: 'item-e2e-1',
                sku: 'SKU-PUMP-741',
                quantityOrdered: 12,
                quantityAllocated: 12,
                remainingDemand: 0,
                status: 'Allocated',
                allocations: [
                  {
                    allocationId: 'alloc-e2e-1',
                    stockOnHandId: 'stock-e2e-1',
                    locationId: 'loc-e2e-1',
                    locationCode: 'A-01-BIN',
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

      if (path === '/api/v1/outbound/orders/reference-clients' && method === 'GET') {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(CLIENT_PAGE) });
      }

      if (path === '/api/v1/outbound/orders/catalog-items' && method === 'GET') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(CATALOG_ITEMS_PAGE),
        });
      }

      if (path === '/api/v1/outbound/orders' && method === 'GET') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            items: orders,
            totalCount: orders.length,
            pageNumber: 1,
            pageSize: 15,
          }),
        });
      }

      if (path === '/api/v1/outbound/orders' && method === 'POST') {
        const body = JSON.parse(route.request().postData() || '{}');
        const created = {
          id: 'ord-e2e-created',
          clientId: 'cli-e2e-1',
          facilityId: body.facilityId,
          facilityName: FACILITIES[0].name,
          customerName: body.shippingAddress.companyName || body.shippingAddress.recipientName,
          orderNumber: body.orderNumber || 'SO-2026-E2E-NEW',
          externalOrderReference: body.externalOrderReference,
          orderType: body.orderType,
          status: 'Released',
          priority: body.priority,
          serviceLevel: body.serviceLevel,
          carrierCode: body.carrierCode,
          orderDateUtc: '2026-11-23T09:00:00Z',
          requiredShipDateUtc: body.requiredShipDateUtc,
          version: 1,
          createdAt: '2026-11-23T09:00:00Z',
          shippingAddress: body.shippingAddress,
          lines: [
            {
              id: 'line-new-1',
              outboundOrderId: 'ord-e2e-created',
              itemId: body.lines[0].itemId,
              sku: 'SKU-PUMP-741',
              itemDescription: 'Hydraulic Valve Assembly',
              lineNumber: 1,
              quantityOrdered: body.lines[0].quantityOrdered,
              quantityAllocated: 0,
              quantityShipped: 0,
              remainingDemand: body.lines[0].quantityOrdered,
              status: 'Pending',
            },
          ],
          statusHistories: [],
        };
        orders.unshift(created);
        return route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify(created) });
      }

      if (method === 'GET' && path.startsWith('/api/v1/outbound/orders/')) {
        const id = path.split('/').pop();
        const found = orders.find((o) => o.id === id) || orders[0];
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(found) });
      }

      return route.fulfill({ status: 404, contentType: 'application/json', body: '{}' });
    });

    await page.route('**/api/v1/outbound/pick-waves**', async (route) => {
      const urlObj = new URL(route.request().url());
      const path = urlObj.pathname;
      const method = route.request().method();

      if (path.endsWith('/release') && method === 'POST') {
        waves[0] = {
          ...waves[0],
          status: 'Released',
          releasedAtUtc: '2026-11-23T09:10:00Z',
        };
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(waves[0]) });
      }

      if (path === '/api/v1/outbound/pick-waves/eligible-clients' && method === 'GET') {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(CLIENT_PAGE) });
      }

      if (path === '/api/v1/outbound/pick-waves' && method === 'GET') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ items: waves, totalCount: waves.length, pageNumber: 1, pageSize: 15 }),
        });
      }

      if (path === '/api/v1/outbound/pick-waves' && method === 'POST') {
        generatedWavePayload = JSON.parse(route.request().postData() || '{}');
        const createdWave = {
          id: 'wave-e2e-1',
          clientId: generatedWavePayload.request.clientId,
          facilityId: generatedWavePayload.request.facilityId,
          waveNumber: 'WAVE-20261123-E2E',
          waveType: generatedWavePayload.request.waveType,
          status: 'Planning',
          priority: 7,
          ordersCount: 1,
          tasksCount: 1,
          totalQuantityToPick: 12,
          totalQuantityPicked: 0,
          estimatedTravelMeters: 84.5,
          createdAt: '2026-11-23T09:05:00Z',
          waveOrders: [
            {
              id: 'wo-e2e-1',
              outboundOrderId: orders[0].id,
              orderNumber: orders[0].orderNumber,
              customerName: orders[0].customerName,
              priority: 7,
              sequenceNumber: 1,
              orderStatus: 'Allocated',
            },
          ],
          tasks: [
            {
              id: 'pt-e2e-1',
              pickWaveId: 'wave-e2e-1',
              outboundOrderId: orders[0].id,
              orderNumber: orders[0].orderNumber,
              outboundOrderLineId: 'line-e2e-1',
              inventoryAllocationId: 'alloc-e2e-1',
              stockOnHandId: 'stock-e2e-1',
              itemId: 'item-e2e-1',
              sku: 'SKU-PUMP-741',
              itemDescription: 'Hydraulic Valve Assembly',
              sourceLocationId: 'loc-e2e-1',
              sourceLocationCode: 'A-01-BIN',
              quantityToPick: 12,
              quantityPicked: 0,
              shortPickQuantity: 0,
              pickSequence: 1,
              status: 'Pending',
              version: 1,
            },
          ],
        };
        waves.unshift(createdWave);
        return route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify(createdWave) });
      }

      if (method === 'GET' && path.startsWith('/api/v1/outbound/pick-waves/')) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(waves[0]) });
      }

      return route.fulfill({ status: 404, contentType: 'application/json', body: '{}' });
    });

    // Sign in via Login page
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('/login');
    await page.fill('input[type="email"]', 'ops@nexus3pl.local');
    await page.fill('input[type="password"]', 'Secret123!');
    await page.click('button[type="submit"]');
    await page.waitForURL('**/app/dashboard');

    // Navigate to Outbound Orders (/app/outbound/orders) via Launchpad
    await page.getByTestId('launchpad-outbound-orders').click();
    await expect(page.getByText('SO-2026-E2E-1')).toBeVisible();

    // Check Stock Availability & Allocate on order
    await page.click('[data-testid="inspect-order-ord-e2e-1"]');
    await page.click('[data-testid="check-stock-availability"]');
    await expect(page.getByTestId('stock-availability-panel')).toBeVisible();

    await page.click('[data-testid="btn-allocate-order"]');
    await expect(page.getByTestId('allocation-result-panel')).toBeVisible();
    await page.screenshot({
      path: testInfo.outputPath('phase741-allocation-drawer-1440.png'),
      fullPage: true,
      animations: 'disabled',
    });

    // Close drawer cleanly via Escape and capture Desktop Orders screenshot
    await page.keyboard.press('Escape');
    await expect(page.getByTestId('order-detail-drawer')).toBeHidden();
    await page.screenshot({
      path: testInfo.outputPath('phase741-outbound-orders-1440.png'),
      fullPage: true,
      animations: 'disabled',
    });

    // Create New Order
    await page.click('[data-testid="open-create-order-btn"]');
    await expect(page.locator('#addr-recipient')).toBeVisible();
    await expect(page.locator('.order-line-row select option[value="item-e2e-1"]')).toHaveCount(1);
    await page.waitForTimeout(250);
    await page.screenshot({
      path: testInfo.outputPath('phase741-create-order-modal-1440.png'),
      fullPage: true,
      animations: 'disabled',
    });
    await page.fill('#addr-recipient', 'Taylor Reed');
    await page.fill('#addr-line1', '742 Harbor Ave');
    await page.fill('#addr-city', 'Chicago');
    await page.fill('#addr-state', 'IL');
    await page.fill('#addr-postal', '60602');
    await page.locator('.order-line-row select').first().selectOption('item-e2e-1');
    await page.click('[data-testid="submit-create-order"]');
    await expect(page.getByText(/Created outbound order/i)).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(page.getByTestId('order-detail-drawer')).toBeHidden();

    // Navigate to Pick Waves (/app/outbound/waves)
    await page.locator('aside.app-sidebar a[href="/app/outbound/waves"]').click();
    await page.click('[data-testid="open-generate-wave-btn"]');
    await page.click('[data-testid="submit-generate-wave"]');

    expect(generatedWavePayload).toHaveProperty('request');
    await expect(page.getByRole('heading', { name: 'Wave WAVE-20261123-E2E' })).toBeVisible();
    await expect(page.getByTestId('wave-travel-estimate')).toContainText('84.5 m');

    // Release planning wave
    await page.click('[data-testid="release-wave-wave-e2e-1"]');
    await page.click('[data-testid="confirm-release-wave-btn"]');
    await expect(page.getByText(/released to floor/i)).toBeVisible();
    await page.screenshot({
      path: testInfo.outputPath('phase741-pick-waves-1440.png'),
      fullPage: true,
      animations: 'disabled',
    });

    // Responsive overflow checks across Tablet (768x1024) and Mobile (375x812, 320x568)
    for (const vp of [
      { width: 768, height: 1024 },
      { width: 375, height: 812 },
      { width: 320, height: 568 },
    ]) {
      await page.setViewportSize(vp);
      await page.waitForTimeout(100);
      if (vp.width === 375) {
        await page.screenshot({
          path: testInfo.outputPath('phase741-pick-waves-375.png'),
          fullPage: true,
          animations: 'disabled',
        });
      }
      const overflowWaves = await page.evaluate(
        () => document.documentElement.scrollWidth > window.innerWidth + 1
      );
      expect(overflowWaves).toBe(false);
    }

    await page.setViewportSize({ width: 1440, height: 900 });
    await page.locator('aside.app-sidebar a[href="/app/outbound/orders"]').click();
    await page.setViewportSize({ width: 375, height: 812 });
    await page.waitForTimeout(400);
    await page.screenshot({
      path: testInfo.outputPath('phase741-outbound-orders-375.png'),
      fullPage: true,
      animations: 'disabled',
    });

    for (const vp of [
      { width: 768, height: 1024 },
      { width: 375, height: 812 },
      { width: 320, height: 568 },
    ]) {
      await page.setViewportSize(vp);
      await page.waitForTimeout(100);
      const overflowOrders = await page.evaluate(
        () => document.documentElement.scrollWidth > window.innerWidth + 1
      );
      expect(overflowOrders).toBe(false);
    }
  });
});
