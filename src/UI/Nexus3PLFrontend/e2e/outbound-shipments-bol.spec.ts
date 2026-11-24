import { test, expect } from '@playwright/test';

const FACILITY = {
  id: 'fac-chi-744',
  facilityCode: 'FAC-CHI1',
  name: 'Chicago Freight Hub',
  city: 'Chicago',
  timezone: 'America/Chicago',
  isActive: true,
};

const CLIENT = {
  id: 'cli-apex-744',
  accountCode: 'APX-744',
  companyName: 'Apex Industrial Corp',
  status: 'Active',
};

const STAGING_LANES = [
  {
    id: 'loc-stg-01',
    locationCode: 'STG-OUT-01',
    barcode: 'STG-OUT-01',
    locationType: 'StagingLane',
    aisleCode: 'STG',
    isActive: true,
  },
  {
    id: 'loc-dock-02',
    locationCode: 'DOCK-DOOR-02',
    barcode: 'DOCK-DOOR-02',
    locationType: 'DockDoor',
    aisleCode: 'DK',
    isActive: true,
  },
];

const DRIVERS = [
  {
    id: 'drv-e2e-744',
    firstName: 'Dan',
    lastName: 'Driver',
    email: 'dan.driver@nexus3pl.local',
    assignedFacilityId: FACILITY.id,
    clientId: null,
  },
];

test.describe('Phase 7.4.4: Shipment Consolidation, Immutable BOL & Driver Custody E2E', () => {
  test('executes Draft -> Add/Remove/Re-Add -> Stage -> Load (ReadyForDispatch) -> Immutable BOL -> Driver Custody Handover (Dispatched) across 4 viewports', async ({
    page,
  }, testInfo) => {
    let shipment: any = null;
    let bol: any = null;

    let eligiblePackages: any[] = [
      {
        id: 'pkg-e2e-1',
        clientId: CLIENT.id,
        facilityId: FACILITY.id,
        outboundOrderId: 'ord-e2e-744',
        orderNumber: 'SO-2026-744-1',
        shipmentId: null,
        handlingUnitId: 'hu-e2e-1',
        handlingUnitLpn: 'CTN-744-001',
        packageNumber: 'PKG-744-001',
        cartonSequence: 1,
        grossWeightKg: 8.4,
        tareWeightKg: 0.45,
        netWeightKg: 7.95,
        lengthCm: 40,
        widthCm: 30,
        heightCm: 25,
        volumetricWeightKg: 6.0,
        carrierCode: 'SIM-FEDEX',
        trackingNumber: 'FDX-744-9901',
        status: 'LabelPrinted',
        hasActiveLabel: true,
        latestLabelId: 'lbl-e2e-1',
      },
      {
        id: 'pkg-e2e-2',
        clientId: CLIENT.id,
        facilityId: FACILITY.id,
        outboundOrderId: 'ord-e2e-744',
        orderNumber: 'SO-2026-744-1',
        shipmentId: null,
        handlingUnitId: 'hu-e2e-2',
        handlingUnitLpn: 'CTN-744-002',
        packageNumber: 'PKG-744-002',
        cartonSequence: 2,
        grossWeightKg: 4.6,
        tareWeightKg: 0.45,
        netWeightKg: 4.15,
        lengthCm: 30,
        widthCm: 20,
        heightCm: 20,
        volumetricWeightKg: 2.4,
        carrierCode: 'SIM-FEDEX',
        trackingNumber: 'FDX-744-9902',
        status: 'LabelPrinted',
        hasActiveLabel: true,
        latestLabelId: 'lbl-e2e-2',
      },
      {
        id: 'pkg-e2e-unlbl',
        clientId: CLIENT.id,
        facilityId: FACILITY.id,
        outboundOrderId: 'ord-e2e-744-2',
        orderNumber: 'SO-2026-744-2',
        shipmentId: null,
        handlingUnitId: 'hu-e2e-3',
        handlingUnitLpn: 'CTN-744-003',
        packageNumber: 'PKG-744-NOLBL',
        cartonSequence: 1,
        grossWeightKg: 3.1,
        tareWeightKg: 0.4,
        netWeightKg: 2.7,
        lengthCm: 25,
        widthCm: 20,
        heightCm: 15,
        volumetricWeightKg: 1.5,
        carrierCode: null,
        trackingNumber: null,
        status: 'Packed',
        hasActiveLabel: false,
        latestLabelId: null,
      },
    ];

    await page.route('**/api/v1/auth/login', async (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ accessToken: 'jwt-744', refreshToken: 'ref-744', tokenType: 'Bearer' }),
      })
    );

    await page.route('**/api/v1/auth/me', async (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          userId: 'ship-lead-e2e',
          email: 'ship@nexus3pl.local',
          firstName: 'Sam',
          lastName: 'ShipLead',
          userType: 'Staff',
          assignedFacilityId: FACILITY.id,
          roles: ['ShippingCoordinator'],
          permissions: ['outbound:ship'],
        }),
      })
    );

    await page.route('**/api/v1/facilities', async (route) =>
      route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([FACILITY]) })
    );

    await page.route('**/api/v1/outbound/shipments**', async (route) => {
      const path = new URL(route.request().url()).pathname;
      const method = route.request().method();

      if (path === '/api/v1/outbound/shipments/staging-locations' && method === 'GET') {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(STAGING_LANES) });
      }
      if (path === '/api/v1/outbound/shipments/eligible-drivers' && method === 'GET') {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(DRIVERS) });
      }
      if (path === '/api/v1/outbound/shipments/reference-clients' && method === 'GET') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ items: [CLIENT], totalCount: 1, pageNumber: 1, pageSize: 100 }),
        });
      }
      if (path === '/api/v1/outbound/shipments/eligible-packages' && method === 'GET') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ items: eligiblePackages, totalCount: eligiblePackages.length, pageNumber: 1, pageSize: 40 }),
        });
      }
      if (path === '/api/v1/outbound/shipments' && method === 'GET') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            items: shipment ? [shipment] : [],
            totalCount: shipment ? 1 : 0,
            pageNumber: 1,
            pageSize: 30,
          }),
        });
      }
      if (path === '/api/v1/outbound/shipments' && method === 'POST') {
        const body = route.request().postDataJSON();
        const stg = STAGING_LANES.find((l) => l.id === body.stagingLocationId);
        shipment = {
          id: 'shp-e2e-744',
          clientId: CLIENT.id,
          clientName: CLIENT.companyName,
          facilityId: FACILITY.id,
          shipmentNumber: 'SHP-FEDEX-20261124-001',
          carrierCode: body.carrierCode,
          carrierServiceLevel: body.serviceLevel,
          status: 'Draft',
          stagingLocationId: stg?.id || null,
          stagingLocationCode: stg?.locationCode || null,
          assignedDriverUserId: null,
          custodyStatus: 'Unassigned',
          trailerNumber: null,
          sealNumber: null,
          billOfLadingId: null,
          billOfLadingNumber: null,
          totalPackagesCount: 0,
          totalWeightKg: 0,
          totalVolumeCm3: 0,
          dispatchedAtUtc: null,
          packages: [],
        };
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            isSuccess: true,
            shipmentId: shipment.id,
            shipmentNumber: shipment.shipmentNumber,
            status: 'Draft',
            totalPackagesCount: 0,
            message: 'Shipment created successfully.',
          }),
        });
      }
      if (path === '/api/v1/outbound/shipments/shp-e2e-744' && method === 'GET') {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(shipment) });
      }
      if (path === '/api/v1/outbound/shipments/shp-e2e-744/packages' && method === 'POST') {
        const body = route.request().postDataJSON();
        const pkg = eligiblePackages.find((p) => p.id === body.packageId);
        eligiblePackages = eligiblePackages.filter((p) => p.id !== body.packageId);
        shipment = {
          ...shipment,
          packages: [
            ...shipment.packages,
            {
              packageId: pkg.id,
              packageNumber: pkg.packageNumber,
              trackingNumber: pkg.trackingNumber,
              carrierCode: pkg.carrierCode,
              grossWeightKg: pkg.grossWeightKg,
              status: pkg.status,
              outboundOrderId: pkg.outboundOrderId,
              orderNumber: pkg.orderNumber,
              currentLocationCode: 'PACK-STN-01',
            },
          ],
        };
        shipment.totalPackagesCount = shipment.packages.length;
        shipment.totalWeightKg = shipment.packages.reduce((s: number, p: any) => s + p.grossWeightKg, 0);
        shipment.totalVolumeCm3 += pkg.lengthCm * pkg.widthCm * pkg.heightCm;
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            isSuccess: true,
            shipmentId: shipment.id,
            packageId: pkg.id,
            packageNumber: pkg.packageNumber,
            totalPackagesCount: shipment.totalPackagesCount,
            totalGrossWeightKg: shipment.totalWeightKg,
            totalVolumeCm3: shipment.totalVolumeCm3,
            message: 'Package consolidated.',
          }),
        });
      }

      const remMatch = path.match(/^\/api\/v1\/outbound\/shipments\/shp-e2e-744\/packages\/([^/]+)$/);
      if (remMatch && method === 'DELETE') {
        const pkgId = remMatch[1];
        const removed = shipment.packages.find((p: any) => p.packageId === pkgId);
        shipment = {
          ...shipment,
          packages: shipment.packages.filter((p: any) => p.packageId !== pkgId),
        };
        shipment.totalPackagesCount = shipment.packages.length;
        shipment.totalWeightKg = shipment.packages.reduce((s: number, p: any) => s + p.grossWeightKg, 0);
        eligiblePackages = [
          {
            id: removed.packageId,
            clientId: CLIENT.id,
            facilityId: FACILITY.id,
            outboundOrderId: removed.outboundOrderId,
            orderNumber: removed.orderNumber,
            shipmentId: null,
            handlingUnitId: 'hu-readded',
            handlingUnitLpn: 'CTN-744-002',
            packageNumber: removed.packageNumber,
            cartonSequence: 2,
            grossWeightKg: removed.grossWeightKg,
            tareWeightKg: 0.45,
            netWeightKg: 4.15,
            lengthCm: 30,
            widthCm: 20,
            heightCm: 20,
            volumetricWeightKg: 2.4,
            carrierCode: removed.carrierCode,
            trackingNumber: removed.trackingNumber,
            status: removed.status,
            hasActiveLabel: true,
            latestLabelId: 'lbl-e2e-2',
          },
          ...eligiblePackages,
        ];
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            isSuccess: true,
            shipmentId: shipment.id,
            packageId: pkgId,
            totalPackagesCount: shipment.totalPackagesCount,
            message: 'Removed',
          }),
        });
      }

      const stageMatch = path.match(/^\/api\/v1\/outbound\/shipments\/packages\/([^/]+)\/stage$/);
      if (stageMatch && method === 'POST') {
        const pkgId = stageMatch[1];
        const body = route.request().postDataJSON();
        const loc = STAGING_LANES.find((l) => l.id === body.stagingLocationId) || STAGING_LANES[0];
        let pkgNumber = '';
        shipment = {
          ...shipment,
          packages: shipment.packages.map((p: any) => {
            if (p.packageId === pkgId) {
              pkgNumber = p.packageNumber;
              return { ...p, status: 'Staged', currentLocationCode: loc.locationCode };
            }
            return p;
          }),
        };
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            isSuccess: true,
            packageId: pkgId,
            packageNumber: pkgNumber,
            packageStatus: 'Staged',
            stagingLocationCode: loc.locationCode,
            message: 'Staged',
          }),
        });
      }

      const loadMatch = path.match(/^\/api\/v1\/outbound\/shipments\/packages\/([^/]+)\/load$/);
      if (loadMatch && method === 'POST') {
        const pkgId = loadMatch[1];
        const body = route.request().postDataJSON();
        let pkgNumber = '';
        const updatedPkgs = shipment.packages.map((p: any) => {
          if (p.packageId === pkgId) {
            pkgNumber = p.packageNumber;
            return { ...p, status: 'Loaded' };
          }
          return p;
        });
        const allLoaded = updatedPkgs.length > 0 && updatedPkgs.every((p: any) => p.status === 'Loaded');
        shipment = {
          ...shipment,
          trailerNumber: body.trailerNumber || shipment.trailerNumber,
          status: allLoaded ? 'ReadyForDispatch' : shipment.status,
          packages: updatedPkgs,
        };
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            isSuccess: true,
            packageId: pkgId,
            packageNumber: pkgNumber,
            packageStatus: 'Loaded',
            shipmentStatus: shipment.status,
            message: 'Loaded',
          }),
        });
      }

      if (path === '/api/v1/outbound/shipments/shp-e2e-744/bol' && method === 'POST') {
        const body = route.request().postDataJSON();
        if (!bol) {
          const snapshot = JSON.stringify({
            BolNumber: 'BOL-SHP-FEDEX-20261124-001',
            Packages: shipment.packages.map((p: any) => ({
              PackageNumber: p.packageNumber,
              TrackingNumber: p.trackingNumber,
              WeightKg: p.grossWeightKg,
              Dimensions: '40x30x25 cm',
            })),
          });
          bol = {
            id: 'bol-e2e-744',
            clientId: CLIENT.id,
            facilityId: FACILITY.id,
            shipmentId: shipment.id,
            bolNumber: 'BOL-SHP-FEDEX-20261124-001',
            carrierCode: shipment.carrierCode,
            carrierName: shipment.carrierCode,
            serviceLevel: shipment.carrierServiceLevel,
            trailerNumber: body.trailerNumber || shipment.trailerNumber,
            sealNumber: body.sealNumber || 'SEAL-744-01',
            totalPackageCount: shipment.totalPackagesCount,
            totalGrossWeightKg: shipment.totalWeightKg,
            totalVolumeCm3: shipment.totalVolumeCm3,
            shipperName: FACILITY.name,
            shipperAddressSnapshot: '100 Distribution Way, Columbus, OH 43215',
            consigneeName: 'Apex Dock Receiver',
            consigneeAddressSnapshot: '500 Commerce Blvd, Columbus, OH 43215',
            packageTrackingNumbers: 'FDX-744-9901, FDX-744-9902',
            specialInstructions: body.specialInstructions || null,
            documentSnapshotJson: snapshot,
            issuedAtUtc: '2026-11-24T09:15:00Z',
            isVoided: false,
          };
          shipment = {
            ...shipment,
            billOfLadingId: bol.id,
            billOfLadingNumber: bol.bolNumber,
            sealNumber: bol.sealNumber,
          };
        }
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            isSuccess: true,
            bolId: bol.id,
            bolNumber: bol.bolNumber,
            shipmentId: shipment.id,
            totalPackageCount: bol.totalPackageCount,
            totalGrossWeightKg: bol.totalGrossWeightKg,
            carrierCode: bol.carrierCode,
            issuedAtUtc: bol.issuedAtUtc,
            documentSnapshotJson: bol.documentSnapshotJson,
            message: 'Issued',
          }),
        });
      }

      if (path === '/api/v1/outbound/shipments/bol/bol-e2e-744' && method === 'GET') {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(bol) });
      }

      if (path === '/api/v1/outbound/shipments/shp-e2e-744/dispatch' && method === 'POST') {
        const body = route.request().postDataJSON();
        shipment = {
          ...shipment,
          status: 'Dispatched',
          custodyStatus: 'Confirmed',
          assignedDriverUserId: body.assignedDriverUserId || null,
          trailerNumber: body.trailerNumber || shipment.trailerNumber,
          sealNumber: body.sealNumber || shipment.sealNumber,
          dispatchedAtUtc: '2026-11-24T09:20:00Z',
        };
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            isSuccess: true,
            shipmentId: shipment.id,
            shipmentNumber: shipment.shipmentNumber,
            status: 'Dispatched',
            dispatchedAtUtc: shipment.dispatchedAtUtc,
            totalPackagesDispatched: shipment.totalPackagesCount,
            totalWeightKg: shipment.totalWeightKg,
            driverName: body.driverName,
            assignedDriverUserId: shipment.assignedDriverUserId,
            billOfLadingNumber: shipment.billOfLadingNumber,
            completedOrderIds: ['ord-e2e-744'],
            message: 'Custody transfer confirmed and shipment dispatched successfully.',
          }),
        });
      }

      return route.fulfill({ status: 404, contentType: 'application/json', body: '{}' });
    });

    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('/login');
    await page.fill('input[type="email"]', 'ship@nexus3pl.local');
    await page.fill('input[type="password"]', 'Secret123!');
    await page.click('button[type="submit"]');
    await page.waitForURL('**/app/dashboard');

    await page.getByTestId('launchpad-shipment-consolidation').click();
    await expect(page.getByTestId('toggle-create-shipment-btn')).toBeVisible();

    // 1. Create Draft Shipment
    await page.click('[data-testid="toggle-create-shipment-btn"]');
    await page.click('[data-testid="submit-create-shipment-btn"]');
    await expect(page.getByTestId('shipment-item-SHP-FEDEX-20261124-001')).toBeVisible();

    // Verify unlabeled carton is blocked from consolidation
    await expect(page.getByTestId('add-eligible-pkg-PKG-744-NOLBL')).toBeDisabled();

    // 2. Consolidate 2 cartons, remove 2nd with reason, re-add 2nd
    await page.click('[data-testid="add-eligible-pkg-PKG-744-001"]');
    await expect(page.getByTestId('consolidated-row-PKG-744-001')).toBeVisible();
    await page.click('[data-testid="add-eligible-pkg-PKG-744-002"]');
    await expect(page.getByTestId('consolidated-row-PKG-744-002')).toBeVisible();

    await page.click('[data-testid="remove-pkg-PKG-744-002"]');
    await page.fill('[data-testid="remove-package-reason-input"]', 'Verify scale label');
    await page.click('[data-testid="confirm-remove-package-btn"]');
    await expect(page.getByTestId('consolidated-row-PKG-744-002')).toHaveCount(0);

    await page.click('[data-testid="add-eligible-pkg-PKG-744-002"]');
    await expect(page.getByTestId('consolidated-row-PKG-744-002')).toBeVisible();

    // Load disabled before stage
    await expect(page.getByTestId('load-pkg-PKG-744-001')).toBeDisabled();

    // 3. Stage both -> Load onto TRL-744-01 -> ReadyForDispatch
    await page.click('[data-testid="stage-pkg-PKG-744-001"]');
    await expect(page.getByTestId('load-pkg-PKG-744-001')).toBeEnabled();
    await page.click('[data-testid="stage-pkg-PKG-744-002"]');
    await expect(page.getByTestId('load-pkg-PKG-744-002')).toBeEnabled();
    await page.fill('[data-testid="dock-trailer-input"]', 'TRL-744-01');
    await page.click('[data-testid="load-pkg-PKG-744-001"]');
    await page.click('[data-testid="load-pkg-PKG-744-002"]');

    await expect(page.getByTestId('open-handover-modal-btn')).toBeEnabled();

    // 4. Issue & View Immutable BOL Snapshot
    await page.click('[data-testid="open-bol-modal-btn"]');
    await page.fill('#bol-seal-input', 'SEAL-744-01');
    await page.fill('#bol-instructions-input', 'Top load cartons only; dock check-in required.');
    await page.click('[data-testid="issue-bol-submit-btn"]');
    await expect(page.getByTestId('bol-snapshot-view')).toBeVisible();

    await page.screenshot({
      path: testInfo.outputPath('phase744-bol-snapshot-desktop-1440.png'),
      fullPage: true,
      animations: 'disabled',
    });

    await page.click('button:has-text("Close")');

    // 5. Driver Custody Handover -> Dispatched
    await page.click('[data-testid="open-handover-modal-btn"]');
    await page.selectOption('[data-testid="handover-driver-select"]', 'drv-e2e-744');
    await page.fill('[data-testid="handover-driver-license"]', 'CDL-IL-74401');
    await page.fill('[data-testid="handover-driver-signature"]', 'Dan Driver Signed');
    await page.click('[data-testid="confirm-dispatch-submit-btn"]');

    await expect(page.getByTestId('shipment-dispatched-banner')).toBeVisible();

    await page.screenshot({
      path: testInfo.outputPath('phase744-dispatched-desktop-1440.png'),
      fullPage: true,
      animations: 'disabled',
    });

    // Multi-viewport responsive checks (768, 375, 320) + mobile screenshots
    for (const vp of [
      { width: 768, height: 1024 },
      { width: 375, height: 812 },
      { width: 320, height: 568 },
    ]) {
      await page.setViewportSize(vp);
      await page.waitForTimeout(100);
      if (vp.width === 375) {
        await page.screenshot({
          path: testInfo.outputPath('phase744-dispatched-mobile-375.png'),
          fullPage: true,
          animations: 'disabled',
        });
      }
      if (vp.width === 320) {
        await page.screenshot({
          path: testInfo.outputPath('phase744-dispatched-narrow-320.png'),
          fullPage: true,
          animations: 'disabled',
        });
      }
      const layoutCheck = await page.evaluate(() => {
        const overflow = document.documentElement.scrollWidth > window.innerWidth + 1;
        const maxCardRight = Array.from(document.querySelectorAll('.card')).reduce(
          (max, el) => Math.max(max, el.getBoundingClientRect().right),
          0
        );
        return { overflow, maxCardRight, innerWidth: window.innerWidth };
      });
      expect(layoutCheck.overflow).toBe(false);
      expect(layoutCheck.maxCardRight).toBeLessThanOrEqual(layoutCheck.innerWidth + 1);
    }
  });
});
