import { test, expect } from '@playwright/test';

const FACILITY = {
  id: 'fac-chi-745',
  facilityCode: 'FAC-CHI1',
  name: 'Chicago Freight Hub',
  city: 'Chicago',
  timezone: 'America/Chicago',
  isActive: true,
};

const TINY_PNG_BASE64 =
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=';

test.describe('Phase 7.4.5: Dispatch Board, Tracking Timeline, POD & Exceptions E2E', () => {
  test('executes Dispatch Board -> Timeline & Order Rollup -> Exception Resolution -> Driver Custody Ack -> POD Submit & OrderDeliveryStatus.Delivered across 4 viewports', async ({
    page,
  }) => {
    const consoleErrors: string[] = [];
    page.on('pageerror', (err) => consoleErrors.push(err.message));

    let authPersona: 'Staff' | 'Driver' = 'Staff';

    const shipmentId = 'shp-e2e-745';
    const pkg1Id = 'pkg-e2e-745-1';
    const pkg2Id = 'pkg-e2e-745-2';
    const orderId = 'ord-e2e-745';
    const excId = 'exc-e2e-745';

    let shipment = {
      shipmentId,
      shipmentNumber: 'SHP-FDX-20261124-745',
      clientId: 'cli-e2e-745',
      clientName: 'Apex Industrial Corp',
      facilityId: FACILITY.id,
      facilityName: FACILITY.name,
      carrierCode: 'SIM-FEDEX',
      carrierServiceLevel: 'StandardGround',
      status: 'Dispatched',
      custodyStatus: 'PendingHandover',
      deliveryStatus: 'OutForDelivery',
      billOfLadingNumber: 'BOL-SHP-FDX-20261124-745',
      totalPackagesCount: 2,
      deliveredPackagesCount: 1,
      openExceptionsCount: 1,
      totalWeightKg: 13.0,
      version: 2,
      assignedDriverUserId: 'drv-e2e-745',
      assignedDriverName: 'Dan Driver',
      assignedDriverEmail: 'dan.driver@nexus3pl.local',
      packages: [
        {
          packageId: pkg1Id,
          packageNumber: 'PKG-745-E2E-1',
          trackingNumber: 'FDX-745-1001',
          carrierCode: 'SIM-FEDEX',
          packageStatus: 'Loaded',
          trackingStatus: 'Delivered',
          outboundOrderId: orderId,
          orderNumber: 'SO-2026-745-1',
          hasActivePod: true,
        },
        {
          packageId: pkg2Id,
          packageNumber: 'PKG-745-E2E-2',
          trackingNumber: 'FDX-745-1002',
          carrierCode: 'SIM-FEDEX',
          packageStatus: 'Loaded',
          trackingStatus: 'OutForDelivery',
          outboundOrderId: orderId,
          orderNumber: 'SO-2026-745-1',
          hasActivePod: false,
        },
      ],
    };

    let exceptionRow = {
      exceptionId: excId,
      clientId: 'cli-e2e-745',
      facilityId: FACILITY.id,
      packageId: pkg2Id,
      packageNumber: 'PKG-745-E2E-2',
      shipmentId,
      shipmentNumber: shipment.shipmentNumber,
      orderId,
      orderNumber: 'SO-2026-745-1',
      trackingNumber: 'FDX-745-1002',
      carrierCode: 'SIM-FEDEX',
      exceptionType: 'AccessRestricted',
      status: 'Open',
      severity: 'Medium',
      resolution: 'PendingCarrierReattempt',
      source: 'DriverHandheld',
      carrierExceptionCode: 'ACC01',
      carrierDescription: 'Loading dock security gate locked',
      occurredAtUtc: '2026-11-24T09:00:00Z',
      attemptNumber: 1,
      reattemptCount: 0,
      contactAttemptsCount: 0,
      createdAt: '2026-11-24T09:00:00Z',
    };

    await page.addInitScript(() => {
      localStorage.setItem('nexus_access_token', 'e2e-token-745');
      localStorage.setItem('nexus_refresh_token', 'e2e-refresh-745');
      localStorage.setItem('nexus_active_facility_id', 'fac-chi-745');
    });

    await page.route('**/api/v1/**', async (route) => {
      const req = route.request();
      const url = req.url();
      const method = req.method();

      if (url.endsWith('/api/v1/auth/me')) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(
            authPersona === 'Staff'
              ? {
                  userId: 'sup-e2e-745',
                  email: 'dispatch.sup@nexus3pl.local',
                  firstName: 'Sam',
                  lastName: 'Dispatcher',
                  userType: 'Staff',
                  tenantId: null,
                  roles: ['OperationsManager'],
                  permissions: ['dispatch:view', 'dispatch:manage-runs', 'facilities:view'],
                }
              : {
                  userId: 'drv-e2e-745',
                  email: 'dan.driver@nexus3pl.local',
                  firstName: 'Dan',
                  lastName: 'Driver',
                  userType: 'Driver',
                  tenantId: null,
                  roles: ['Driver'],
                  permissions: ['dispatch:execute-delivery'],
                }
          ),
        });
      }

      if (url.endsWith('/api/v1/facilities')) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([FACILITY]) });
      }

      if (url.includes('/api/v1/outbound/dispatch/board')) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ items: [shipment], totalCount: 1, pageNumber: 1, pageSize: 25 }),
        });
      }

      if (url.endsWith('/api/v1/outbound/dispatch/courier/shipments')) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify([shipment]),
        });
      }

      if (url.includes('/api/v1/outbound/shipments/eligible-drivers')) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify([
            {
              id: 'drv-e2e-745',
              firstName: 'Dan',
              lastName: 'Driver',
              email: 'dan.driver@nexus3pl.local',
            },
          ]),
        });
      }

      if (url.includes(`/api/v1/outbound/dispatch/packages/${pkg1Id}/timeline`)) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            packageId: pkg1Id,
            packageNumber: 'PKG-745-E2E-1',
            trackingNumber: 'FDX-745-1001',
            carrierCode: 'SIM-FEDEX',
            currentTrackingStatus: 'Delivered',
            deliveredAtUtc: '2026-11-24T10:15:00Z',
            events: [
              {
                id: 'ev-e2e-1',
                milestoneStatus: 'Delivered',
                carrierEventCode: 'POD_CONFIRMED',
                carrierDescription: 'Signed at receiving dock',
                location: 'Chicago, IL',
                occurredAtUtc: '2026-11-24T10:15:00Z',
                receivedAtUtc: '2026-11-24T10:15:02Z',
              },
            ],
          }),
        });
      }

      if (url.includes(`/api/v1/outbound/dispatch/orders/${orderId}/delivery-status`)) {
        const delivCount = shipment.packages.filter((p) => p.trackingStatus === 'Delivered').length;
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            outboundOrderId: orderId,
            deliveryStatus: delivCount === 2 ? 'Delivered' : 'PartiallyDelivered',
            totalPackages: 2,
            deliveredPackages: delivCount,
            inTransitPackages: 2 - delivCount,
            exceptionPackages: shipment.openExceptionsCount,
          }),
        });
      }

      if (url.endsWith(`/api/v1/outbound/dispatch/packages/${pkg1Id}/pod`)) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'pod-e2e-1',
            clientId: 'cli-e2e-745',
            facilityId: FACILITY.id,
            packageId: pkg1Id,
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

      if (url.includes('/api/v1/outbound/dispatch/exceptions?')) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ items: [exceptionRow], totalCount: 1, pageNumber: 1, pageSize: 25 }),
        });
      }

      if (url.endsWith(`/api/v1/outbound/dispatch/exceptions/${excId}/resolve`)) {
        exceptionRow = { ...exceptionRow, status: 'Resolved', resolution: 'Resolved' };
        shipment = { ...shipment, openExceptionsCount: 0 };
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            isSuccess: true,
            isDuplicate: false,
            exceptionId: excId,
            packageId: pkg2Id,
            shipmentId,
            orderId,
            trackingNumber: 'FDX-745-1002',
            carrierCode: 'SIM-FEDEX',
            exceptionType: 'AccessRestricted',
            status: 'Resolved',
            severity: 'Medium',
            packageTrackingStatus: 'OutForDelivery',
            orderDeliveryStatus: 'PartiallyDelivered',
            message: 'Resolved cleanly',
          }),
        });
      }

      if (url.endsWith(`/api/v1/outbound/dispatch/shipments/${shipmentId}/acknowledge-custody`)) {
        shipment = { ...shipment, custodyStatus: 'Confirmed', version: 3 };
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            isSuccess: true,
            shipmentId,
            shipmentNumber: shipment.shipmentNumber,
            assignedDriverUserId: 'drv-e2e-745',
            custodyStatus: 'Confirmed',
            custodyTransferId: 'xfer-e2e-745',
            transferredAtUtc: '2026-11-24T11:00:00Z',
            version: 3,
            message: 'Handover confirmed',
          }),
        });
      }

      if (url.endsWith(`/api/v1/outbound/dispatch/packages/${pkg2Id}/pod`) && method === 'POST') {
        shipment = {
          ...shipment,
          packages: shipment.packages.map((p) =>
            p.packageId === pkg2Id ? { ...p, hasActivePod: true } : p
          ),
        };
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            isSuccess: true,
            isDuplicate: false,
            proofOfDeliveryId: 'pod-e2e-2',
            packageId: pkg2Id,
            packageNumber: 'PKG-745-E2E-2',
            trackingNumber: 'FDX-745-1002',
            carrierCode: 'SIM-FEDEX',
            isStorageFinalized: true,
            message: 'POD submitted',
          }),
        });
      }

      if (url.endsWith(`/api/v1/outbound/dispatch/packages/${pkg2Id}/confirm-delivery`)) {
        shipment = {
          ...shipment,
          deliveryStatus: 'Delivered',
          deliveredPackagesCount: 2,
          packages: shipment.packages.map((p) => ({ ...p, trackingStatus: 'Delivered' })),
        };
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            isSuccess: true,
            isAlreadyDelivered: false,
            packageId: pkg2Id,
            packageNumber: 'PKG-745-E2E-2',
            trackingNumber: 'FDX-745-1002',
            carrierCode: 'SIM-FEDEX',
            proofOfDeliveryId: 'pod-e2e-2',
            deliveredAtUtc: '2026-11-24T11:05:00Z',
            packageTrackingStatus: 'Delivered',
            orderDeliveryStatus: 'Delivered',
            outboundOrderId: orderId,
            orderNumber: 'SO-2026-745-1',
            shipmentId,
            message: 'Delivered cleanly',
          }),
        });
      }

      return route.fulfill({ status: 404, body: JSON.stringify({ message: `Unmocked ${method} ${url}` }) });
    });

    // 1. Desktop Staff Dispatch Board, Tracking Rollup & Exceptions
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('/app/transportation');
    await expect(page.getByTestId(`board-shipment-${shipmentId}`)).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);

    await page.getByTestId(`open-timeline-btn-${pkg1Id}`).click();
    await expect(page.getByTestId('order-delivery-rollup-card')).toContainText('PartiallyDelivered');
    await page.keyboard.press('Escape');
    await expect(page.getByTestId('tracking-timeline-modal')).toHaveCount(0);

    await page.getByTestId(`open-pod-btn-${pkg1Id}`).click();
    await expect(page.getByTestId('pod-signature-preview')).toBeVisible();
    await page.keyboard.press('Escape');

    await page.getByTestId('tab-exceptions-queue').click();
    await expect(page.getByTestId(`exception-card-${excId}`)).toBeVisible();
    await page.getByTestId(`exc-resolve-btn-${excId}`).click();
    await page.getByTestId('exc-notes-input').fill('Access granted by site manager');
    await page.getByTestId('exc-action-submit-btn').click();
    await expect(page.getByTestId(`exception-card-${excId}`)).toContainText('Resolved');

    // 2. Switch to Mobile Driver Courier Handheld (375x812 -> 320x568 -> 768x1024)
    authPersona = 'Driver';
    await page.setViewportSize({ width: 375, height: 812 });
    await page.reload();
    await expect(page.getByTestId('courier-handheld-panel')).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);

    await page.getByTestId('custody-ack-signature-input').fill('Dan Driver');
    await page.getByTestId(`acknowledge-custody-btn-${shipmentId}`).click();
    await expect(page.getByTestId(`custody-ack-gate-${shipmentId}`)).toHaveCount(0);

    await page.getByTestId(`courier-pod-btn-${pkg2Id}`).click();
    await page.getByTestId('pod-recipient-input').fill('Sarah Receiver');
    await page.getByTestId('pod-submit-btn').click();
    await expect(page.getByTestId('pod-preview-card')).toBeVisible();
    await page.getByTestId('pod-confirm-delivery-btn').click();
    await expect(page.getByTestId('pod-confirmation-banner')).toContainText('Delivered');

    for (const vp of [
      { width: 320, height: 568 },
      { width: 768, height: 1024 },
    ]) {
      await page.setViewportSize(vp);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    }

    expect(consoleErrors).toEqual([]);
  });
});
