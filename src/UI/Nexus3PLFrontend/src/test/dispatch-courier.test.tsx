import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { AuthProvider } from '../context/AuthContext';
import { FacilityProvider } from '../context/FacilityContext';
import { RouterProvider } from '../context/RouterContext';
import { Sidebar } from '../components/layout/Sidebar';
import { LaunchpadGrid } from '../components/dashboard/LaunchpadGrid';
import { DispatchAndCourierPage } from '../pages/transportation/DispatchAndCourierPage';
import { tokenManager } from '../services/apiClient';
import { UserType } from '../types/auth';
import {
  CarrierServiceLevel,
  DeliveryExceptionSeverity,
  DeliveryExceptionSource,
  DeliveryExceptionStatus,
  DeliveryExceptionType,
  DeliveryExceptionResolution,
  OrderDeliveryStatus,
  PackageStatus,
  PodEvidenceSource,
  ShipmentCustodyStatus,
  ShipmentStatus,
  TrackingMilestoneStatus,
} from '../types/outbound';

const FACILITY_A = {
  id: 'fac-745-a',
  facilityCode: 'FAC-CHI',
  name: 'Chicago Freight Hub',
  city: 'Chicago',
  timezone: 'America/Chicago',
  isActive: true,
};

const SHIPMENT_1_ID = 'shp-745-1';
const PKG_1_ID = 'pkg-745-1';
const PKG_2_ID = 'pkg-745-2';
const ORDER_1_ID = 'ord-745-1';
const EXC_1_ID = 'exc-745-1';
const POD_1_ID = 'pod-745-1';

function renderWithShell(ui: React.ReactElement, initialPath = '/app/transportation') {
  return render(
    <AuthProvider>
      <FacilityProvider>
        <RouterProvider initialPath={initialPath}>{ui}</RouterProvider>
      </FacilityProvider>
    </AuthProvider>
  );
}

describe('Phase 7.4.5: Dispatch Board, Tracking, POD & Courier Exceptions UI', () => {
  beforeEach(() => {
    tokenManager.clearSession();
    tokenManager.setTokens('token-745', 'refresh-745');
    tokenManager.setActiveFacilityId(FACILITY_A.id);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    tokenManager.clearSession();
  });

  function setupFetchMock(
    userProfile: {
      userId: string;
      email: string;
      userType: UserType;
      permissions: string[];
      roles?: string[];
      tenantId?: string | null;
    }
  ) {
    let shipmentState = {
      shipmentId: SHIPMENT_1_ID,
      shipmentNumber: 'SHP-745-001',
      clientId: 'cli-745-a',
      clientName: 'Apex Freight Corp',
      facilityId: FACILITY_A.id,
      facilityName: FACILITY_A.name,
      carrierCode: 'SIM-FEDEX',
      carrierServiceLevel: CarrierServiceLevel.StandardGround,
      status: ShipmentStatus.Dispatched,
      custodyStatus: ShipmentCustodyStatus.PendingHandover,
      deliveryStatus: TrackingMilestoneStatus.OutForDelivery,
      billOfLadingNumber: 'BOL-SHP-745-001',
      totalPackagesCount: 2,
      deliveredPackagesCount: 0,
      openExceptionsCount: 1,
      totalWeightKg: 11.4,
      version: 2,
      assignedDriverUserId: 'drv-745-a',
      assignedDriverName: 'Dan Driver',
      assignedDriverEmail: 'dan.driver@nexus3pl.local',
      packages: [
        {
          packageId: PKG_1_ID,
          packageNumber: 'PKG-745-001',
          trackingNumber: 'FDX-745-001',
          carrierCode: 'SIM-FEDEX',
          packageStatus: PackageStatus.Loaded,
          trackingStatus: TrackingMilestoneStatus.OutForDelivery,
          outboundOrderId: ORDER_1_ID,
          orderNumber: 'SO-745-001',
          hasActivePod: true,
        },
        {
          packageId: PKG_2_ID,
          packageNumber: 'PKG-745-002',
          trackingNumber: 'FDX-745-002',
          carrierCode: 'SIM-FEDEX',
          packageStatus: PackageStatus.Loaded,
          trackingStatus: TrackingMilestoneStatus.OutForDelivery,
          outboundOrderId: ORDER_1_ID,
          orderNumber: 'SO-745-001',
          hasActivePod: false,
        },
      ],
    };

    let activePod: any = {
      id: POD_1_ID,
      clientId: 'cli-745-a',
      facilityId: FACILITY_A.id,
      packageId: PKG_1_ID,
      packageNumber: 'PKG-745-001',
      shipmentId: SHIPMENT_1_ID,
      trackingNumber: 'FDX-745-001',
      carrierCode: 'SIM-FEDEX',
      recipientName: 'Jane Receiver',
      deliveryLocationDescription: 'Dock Door 1',
      isSigned: true,
      isStorageFinalized: true,
      preSignedSignatureUrl: 'https://storage.local/sig1.png',
      preSignedPhotoUrl: 'https://storage.local/photo1.png',
      evidenceSource: PodEvidenceSource.DriverHandheld,
      capturedAtUtc: '2026-11-24T10:00:00Z',
      deliveredAtUtc: '2026-11-24T10:00:00Z',
      isSuperseded: false,
    };

    let exceptionRow = {
      exceptionId: EXC_1_ID,
      clientId: 'cli-745-a',
      facilityId: FACILITY_A.id,
      packageId: PKG_2_ID,
      packageNumber: 'PKG-745-002',
      shipmentId: SHIPMENT_1_ID,
      shipmentNumber: 'SHP-745-001',
      orderId: ORDER_1_ID,
      orderNumber: 'SO-745-001',
      trackingNumber: 'FDX-745-002',
      carrierCode: 'SIM-FEDEX',
      exceptionType: DeliveryExceptionType.RecipientUnavailable,
      status: DeliveryExceptionStatus.Open,
      severity: DeliveryExceptionSeverity.Medium,
      resolution: DeliveryExceptionResolution.PendingCarrierReattempt,
      source: DeliveryExceptionSource.DriverHandheld,
      carrierExceptionCode: 'NI',
      carrierDescription: 'Loading dock gate closed',
      occurredAtUtc: '2026-11-24T09:15:00Z',
      attemptNumber: 1,
      reattemptCount: 0,
      contactAttemptsCount: 0,
      createdAt: '2026-11-24T09:15:00Z',
    };

    const calls: Array<{ method: string; url: string; body?: any }> = [];

    const mockFetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      const method = (init?.method || 'GET').toUpperCase();
      const parsedBody =
        typeof init?.body === 'string' && init.body.startsWith('{') ? JSON.parse(init.body) : undefined;
      calls.push({ method, url, body: parsedBody });

      if (url.endsWith('/api/v1/auth/me')) {
        return new Response(
          JSON.stringify({
            userId: userProfile.userId,
            email: userProfile.email,
            firstName: 'Test',
            lastName: 'User',
            userType: userProfile.userType,
            tenantId: userProfile.tenantId ?? null,
            companyName: null,
            roles: userProfile.roles || ['TestRole'],
            permissions: userProfile.permissions,
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }

      if (url.endsWith('/api/v1/facilities')) {
        return new Response(JSON.stringify([FACILITY_A]), { status: 200 });
      }

      if (url.includes('/api/v1/outbound/dispatch/board')) {
        return new Response(
          JSON.stringify({
            items: [shipmentState],
            totalCount: 1,
            pageNumber: 1,
            pageSize: 25,
          }),
          { status: 200 }
        );
      }

      if (url.endsWith('/api/v1/outbound/dispatch/courier/shipments')) {
        return new Response(JSON.stringify([shipmentState]), { status: 200 });
      }

      if (url.includes('/api/v1/outbound/shipments/eligible-drivers')) {
        return new Response(
          JSON.stringify([
            {
              id: 'drv-745-a',
              firstName: 'Dan',
              lastName: 'Driver',
              email: 'dan.driver@nexus3pl.local',
              assignedFacilityId: FACILITY_A.id,
            },
          ]),
          { status: 200 }
        );
      }

      if (url.includes(`/api/v1/outbound/dispatch/packages/${PKG_1_ID}/timeline`)) {
        return new Response(
          JSON.stringify({
            packageId: PKG_1_ID,
            packageNumber: 'PKG-745-001',
            trackingNumber: 'FDX-745-001',
            carrierCode: 'SIM-FEDEX',
            currentTrackingStatus: shipmentState.packages[0].trackingStatus,
            events: [
              {
                id: 'evt-1',
                milestoneStatus: TrackingMilestoneStatus.OutForDelivery,
                carrierEventCode: 'OFD',
                carrierDescription: 'Out for final delivery',
                location: 'Chicago IL',
                occurredAtUtc: '2026-11-24T08:30:00Z',
                receivedAtUtc: '2026-11-24T08:31:00Z',
              },
            ],
          }),
          { status: 200 }
        );
      }

      if (url.includes(`/api/v1/outbound/dispatch/orders/${ORDER_1_ID}/delivery-status`)) {
        const deliveredCount = shipmentState.packages.filter(
          (p) => p.trackingStatus === TrackingMilestoneStatus.Delivered
        ).length;
        return new Response(
          JSON.stringify({
            outboundOrderId: ORDER_1_ID,
            deliveryStatus:
              deliveredCount === 2
                ? OrderDeliveryStatus.Delivered
                : deliveredCount === 1
                ? OrderDeliveryStatus.PartiallyDelivered
                : OrderDeliveryStatus.InTransit,
            totalPackages: 2,
            deliveredPackages: deliveredCount,
            inTransitPackages: 2 - deliveredCount,
            exceptionPackages: 0,
            deliveredAtUtc: deliveredCount === 2 ? '2026-11-24T12:00:00Z' : null,
          }),
          { status: 200 }
        );
      }

      if (url.endsWith(`/api/v1/outbound/dispatch/packages/${PKG_1_ID}/pod`) && method === 'GET') {
        return new Response(JSON.stringify(activePod), { status: 200 });
      }

      if (url.endsWith(`/api/v1/outbound/dispatch/packages/${PKG_2_ID}/pod`) && method === 'GET') {
        return new Response(JSON.stringify({ message: 'Not found' }), { status: 404 });
      }

      if (url.includes(`/api/v1/outbound/dispatch/pod/${POD_1_ID}/supersede`)) {
        activePod = {
          ...activePod,
          id: 'pod-745-superseded-new',
          recipientName: parsedBody.replacementSubmission.recipientName,
          evidenceSource: PodEvidenceSource.ManualVerification,
        };
        return new Response(
          JSON.stringify({
            isSuccess: true,
            isDuplicate: false,
            proofOfDeliveryId: activePod.id,
            packageId: PKG_1_ID,
            packageNumber: 'PKG-745-001',
            trackingNumber: 'FDX-745-001',
            carrierCode: 'SIM-FEDEX',
            isStorageFinalized: true,
            message: 'Superseded cleanly',
          }),
          { status: 200 }
        );
      }

      if (url.endsWith(`/api/v1/outbound/dispatch/shipments/${SHIPMENT_1_ID}/acknowledge-custody`)) {
        shipmentState = {
          ...shipmentState,
          custodyStatus: ShipmentCustodyStatus.Confirmed,
          version: 3,
        };
        return new Response(
          JSON.stringify({
            isSuccess: true,
            shipmentId: SHIPMENT_1_ID,
            shipmentNumber: shipmentState.shipmentNumber,
            assignedDriverUserId: userProfile.userId,
            custodyStatus: ShipmentCustodyStatus.Confirmed,
            custodyTransferId: 'xfer-745-1',
            transferredAtUtc: '2026-11-24T10:15:00Z',
            version: 3,
            message: 'Handover confirmed',
          }),
          { status: 200 }
        );
      }

      if (url.endsWith(`/api/v1/outbound/dispatch/packages/${PKG_2_ID}/pod`) && method === 'POST') {
        const isSignedReq =
          init?.body instanceof FormData ? Boolean(init.body.get('signature')) : Boolean(parsedBody?.signatureBase64);
        shipmentState = {
          ...shipmentState,
          packages: shipmentState.packages.map((p) =>
            p.packageId === PKG_2_ID ? { ...p, hasActivePod: true } : p
          ),
        };
        return new Response(
          JSON.stringify({
            isSuccess: true,
            isDuplicate: false,
            proofOfDeliveryId: 'pod-745-2',
            packageId: PKG_2_ID,
            packageNumber: 'PKG-745-002',
            trackingNumber: 'FDX-745-002',
            carrierCode: 'SIM-FEDEX',
            recipientName: 'Server Alice Official',
            deliveryLocationDescription: 'Bay 9',
            isSigned: isSignedReq,
            evidenceSource: PodEvidenceSource.DriverHandheld,
            capturedAtUtc: '2026-11-24T11:55:00Z',
            deliveredAtUtc: '2026-11-24T11:55:00Z',
            isStorageFinalized: true,
            message: 'POD submitted',
          }),
          { status: 200 }
        );
      }

      if (url.endsWith(`/api/v1/outbound/dispatch/packages/${PKG_2_ID}/confirm-delivery`)) {
        shipmentState = {
          ...shipmentState,
          deliveredPackagesCount: 2,
          deliveryStatus: TrackingMilestoneStatus.Delivered,
          packages: shipmentState.packages.map((p) => ({
            ...p,
            trackingStatus: TrackingMilestoneStatus.Delivered,
          })),
        };
        return new Response(
          JSON.stringify({
            isSuccess: true,
            isAlreadyDelivered: false,
            packageId: PKG_2_ID,
            packageNumber: 'PKG-745-002',
            trackingNumber: 'FDX-745-002',
            carrierCode: 'SIM-FEDEX',
            proofOfDeliveryId: 'pod-745-2',
            deliveredAtUtc: '2026-11-24T12:00:00Z',
            packageTrackingStatus: TrackingMilestoneStatus.Delivered,
            orderDeliveryStatus: OrderDeliveryStatus.Delivered,
            outboundOrderId: ORDER_1_ID,
            orderNumber: 'SO-745-001',
            shipmentId: SHIPMENT_1_ID,
            message: 'Delivered cleanly',
          }),
          { status: 200 }
        );
      }

      if (url.includes('/api/v1/outbound/dispatch/exceptions?')) {
        return new Response(
          JSON.stringify({
            items: [exceptionRow],
            totalCount: 1,
            pageNumber: 1,
            pageSize: 25,
          }),
          { status: 200 }
        );
      }

      if (url.endsWith(`/api/v1/outbound/dispatch/exceptions/${EXC_1_ID}/resolve`)) {
        exceptionRow = {
          ...exceptionRow,
          status: DeliveryExceptionStatus.Resolved,
          resolution: DeliveryExceptionResolution.Resolved,
        };
        return new Response(
          JSON.stringify({
            isSuccess: true,
            isDuplicate: false,
            exceptionId: EXC_1_ID,
            packageId: PKG_2_ID,
            shipmentId: SHIPMENT_1_ID,
            orderId: ORDER_1_ID,
            trackingNumber: 'FDX-745-002',
            carrierCode: 'SIM-FEDEX',
            exceptionType: DeliveryExceptionType.RecipientUnavailable,
            status: DeliveryExceptionStatus.Resolved,
            severity: DeliveryExceptionSeverity.Medium,
            packageTrackingStatus: TrackingMilestoneStatus.InTransit,
            orderDeliveryStatus: OrderDeliveryStatus.InTransit,
            message: 'Resolved',
          }),
          { status: 200 }
        );
      }

      return new Response(JSON.stringify({ message: `Unhandled mock route: ${method} ${url}` }), {
        status: 404,
      });
    });

    vi.stubGlobal('fetch', mockFetch);
    return calls;
  }

  it('denies Dispatch & Courier page and makes zero API calls for unauthorized View-Only Inbound user', async () => {
    const calls = setupFetchMock({
      userId: 'usr-inb-only',
      email: 'inb@nexus3pl.local',
      userType: UserType.Staff,
      permissions: ['inbound:view', 'facilities:view'],
    });

    renderWithShell(
      <>
        <Sidebar />
        <LaunchpadGrid />
        <DispatchAndCourierPage />
      </>
    );

    await waitFor(() => expect(screen.getByTestId('dispatch-forbidden')).toBeTruthy());
    expect(screen.queryByTestId('launchpad-transportation')).toBeNull();
    expect(calls.some((c) => c.url.includes('/api/v1/outbound/dispatch'))).toBe(false);
  });

  it('locks Driver to Courier Handheld ONLY (zero board/exceptions/warehouse calls), acknowledges custody, submits POD and confirms OrderDeliveryStatus.Delivered', async () => {
    const calls = setupFetchMock({
      userId: 'drv-745-a',
      email: 'dan.driver@nexus3pl.local',
      userType: UserType.Driver,
      roles: ['Driver'],
      permissions: ['dispatch:execute-delivery'],
    });

    renderWithShell(
      <>
        <Sidebar />
        <LaunchpadGrid />
        <DispatchAndCourierPage />
      </>
    );

    await waitFor(() => expect(screen.getByTestId('courier-handheld-panel')).toBeTruthy());
    expect(screen.getByTestId('launchpad-transportation')).toBeTruthy();
    expect(screen.queryByTestId('tab-dispatch-board')).toBeNull();
    expect(screen.queryByTestId('tab-exceptions-queue')).toBeNull();
    expect(screen.queryByTestId('launchpad-shipment-consolidation')).toBeNull();

    // Driver never calls facility board, exceptions, timeline, or assign-driver
    expect(calls.some((c) => c.url.includes('/dispatch/board'))).toBe(false);
    expect(calls.some((c) => c.url.includes('/dispatch/exceptions?'))).toBe(false);

    // Acknowledge custody handover before POD
    await waitFor(() => expect(screen.getByTestId(`custody-ack-gate-${SHIPMENT_1_ID}`)).toBeTruthy());
    fireEvent.change(screen.getByTestId('custody-ack-signature-input'), {
      target: { value: 'Dan Driver Real' },
    });
    fireEvent.click(screen.getByTestId(`acknowledge-custody-btn-${SHIPMENT_1_ID}`));

    await waitFor(() =>
      expect(screen.queryByTestId(`custody-ack-gate-${SHIPMENT_1_ID}`)).toBeNull()
    );

    // Capture DriverHandheld POD for PKG_2 -> Confirm Delivery -> OrderDeliveryStatus.Delivered
    fireEvent.click(screen.getByTestId(`courier-pod-btn-${PKG_2_ID}`));
    await waitFor(() => expect(screen.getByTestId('pod-submit-form')).toBeTruthy());
    expect(screen.queryByTestId('pod-source-select')).toBeNull(); // Driver channel locked to DriverHandheld

    fireEvent.change(screen.getByTestId('pod-recipient-input'), {
      target: { value: 'Warehouse Receiver' },
    });
    fireEvent.click(screen.getByTestId('pod-submit-btn'));

    await waitFor(() => expect(screen.getByTestId('pod-preview-card')).toBeTruthy());
    const podPostCall = calls.find((c) => c.method === 'POST' && c.url.endsWith(`/packages/${PKG_2_ID}/pod`));
    expect(podPostCall?.body?.isSigned).toBe(false);
    expect(screen.getByTestId('pod-signed-status').textContent).toContain(
      'Recipient Name Acknowledgment (Unsigned)'
    );
    expect(screen.getByTestId('pod-preview-card').textContent).toContain('Server Alice Official');
    fireEvent.click(screen.getByTestId('pod-confirm-delivery-btn'));

    await waitFor(() => expect(screen.getByTestId('pod-confirmation-banner')).toBeTruthy());
    expect(screen.getByTestId('pod-confirmation-banner').textContent).toContain('Delivered');
  });

  it('allows Staff supervisor to view timeline + order rollup, supersede POD, and resolve delivery exceptions, while Client stays strictly read-only', async () => {
    setupFetchMock({
      userId: 'sup-745-a',
      email: 'sup@nexus3pl.local',
      userType: UserType.Staff,
      roles: ['OperationsManager'],
      permissions: ['dispatch:view', 'dispatch:manage-runs', 'facilities:view'],
    });

    const { unmount } = renderWithShell(<DispatchAndCourierPage />);

    await waitFor(() => expect(screen.getByTestId(`board-shipment-${SHIPMENT_1_ID}`)).toBeTruthy());

    // Open Tracking Timeline & Order Rollup
    fireEvent.click(screen.getByTestId(`open-timeline-btn-${PKG_1_ID}`));
    await waitFor(() => expect(screen.getByTestId('order-delivery-rollup-card')).toBeTruthy());
    expect(screen.getByTestId('milestone-step-OutForDelivery').className).toContain('active');
    fireEvent.click(screen.getByRole('button', { name: 'Close' }));

    // Open POD Preview & Supersede POD
    fireEvent.click(screen.getByTestId(`open-pod-btn-${PKG_1_ID}`));
    await waitFor(() => expect(screen.getByTestId('pod-signature-preview')).toBeTruthy());
    fireEvent.click(screen.getByTestId('pod-open-supersede-btn'));
    fireEvent.change(screen.getByTestId('pod-supersede-reason'), {
      target: { value: 'Corrected dock signer name' },
    });
    fireEvent.change(screen.getByTestId('pod-recipient-input'), {
      target: { value: 'Jane Receiver Fixed' },
    });
    fireEvent.change(screen.getByTestId('pod-verification-reason'), {
      target: { value: 'Verified signed BOL paper receipt' },
    });
    fireEvent.click(screen.getByTestId('pod-supersede-submit-btn'));
    await waitFor(() => expect(screen.getByTestId('pod-preview-card').textContent).toContain('Jane Receiver Fixed'));
    fireEvent.click(screen.getByRole('button', { name: 'Close modal dialog' }));

    // Switch to Delivery Exceptions & Resolve
    fireEvent.click(screen.getByTestId('tab-exceptions-queue'));
    await waitFor(() => expect(screen.getByTestId(`exception-card-${EXC_1_ID}`)).toBeTruthy());
    fireEvent.click(screen.getByTestId(`exc-resolve-btn-${EXC_1_ID}`));
    fireEvent.change(screen.getByTestId('exc-notes-input'), {
      target: { value: 'Redelivered via dock door 2' },
    });
    fireEvent.click(screen.getByTestId('exc-action-submit-btn'));
    await waitFor(() =>
      expect(screen.getByTestId(`exception-card-${EXC_1_ID}`).textContent).toContain('Resolved')
    );

    unmount();

    // ClientAdmin read-only check
    setupFetchMock({
      userId: 'cli-user-745',
      email: 'client@apex.test',
      userType: UserType.Client,
      tenantId: 'cli-745-a',
      roles: ['ClientAdmin'],
      permissions: ['dispatch:view', 'facilities:view'],
    });
    renderWithShell(<DispatchAndCourierPage />);
    await waitFor(() => expect(screen.getByTestId(`board-shipment-${SHIPMENT_1_ID}`)).toBeTruthy());
    expect(screen.queryByTestId(`assign-driver-btn-${SHIPMENT_1_ID}`)).toBeNull();
    expect(screen.queryByTestId(`sync-tracking-btn-${SHIPMENT_1_ID}`)).toBeNull();
    expect(screen.queryByTestId('tab-courier-handheld')).toBeNull();
  });
});
