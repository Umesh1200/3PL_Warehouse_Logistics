import { test, expect } from '@playwright/test';

const FACILITIES = [
  {
    id: 'fac-chi-743',
    facilityCode: 'FAC-CHI1',
    name: 'Chicago Packing Hub',
    city: 'Chicago',
    timezone: 'America/Chicago',
    isActive: true,
  },
];

const STATION = {
  id: 'stn-e2e-1',
  locationCode: 'PACK-STN-01',
  barcode: 'PACK-STN-01',
  aisleCode: 'PK',
  isActive: true,
};

const MATERIAL_MED = {
  id: 'mat-e2e-med',
  clientId: null,
  facilityId: FACILITIES[0].id,
  code: 'BOX-MED-01',
  name: 'Medium Corrugated Box',
  materialType: 'CorrugatedBox',
  lengthCm: 40,
  widthCm: 30,
  heightCm: 25,
  maxWeightKg: 20,
  tareWeightKg: 0.45,
  costPerUnit: 1.25,
};

const ORDER_PACK = {
  id: 'ord-e2e-743',
  clientId: 'cli-e2e-743',
  orderNumber: 'SO-2026-PACK-743',
  customerName: 'Precision Robotics Corp',
  status: 'InProgress',
  priority: 8,
  serviceLevel: 'StandardGround',
  totalAllocatedUnits: 4,
  totalPickedUnits: 4,
  totalPackedUnits: 0,
};

test.describe('Phase 7.4.3: Packing, Cartonization & Carrier Labeling E2E', () => {
  test('executes full Session Start -> Recommend -> Create -> Idempotent Pack -> Close -> Tote Release -> Rate -> Label Failure Recovery -> Void -> Complete across 4 viewports', async ({
    page,
  }, testInfo) => {
    const packRequests: any[] = [];
    const labelRequests: any[] = [];
    let labelGenCount = 0;

    let sessionDetail: any = {
      id: 'sess-e2e-743',
      clientId: ORDER_PACK.clientId,
      facilityId: FACILITIES[0].id,
      packingStationLocationId: STATION.id,
      packingStationLocationCode: STATION.locationCode,
      outboundOrderId: ORDER_PACK.id,
      orderNumber: ORDER_PACK.orderNumber,
      operatorUserId: 'bench-lead-1',
      operatorEmail: 'packer@nexus3pl.local',
      status: 'Active',
      startedAtUtc: '2026-11-24T05:00:00Z',
      completedAtUtc: null,
      activePackagesCount: 0,
      notes: null,
      orderLines: [
        {
          orderLineId: 'ol-743-1',
          itemId: 'item-743-1',
          sku: 'SKU-ACTUATOR-743',
          description: 'Precision Linear Actuator',
          quantityOrdered: 4,
          quantityAllocated: 4,
          quantityPicked: 4,
          quantityPacked: 0,
          remainingToPack: 4,
          lineStatus: 'Allocated',
        },
      ],
      packages: [],
      pickedTotes: [
        {
          handlingUnitId: 'hu-tote-e2e-1',
          lpnCode: 'TOTE-743-E2E',
          status: 'InUse',
          locationCode: 'PACK-STN-01',
          totalQuantity: 4,
        },
      ],
    };

    await page.route('**/api/v1/auth/login', async (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ accessToken: 'jwt-743', refreshToken: 'ref-743', tokenType: 'Bearer' }),
      })
    );

    await page.route('**/api/v1/auth/me', async (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          userId: 'bench-lead-1',
          email: 'packer@nexus3pl.local',
          firstName: 'Pat',
          lastName: 'PackLead',
          userType: 'Staff',
          assignedFacilityId: FACILITIES[0].id,
          roles: ['PackAndShipLead'],
          permissions: ['outbound:pack', 'outbound:ship'],
        }),
      })
    );

    await page.route('**/api/v1/facilities', async (route) =>
      route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(FACILITIES) })
    );

    await page.route('**/api/v1/outbound/packing/**', async (route) => {
      const path = new URL(route.request().url()).pathname;
      const method = route.request().method();

      if (path === '/api/v1/outbound/packing/stations' && method === 'GET') {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([STATION]) });
      }
      if (path === '/api/v1/outbound/packing/packaging-materials' && method === 'GET') {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([MATERIAL_MED]) });
      }
      if (path === '/api/v1/outbound/packing/packable-orders' && method === 'GET') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ items: [ORDER_PACK], totalCount: 1, pageNumber: 1, pageSize: 50 }),
        });
      }
      if (path === '/api/v1/outbound/packing/sessions' && method === 'GET') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ items: [sessionDetail], totalCount: 1, pageNumber: 1, pageSize: 25 }),
        });
      }
      if (path === '/api/v1/outbound/packing/sessions/start' && method === 'POST') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            isSuccess: true,
            sessionId: sessionDetail.id,
            status: 'Active',
            outboundOrderId: ORDER_PACK.id,
            orderNumber: ORDER_PACK.orderNumber,
            packingStationLocationId: STATION.id,
            stationLocationCode: STATION.locationCode,
            operatorUserId: 'bench-lead-1',
            startedAtUtc: sessionDetail.startedAtUtc,
            activePackagesCount: 0,
            message: 'Packing session started.',
          }),
        });
      }
      if (path === `/api/v1/outbound/packing/sessions/${sessionDetail.id}` && method === 'GET') {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(sessionDetail) });
      }
      if (path === '/api/v1/outbound/packing/cartonization/recommend' && method === 'POST') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            isSuccess: true,
            recommendedPackageCount: 1,
            totalEstimatedGrossWeightKg: 6.45,
            totalEstimatedVolumeCm3: 4800,
            message: "Recommended 1 carton: 'BOX-MED-01' (Medium Corrugated Box).",
            warnings: [],
            recommendedPackages: [
              {
                packageIndex: 1,
                packagingMaterialId: MATERIAL_MED.id,
                packagingMaterialCode: MATERIAL_MED.code,
                packagingMaterialName: MATERIAL_MED.name,
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
                items: [
                  {
                    itemId: 'item-743-1',
                    sku: 'SKU-ACTUATOR-743',
                    quantity: 4,
                    totalWeightKg: 6,
                    totalVolumeCm3: 4800,
                  },
                ],
              },
            ],
          }),
        });
      }
      if (path === '/api/v1/outbound/packing/packages/create' && method === 'POST') {
        const pkg = {
          id: 'pkg-e2e-743',
          clientId: ORDER_PACK.clientId,
          facilityId: FACILITIES[0].id,
          outboundOrderId: ORDER_PACK.id,
          orderNumber: ORDER_PACK.orderNumber,
          shipmentId: null,
          handlingUnitId: 'hu-ctn-e2e',
          handlingUnitLpn: 'CTN-SO-2026-PACK-743-01',
          packagingMaterialId: MATERIAL_MED.id,
          packageNumber: 'PKG-SO-2026-PACK-743-01',
          cartonSequence: 1,
          grossWeightKg: 0,
          tareWeightKg: 0.45,
          netWeightKg: 0,
          lengthCm: 40,
          widthCm: 30,
          heightCm: 25,
          volumetricWeightKg: 6,
          carrierCode: null,
          trackingNumber: null,
          status: 'Open',
          hasActiveLabel: false,
          latestLabelId: null,
        };
        sessionDetail = { ...sessionDetail, activePackagesCount: 1, packages: [pkg] };
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            isSuccess: true,
            packageId: pkg.id,
            packageNumber: pkg.packageNumber,
            handlingUnitId: pkg.handlingUnitId,
            handlingUnitLpn: pkg.handlingUnitLpn,
            status: 'Open',
            packagingMaterialId: MATERIAL_MED.id,
            packagingMaterialCode: MATERIAL_MED.code,
            lengthCm: 40,
            widthCm: 30,
            heightCm: 25,
            tareWeightKg: 0.45,
            message: 'Carton created successfully.',
          }),
        });
      }
      if (path === '/api/v1/outbound/packing/items/pack' && method === 'POST') {
        const body = JSON.parse(route.request().postData() || '{}');
        packRequests.push(body);
        await new Promise((r) => setTimeout(r, 80));
        sessionDetail = {
          ...sessionDetail,
          orderLines: [{ ...sessionDetail.orderLines[0], quantityPacked: 4, remainingToPack: 0 }],
          pickedTotes: [{ ...sessionDetail.pickedTotes[0], totalQuantity: 0 }],
        };
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            isSuccess: true,
            packageId: 'pkg-e2e-743',
            packageNumber: 'PKG-SO-2026-PACK-743-01',
            packageLineId: 'pl-e2e-1',
            itemId: 'item-743-1',
            sku: 'SKU-ACTUATOR-743',
            quantityPacked: body.quantity,
            remainingToteQuantity: 0,
            remainingOrderLineDemand: 0,
            sourceToteLpn: body.scannedToteLpn,
            cartonLpn: 'CTN-SO-2026-PACK-743-01',
            inventoryTransactionId: 901,
            isOrderLineComplete: true,
            isOrderFullyPacked: true,
            message: 'Successfully packed 4 units.',
          }),
        });
      }
      if (path.endsWith('/close') && method === 'POST') {
        const body = JSON.parse(route.request().postData() || '{}');
        sessionDetail = {
          ...sessionDetail,
          packages: [
            {
              ...sessionDetail.packages[0],
              status: 'Packed',
              grossWeightKg: body.scaleGrossWeightKg,
              netWeightKg: Number(body.scaleGrossWeightKg) - 0.45,
            },
          ],
        };
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            isSuccess: true,
            packageId: 'pkg-e2e-743',
            packageNumber: 'PKG-SO-2026-PACK-743-01',
            grossWeightKg: body.scaleGrossWeightKg,
            tareWeightKg: 0.45,
            netWeightKg: Number(body.scaleGrossWeightKg) - 0.45,
            volumetricWeightKg: 6,
            status: 'Packed',
            message: 'Package sealed.',
          }),
        });
      }
      if (path === '/api/v1/outbound/packing/totes/release' && method === 'POST') {
        sessionDetail = {
          ...sessionDetail,
          pickedTotes: [{ ...sessionDetail.pickedTotes[0], status: 'Available' }],
        };
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            isSuccess: true,
            handlingUnitId: 'hu-tote-e2e-1',
            lpnCode: 'TOTE-743-E2E',
            status: 'Available',
            message: "Tote 'TOTE-743-E2E' returned to Available pool.",
          }),
        });
      }
      if (path.endsWith('/complete') && method === 'POST') {
        sessionDetail = { ...sessionDetail, status: 'Completed' };
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            isSuccess: true,
            sessionId: sessionDetail.id,
            status: 'Completed',
            completedAtUtc: '2026-11-24T05:15:00Z',
            totalPackagesCount: 1,
            totalWeightKg: 6.45,
            message: 'Packing session completed.',
          }),
        });
      }
      return route.fulfill({ status: 404, contentType: 'application/json', body: '{}' });
    });

    await page.route('**/api/v1/outbound/shipping/**', async (route) => {
      const path = new URL(route.request().url()).pathname;
      const method = route.request().method();

      if (path === '/api/v1/outbound/shipping/rates' && method === 'POST') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify([
            {
              carrierCode: 'SIM-FEDEX',
              carrierName: 'FedEx Express (Simulated)',
              serviceLevel: 'StandardGround',
              serviceDescription: 'FedEx Express (Simulated) - Ground Delivery',
              totalAmount: 24.11,
              currency: 'USD',
              estimatedDeliveryDays: 3,
              isGuaranteed: false,
            },
            {
              carrierCode: 'SIM-UPS',
              carrierName: 'UPS Ground (Simulated)',
              serviceLevel: 'SecondDayAir',
              serviceDescription: 'UPS Ground (Simulated) - 2-Day Expedited Air',
              totalAmount: 38.99,
              currency: 'USD',
              estimatedDeliveryDays: 2,
              isGuaranteed: true,
            },
          ]),
        });
      }

      if (path === '/api/v1/outbound/shipping/labels/generate' && method === 'POST') {
        const body = JSON.parse(route.request().postData() || '{}');
        labelRequests.push(body);
        labelGenCount++;
        if (body.simulateFailure) {
          return route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({
              isSuccess: false,
              packageId: body.packageId,
              packageNumber: 'PKG-SO-2026-PACK-743-01',
              labelId: '00000000-0000-0000-0000-000000000000',
              trackingNumber: '',
              carrierCode: body.carrierCode,
              serviceLevel: body.serviceLevel,
              labelFormat: body.labelFormat,
              labelData: '',
              rateCharged: 0,
              packageStatus: 'Packed',
              labelStatus: 'Generated',
              isSimulated: true,
              message: 'Failed to generate shipping label from carrier.',
              errorMessage: 'Simulated Carrier HTTP 503 Timeout',
            }),
          });
        }

        sessionDetail = {
          ...sessionDetail,
          packages: [
            {
              ...sessionDetail.packages[0],
              status: 'LabelPrinted',
              carrierCode: body.carrierCode,
              trackingNumber: '1Z-SIM-743-9901',
              hasActiveLabel: true,
              latestLabelId: 'lbl-e2e-743',
            },
          ],
        };
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            isSuccess: true,
            packageId: body.packageId,
            packageNumber: 'PKG-SO-2026-PACK-743-01',
            labelId: 'lbl-e2e-743',
            trackingNumber: '1Z-SIM-743-9901',
            carrierCode: body.carrierCode,
            serviceLevel: body.serviceLevel,
            labelFormat: body.labelFormat,
            labelData: '^XA\n^FO50,50^ADN,36,20^FDNEXUS3PL^FS\n^FO50,120^BY3^BCN,100,Y,N,N^FD1Z-SIM-743-9901^FS\n^XZ',
            routingCode: 'UPS-2DA-CHI',
            rateCharged: 38.99,
            packageStatus: 'LabelPrinted',
            labelStatus: 'Generated',
            isSimulated: true,
            message: 'Shipping label generated successfully.',
          }),
        });
      }

      if (path.endsWith('/void') && method === 'POST') {
        sessionDetail = {
          ...sessionDetail,
          packages: [
            {
              ...sessionDetail.packages[0],
              status: 'Packed',
              carrierCode: null,
              trackingNumber: null,
              hasActiveLabel: false,
              latestLabelId: null,
            },
          ],
        };
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            isSuccess: true,
            labelId: 'lbl-e2e-743',
            packageId: 'pkg-e2e-743',
            trackingNumber: '1Z-SIM-743-9901',
            labelStatus: 'Voided',
            packageStatus: 'Packed',
            voidConfirmationNumber: 'VOID-E2E-743',
            message: 'Shipping label successfully voided.',
          }),
        });
      }

      return route.fulfill({ status: 404, contentType: 'application/json', body: '{}' });
    });

    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('/login');
    await page.fill('input[type="email"]', 'packer@nexus3pl.local');
    await page.fill('input[type="password"]', 'Secret123!');
    await page.click('button[type="submit"]');
    await page.waitForURL('**/app/dashboard');

    await page.getByTestId('launchpad-packing').click();
    await expect(page.getByTestId('packing-workbench-home')).toBeVisible();

    await page.screenshot({
      path: testInfo.outputPath('phase743-workbench-home-1440.png'),
      fullPage: true,
      animations: 'disabled',
    });

    // 1. Start session -> 2. Run 3D cartonization -> 3. Create carton from recommendation
    await page.click('[data-testid="start-packing-session-btn"]');
    await expect(page.getByTestId('packing-session-workbench')).toBeVisible();

    await page.click('[data-testid="recommend-cartonization-btn"]');
    await expect(page.getByTestId('carton-rec-card-1')).toBeVisible();
    await page.click('[data-testid="apply-rec-box-btn-1"]');
    await expect(page.getByTestId('pack-item-form')).toBeVisible();

    // 4. Scan Tote + SKU + double-click idempotent Pack guard
    await page.fill('[data-testid="pack-scan-tote"]', 'TOTE-743-E2E');
    await page.fill('[data-testid="pack-scan-item"]', 'SKU-ACTUATOR-743');
    await page.fill('[data-testid="pack-qty-input"]', '4');
    await page.dblclick('[data-testid="pack-item-btn"]');
    await expect(page.getByTestId('packing-feedback-banner')).toContainText('Tx #901');
    expect(packRequests).toHaveLength(1);
    expect(packRequests[0].idempotencyKey).toBeTruthy();

    // 5. Scale Close Carton + Release Empty Tote
    await page.fill('[data-testid="close-package-weight-input"]', '6.45');
    await page.click('[data-testid="close-package-btn"]');
    await expect(page.getByTestId('packing-feedback-banner')).toContainText('Sealed carton');

    await page.click('[data-testid="release-tote-btn-TOTE-743-E2E"]');
    await expect(page.getByTestId('packing-feedback-banner')).toContainText('Available pool');

    // 6. Quote Carrier Rates -> Simulate Failure (stays Packed) -> Retry Generate ZPL Label -> Complete Session
    await page.click('[data-testid="quote-rates-btn"]');
    await expect(page.getByTestId('rate-quotes-list')).toBeVisible();
    await page.click('[data-testid="select-rate-SIM-UPS-SecondDayAir"]');

    await page.check('[data-testid="simulate-label-fail-checkbox"]');
    await page.click('[data-testid="generate-label-btn"]');
    await expect(page.getByTestId('carrier-panel-error')).toContainText('Simulated Carrier HTTP 503 Timeout');

    await page.uncheck('[data-testid="simulate-label-fail-checkbox"]');
    await page.click('[data-testid="generate-label-btn"]');
    await expect(page.getByTestId('active-tracking-number')).toContainText('1Z-SIM-743-9901');
    await expect(page.getByTestId('label-payload-preview')).toContainText('1Z-SIM-743-9901');
    expect(labelGenCount).toBe(2);
    expect(labelRequests[0].idempotencyKey).toBe(labelRequests[1].idempotencyKey);

    // Capture full active desktop workbench screenshot with Cartonization, Cartons, Rates & ZPL Label Preview!
    await page.screenshot({
      path: testInfo.outputPath('phase743-session-label-desktop-1440.png'),
      fullPage: true,
      animations: 'disabled',
    });

    // Multi-viewport responsive checks (tablet 768, mobile 375, narrow 320) + screenshots
    for (const vp of [
      { width: 768, height: 1024 },
      { width: 375, height: 812 },
      { width: 320, height: 568 },
    ]) {
      await page.setViewportSize(vp);
      await page.waitForTimeout(100);
      if (vp.width === 375) {
        await page.screenshot({
          path: testInfo.outputPath('phase743-session-mobile-375.png'),
          fullPage: true,
          animations: 'disabled',
        });
      }
      if (vp.width === 320) {
        await page.screenshot({
          path: testInfo.outputPath('phase743-session-narrow-320.png'),
          fullPage: true,
          animations: 'disabled',
        });
      }
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1);
      expect(overflow).toBe(false);
    }

    await page.setViewportSize({ width: 1440, height: 900 });

    // 7. Void Label -> Complete Session
    await page.click('[data-testid="open-void-label-btn"]');
    await page.fill('[data-testid="void-label-reason-input"]', 'Rate optimization');
    await page.click('[data-testid="confirm-void-label-btn"]');
    await expect(page.getByTestId('packing-feedback-banner')).toContainText('Voided label');

    await page.click('[data-testid="complete-session-btn"]');
    await expect(page.getByTestId('packing-feedback-banner')).toContainText('Packing session completed');
  });
});
