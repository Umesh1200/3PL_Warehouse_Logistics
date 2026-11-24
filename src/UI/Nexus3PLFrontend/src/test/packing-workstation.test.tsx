import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { AuthProvider } from '../context/AuthContext';
import { FacilityProvider } from '../context/FacilityContext';
import { RouterProvider } from '../context/RouterContext';
import { Sidebar } from '../components/layout/Sidebar';
import { LaunchpadGrid } from '../components/dashboard/LaunchpadGrid';
import { PackingWorkstationPage } from '../pages/outbound/PackingWorkstationPage';
import { tokenManager } from '../services/apiClient';
import { UserType } from '../types/auth';
import {
  CarrierServiceLevel,
  HandlingUnitStatus,
  OutboundOrderLineStatus,
  OutboundOrderStatus,
  PackageStatus,
  PackagingMaterialType,
  PackingSessionStatus,
  ShippingLabelStatus,
} from '../types/outbound';

const FACILITY_A = {
  id: 'fac-pack-a',
  facilityCode: 'FAC-A',
  name: 'Chicago Packing Hub',
  city: 'Chicago',
  timezone: 'America/Chicago',
  isActive: true,
};

const sampleStation = {
  id: 'stn-743-1',
  locationCode: 'PACK-STN-01',
  barcode: 'PACK-STN-01',
  aisleCode: 'PK',
  isActive: true,
};

const sampleMaterial = {
  id: 'mat-743-1',
  clientId: null,
  facilityId: FACILITY_A.id,
  code: 'BOX-MED-01',
  name: 'Medium Corrugated Box',
  materialType: PackagingMaterialType.CorrugatedBox,
  lengthCm: 40,
  widthCm: 30,
  heightCm: 25,
  maxWeightKg: 20,
  tareWeightKg: 0.45,
  costPerUnit: 1.2,
};

const sampleOrder = {
  id: 'ord-743-1',
  clientId: 'cli-743-1',
  orderNumber: 'SO-2026-PACK-743',
  customerName: 'Apex Robotics',
  status: OutboundOrderStatus.InProgress,
  priority: 8,
  serviceLevel: CarrierServiceLevel.StandardGround,
  totalAllocatedUnits: 4,
  totalPickedUnits: 4,
  totalPackedUnits: 0,
};

function makeSessionDetail(overrides: Partial<any> = {}): any {
  return {
    id: 'sess-743-1',
    clientId: 'cli-743-1',
    facilityId: FACILITY_A.id,
    packingStationLocationId: sampleStation.id,
    packingStationLocationCode: sampleStation.locationCode,
    outboundOrderId: sampleOrder.id,
    orderNumber: sampleOrder.orderNumber,
    operatorUserId: 'pack-only-1',
    operatorEmail: 'packer@nexus.local',
    status: PackingSessionStatus.Active,
    startedAtUtc: '2026-11-24T05:00:00Z',
    completedAtUtc: null,
    activePackagesCount: 0,
    notes: null,
    orderLines: [
      {
        orderLineId: 'line-743-1',
        itemId: 'item-743-1',
        sku: 'SKU-SERVO-743',
        description: 'Servo Actuator Unit',
        quantityOrdered: 4,
        quantityAllocated: 4,
        quantityPicked: 4,
        quantityPacked: 0,
        remainingToPack: 4,
        lineStatus: OutboundOrderLineStatus.Allocated,
      },
    ],
    packages: [],
    pickedTotes: [
      {
        handlingUnitId: 'hu-tote-743',
        lpnCode: 'TOTE-743-01',
        status: HandlingUnitStatus.InUse,
        locationCode: 'PACK-STN-01',
        totalQuantity: 4,
      },
    ],
    ...overrides,
  };
}

function renderWithShell(ui: React.ReactElement, initialPath = '/app/outbound/packing') {
  return render(
    <AuthProvider>
      <FacilityProvider>
        <RouterProvider initialPath={initialPath}>{ui}</RouterProvider>
      </FacilityProvider>
    </AuthProvider>
  );
}

describe('Phase 7.4.3: Packing, Cartonization & Carrier Labeling Workstation', () => {
  beforeEach(() => {
    tokenManager.clearSession();
    tokenManager.setTokens('token-743', 'refresh-743');
    tokenManager.setActiveFacilityId(FACILITY_A.id);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    tokenManager.clearSession();
  });

  function mockBackend(options: {
    user: Record<string, any>;
    onFetch?: (url: string, init?: RequestInit) => Response | undefined | Promise<Response | undefined>;
  }) {
    const spy = vi.fn().mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      const custom = options.onFetch ? await options.onFetch(url, init) : undefined;
      if (custom) return custom;

      if (url.endsWith('/api/v1/auth/me')) {
        return { ok: true, status: 200, json: async () => options.user } as Response;
      }
      if (url.endsWith('/api/v1/facilities')) {
        return { ok: true, status: 200, json: async () => [FACILITY_A] } as Response;
      }
      if (url.startsWith('/api/v1/outbound/packing/stations')) {
        return { ok: true, status: 200, json: async () => [sampleStation] } as Response;
      }
      if (url.startsWith('/api/v1/outbound/packing/packaging-materials')) {
        return { ok: true, status: 200, json: async () => [sampleMaterial] } as Response;
      }
      if (url.startsWith('/api/v1/outbound/packing/packable-orders')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({ items: [sampleOrder], totalCount: 1, pageNumber: 1, pageSize: 50 }),
        } as Response;
      }
      if (url === `/api/v1/outbound/packing/sessions?facilityId=${FACILITY_A.id}&pageSize=25`) {
        return {
          ok: true,
          status: 200,
          json: async () => ({ items: [], totalCount: 0, pageNumber: 1, pageSize: 25 }),
        } as Response;
      }
      if (url.startsWith('/api/v1/outbound/shipments/eligible-packages')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({ items: [], totalCount: 0, pageNumber: 1, pageSize: 50 }),
        } as Response;
      }
      return { ok: false, status: 404, json: async () => ({ detail: `Unhandled ${url}` }) } as Response;
    });
    vi.stubGlobal('fetch', spy);
    return spy;
  }

  it('Pack-Only staff executes start -> recommend -> create -> idempotent pack -> close -> release tote -> complete without calling /shipping/*', async () => {
    let detail = makeSessionDetail();
    const packBodies: any[] = [];
    let firstPackFail = true;

    const fetchSpy = mockBackend({
      user: {
        userId: 'pack-only-1',
        email: 'packer@nexus.local',
        userType: UserType.Staff,
        roles: ['PackBenchOperator'],
        permissions: ['outbound:pack'],
      },
      onFetch: async (url, init) => {
        if (url === '/api/v1/outbound/packing/sessions/start' && init?.method === 'POST') {
          return {
            ok: true,
            status: 200,
            json: async () => ({
              isSuccess: true,
              sessionId: 'sess-743-1',
              status: PackingSessionStatus.Active,
              outboundOrderId: sampleOrder.id,
              orderNumber: sampleOrder.orderNumber,
              packingStationLocationId: sampleStation.id,
              stationLocationCode: sampleStation.locationCode,
              operatorUserId: 'pack-only-1',
              startedAtUtc: '2026-11-24T05:00:00Z',
              activePackagesCount: 0,
              message: 'Created new packing session.',
            }),
          } as Response;
        }
        if (url === '/api/v1/outbound/packing/sessions/sess-743-1') {
          return { ok: true, status: 200, json: async () => detail } as Response;
        }
        if (url === '/api/v1/outbound/packing/cartonization/recommend') {
          return {
            ok: true,
            status: 200,
            json: async () => ({
              isSuccess: true,
              recommendedPackageCount: 1,
              totalEstimatedGrossWeightKg: 6.45,
              totalEstimatedVolumeCm3: 4800,
              message: 'Recommended 1 carton: BOX-MED-01.',
              warnings: [],
              recommendedPackages: [
                {
                  packageIndex: 1,
                  packagingMaterialId: sampleMaterial.id,
                  packagingMaterialCode: sampleMaterial.code,
                  packagingMaterialName: sampleMaterial.name,
                  materialType: sampleMaterial.materialType,
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
                  volumeUtilizationPercentage: 16,
                  items: [
                    {
                      itemId: 'item-743-1',
                      sku: 'SKU-SERVO-743',
                      quantity: 4,
                      totalWeightKg: 6,
                      totalVolumeCm3: 4800,
                    },
                  ],
                },
              ],
            }),
          } as Response;
        }
        if (url === '/api/v1/outbound/packing/packages/create') {
          const pkg = {
            id: 'pkg-743-1',
            clientId: 'cli-743-1',
            facilityId: FACILITY_A.id,
            outboundOrderId: sampleOrder.id,
            orderNumber: sampleOrder.orderNumber,
            shipmentId: null,
            handlingUnitId: 'hu-ctn-1',
            handlingUnitLpn: 'CTN-SO-2026-PACK-743-01',
            packagingMaterialId: sampleMaterial.id,
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
            status: PackageStatus.Open,
            hasActiveLabel: false,
            latestLabelId: null,
          };
          detail = { ...detail, activePackagesCount: 1, packages: [pkg] };
          return {
            ok: true,
            status: 200,
            json: async () => ({
              isSuccess: true,
              packageId: pkg.id,
              packageNumber: pkg.packageNumber,
              handlingUnitId: pkg.handlingUnitId,
              handlingUnitLpn: pkg.handlingUnitLpn,
              status: PackageStatus.Open,
              packagingMaterialId: sampleMaterial.id,
              packagingMaterialCode: sampleMaterial.code,
              lengthCm: 40,
              widthCm: 30,
              heightCm: 25,
              tareWeightKg: 0.45,
              message: 'Carton created.',
            }),
          } as Response;
        }
        if (url === '/api/v1/outbound/packing/items/pack') {
          const body = JSON.parse(String(init?.body));
          packBodies.push(body);
          if (firstPackFail) {
            firstPackFail = false;
            return { ok: false, status: 409, json: async () => ({ detail: 'Transient tote version conflict' }) } as Response;
          }
          detail = {
            ...detail,
            orderLines: [{ ...detail.orderLines[0], quantityPacked: 4, remainingToPack: 0 }],
            pickedTotes: [{ ...detail.pickedTotes[0], totalQuantity: 0 }],
          };
          return {
            ok: true,
            status: 200,
            json: async () => ({
              isSuccess: true,
              packageId: 'pkg-743-1',
              packageNumber: 'PKG-SO-2026-PACK-743-01',
              packageLineId: 'pl-1',
              itemId: 'item-743-1',
              sku: 'SKU-SERVO-743',
              quantityPacked: 4,
              remainingToteQuantity: 0,
              remainingOrderLineDemand: 0,
              sourceToteLpn: 'TOTE-743-01',
              cartonLpn: 'CTN-SO-2026-PACK-743-01',
              inventoryTransactionId: 801,
              isOrderLineComplete: true,
              isOrderFullyPacked: true,
              message: 'Packed 4 units.',
            }),
          } as Response;
        }
        if (url === '/api/v1/outbound/packing/packages/pkg-743-1/close') {
          detail = {
            ...detail,
            packages: [{ ...detail.packages[0], status: PackageStatus.Packed, grossWeightKg: 6.45, netWeightKg: 6.0 }],
          };
          return {
            ok: true,
            status: 200,
            json: async () => ({
              isSuccess: true,
              packageId: 'pkg-743-1',
              packageNumber: 'PKG-SO-2026-PACK-743-01',
              grossWeightKg: 6.45,
              tareWeightKg: 0.45,
              netWeightKg: 6.0,
              volumetricWeightKg: 6.0,
              status: PackageStatus.Packed,
              message: 'Package sealed.',
            }),
          } as Response;
        }
        if (url === '/api/v1/outbound/packing/totes/release') {
          detail = {
            ...detail,
            pickedTotes: [{ ...detail.pickedTotes[0], status: HandlingUnitStatus.Available }],
          };
          return {
            ok: true,
            status: 200,
            json: async () => ({
              isSuccess: true,
              handlingUnitId: 'hu-tote-743',
              lpnCode: 'TOTE-743-01',
              status: HandlingUnitStatus.Available,
              message: "Tote 'TOTE-743-01' returned to Available pool.",
            }),
          } as Response;
        }
        if (url === '/api/v1/outbound/packing/sessions/sess-743-1/complete') {
          detail = { ...detail, status: PackingSessionStatus.Completed };
          return {
            ok: true,
            status: 200,
            json: async () => ({
              isSuccess: true,
              sessionId: 'sess-743-1',
              status: PackingSessionStatus.Completed,
              completedAtUtc: '2026-11-24T05:10:00Z',
              totalPackagesCount: 1,
              totalWeightKg: 6.45,
              message: 'Session completed.',
            }),
          } as Response;
        }
        return undefined;
      },
    });

    renderWithShell(<PackingWorkstationPage />);

    // Start session
    await waitFor(() => {
      expect((screen.getByTestId('start-packing-session-btn') as HTMLButtonElement).disabled).toBe(false);
    });
    fireEvent.click(screen.getByTestId('start-packing-session-btn'));

    // Run Cartonization & apply recommended box
    fireEvent.click(await screen.findByTestId('recommend-cartonization-btn'));
    fireEvent.click(await screen.findByTestId('apply-rec-box-btn-1'));
    expect(await screen.findByTestId('pack-item-form')).toBeDefined();

    // Pack-Only hides shipping actions and shows ship-permission notice
    expect(await screen.findByTestId('carrier-ship-permission-notice')).toBeDefined();

    // Pack item: first click hits 409; retry preserves exact same idempotencyKey!
    fireEvent.change(screen.getByTestId('pack-scan-tote'), { target: { value: 'TOTE-743-01' } });
    fireEvent.change(screen.getByTestId('pack-scan-item'), { target: { value: 'SKU-SERVO-743' } });
    fireEvent.change(screen.getByTestId('pack-qty-input'), { target: { value: '4' } });
    fireEvent.click(screen.getByTestId('pack-item-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('packing-feedback-banner').textContent).toContain('Transient tote version conflict');
    });

    fireEvent.click(screen.getByTestId('pack-item-btn'));
    await waitFor(() => {
      expect(screen.getByTestId('packing-feedback-banner').textContent).toContain('Tx #801');
    });
    expect(packBodies).toHaveLength(2);
    expect(packBodies[0].idempotencyKey).toBeTruthy();
    expect(packBodies[1].idempotencyKey).toBe(packBodies[0].idempotencyKey);

    // Seal carton, release empty tote, complete session
    fireEvent.change(screen.getByTestId('close-package-weight-input'), { target: { value: '6.45' } });
    fireEvent.click(screen.getByTestId('close-package-btn'));
    await waitFor(() => {
      expect(screen.getByTestId('packing-feedback-banner').textContent).toMatch(/Sealed carton/i);
    });

    fireEvent.click(await screen.findByTestId('release-tote-btn-TOTE-743-01'));
    await waitFor(() => {
      expect(screen.getByTestId('packing-feedback-banner').textContent).toContain('Available pool');
    });

    fireEvent.click(screen.getByTestId('complete-session-btn'));
    await waitFor(() => {
      expect(screen.getByTestId('packing-feedback-banner').textContent).toMatch(/Packing session completed/i);
    });

    const calledUrls = fetchSpy.mock.calls.map(([u]) => String(u));
    expect(calledUrls.some((u) => u.startsWith('/api/v1/outbound/shipping/'))).toBe(false);
  });

  it('Ship-Only staff manages rates, label failure recovery, idempotent label generation, and void without calling /packing/*', async () => {
    let currentPkg: any = {
      id: 'pkg-ship-1',
      clientId: 'cli-1',
      facilityId: FACILITY_A.id,
      outboundOrderId: 'ord-1',
      orderNumber: 'SO-SHIP-ONLY-1',
      shipmentId: null,
      handlingUnitId: 'hu-1',
      handlingUnitLpn: 'CTN-SHIP-1',
      packageNumber: 'PKG-SHIP-01',
      cartonSequence: 1,
      grossWeightKg: 5.5,
      tareWeightKg: 0.5,
      netWeightKg: 5.0,
      lengthCm: 40,
      widthCm: 30,
      heightCm: 20,
      volumetricWeightKg: 4.8,
      carrierCode: null,
      trackingNumber: null,
      status: PackageStatus.Packed,
      hasActiveLabel: false,
      latestLabelId: null,
    };
    const labelBodies: any[] = [];
    let generateAttempts = 0;

    const fetchSpy = mockBackend({
      user: {
        userId: 'ship-only-1',
        email: 'shipper@nexus.local',
        userType: UserType.Staff,
        roles: ['ShippingDesk'],
        permissions: ['outbound:ship'],
      },
      onFetch: async (url, init) => {
        if (url.startsWith('/api/v1/outbound/shipments/eligible-packages')) {
          return {
            ok: true,
            status: 200,
            json: async () => ({ items: [currentPkg], totalCount: 1, pageNumber: 1, pageSize: 50 }),
          } as Response;
        }
        if (url === '/api/v1/outbound/shipping/rates') {
          return {
            ok: true,
            status: 200,
            json: async () => [
              {
                carrierCode: 'SIM-UPS',
                carrierName: 'UPS Ground (Simulated)',
                serviceLevel: CarrierServiceLevel.StandardGround,
                serviceDescription: 'UPS Ground - 3 Days',
                totalAmount: 20.08,
                currency: 'USD',
                estimatedDeliveryDays: 3,
                isGuaranteed: false,
              },
            ],
          } as Response;
        }
        if (url === '/api/v1/outbound/shipping/labels/generate') {
          generateAttempts++;
          const body = JSON.parse(String(init?.body));
          labelBodies.push(body);
          if (generateAttempts === 1) {
            return {
              ok: true,
              status: 200,
              json: async () => ({
                isSuccess: false,
                packageId: currentPkg.id,
                packageNumber: currentPkg.packageNumber,
                labelId: '00000000-0000-0000-0000-000000000000',
                trackingNumber: '',
                carrierCode: body.carrierCode,
                serviceLevel: body.serviceLevel,
                labelFormat: 'ZPL',
                labelData: '',
                rateCharged: 0,
                packageStatus: PackageStatus.Packed,
                labelStatus: ShippingLabelStatus.Generated,
                isSimulated: true,
                message: 'Carrier rejected request.',
                errorMessage: 'Simulated upstream timeout',
              }),
            } as Response;
          }
          currentPkg = {
            ...currentPkg,
            status: PackageStatus.LabelPrinted,
            carrierCode: 'SIM-UPS',
            trackingNumber: '1Z999AA101',
            hasActiveLabel: true,
            latestLabelId: 'lbl-743-1',
          };
          return {
            ok: true,
            status: 200,
            json: async () => ({
              isSuccess: true,
              packageId: currentPkg.id,
              packageNumber: currentPkg.packageNumber,
              labelId: 'lbl-743-1',
              trackingNumber: '1Z999AA101',
              carrierCode: 'SIM-UPS',
              serviceLevel: CarrierServiceLevel.StandardGround,
              labelFormat: 'ZPL',
              labelData: '^XA^FO50,50^FD1Z999AA101^FS^XZ',
              routingCode: 'UPS-GND-743',
              rateCharged: 20.08,
              packageStatus: PackageStatus.LabelPrinted,
              labelStatus: ShippingLabelStatus.Generated,
              isSimulated: true,
              message: 'Label generated.',
            }),
          } as Response;
        }
        if (url === '/api/v1/outbound/shipping/labels/lbl-743-1/void') {
          currentPkg = {
            ...currentPkg,
            status: PackageStatus.Packed,
            carrierCode: null,
            trackingNumber: null,
            hasActiveLabel: false,
            latestLabelId: null,
          };
          return {
            ok: true,
            status: 200,
            json: async () => ({
              isSuccess: true,
              labelId: 'lbl-743-1',
              packageId: currentPkg.id,
              trackingNumber: '1Z999AA101',
              labelStatus: ShippingLabelStatus.Voided,
              packageStatus: PackageStatus.Packed,
              voidConfirmationNumber: 'VOID-991',
              message: 'Voided.',
            }),
          } as Response;
        }
        return undefined;
      },
    });

    renderWithShell(<PackingWorkstationPage />);
    expect(await screen.findByTestId('ship-only-label-desk')).toBeDefined();

    fireEvent.click(await screen.findByTestId('quote-rates-btn'));
    fireEvent.click(await screen.findByTestId('select-rate-SIM-UPS-StandardGround'));

    // First generate fails cleanly preserving idempotencyKey and Packed status
    fireEvent.click(screen.getByTestId('generate-label-btn'));
    expect((await screen.findByTestId('carrier-panel-error')).textContent).toContain('Simulated upstream timeout');

    // Retry reuses exact same idempotencyKey and succeeds -> shows ZPL preview
    fireEvent.click(screen.getByTestId('generate-label-btn'));
    expect((await screen.findByTestId('active-tracking-number')).textContent).toBe('1Z999AA101');
    expect((await screen.findByTestId('label-payload-preview')).textContent).toContain('^XA^FO50,50^FD1Z999AA101');
    expect(labelBodies).toHaveLength(2);
    expect(labelBodies[0].idempotencyKey).toBe(labelBodies[1].idempotencyKey);

    // Void label reverts to Packed
    fireEvent.click(screen.getByTestId('open-void-label-btn'));
    fireEvent.change(screen.getByTestId('void-label-reason-input'), { target: { value: 'Customer rate change' } });
    fireEvent.click(screen.getByTestId('confirm-void-label-btn'));
    await waitFor(() => {
      expect(screen.getByTestId('packing-feedback-banner').textContent).toContain('Voided label 1Z999AA101');
    });

    const calledUrls = fetchSpy.mock.calls.map(([u]) => String(u));
    expect(calledUrls.some((u) => u.startsWith('/api/v1/outbound/packing/'))).toBe(false);
  });

  it('rejects ClientAdmin, Driver (even with legacy claims), and ViewOnly Staff with 0 packing/shipping calls', async () => {
    for (const badUser of [
      {
        userId: 'client-legacy',
        email: 'c@t.com',
        userType: UserType.Client,
        tenantId: 'c1',
        roles: ['ClientAdmin'],
        permissions: ['outbound:pack', 'outbound:ship'],
      },
      {
        userId: 'driver-legacy',
        email: 'd@t.com',
        userType: UserType.Driver,
        roles: ['Driver'],
        permissions: ['outbound:pack', 'outbound:ship'],
      },
      {
        userId: 'view-only-staff',
        email: 'v@t.com',
        userType: UserType.Staff,
        roles: ['Viewer'],
        permissions: ['outbound:orders:view', 'outbound:tasks:view'],
      },
    ]) {
      const fetchSpy = mockBackend({ user: badUser });
      const { unmount } = renderWithShell(
        <div>
          <Sidebar />
          <LaunchpadGrid />
          <PackingWorkstationPage />
        </div>
      );

      expect(await screen.findByTestId('packing-forbidden-card')).toBeDefined();
      expect(screen.queryByTestId('launchpad-packing')).toBeNull();
      expect(screen.queryByText('Packing & Cartonization')).toBeNull();

      const calledUrls = fetchSpy.mock.calls.map(([u]) => String(u));
      expect(calledUrls.some((u) => u.includes('/packing/') || u.includes('/shipping/'))).toBe(false);
      unmount();
    }
  });

  it('fails closed on malformed packing session DTOs and surfaces distinct error retry banner', async () => {
    let returnMalformed = true;
    mockBackend({
      user: {
        userId: 'pack-err',
        email: 'packer@nexus.local',
        userType: UserType.Staff,
        roles: ['PackBenchOperator'],
        permissions: ['outbound:pack'],
      },
      onFetch: async (url) => {
        if (url.startsWith('/api/v1/outbound/packing/sessions?')) {
          return {
            ok: true,
            status: 200,
            json: async () => ({
              items: returnMalformed
                ? [
                    {
                      id: 's-bad',
                      facilityId: FACILITY_A.id,
                      outboundOrderId: 'ord-1',
                      orderNumber: 'SO-BAD',
                      status: 'BogusPackingStatus',
                    },
                  ]
                : [],
              totalCount: returnMalformed ? 1 : 0,
              pageNumber: 1,
              pageSize: 25,
            }),
          } as Response;
        }
        return undefined;
      },
    });

    renderWithShell(<PackingWorkstationPage />);
    expect((await screen.findByTestId('packing-load-error')).textContent).toMatch(/PackingSessionStatus/i);

    returnMalformed = false;
    fireEvent.click(screen.getByTestId('packing-retry-btn'));
    expect(await screen.findByTestId('packing-sessions-empty')).toBeDefined();
  });
});
