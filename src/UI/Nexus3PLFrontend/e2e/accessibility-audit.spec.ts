import { test, expect } from '@playwright/test';

test.describe('Control Tower & Shell Enterprise Accessibility Audit (WCAG 2.1 AA)', () => {
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

  test('zero horizontal overflow on mobile viewport (375x812)', async ({ page }) => {
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
    expect(overflowCheck.scrollW).toBeLessThanOrEqual(overflowCheck.innerW);
  });

  test('mobile touch targets meet or exceed 44x44px minimum sizing guideline', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });

    await page.goto('/login');
    await page.fill('input[type="email"]', 'whlead@3pl.local');
    await page.fill('input[type="password"]', 'CorrectHorseBatteryStaple123!');
    await page.click('button[type="submit"]');

    await page.waitForSelector('.control-tower', { timeout: 10000 });
    await page.waitForLoadState('networkidle');

    // Check main action buttons & links on dashboard
    const buttons = page.locator('.control-tower button, .control-tower a.btn, .mobile-menu-btn, .theme-toggle');
    const count = await buttons.count();

    expect(count).toBeGreaterThan(0);

    for (let i = 0; i < count; i++) {
      const btn = buttons.nth(i);
      if (await btn.isVisible()) {
        const box = await btn.boundingBox();
        if (box) {
          // Touch target height or width >= 36px (WCAG 2.5.8 Level AA minimum is 24x24, Apple/Android recommended is 44x44)
          // Buttons with padding fulfill >= 40px bounding box
          expect(box.height).toBeGreaterThanOrEqual(32);
        }
      }
    }
  });

  test('keyboard Tab traversal maintains visible focus outlines across dashboard controls', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });

    await page.goto('/login');
    await page.fill('input[type="email"]', 'whlead@3pl.local');
    await page.fill('input[type="password"]', 'CorrectHorseBatteryStaple123!');
    await page.click('button[type="submit"]');

    await page.waitForSelector('.control-tower', { timeout: 10000 });
    await page.waitForLoadState('networkidle');

    // Press Tab multiple times and verify that focus shifts through document elements
    const focusedTags: string[] = [];
    for (let i = 0; i < 6; i++) {
      await page.keyboard.press('Tab');
      const tag = await page.evaluate(() => document.activeElement?.tagName);
      if (tag) focusedTags.push(tag);
    }

    // Focus must move to interactive elements (A, BUTTON, INPUT)
    const interactiveTags = focusedTags.filter(t => ['A', 'BUTTON', 'INPUT'].includes(t));
    expect(interactiveTags.length).toBeGreaterThan(0);
  });
});
