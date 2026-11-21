import { test, expect } from '@playwright/test';

test.describe('Control Tower & Shell Enterprise Accessibility & Deep-Link Audit (WCAG 2.1 AA & WCAG 2.2)', () => {
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

  test.beforeEach(async ({ page }) => {
    // Intercept auth
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
            firstName: 'Sarah',
            lastName: 'Connor',
            userType: 1,
            clientId: null,
            status: 1,
            roles: ['WarehouseDirector'],
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
          firstName: 'Sarah',
          lastName: 'Connor',
          userType: 1,
          tenantId: null,
          companyName: 'Nexus3PL Enterprise Logistics',
          roles: ['WarehouseDirector'],
          permissions: [
            'analytics:control-tower:view',
            'inbound:view',
            'inbound:receive',
            'inbound:putaway',
            'inbound:putaway:execute',
            'inbound:putaway:manage',
            'inventory:view',
            'facilities:view',
            'clients:view',
          ],
        }),
      });
    });

    await page.route('**/api/v1/facilities', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([mockFacility]),
      });
    });

    await page.route('**/api/v1/control-tower/summary**', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          facilityId: mockFacility.id,
          asOfUtc: new Date().toISOString(),
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
          evaluatedAtUtc: new Date().toISOString(),
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

    await page.route('**/api/v1/putaway/tasks**', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          items: [
            {
              id: 'tsk-001',
              facilityId: mockFacility.id,
              sku: 'SKU-AUDIO-1',
              itemDescription: 'Noise-Canceling Headset',
              sourceLocationCode: 'STG-01',
              suggestedLocationCode: 'RCK-02-B',
              quantity: 20,
              status: 'Pending',
              priority: 'Normal',
              lpnCode: 'LPN-AUD-001',
            },
          ],
          totalCount: 1,
          pageNumber: 1,
          pageSize: 5,
        }),
      });
    });

    await page.route('**/api/v1/receiving/sessions**', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          items: [
            {
              id: 'rec-001',
              sessionNumber: 'REC-2026-99',
              doorNumber: 'DOOR-02',
              trailerNumber: 'TRL-9988',
              status: 'Started',
              lines: [],
            },
          ],
          totalCount: 1,
          pageNumber: 1,
          pageSize: 5,
        }),
      });
    });
  });

  test('zero horizontal overflow on mobile viewport (375x812, WCAG 2.1 Reflow 1.4.10)', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });

    await page.goto('/login');
    await page.fill('input[type="email"]', 'whlead@3pl.local');
    await page.fill('input[type="password"]', 'CorrectHorseBatteryStaple123!');
    await page.click('button[type="submit"]');

    await page.waitForSelector('.control-tower', { timeout: 10000 });
    await page.waitForLoadState('networkidle');

    const overflowCheck = await page.evaluate(() => {
      const scrollW = document.documentElement.scrollWidth;
      const clientW = document.documentElement.clientWidth;
      const innerW = window.innerWidth;
      return { scrollW, clientW, innerW, hasOverflow: scrollW > clientW };
    });

    expect(overflowCheck.hasOverflow).toBe(false);
    expect(overflowCheck.scrollW).toBeLessThanOrEqual(overflowCheck.innerW);
  });

  test('mobile operational touch targets meet or exceed 44x44px target standard (WCAG 2.2 Target Size & Mobile Ergonomics)', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });

    await page.goto('/login');
    await page.fill('input[type="email"]', 'whlead@3pl.local');
    await page.fill('input[type="password"]', 'CorrectHorseBatteryStaple123!');
    await page.click('button[type="submit"]');

    await page.waitForSelector('.control-tower', { timeout: 10000 });
    await page.waitForLoadState('networkidle');

    // Check specific primary action controls on mobile
    const actionSelectors = [
      '.mobile-nav-toggle',
      '.btn-signout',
      '.ct-refresh-btn',
      '.queue-action-link',
      '.launchpad-card',
    ];

    for (const sel of actionSelectors) {
      const element = page.locator(sel).first();
      if (await element.isVisible()) {
        const box = await element.boundingBox();
        expect(box).not.toBeNull();
        if (box) {
          expect(box.height).toBeGreaterThanOrEqual(44);
          expect(box.width).toBeGreaterThanOrEqual(44);
        }
      }
    }
  });

  test('keyboard Tab traversal maintains computed focus outline and visibility across dashboard controls (WCAG 2.1 Criteria 2.1.1 & 2.4.7)', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });

    await page.goto('/login');
    await page.fill('input[type="email"]', 'whlead@3pl.local');
    await page.fill('input[type="password"]', 'CorrectHorseBatteryStaple123!');
    await page.click('button[type="submit"]');

    await page.waitForSelector('.control-tower', { timeout: 10000 });
    await page.waitForLoadState('networkidle');

    // Press Tab multiple times and verify that active interactive elements show focus
    for (let i = 0; i < 5; i++) {
      await page.keyboard.press('Tab');
      const focusStyle = await page.evaluate(() => {
        const el = document.activeElement;
        if (!el || el === document.body) return null;
        const s = window.getComputedStyle(el);
        return {
          tag: el.tagName,
          outlineWidth: parseFloat(s.outlineWidth) || 0,
          outlineStyle: s.outlineStyle,
          boxShadow: s.boxShadow,
        };
      });
      if (focusStyle) {
        expect(focusStyle.outlineWidth > 0 || focusStyle.boxShadow !== 'none').toBe(true);
      }
    }
  });

  test('browser regression: execution-only deep-link from Control Tower opens RF mode without unauthorized list requests (P-CT-02 & Section B)', async ({ page }) => {
    // Configure execution-only floor operator profile
    await page.route('**/api/v1/auth/me', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          userId: 'e2e-floor-op',
          email: 'operator@3pl.local',
          firstName: 'Marcus',
          lastName: 'Chen',
          userType: 1,
          tenantId: null,
          companyName: 'Nexus3PL Enterprise Logistics',
          roles: ['FloorOperator'],
          permissions: [
            'inbound:putaway:execute',
          ],
        }),
      });
    });

    const mockRfTask = {
      id: 'tsk-rf-99',
      facilityId: mockFacility.id,
      sku: 'SKU-RF-SCAN',
      itemDescription: 'Rugged Handheld Barcode Scanner',
      sourceLocationCode: 'STG-PALLET-01',
      suggestedLocationCode: 'RCK-04-12-A',
      suggestedLocationBarcode: 'LOC-RCK-04-12-A',
      quantity: 15,
      status: 'Pending',
      priority: 'Normal',
      lpnCode: 'LPN-RF-7788',
    };

    let putawayListCallsCount = 0;
    let putawayNextCallsCount = 0;

    await page.route('**/api/v1/putaway/tasks?**', async (route) => {
      putawayListCallsCount++;
      // Backend strictly rejects listing without inbound:view
      return route.fulfill({ status: 403, body: JSON.stringify({ message: 'Forbidden: inbound:view required' }) });
    });

    await page.route('**/api/v1/putaway/tasks/next**', async (route) => {
      putawayNextCallsCount++;
      return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(mockRfTask) });
    });

    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('/login');
    await page.fill('input[type="email"]', 'whlead@3pl.local');
    await page.fill('input[type="password"]', 'CorrectHorseBatteryStaple123!');
    await page.click('button[type="submit"]');

    await page.waitForSelector('.control-tower', { timeout: 10000 });
    await page.waitForLoadState('networkidle');

    // On Control Tower, floor operator sees "Launch RF Putaway Scanner"
    const launchRfBtn = page.locator('a:has-text("Launch RF Putaway Scanner")');
    await expect(launchRfBtn).toBeVisible();

    // Verify 0 list calls were triggered from the dashboard
    expect(putawayListCallsCount).toBe(0);

    // Click to navigate to Putaway module
    await launchRfBtn.click();

    // Verify page navigates to /app/inbound/putaway and opens directly in RF mode
    await page.waitForSelector('[data-testid="tab-rf"]', { timeout: 10000 });
    const rfTab = page.locator('[data-testid="tab-rf"]');
    await expect(rfTab).toHaveClass(/active/);

    // Queue tab should NOT be rendered for execution-only user
    const queueTab = page.locator('[data-testid="tab-queue"]');
    await expect(queueTab).toHaveCount(0);

    // Verify that NO unauthorized task list calls were triggered during navigation or load
    expect(putawayListCallsCount).toBe(0);

    // Verify authorized /next call was executed and task details are displayed
    expect(putawayNextCallsCount).toBeGreaterThanOrEqual(1);
    await expect(page.getByText('LPN-RF-7788', { exact: true })).toBeVisible();
    await expect(page.getByText('RCK-04-12-A', { exact: true })).toBeVisible();
  });
});
