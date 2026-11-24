import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { AuthProvider } from '../context/AuthContext';
import { FacilityProvider } from '../context/FacilityContext';
import { RouterProvider } from '../context/RouterContext';
import { Sidebar } from '../components/layout/Sidebar';
import { LaunchpadGrid } from '../components/dashboard/LaunchpadGrid';
import { ShipmentConsolidationPage } from '../pages/outbound/ShipmentConsolidationPage';
import { tokenManager } from '../services/apiClient';
import { outboundApi } from '../services/outboundApi';
import { UserType } from '../types/auth';
import {
  CarrierServiceLevel,
  PackageStatus,
  ShipmentCustodyStatus,
  ShipmentStatus,
} from '../types/outbound';

const FACILITY_A = {
  id: 'fac-ship-a',
  facilityCode: 'FAC-CHI',
  name: 'Chicago Dispatch Hub',
  city: 'Chicago',
  timezone: 'America/Chicago',
  isActive: true,
};

const CLIENT_A = {
  id: 'cli-ship-a',
  accountCode: 'ALP-744',
  companyName: 'Apex Freight Corp',
  status: 'Active',
};

const STAGING_LOC = {
  id: 'loc-stg-1',
  locationCode: 'STG-OUT-01',
  barcode: 'STG-OUT-01',
  locationType: 'StagingLane',
  aisleCode: 'STG',
  isActive: true,
};

const DRIVER_A = {
  id: 'drv-744-a',
  firstName: 'Dan',
  lastName: 'Driver',
  email: 'dan.driver@nexus3pl.local',
  assignedFacilityId: FACILITY_A.id,
  clientId: null,
};

const ELIGIBLE_PKG_READY = {
  id: 'pkg-744-ready',
  clientId: CLIENT_A.id,
  facilityId: FACILITY_A.id,
  outboundOrderId: 'ord-744-1',
  orderNumber: 'SO-744-001',
  shipmentId: null,
  handlingUnitId: 'hu-744-1',
  handlingUnitLpn: 'CTN-744-01',
  packageNumber: 'PKG-744-001',
  cartonSequence: 1,
  grossWeightKg: 6.5,
  tareWeightKg: 0.4,
  netWeightKg: 6.1,
  lengthCm: 40,
  widthCm: 30,
  heightCm: 25,
  volumetricWeightKg: 6.0,
  carrierCode: 'SIM-FEDEX',
  trackingNumber: 'TRK-744-001',
  status: PackageStatus.LabelPrinted,
  hasActiveLabel: true,
  latestLabelId: 'lbl-744-1',
};

const ELIGIBLE_PKG_NO_LABEL = {
  ...ELIGIBLE_PKG_READY,
  id: 'pkg-744-nolbl',
  packageNumber: 'PKG-744-NOLBL',
  carrierCode: null,
  trackingNumber: null,
  status: PackageStatus.Packed,
  hasActiveLabel: false,
  latestLabelId: null,
};

const ELIGIBLE_PKG_UPS = {
  ...ELIGIBLE_PKG_READY,
  id: 'pkg-744-ups',
  packageNumber: 'PKG-744-UPS',
  carrierCode: 'SIM-UPS',
  trackingNumber: '1Z-UPS-744',
};

function renderWithShell(ui: React.ReactElement, initialPath = '/app/outbound/shipping') {
  return render(
    <AuthProvider>
      <FacilityProvider>
        <RouterProvider initialPath={initialPath}>{ui}</RouterProvider>
      </FacilityProvider>
    </AuthProvider>
  );
}

describe('Phase 7.4.4: Shipment Consolidation, BOL & Dispatch Custody UI', () => {
  beforeEach(() => {
    tokenManager.clearSession();
    tokenManager.setTokens('token-744', 'refresh-744');
    tokenManager.setActiveFacilityId(FACILITY_A.id);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    tokenManager.clearSession();
  });

  function mockShipBackend(
    user: Record<string, any>,
    customHandler?: (url: string, init?: RequestInit) => Response | Promise<Response | undefined> | undefined
  ) {
    const spy = vi.fn().mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (customHandler) {
        const res = await customHandler(url, init);
        if (res) return res;
      }

      if (url.endsWith('/api/v1/auth/me')) {
        return { ok: true, status: 200, json: async () => user } as Response;
      }
      if (url.endsWith('/api/v1/facilities')) {
        return { ok: true, status: 200, json: async () => [FACILITY_A] } as Response;
      }
      if (url.includes('/api/v1/outbound/shipments/staging-locations')) {
        return { ok: true, status: 200, json: async () => [STAGING_LOC] } as Response;
      }
      if (url.includes('/api/v1/outbound/shipments/eligible-drivers')) {
        return { ok: true, status: 200, json: async () => [DRIVER_A] } as Response;
      }
      if (url.includes('/api/v1/outbound/shipments/reference-clients')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({ items: [CLIENT_A], totalCount: 1, pageNumber: 1, pageSize: 100 }),
        } as Response;
      }
      if (url.includes('/api/v1/outbound/shipments/eligible-packages')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            items: [ELIGIBLE_PKG_READY, ELIGIBLE_PKG_NO_LABEL, ELIGIBLE_PKG_UPS],
            totalCount: 3,
            pageNumber: 1,
            pageSize: 40,
          }),
        } as Response;
      }
      if (url.match(/\/api\/v1\/outbound\/shipments(\?|$)/)) {
        return {
          ok: true,
          status: 200,
          json: async () => ({ items: [], totalCount: 0, pageNumber: 1, pageSize: 30 }),
        } as Response;
      }

      return { ok: false, status: 404, json: async () => ({ message: `Unmocked ${url}` }) } as Response;
    });
    vi.spyOn(globalThis, 'fetch').mockImplementation(spy as any);
    return spy;
  }

  it('hides Shipment Consolidation nav & launchpad from Client, Driver, and PackOnly users and fires zero shipment API calls', async () => {
    for (const deniedUser of [
      {
        userId: 'cli-user',
        email: 'client@apex.test',
        firstName: 'Client',
        lastName: 'Admin',
        userType: UserType.Client,
        clientId: CLIENT_A.id,
        roles: ['ClientAdmin'],
        permissions: ['outbound:ship', 'dispatch:view', 'outbound:orders:view'],
      },
      {
        userId: 'drv-user',
        email: 'driver@apex.test',
        firstName: 'Dan',
        lastName: 'Driver',
        userType: UserType.Driver,
        roles: ['Driver'],
        permissions: ['dispatch:execute-delivery'],
      },
      {
        userId: 'pack-only',
        email: 'pack@apex.test',
        firstName: 'Pam',
        lastName: 'Pack',
        userType: UserType.Staff,
        assignedFacilityId: FACILITY_A.id,
        roles: ['Packer'],
        permissions: ['outbound:pack'],
      },
    ]) {
      const fetchSpy = mockShipBackend(deniedUser);
      const { unmount } = renderWithShell(
        <>
          <Sidebar />
          <LaunchpadGrid />
          <ShipmentConsolidationPage />
        </>
      );

      await waitFor(() => {
        expect(screen.getByTestId('shipping-access-denied')).toBeTruthy();
      });

      expect(screen.queryByTestId('launchpad-shipment-consolidation')).toBeNull();
      expect(screen.queryByText('Shipment Consolidation')).toBeNull();
      const shipCalls = fetchSpy.mock.calls.filter(([u]) =>
        String(u).includes('/api/v1/outbound/shipments')
      );
      expect(shipCalls).toHaveLength(0);
      unmount();
    }
  });

  it('shows Shipment Consolidation to authorized Staff and executes Create -> Add -> Stage -> Load -> BOL -> Driver Custody Handover', async () => {
    let shipmentState: any = null;
    let bolState: any = null;
    const dispatchPayloads: any[] = [];

    mockShipBackend(
      {
        userId: 'ship-lead-1',
        email: 'ship@nexus3pl.local',
        firstName: 'Sam',
        lastName: 'ShipLead',
        userType: UserType.Staff,
        assignedFacilityId: FACILITY_A.id,
        roles: ['ShippingCoordinator'],
        permissions: ['outbound:ship'],
      },
      async (url, init) => {
        const method = init?.method || 'GET';

        if (url.match(/\/api\/v1\/outbound\/shipments(\?|$)/) && method === 'GET') {
          return {
            ok: true,
            status: 200,
            json: async () => ({
              items: shipmentState ? [shipmentState] : [],
              totalCount: shipmentState ? 1 : 0,
              pageNumber: 1,
              pageSize: 30,
            }),
          } as Response;
        }

        if (url.endsWith('/api/v1/outbound/shipments') && method === 'POST') {
          shipmentState = {
            id: 'shp-744-1',
            clientId: CLIENT_A.id,
            clientName: CLIENT_A.companyName,
            facilityId: FACILITY_A.id,
            shipmentNumber: 'SHP-FEDEX-20261124-001',
            carrierCode: 'SIM-FEDEX',
            carrierServiceLevel: CarrierServiceLevel.StandardGround,
            status: ShipmentStatus.Draft,
            stagingLocationId: STAGING_LOC.id,
            stagingLocationCode: STAGING_LOC.locationCode,
            custodyStatus: ShipmentCustodyStatus.Unassigned,
            totalPackagesCount: 0,
            totalWeightKg: 0,
            totalVolumeCm3: 0,
            packages: [],
          };
          return {
            ok: true,
            status: 200,
            json: async () => ({
              isSuccess: true,
              shipmentId: shipmentState.id,
              shipmentNumber: shipmentState.shipmentNumber,
              status: ShipmentStatus.Draft,
              totalPackagesCount: 0,
              message: 'Created',
            }),
          } as Response;
        }

        if (url.endsWith('/api/v1/outbound/shipments/shp-744-1') && method === 'GET') {
          return { ok: true, status: 200, json: async () => shipmentState } as Response;
        }

        if (url.includes('/api/v1/outbound/shipments/eligible-packages')) {
          const items = shipmentState?.packages?.length
            ? [ELIGIBLE_PKG_NO_LABEL, ELIGIBLE_PKG_UPS]
            : [ELIGIBLE_PKG_READY, ELIGIBLE_PKG_NO_LABEL, ELIGIBLE_PKG_UPS];
          return {
            ok: true,
            status: 200,
            json: async () => ({ items, totalCount: items.length, pageNumber: 1, pageSize: 40 }),
          } as Response;
        }

        if (url.endsWith('/api/v1/outbound/shipments/shp-744-1/packages') && method === 'POST') {
          shipmentState = {
            ...shipmentState,
            totalPackagesCount: 1,
            totalWeightKg: 6.5,
            totalVolumeCm3: 30000,
            packages: [
              {
                packageId: ELIGIBLE_PKG_READY.id,
                packageNumber: ELIGIBLE_PKG_READY.packageNumber,
                trackingNumber: ELIGIBLE_PKG_READY.trackingNumber,
                carrierCode: ELIGIBLE_PKG_READY.carrierCode,
                grossWeightKg: 6.5,
                status: PackageStatus.LabelPrinted,
                outboundOrderId: ELIGIBLE_PKG_READY.outboundOrderId,
                orderNumber: ELIGIBLE_PKG_READY.orderNumber,
                currentLocationCode: 'PACK-STN-01',
              },
            ],
          };
          return {
            ok: true,
            status: 200,
            json: async () => ({
              isSuccess: true,
              shipmentId: shipmentState.id,
              packageId: ELIGIBLE_PKG_READY.id,
              packageNumber: ELIGIBLE_PKG_READY.packageNumber,
              totalPackagesCount: 1,
              totalGrossWeightKg: 6.5,
              totalVolumeCm3: 30000,
              message: 'Consolidated',
            }),
          } as Response;
        }

        if (url.endsWith(`/api/v1/outbound/shipments/packages/${ELIGIBLE_PKG_READY.id}/stage`) && method === 'POST') {
          shipmentState = {
            ...shipmentState,
            packages: shipmentState.packages.map((p: any) => ({
              ...p,
              status: PackageStatus.Staged,
              currentLocationCode: STAGING_LOC.locationCode,
            })),
          };
          return {
            ok: true,
            status: 200,
            json: async () => ({
              isSuccess: true,
              packageId: ELIGIBLE_PKG_READY.id,
              packageNumber: ELIGIBLE_PKG_READY.packageNumber,
              packageStatus: PackageStatus.Staged,
              stagingLocationCode: STAGING_LOC.locationCode,
              message: 'Staged',
            }),
          } as Response;
        }

        if (url.endsWith(`/api/v1/outbound/shipments/packages/${ELIGIBLE_PKG_READY.id}/load`) && method === 'POST') {
          shipmentState = {
            ...shipmentState,
            status: ShipmentStatus.ReadyForDispatch,
            trailerNumber: 'TRL-744',
            packages: shipmentState.packages.map((p: any) => ({
              ...p,
              status: PackageStatus.Loaded,
            })),
          };
          return {
            ok: true,
            status: 200,
            json: async () => ({
              isSuccess: true,
              packageId: ELIGIBLE_PKG_READY.id,
              packageNumber: ELIGIBLE_PKG_READY.packageNumber,
              packageStatus: PackageStatus.Loaded,
              shipmentStatus: ShipmentStatus.ReadyForDispatch,
              message: 'Loaded',
            }),
          } as Response;
        }

        if (url.endsWith('/api/v1/outbound/shipments/shp-744-1/bol') && method === 'POST') {
          const snapshot = JSON.stringify({
            BolNumber: 'BOL-SHP-FEDEX-20261124-001',
            Packages: [{ PackageNumber: 'PKG-744-001', TrackingNumber: 'TRK-744-001', WeightKg: 6.5, Dimensions: '40x30x25 cm' }],
          });
          bolState = {
            id: 'bol-744-1',
            clientId: CLIENT_A.id,
            facilityId: FACILITY_A.id,
            shipmentId: shipmentState.id,
            bolNumber: 'BOL-SHP-FEDEX-20261124-001',
            carrierCode: 'SIM-FEDEX',
            carrierName: 'SIM-FEDEX',
            serviceLevel: 'StandardGround',
            trailerNumber: 'TRL-744',
            sealNumber: 'SEAL-744',
            totalPackageCount: 1,
            totalGrossWeightKg: 6.5,
            totalVolumeCm3: 30000,
            shipperName: FACILITY_A.name,
            shipperAddressSnapshot: '100 Distribution Way, Columbus, OH 43215',
            consigneeName: 'Apex Consignee',
            consigneeAddressSnapshot: '500 Commerce Blvd, Columbus, OH 43215',
            packageTrackingNumbers: 'TRK-744-001',
            specialInstructions: 'Fragile freight',
            documentSnapshotJson: snapshot,
            issuedAtUtc: '2026-11-24T09:00:00Z',
            isVoided: false,
          };
          shipmentState = {
            ...shipmentState,
            billOfLadingId: bolState.id,
            billOfLadingNumber: bolState.bolNumber,
            sealNumber: 'SEAL-744',
          };
          return {
            ok: true,
            status: 200,
            json: async () => ({
              isSuccess: true,
              bolId: bolState.id,
              bolNumber: bolState.bolNumber,
              shipmentId: shipmentState.id,
              totalPackageCount: 1,
              totalGrossWeightKg: 6.5,
              carrierCode: 'SIM-FEDEX',
              issuedAtUtc: bolState.issuedAtUtc,
              documentSnapshotJson: snapshot,
              message: 'Issued',
            }),
          } as Response;
        }

        if (url.endsWith('/api/v1/outbound/shipments/bol/bol-744-1') && method === 'GET') {
          return { ok: true, status: 200, json: async () => bolState } as Response;
        }

        if (url.endsWith('/api/v1/outbound/shipments/shp-744-1/dispatch') && method === 'POST') {
          const parsed = JSON.parse(String(init?.body || '{}'));
          dispatchPayloads.push(parsed);
          shipmentState = {
            ...shipmentState,
            status: ShipmentStatus.Dispatched,
            custodyStatus: ShipmentCustodyStatus.Confirmed,
            assignedDriverUserId: parsed.assignedDriverUserId,
            dispatchedAtUtc: '2026-11-24T09:05:00Z',
          };
          return {
            ok: true,
            status: 200,
            json: async () => ({
              isSuccess: true,
              shipmentId: shipmentState.id,
              shipmentNumber: shipmentState.shipmentNumber,
              status: ShipmentStatus.Dispatched,
              dispatchedAtUtc: shipmentState.dispatchedAtUtc,
              totalPackagesDispatched: 1,
              totalWeightKg: 6.5,
              driverName: parsed.driverName,
              assignedDriverUserId: parsed.assignedDriverUserId,
              billOfLadingNumber: shipmentState.billOfLadingNumber,
              completedOrderIds: ['ord-744-1'],
              message: 'Dispatched',
            }),
          } as Response;
        }

        return undefined;
      }
    );

    renderWithShell(
      <>
        <LaunchpadGrid />
        <ShipmentConsolidationPage />
      </>
    );

    await waitFor(() => {
      expect(screen.getByTestId('launchpad-shipment-consolidation')).toBeTruthy();
      expect(screen.getByTestId('toggle-create-shipment-btn')).toBeTruthy();
    });

    // 1. Create Draft Shipment (wait until reference clients hydrate and enable submit)
    fireEvent.click(screen.getByTestId('toggle-create-shipment-btn'));
    await waitFor(() => {
      expect((screen.getByTestId('submit-create-shipment-btn') as HTMLButtonElement).disabled).toBe(false);
    });
    fireEvent.click(screen.getByTestId('submit-create-shipment-btn'));
    await waitFor(() => {
      expect(screen.getByTestId('shipment-item-SHP-FEDEX-20261124-001')).toBeTruthy();
    });

    // Unlabeled and wrong-carrier cartons are blocked from consolidating
    expect((screen.getByTestId('add-eligible-pkg-PKG-744-NOLBL') as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getByTestId('add-eligible-pkg-PKG-744-UPS') as HTMLButtonElement).disabled).toBe(true);

    // 2. Consolidate labeled SIM-FEDEX carton
    fireEvent.click(screen.getByTestId('add-eligible-pkg-PKG-744-001'));
    await waitFor(() => {
      expect(screen.getByTestId('consolidated-row-PKG-744-001')).toBeTruthy();
    });

    // Load disabled until carton is staged; handover disabled until ReadyForDispatch
    expect((screen.getByTestId('load-pkg-PKG-744-001') as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getByTestId('open-handover-modal-btn') as HTMLButtonElement).disabled).toBe(true);

    // 3. Stage carton -> Load carton -> ReadyForDispatch
    fireEvent.click(screen.getByTestId('stage-pkg-PKG-744-001'));
    await waitFor(() => {
      expect((screen.getByTestId('load-pkg-PKG-744-001') as HTMLButtonElement).disabled).toBe(false);
    });

    fireEvent.change(screen.getByTestId('dock-trailer-input'), { target: { value: 'TRL-744' } });
    fireEvent.click(screen.getByTestId('load-pkg-PKG-744-001'));
    await waitFor(() => {
      expect((screen.getByTestId('open-handover-modal-btn') as HTMLButtonElement).disabled).toBe(false);
    });

    // 4. Issue Immutable BOL Snapshot
    fireEvent.click(screen.getByTestId('open-bol-modal-btn'));
    await waitFor(() => {
      expect(screen.getByTestId('bol-issue-form')).toBeTruthy();
    });
    fireEvent.click(screen.getByTestId('issue-bol-submit-btn'));
    await waitFor(() => {
      expect(screen.getByTestId('bol-snapshot-view')).toBeTruthy();
      expect(screen.getByText('Immutable Snapshot Issued')).toBeTruthy();
    });
    fireEvent.click(screen.getByText('Close'));

    // 5. Driver Custody Handover
    fireEvent.click(screen.getByTestId('open-handover-modal-btn'));
    await waitFor(() => {
      expect(screen.getByTestId('driver-handover-form')).toBeTruthy();
    });
    fireEvent.change(screen.getByTestId('handover-driver-select'), { target: { value: DRIVER_A.id } });
    fireEvent.change(screen.getByTestId('handover-driver-signature'), { target: { value: 'Dan Driver Signed' } });
    fireEvent.click(screen.getByTestId('confirm-dispatch-submit-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('shipment-dispatched-banner')).toBeTruthy();
    });
    expect(dispatchPayloads).toHaveLength(1);
    expect(dispatchPayloads[0].idempotencyKey).toMatch(/^disp-/);
    expect(dispatchPayloads[0].assignedDriverUserId).toBe(DRIVER_A.id);
  });

  it('rejects malformed shipment list DTO payloads cleanly', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ items: [{ shipmentNumber: 'MISSING-ID' }], totalCount: 1, pageNumber: 1, pageSize: 30 }),
    } as Response);

    await expect(outboundApi.getShipments({ facilityId: FACILITY_A.id })).rejects.toThrow(
      /malformed or incomplete shipment/i
    );
  });
});
