import { test, expect } from '@playwright/test';

const FACILITIES = [
  {
    id: 'fac-chi-742',
    facilityCode: 'FAC-CHI1',
    name: 'Chicago Central Hub',
    city: 'Chicago',
    timezone: 'America/Chicago',
    isActive: true,
  },
];

test.describe('Phase 7.4.2: RF Directed Pick & Short-Pick Workstation E2E', () => {
  test('executes Pick-Only RF error/claim-race/double-scan/short-pick/204 flow across 4 viewports', async ({
    page,
  }, testInfo) => {
    const queueListCalls: string[] = [];
    const confirmCalls: any[] = [];
    const shortPickCalls: any[] = [];
    let nextCallCount = 0;
    let claimAttempts = 0;
    let currentTaskIdx = 0;

    const tasks: any[] = [
      {
        id: 'pt-e2e-1',
        pickWaveId: 'wave-e2e-1',
        outboundOrderId: 'ord-e2e-1',
        orderNumber: 'SO-2026-PICK-101',
        outboundOrderLineId: 'line-e2e-1',
        inventoryAllocationId: 'alloc-e2e-1',
        stockOnHandId: 'stock-e2e-1',
        itemId: 'item-e2e-1',
        sku: 'SKU-MOTOR-742',
        itemDescription: 'Servo Drive Motor Assembly',
        sourceLocationId: 'loc-e2e-1',
        sourceLocationCode: 'A-04-02',
        lotId: 'lot-e2e-1',
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
      },
      {
        id: 'pt-e2e-2',
        pickWaveId: 'wave-e2e-1',
        outboundOrderId: 'ord-e2e-2',
        orderNumber: 'SO-2026-PICK-102',
        outboundOrderLineId: 'line-e2e-2',
        inventoryAllocationId: 'alloc-e2e-2',
        stockOnHandId: 'stock-e2e-2',
        itemId: 'item-e2e-2',
        sku: 'SKU-VALVE-742',
        itemDescription: 'Hydraulic Control Valve',
        sourceLocationId: 'loc-e2e-2',
        sourceLocationCode: 'B-02-08',
        targetToteBarcode: 'TOTE-742',
        quantityToPick: 5,
        quantityPicked: 0,
        shortPickQuantity: 0,
        pickSequence: 2,
        status: 'Pending',
        assignedUserId: null,
        version: 1,
      },
    ];

    await page.route('**/api/v1/auth/login', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          accessToken: 'jwt-picker-742',
          refreshToken: 'ref-picker-742',
          tokenType: 'Bearer',
        }),
      });
    });

    await page.route('**/api/v1/auth/me', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          userId: 'picker-e2e-1',
          email: 'picker@nexus3pl.local',
          firstName: 'Pat',
          lastName: 'Picker',
          userType: 'Staff',
          assignedFacilityId: FACILITIES[0].id,
          roles: ['WarehouseOperator'],
          permissions: ['outbound:pick'],
        }),
      });
    });

    await page.route('**/api/v1/facilities', async (route) => {
      return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(FACILITIES) });
    });

    await page.route('**/api/v1/outbound/pick-tasks**', async (route) => {
      const urlObj = new URL(route.request().url());
      const path = urlObj.pathname;
      const method = route.request().method();

      if (path === '/api/v1/outbound/pick-tasks' && method === 'GET') {
        queueListCalls.push(route.request().url());
        return route.fulfill({ status: 403, contentType: 'application/json', body: '{}' });
      }

      if (path === '/api/v1/outbound/pick-tasks/next' && method === 'GET') {
        nextCallCount++;
        if (nextCallCount === 1) {
          return route.fulfill({
            status: 500,
            contentType: 'application/json',
            body: JSON.stringify({ detail: 'Transient pick engine error' }),
          });
        }
        if (currentTaskIdx >= tasks.length) {
          return route.fulfill({ status: 204 });
        }
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(tasks[currentTaskIdx]),
        });
      }

      if (path.endsWith('/claim') && method === 'POST') {
        const taskId = path.split('/').slice(-2, -1)[0];
        claimAttempts++;
        if (claimAttempts === 1) {
          // Simulate concurrent claim conflict race on first claim attempt
          return route.fulfill({
            status: 409,
            contentType: 'application/json',
            body: JSON.stringify({ detail: 'Task already claimed by another operator' }),
          });
        }
        const idx = tasks.findIndex((t) => t.id === taskId);
        tasks[idx] = {
          ...tasks[idx],
          status: 'Assigned',
          assignedUserId: 'picker-e2e-1',
          version: 2,
        };
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(tasks[idx]),
        });
      }

      if (path.endsWith('/confirm') && method === 'POST') {
        const body = JSON.parse(route.request().postData() || '{}');
        confirmCalls.push(body);
        await new Promise((r) => setTimeout(r, 80));
        const completedId = path.split('/').slice(-2, -1)[0];
        currentTaskIdx = 1;
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            isSuccess: true,
            pickTaskId: completedId,
            taskStatus: 'Completed',
            quantityPicked: body.quantity,
            shortPickQuantity: 0,
            destinationStockId: 'dst-1',
            destinationHandlingUnitId: 'hu-1',
            destinationToteBarcode: body.scannedToteLpn,
            inventoryTransactionId: 901,
            waveCompleted: false,
            orderCompleted: false,
            message: `Confirmed ${body.quantity} unit(s) into ${body.scannedToteLpn}.`,
          }),
        });
      }

      if (path.endsWith('/short-pick') && method === 'POST') {
        const body = JSON.parse(route.request().postData() || '{}');
        shortPickCalls.push(body);
        const shortId = path.split('/').slice(-2, -1)[0];
        currentTaskIdx = 2;
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            isSuccess: true,
            pickTaskId: shortId,
            taskStatus: 'ShortPicked',
            quantityPicked: body.quantityPicked,
            discrepancyQuantity: 5 - Number(body.quantityPicked),
            pickExceptionId: 'exc-e2e-1',
            generatedCycleCountId: 'cc-e2e-1',
            locationLockApplied: true,
            message: 'Short-pick recorded.',
          }),
        });
      }

      return route.fulfill({ status: 404, contentType: 'application/json', body: '{}' });
    });

    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('/login');
    await page.fill('input[type="email"]', 'picker@nexus3pl.local');
    await page.fill('input[type="password"]', 'Secret123!');
    await page.click('button[type="submit"]');
    await page.waitForURL('**/app/dashboard');

    // Launch Directed Picking via Launchpad
    await page.getByTestId('launchpad-picking').click();

    // Error state must appear on 500 (never conflated with empty queue)
    await expect(page.getByTestId('rf-error-banner')).toBeVisible();
    await expect(page.getByTestId('rf-empty-state')).toHaveCount(0);

    // Retry recovers Task 1 in Pending Claim Gate
    await page.click('[data-testid="rf-retry-next-btn"]');
    await expect(page.getByTestId('rf-claim-gate')).toBeVisible();
    await expect(page.getByTestId('confirm-pick-btn')).toBeDisabled();

    // First claim hits 409 conflict and reloads cleanly; second claim locks Task 1
    await page.click('[data-testid="claim-pick-task-btn"]');
    await expect(page.getByTestId('rf-claim-gate')).toBeVisible();
    await page.click('[data-testid="claim-pick-task-btn"]');
    await expect(page.getByTestId('rf-claim-gate')).toHaveCount(0);
    await expect(page.getByText('Claimed by You')).toBeVisible();

    // Fill barcodes
    await page.fill('[data-testid="rf-scan-location"]', 'A-04-02');
    await page.fill('[data-testid="rf-scan-item"]', 'SKU-MOTOR-742');
    await page.fill('[data-testid="rf-scan-lot"]', 'LOT-742-A');
    await page.fill('[data-testid="rf-scan-tote"]', 'TOTE-742');
    await expect(page.locator('[data-testid="rf-scan-location"]')).toHaveValue('A-04-02');

    // Desktop RF Pick screenshot
    await page.screenshot({
      path: testInfo.outputPath('phase742-rf-pick-desktop-1440.png'),
      fullPage: true,
      animations: 'disabled',
    });

    // Mobile & Narrow Mobile responsive overflow + touch target verification before completing Task 1
    for (const vp of [
      { width: 768, height: 1024 },
      { width: 375, height: 812 },
      { width: 320, height: 568 },
    ]) {
      await page.setViewportSize(vp);
      await page.waitForTimeout(100);
      if (vp.width === 375) {
        await page.screenshot({
          path: testInfo.outputPath('phase742-rf-pick-mobile-375.png'),
          fullPage: true,
          animations: 'disabled',
        });
      }
      if (vp.width === 320) {
        await page.screenshot({
          path: testInfo.outputPath('phase742-rf-pick-320.png'),
          fullPage: true,
          animations: 'disabled',
        });
      }
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1);
      expect(overflow).toBe(false);
    }

    await page.setViewportSize({ width: 1440, height: 900 });

    // Double-click rapid submit guard sends ONLY ONE confirm request
    await page.dblclick('[data-testid="confirm-pick-btn"]');
    await expect(page.getByTestId('picking-feedback-banner')).toContainText('TOTE-742');
    expect(confirmCalls).toHaveLength(1);
    expect(confirmCalls[0].idempotencyKey).toBeTruthy();

    // Task 2 loaded -> Claim -> Open Short-Pick Modal -> Enforce Fresh Physical Scans for Positive Qty -> Submit
    await expect(page.getByText('SKU-VALVE-742')).toBeVisible();
    await page.click('[data-testid="claim-pick-task-btn"]');
    await expect(page.getByTestId('open-short-pick-btn')).toBeEnabled();
    await page.click('[data-testid="open-short-pick-btn"]');
    await expect(page.getByTestId('short-pick-form')).toBeVisible();
    await expect(page.getByTestId('short-pick-location-input')).toHaveValue('');
    await expect(page.getByTestId('short-pick-item-input')).toHaveValue('');
    await expect(page.getByTestId('short-pick-tote-input')).toHaveValue('');

    await page.fill('[data-testid="short-pick-qty-input"]', '2');
    await page.selectOption('[data-testid="short-pick-reason-select"]', 'DamagedStock');
    await page.fill('[data-testid="short-pick-notes-input"]', '3 crushed valves in bin');
    await page.click('[data-testid="submit-short-pick-btn"]');
    await expect(page.getByTestId('short-pick-error')).toBeVisible();
    expect(shortPickCalls).toHaveLength(0);

    await page.fill('[data-testid="short-pick-location-input"]', 'B-02-08');
    await page.fill('[data-testid="short-pick-item-input"]', 'SKU-VALVE-742');
    await page.fill('[data-testid="short-pick-tote-input"]', 'TOTE-742');
    await page.screenshot({
      path: testInfo.outputPath('phase742-short-pick-modal-1440.png'),
      fullPage: true,
      animations: 'disabled',
    });
    await page.click('[data-testid="submit-short-pick-btn"]');

    await expect(page.getByTestId('picking-feedback-banner')).toContainText('Short-pick recorded (3 unit(s) short)');
    expect(shortPickCalls).toHaveLength(1);
    expect(shortPickCalls[0].scannedLocationBarcode).toBe('B-02-08');
    expect(shortPickCalls[0].scannedItemBarcode).toBe('SKU-VALVE-742');
    expect(shortPickCalls[0].scannedToteLpn).toBe('TOTE-742');
    await expect(page.getByTestId('rf-empty-state')).toBeVisible();

    // Pick-only operator must NEVER call supervisor queue listing
    expect(queueListCalls).toHaveLength(0);
  });

  test('executes Supervisor Queue & Task Assignment across 4 viewports without users:view or outbound:pick', async ({
    page,
  }, testInfo) => {
    const nextCalls: string[] = [];
    const queueTasks: any[] = [
      {
        id: 'pt-sup-1',
        pickWaveId: 'wave-sup-1',
        outboundOrderId: 'ord-sup-1',
        orderNumber: 'SO-2026-SUP-01',
        outboundOrderLineId: 'line-sup-1',
        inventoryAllocationId: 'alloc-sup-1',
        stockOnHandId: 'stock-sup-1',
        itemId: 'item-sup-1',
        sku: 'SKU-GEAR-800',
        itemDescription: 'Precision Planetary Gearbox',
        sourceLocationId: 'loc-sup-1',
        sourceLocationCode: 'C-01-04',
        quantityToPick: 8,
        quantityPicked: 0,
        shortPickQuantity: 0,
        pickSequence: 1,
        status: 'Pending',
        assignedUserId: null,
        version: 1,
      },
    ];

    await page.route('**/api/v1/auth/login', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ accessToken: 'jwt-sup-742', refreshToken: 'ref-sup-742', tokenType: 'Bearer' }),
      });
    });

    await page.route('**/api/v1/auth/me', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          userId: 'sup-e2e-1',
          email: 'supervisor@nexus3pl.local',
          firstName: 'Sam',
          lastName: 'Supervisor',
          userType: 'Staff',
          assignedFacilityId: FACILITIES[0].id,
          roles: ['PickSupervisor'],
          permissions: ['outbound:tasks:view', 'outbound:tasks:manage'],
        }),
      });
    });

    await page.route('**/api/v1/facilities', async (route) => {
      return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(FACILITIES) });
    });

    await page.route('**/api/v1/outbound/pick-tasks**', async (route) => {
      const path = new URL(route.request().url()).pathname;
      const method = route.request().method();

      if (path === '/api/v1/outbound/pick-tasks/next') {
        nextCalls.push(route.request().url());
        return route.fulfill({ status: 403 });
      }
      if (path === '/api/v1/outbound/pick-tasks/eligible-operators' && method === 'GET') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            items: [
              {
                id: 'op-chi-1',
                firstName: 'Pat',
                lastName: 'Picker',
                email: 'picker@nexus3pl.local',
                assignedFacilityId: FACILITIES[0].id,
              },
            ],
            totalCount: 1,
            pageNumber: 1,
            pageSize: 100,
          }),
        });
      }
      if (path === '/api/v1/outbound/pick-tasks' && method === 'GET') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ items: queueTasks, totalCount: queueTasks.length, pageNumber: 1, pageSize: 15 }),
        });
      }
      if (path.endsWith('/assign') && method === 'POST') {
        const body = JSON.parse(route.request().postData() || '{}');
        queueTasks[0] = {
          ...queueTasks[0],
          status: 'Assigned',
          assignedUserId: body.operatorUserId,
          version: 2,
        };
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(queueTasks[0]) });
      }
      return route.fulfill({ status: 404, contentType: 'application/json', body: '{}' });
    });

    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('/login');
    await page.fill('input[type="email"]', 'supervisor@nexus3pl.local');
    await page.fill('input[type="password"]', 'Secret123!');
    await page.click('button[type="submit"]');
    await page.waitForURL('**/app/dashboard');

    await page.getByTestId('launchpad-picking').click();
    await expect(page.getByTestId('pick-queue-view')).toBeVisible();
    await expect(page.getByText('SKU-GEAR-800')).toBeVisible();
    await expect(page.getByTestId('tab-pick-rf')).toHaveCount(0);

    await page.screenshot({
      path: testInfo.outputPath('phase742-pick-queue-1440.png'),
      fullPage: true,
      animations: 'disabled',
    });

    // Assign operator via eligible-operators endpoint
    await page.click('[data-testid="assign-task-btn-pt-sup-1"]');
    await expect(page.getByTestId('assign-operator-select')).toHaveValue('op-chi-1');
    await page.click('[data-testid="submit-assign-task-btn"]');
    await expect(page.getByTestId('picking-feedback-banner')).toContainText('Assigned task SKU-GEAR-800');

    for (const vp of [
      { width: 768, height: 1024 },
      { width: 375, height: 812 },
      { width: 320, height: 568 },
    ]) {
      await page.setViewportSize(vp);
      await page.waitForTimeout(100);
      if (vp.width === 375) {
        await page.screenshot({
          path: testInfo.outputPath('phase742-pick-queue-375.png'),
          fullPage: true,
          animations: 'disabled',
        });
      }
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1);
      expect(overflow).toBe(false);
    }

    expect(nextCalls).toHaveLength(0);
  });
});
