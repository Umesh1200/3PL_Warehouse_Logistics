import React, { useCallback, useEffect, useRef, useState } from 'react';
import { useAuth } from '../../context/AuthContext';
import { useFacility } from '../../context/FacilityContext';
import { UserType, normalizeUserTypeStrict } from '../../types/auth';
import {
  DeliveryExceptionQueueItemDto,
  DeliveryExceptionSeverity,
  DeliveryExceptionStatus,
  DispatchBoardPackageDto,
  DispatchBoardShipmentDto,
  EligibleShipmentDriverDto,
  ShipmentCustodyStatus,
  ShipmentStatus,
  TrackingMilestoneStatus,
} from '../../types/outbound';
import { dispatchApi } from '../../services/dispatchApi';
import { outboundApi } from '../../services/outboundApi';
import {
  DeliveryExceptionSeverityBadge,
  DeliveryExceptionStatusBadge,
  ShipmentCustodyBadge,
  ShipmentStatusBadge,
  TrackingMilestoneBadge,
  formatServiceLevelLabel,
} from '../../components/outbound/OutboundStatusBadge';
import { DriverAssignModal } from '../../components/outbound/DriverAssignModal';
import { TrackingTimelineModal } from '../../components/outbound/TrackingTimelineModal';
import { ProofOfDeliveryModal } from '../../components/outbound/ProofOfDeliveryModal';
import {
  DeliveryExceptionActionModal,
  ExceptionModalMode,
} from '../../components/outbound/DeliveryExceptionActionModal';
import { AppIcon } from '../../components/common/AppIcon';
import '../../styles/outbound.css';

type DispatchTab = 'board' | 'exceptions' | 'courier';

export const DispatchAndCourierPage: React.FC = () => {
  const { user, hasPermission, isSuperAdmin, isLoading: isAuthLoading } = useAuth();
  const { activeFacility } = useFacility();

  const userType = user ? normalizeUserTypeStrict(user.userType) : null;
  const isStaff = userType === UserType.Staff;
  const isClient = userType === UserType.Client;
  const isDriver = userType === UserType.Driver;

  const canViewBoard = (isStaff || isClient) && (isSuperAdmin || hasPermission('dispatch:view'));
  const canManageRuns = isStaff && (isSuperAdmin || hasPermission('dispatch:manage-runs'));
  const canExecuteDelivery =
    (isDriver || isStaff) && (isSuperAdmin || hasPermission('dispatch:execute-delivery'));
  const hasAnyAccess = canViewBoard || canExecuteDelivery;

  const defaultTab: DispatchTab = canViewBoard ? 'board' : 'courier';
  const [activeTab, setActiveTab] = useState<DispatchTab>(defaultTab);

  useEffect(() => {
    if (activeTab === 'board' && !canViewBoard && canExecuteDelivery) {
      setActiveTab('courier');
    } else if (activeTab === 'exceptions' && !canViewBoard && canExecuteDelivery) {
      setActiveTab('courier');
    } else if (activeTab === 'courier' && !canExecuteDelivery && canViewBoard) {
      setActiveTab('board');
    }
  }, [activeTab, canViewBoard, canExecuteDelivery]);

  const [feedback, setFeedback] = useState<{
    type: 'success' | 'warning' | 'error';
    message: string;
  } | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  // Dispatch Board State
  const [boardShipments, setBoardShipments] = useState<DispatchBoardShipmentDto[]>([]);
  const [eligibleDrivers, setEligibleDrivers] = useState<EligibleShipmentDriverDto[]>([]);
  const [statusFilter, setStatusFilter] = useState<string>('');
  const [deliveryFilter, setDeliveryFilter] = useState<string>('');
  const [openExcOnlyBoard, setOpenExcOnlyBoard] = useState(false);
  const [searchTerm, setSearchTerm] = useState('');

  // Exceptions Queue State
  const [exceptions, setExceptions] = useState<DeliveryExceptionQueueItemDto[]>([]);
  const [excStatusFilter, setExcStatusFilter] = useState<string>('');
  const [excSeverityFilter, setExcSeverityFilter] = useState<string>('');
  const [excOpenOnly, setExcOpenOnly] = useState(true);
  const [excSearch, setExcSearch] = useState('');

  // Courier Handheld State
  const [courierShipments, setCourierShipments] = useState<DispatchBoardShipmentDto[]>([]);
  const [custodySignatureInput, setCustodySignatureInput] = useState('');
  const [custodyLicenseInput, setCustodyLicenseInput] = useState('');
  const [manualPackageId, setManualPackageId] = useState('');
  const [manualShipmentId, setManualShipmentId] = useState('');
  const [acknowledgingId, setAcknowledgingId] = useState<string | null>(null);
  const [syncingShipmentId, setSyncingShipmentId] = useState<string | null>(null);

  // Modals
  const [assignModalShipment, setAssignModalShipment] = useState<DispatchBoardShipmentDto | null>(null);
  const [timelineTarget, setTimelineTarget] = useState<{
    packageId: string;
    orderId: string | null;
    shipmentId: string | null;
    packageNumber?: string | null;
  } | null>(null);
  const [podTarget, setPodTarget] = useState<{
    packageId: string;
    packageNumber?: string | null;
  } | null>(null);
  const [exceptionModalState, setExceptionModalState] = useState<{
    isOpen: boolean;
    mode: ExceptionModalMode;
    packageId?: string | null;
    packageNumber?: string | null;
    exceptionItem?: DeliveryExceptionQueueItemDto | null;
  }>({ isOpen: false, mode: 'driverReport' });

  const prevFacilityRef = useRef<string | undefined>(undefined);
  useEffect(() => {
    const nextId = activeFacility?.id;
    if (prevFacilityRef.current && nextId && prevFacilityRef.current !== nextId) {
      setBoardShipments([]);
      setExceptions([]);
      setEligibleDrivers([]);
    }
    prevFacilityRef.current = nextId;
  }, [activeFacility?.id]);

  const loadTabData = useCallback(
    async (signal?: AbortSignal) => {
      if (!hasAnyAccess) return;
      setLoading(true);
      setLoadError(null);
      try {
        if (activeTab === 'board' && canViewBoard) {
          const [boardRes, driversRes] = await Promise.all([
            dispatchApi.getDispatchBoard(
              {
                facilityId: activeFacility?.id,
                status: statusFilter ? (statusFilter as ShipmentStatus) : undefined,
                deliveryStatus: deliveryFilter ? (deliveryFilter as TrackingMilestoneStatus) : undefined,
                hasOpenExceptions: openExcOnlyBoard ? true : undefined,
                searchTerm: searchTerm || undefined,
                pageSize: 25,
              },
              signal
            ),
            canManageRuns && activeFacility
              ? outboundApi
                  .getEligibleShipmentDrivers(activeFacility.id, undefined, signal)
                  .catch(() => [])
              : Promise.resolve([]),
          ]);
          setBoardShipments(boardRes.items);
          setEligibleDrivers(driversRes);
        } else if (activeTab === 'exceptions' && canViewBoard) {
          const excRes = await dispatchApi.getDeliveryExceptions(
            {
              facilityId: activeFacility?.id,
              status: excStatusFilter ? (excStatusFilter as DeliveryExceptionStatus) : undefined,
              severity: excSeverityFilter
                ? (excSeverityFilter as DeliveryExceptionSeverity)
                : undefined,
              openOnly: excOpenOnly ? true : undefined,
              searchTerm: excSearch || undefined,
              pageSize: 25,
            },
            signal
          );
          setExceptions(excRes.items);
        } else if (activeTab === 'courier' && canExecuteDelivery) {
          if (isDriver || !canViewBoard) {
            const runs = await dispatchApi.getCourierAssignedShipments(signal);
            setCourierShipments(runs);
          } else {
            const boardRes = await dispatchApi.getDispatchBoard(
              {
                facilityId: activeFacility?.id,
                status: ShipmentStatus.Dispatched,
                pageSize: 25,
              },
              signal
            );
            setCourierShipments(boardRes.items);
          }
        }
      } catch (err: any) {
        if (err.name === 'AbortError') return;
        setLoadError(err.message || 'Failed to load dispatch and courier workspace.');
      } finally {
        setLoading(false);
      }
    },
    [
      hasAnyAccess,
      activeTab,
      canViewBoard,
      canManageRuns,
      canExecuteDelivery,
      isDriver,
      activeFacility?.id,
      statusFilter,
      deliveryFilter,
      openExcOnlyBoard,
      searchTerm,
      excStatusFilter,
      excSeverityFilter,
      excOpenOnly,
      excSearch,
    ]
  );

  useEffect(() => {
    if (isAuthLoading || !hasAnyAccess) return;
    const ctrl = new AbortController();
    loadTabData(ctrl.signal);
    return () => ctrl.abort();
  }, [isAuthLoading, hasAnyAccess, loadTabData]);

  const handleSyncTracking = async (shipment: DispatchBoardShipmentDto) => {
    if (!canManageRuns || syncingShipmentId) return;
    setSyncingShipmentId(shipment.shipmentId);
    setFeedback(null);
    try {
      const res = await dispatchApi.syncShipmentTracking(shipment.shipmentId);
      setFeedback({
        type: 'success',
        message: `Synced ${shipment.shipmentNumber}: ${res.newEventsIngested} new carrier event(s), milestone ${res.shipmentDeliveryStatus}.`,
      });
      await loadTabData();
    } catch (err: any) {
      setFeedback({ type: 'error', message: err.message || 'Failed to sync shipment tracking.' });
    } finally {
      setSyncingShipmentId(null);
    }
  };

  const handleAcknowledgeCustody = async (shipmentId: string, expectedVersion?: number) => {
    if (!canExecuteDelivery || acknowledgingId) return;
    if (!custodySignatureInput.trim() || custodySignatureInput.trim().length < 2) {
      setFeedback({
        type: 'error',
        message: 'Driver custody signature (minimum 2 characters) is required to acknowledge handover.',
      });
      return;
    }
    setAcknowledgingId(shipmentId);
    setFeedback(null);
    try {
      const res = await dispatchApi.acknowledgeShipmentCustody(shipmentId, {
        driverSignature: custodySignatureInput.trim(),
        driverLicenseNumber: custodyLicenseInput.trim() || null,
        expectedVersion: expectedVersion ?? null,
      });
      setCustodySignatureInput('');
      setCustodyLicenseInput('');
      setFeedback({
        type: 'success',
        message: `Custody confirmed for ${res.shipmentNumber} (Version v${res.version}).`,
      });
      await loadTabData();
    } catch (err: any) {
      setFeedback({
        type: 'error',
        message: err.message || 'Failed to acknowledge custody handover.',
      });
    } finally {
      setAcknowledgingId(null);
    }
  };

  const handleQuickConfirmDelivery = async (pkg: DispatchBoardPackageDto) => {
    if (!canExecuteDelivery) return;
    setFeedback(null);
    try {
      const conf = await dispatchApi.confirmPackageDelivery(pkg.packageId, {});
      setFeedback({
        type: 'success',
        message: `Confirmed Delivered for ${conf.packageNumber} • Order Delivery Status: ${conf.orderDeliveryStatus}`,
      });
      await loadTabData();
    } catch (err: any) {
      setFeedback({
        type: 'error',
        message: err.message || 'Confirm delivery failed. Capture POD first if required.',
      });
    }
  };

  if (!isAuthLoading && !hasAnyAccess) {
    return (
      <div className="outbound-forbidden-card" role="alert" data-testid="dispatch-forbidden">
        <AppIcon name="Lock" size="lg" color="danger" />
        <h3>Dispatch &amp; Courier Access Restricted</h3>
        <p>
          Requires <code>dispatch:view</code> or <code>dispatch:execute-delivery</code> authorization.
        </p>
      </div>
    );
  }

  return (
    <div className="outbound-page-container" data-testid="dispatch-courier-page">
      <div className="outbound-header-bar">
        <div>
          <h2>Dispatch, Tracking &amp; Courier Operations</h2>
          <p className="outbound-subtitle">
            Monitor dispatched shipments, carrier milestone timelines, Proof of Delivery (POD) evidence,
            and last-mile delivery exceptions.
          </p>
        </div>
        <div className="picking-mode-tabs" role="tablist" aria-label="Dispatch workspace views">
          {canViewBoard && (
            <button
              type="button"
              role="tab"
              aria-selected={activeTab === 'board'}
              className={`btn ${activeTab === 'board' ? 'btn-primary' : 'btn-secondary'}`}
              onClick={() => setActiveTab('board')}
              data-testid="tab-dispatch-board"
            >
              Dispatch Board &amp; Tracking
            </button>
          )}
          {canViewBoard && (
            <button
              type="button"
              role="tab"
              aria-selected={activeTab === 'exceptions'}
              className={`btn ${activeTab === 'exceptions' ? 'btn-primary' : 'btn-secondary'}`}
              onClick={() => setActiveTab('exceptions')}
              data-testid="tab-exceptions-queue"
            >
              Delivery Exceptions
            </button>
          )}
          {canExecuteDelivery && (
            <button
              type="button"
              role="tab"
              aria-selected={activeTab === 'courier'}
              className={`btn ${activeTab === 'courier' ? 'btn-primary' : 'btn-secondary'}`}
              onClick={() => setActiveTab('courier')}
              data-testid="tab-courier-handheld"
            >
              Courier Handheld
            </button>
          )}
        </div>
      </div>

      {feedback && (
        <div
          className={`outbound-alert ${feedback.type}`}
          role={feedback.type === 'error' ? 'alert' : 'status'}
          data-testid="dispatch-feedback"
        >
          <span>{feedback.message}</span>
          <button
            type="button"
            className="alert-dismiss"
            onClick={() => setFeedback(null)}
            aria-label="Dismiss notification"
          >
            ×
          </button>
        </div>
      )}

      {loadError && (
        <div className="outbound-alert error" role="alert">
          <span>{loadError}</span>
          <button type="button" className="btn btn-sm btn-secondary" onClick={() => loadTabData()}>
            Retry
          </button>
        </div>
      )}

      {/* =================================================================== */}
      {/* TAB 1: DISPATCH BOARD & TRACKING (Staff + Read-Only Client)          */}
      {/* =================================================================== */}
      {activeTab === 'board' && canViewBoard && (
        <div className="outbound-form-stack" data-testid="dispatch-board-panel">
          <div className="outbound-card shipping-queue-filters">
            <select
              aria-label="Filter by shipment status"
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value)}
              data-testid="board-status-filter"
            >
              <option value="">All Shipment Statuses</option>
              <option value={ShipmentStatus.ReadyForDispatch}>ReadyForDispatch</option>
              <option value={ShipmentStatus.Dispatched}>Dispatched</option>
            </select>

            <select
              aria-label="Filter by delivery milestone"
              value={deliveryFilter}
              onChange={(e) => setDeliveryFilter(e.target.value)}
              data-testid="board-delivery-filter"
            >
              <option value="">All Delivery Milestones</option>
              {Object.values(TrackingMilestoneStatus).map((m) => (
                <option key={m} value={m}>
                  {m}
                </option>
              ))}
            </select>

            <input
              type="text"
              placeholder="Search shipment #, BOL #, tracking #, trailer..."
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              aria-label="Search dispatch board"
              data-testid="board-search-input"
            />

            <label className="checkbox-inline" style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', fontSize: '13px' }}>
              <input
                type="checkbox"
                checked={openExcOnlyBoard}
                onChange={(e) => setOpenExcOnlyBoard(e.target.checked)}
                data-testid="board-open-exc-toggle"
              />
              Open Exceptions Only
            </label>

            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => loadTabData()}
              data-testid="board-refresh-btn"
            >
              Refresh
            </button>
          </div>

          {loading ? (
            <div className="outbound-card empty-inline-state">Loading dispatch board...</div>
          ) : boardShipments.length === 0 ? (
            <div className="outbound-card empty-inline-state" data-testid="board-empty-state">
              No outbound shipments match the current dispatch filters.
            </div>
          ) : (
            <div className="dispatch-board-grid">
              {boardShipments.map((s) => (
                <article
                  key={s.shipmentId}
                  className="outbound-card dispatch-shipment-card"
                  data-testid={`board-shipment-${s.shipmentId}`}
                >
                  <div className="dispatch-shipment-header">
                    <div>
                      <strong className="mono-cell">{s.shipmentNumber}</strong>{' '}
                      <span className="text-muted">
                        ({s.carrierCode} • {formatServiceLevelLabel(s.carrierServiceLevel)})
                      </span>
                      <div className="dispatch-shipment-meta" style={{ marginTop: '4px' }}>
                        {s.clientName && <span>Client: {s.clientName}</span>}
                        <span>BOL: {s.billOfLadingNumber || '—'}</span>
                        <span>Driver: {s.assignedDriverName || 'External / Unassigned'}</span>
                        <span>
                          Delivered: <strong>{s.deliveredPackagesCount} / {s.totalPackagesCount}</strong>
                        </span>
                        <span>Open Exceptions: {s.openExceptionsCount}</span>
                      </div>
                    </div>

                    <div className="rf-header-badges">
                      <ShipmentStatusBadge status={s.status} />
                      <ShipmentCustodyBadge status={s.custodyStatus} />
                      <TrackingMilestoneBadge status={s.deliveryStatus} />
                    </div>
                  </div>

                  {canManageRuns && (
                    <div className="dispatch-package-actions">
                      <button
                        type="button"
                        className="btn btn-sm btn-secondary"
                        onClick={() => setAssignModalShipment(s)}
                        disabled={s.deliveryStatus === TrackingMilestoneStatus.Delivered}
                        data-testid={`assign-driver-btn-${s.shipmentId}`}
                      >
                        {s.assignedDriverUserId ? 'Reassign / Revoke Driver' : 'Assign Driver'}
                      </button>
                      <button
                        type="button"
                        className="btn btn-sm btn-secondary"
                        disabled={syncingShipmentId === s.shipmentId}
                        onClick={() => handleSyncTracking(s)}
                        data-testid={`sync-tracking-btn-${s.shipmentId}`}
                      >
                        {syncingShipmentId === s.shipmentId ? 'Syncing...' : 'Sync Tracking'}
                      </button>
                    </div>
                  )}

                  <div className="outbound-form-stack">
                    {s.packages.map((pkg) => (
                      <div
                        key={pkg.packageId}
                        className="dispatch-package-row"
                        data-testid={`board-package-${pkg.packageId}`}
                      >
                        <div>
                          <strong className="mono-cell">{pkg.packageNumber}</strong>
                          {pkg.trackingNumber && (
                            <span className="mono-cell" style={{ marginLeft: '8px', fontSize: '12.5px' }}>
                              TRK: {pkg.trackingNumber}
                            </span>
                          )}
                          <div className="dispatch-shipment-meta">
                            <span>Order: {pkg.orderNumber || pkg.outboundOrderId}</span>
                            <TrackingMilestoneBadge status={pkg.trackingStatus} />
                            {pkg.hasActivePod && <span className="badge badge-success">POD On File</span>}
                          </div>
                        </div>

                        <div className="dispatch-package-actions">
                          <button
                            type="button"
                            className="btn btn-sm btn-secondary"
                            onClick={() =>
                              setTimelineTarget({
                                packageId: pkg.packageId,
                                orderId: pkg.outboundOrderId,
                                shipmentId: s.shipmentId,
                                packageNumber: pkg.packageNumber,
                              })
                            }
                            data-testid={`open-timeline-btn-${pkg.packageId}`}
                          >
                            Tracking &amp; Order Rollup
                          </button>
                          <button
                            type="button"
                            className="btn btn-sm btn-secondary"
                            onClick={() =>
                              setPodTarget({
                                packageId: pkg.packageId,
                                packageNumber: pkg.packageNumber,
                              })
                            }
                            data-testid={`open-pod-btn-${pkg.packageId}`}
                          >
                            {pkg.hasActivePod
                              ? canManageRuns
                                ? 'View / Supersede POD'
                                : 'View POD'
                              : canExecuteDelivery
                              ? 'Capture POD'
                              : 'POD Status'}
                          </button>
                        </div>
                      </div>
                    ))}
                  </div>
                </article>
              ))}
            </div>
          )}
        </div>
      )}

      {/* =================================================================== */}
      {/* TAB 2: DELIVERY EXCEPTIONS RESOLUTION DESK                          */}
      {/* =================================================================== */}
      {activeTab === 'exceptions' && canViewBoard && (
        <div className="outbound-form-stack" data-testid="exceptions-panel">
          <div className="outbound-card shipping-queue-filters">
            <select
              aria-label="Filter by exception status"
              value={excStatusFilter}
              onChange={(e) => setExcStatusFilter(e.target.value)}
              data-testid="exc-status-filter"
            >
              <option value="">All Exception Statuses</option>
              {Object.values(DeliveryExceptionStatus).map((st) => (
                <option key={st} value={st}>
                  {st}
                </option>
              ))}
            </select>

            <select
              aria-label="Filter by severity"
              value={excSeverityFilter}
              onChange={(e) => setExcSeverityFilter(e.target.value)}
              data-testid="exc-severity-filter"
            >
              <option value="">All Severities</option>
              {Object.values(DeliveryExceptionSeverity).map((sev) => (
                <option key={sev} value={sev}>
                  {sev}
                </option>
              ))}
            </select>

            <input
              type="text"
              placeholder="Search tracking #, code, description..."
              value={excSearch}
              onChange={(e) => setExcSearch(e.target.value)}
              aria-label="Search exceptions"
            />

            <label style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', fontSize: '13px' }}>
              <input
                type="checkbox"
                checked={excOpenOnly}
                onChange={(e) => setExcOpenOnly(e.target.checked)}
                data-testid="exc-open-only-toggle"
              />
              Open Only
            </label>

            <button type="button" className="btn btn-secondary" onClick={() => loadTabData()}>
              Refresh
            </button>
          </div>

          {loading ? (
            <div className="outbound-card empty-inline-state">Loading delivery exceptions...</div>
          ) : exceptions.length === 0 ? (
            <div className="outbound-card empty-inline-state" data-testid="exceptions-empty-state">
              No delivery exceptions match the current filter.
            </div>
          ) : (
            <div className="dispatch-board-grid">
              {exceptions.map((exc) => {
                const isClosed =
                  exc.status === DeliveryExceptionStatus.Resolved ||
                  exc.status === DeliveryExceptionStatus.Voided;
                return (
                  <article
                    key={exc.exceptionId}
                    className="outbound-card dispatch-shipment-card"
                    data-testid={`exception-card-${exc.exceptionId}`}
                  >
                    <div className="dispatch-shipment-header">
                      <div>
                        <strong className="mono-cell">{exc.trackingNumber}</strong> •{' '}
                        <span>{exc.exceptionType}</span>
                        <div className="dispatch-shipment-meta" style={{ marginTop: '4px' }}>
                          <span>Package: {exc.packageNumber || exc.packageId}</span>
                          {exc.orderNumber && <span>Order: {exc.orderNumber}</span>}
                          <span>Source: {exc.source}</span>
                          <span>Contacts: {exc.contactAttemptsCount}</span>
                          <span>Reattempts: {exc.reattemptCount}</span>
                          {exc.correctedAddress && <span>Corrected: {exc.correctedAddress}</span>}
                          {exc.holdDepotLocation && <span>Depot: {exc.holdDepotLocation}</span>}
                        </div>
                        <p style={{ margin: '6px 0 0', fontSize: '13px' }}>{exc.carrierDescription}</p>
                      </div>

                      <div className="rf-header-badges">
                        <DeliveryExceptionSeverityBadge severity={exc.severity} />
                        <DeliveryExceptionStatusBadge status={exc.status} />
                      </div>
                    </div>

                    {canManageRuns && (
                      <div className="dispatch-package-actions">
                        {!isClosed && (
                          <>
                            <button
                              type="button"
                              className="btn btn-sm btn-secondary"
                              onClick={() =>
                                setExceptionModalState({
                                  isOpen: true,
                                  mode: 'contact',
                                  exceptionItem: exc,
                                })
                              }
                              data-testid={`exc-contact-btn-${exc.exceptionId}`}
                            >
                              Log Contact
                            </button>
                            <button
                              type="button"
                              className="btn btn-sm btn-secondary"
                              onClick={() =>
                                setExceptionModalState({
                                  isOpen: true,
                                  mode: exc.scheduledReattemptDateUtc ? 'reschedule' : 'reattempt',
                                  exceptionItem: exc,
                                })
                              }
                              data-testid={`exc-reattempt-btn-${exc.exceptionId}`}
                            >
                              {exc.scheduledReattemptDateUtc ? 'Reschedule' : 'Schedule Reattempt'}
                            </button>
                            <button
                              type="button"
                              className="btn btn-sm btn-secondary"
                              onClick={() =>
                                setExceptionModalState({
                                  isOpen: true,
                                  mode: 'updateAddress',
                                  exceptionItem: exc,
                                })
                              }
                              data-testid={`exc-address-btn-${exc.exceptionId}`}
                            >
                              Fix Address
                            </button>
                            <button
                              type="button"
                              className="btn btn-sm btn-secondary"
                              onClick={() =>
                                setExceptionModalState({
                                  isOpen: true,
                                  mode: 'redirectDepot',
                                  exceptionItem: exc,
                                })
                              }
                              data-testid={`exc-depot-btn-${exc.exceptionId}`}
                            >
                              Depot Redirect
                            </button>
                            <button
                              type="button"
                              className="btn btn-sm btn-primary"
                              onClick={() =>
                                setExceptionModalState({
                                  isOpen: true,
                                  mode: 'resolve',
                                  exceptionItem: exc,
                                })
                              }
                              data-testid={`exc-resolve-btn-${exc.exceptionId}`}
                            >
                              Resolve
                            </button>
                            <button
                              type="button"
                              className="btn btn-sm btn-danger-outline"
                              onClick={() =>
                                setExceptionModalState({
                                  isOpen: true,
                                  mode: 'void',
                                  exceptionItem: exc,
                                })
                              }
                              data-testid={`exc-void-btn-${exc.exceptionId}`}
                            >
                              Void
                            </button>
                          </>
                        )}
                        {isClosed && (
                          <button
                            type="button"
                            className="btn btn-sm btn-secondary"
                            onClick={() =>
                              setExceptionModalState({
                                isOpen: true,
                                mode: 'reopen',
                                exceptionItem: exc,
                              })
                            }
                            data-testid={`exc-reopen-btn-${exc.exceptionId}`}
                          >
                            Reopen Exception
                          </button>
                        )}
                      </div>
                    )}
                  </article>
                );
              })}
            </div>
          )}
        </div>
      )}

      {/* =================================================================== */}
      {/* TAB 3: COURIER HANDHELD (Driver Custody, POD & Driver Exception)    */}
      {/* =================================================================== */}
      {activeTab === 'courier' && canExecuteDelivery && (
        <div className="outbound-form-stack" data-testid="courier-handheld-panel">
          <div className="outbound-card outbound-form-stack">
            <div className="dispatch-shipment-header">
              <div>
                <h3 style={{ margin: 0 }}>Assigned Delivery Stops</h3>
                <p className="outbound-subtitle" style={{ margin: '4px 0 0' }}>
                  Acknowledge reassigned custody handovers, capture signed Proof of Delivery, confirm
                  consignee delivery, or report field exceptions.
                </p>
              </div>
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => loadTabData()}
                data-testid="courier-refresh-btn"
              >
                Refresh Stops
              </button>
            </div>

            {loading ? (
              <div className="empty-inline-state">Loading active courier custody stops...</div>
            ) : courierShipments.length === 0 ? (
              <div className="empty-inline-state" data-testid="courier-empty-state">
                No active dispatched runs assigned to your courier identity. Use quick barcode entry below
                if scanning an assigned manifest ID.
              </div>
            ) : (
              <div className="dispatch-board-grid">
                {courierShipments.map((s) => (
                  <article
                    key={s.shipmentId}
                    className="outbound-card dispatch-shipment-card"
                    data-testid={`courier-shipment-${s.shipmentId}`}
                  >
                    <div className="dispatch-shipment-header">
                      <div>
                        <strong className="mono-cell">{s.shipmentNumber}</strong> •{' '}
                        <span>{s.carrierCode}</span>
                        <div className="dispatch-shipment-meta" style={{ marginTop: '4px' }}>
                          <span>BOL: {s.billOfLadingNumber || '—'}</span>
                          <span>Trailer: {s.trailerNumber || '—'}</span>
                          <span>
                            Delivered: {s.deliveredPackagesCount} / {s.totalPackagesCount}
                          </span>
                        </div>
                      </div>
                      <div className="rf-header-badges">
                        <ShipmentCustodyBadge status={s.custodyStatus} />
                        <TrackingMilestoneBadge status={s.deliveryStatus} />
                      </div>
                    </div>

                    {s.custodyStatus === ShipmentCustodyStatus.PendingHandover && (
                      <div className="rf-claim-gate" data-testid={`custody-ack-gate-${s.shipmentId}`}>
                        <div>
                          <strong>Custody Handover Acknowledgment Required</strong>
                          <p style={{ margin: '4px 0 0', fontSize: '13px' }}>
                            Sign below to accept physical custody before submitting POD or exceptions.
                          </p>
                          <div className="form-grid-2col" style={{ marginTop: '8px' }}>
                            <input
                              type="text"
                              placeholder="Driver Signature *"
                              value={custodySignatureInput}
                              onChange={(e) => setCustodySignatureInput(e.target.value)}
                              aria-label="Driver custody signature"
                              data-testid="custody-ack-signature-input"
                            />
                            <input
                              type="text"
                              placeholder="License / Badge # (Optional)"
                              value={custodyLicenseInput}
                              onChange={(e) => setCustodyLicenseInput(e.target.value)}
                              aria-label="Driver license number"
                            />
                          </div>
                        </div>
                        <button
                          type="button"
                          className="btn btn-primary courier-touch-btn"
                          disabled={acknowledgingId === s.shipmentId}
                          onClick={() => handleAcknowledgeCustody(s.shipmentId, s.version)}
                          data-testid={`acknowledge-custody-btn-${s.shipmentId}`}
                        >
                          {acknowledgingId === s.shipmentId
                            ? 'Confirming...'
                            : 'Acknowledge Custody'}
                        </button>
                      </div>
                    )}

                    <div className="outbound-form-stack">
                      {s.packages.map((pkg) => {
                        const custodyReady = s.custodyStatus === ShipmentCustodyStatus.Confirmed;
                        return (
                          <div
                            key={pkg.packageId}
                            className="dispatch-package-row"
                            data-testid={`courier-package-${pkg.packageId}`}
                          >
                            <div>
                              <strong className="mono-cell">{pkg.packageNumber}</strong>
                              {pkg.trackingNumber && (
                                <span className="mono-cell" style={{ marginLeft: '8px', fontSize: '12.5px' }}>
                                  {pkg.trackingNumber}
                                </span>
                              )}
                              <div className="dispatch-shipment-meta">
                                <span>Order: {pkg.orderNumber || pkg.outboundOrderId}</span>
                                <TrackingMilestoneBadge status={pkg.trackingStatus} />
                              </div>
                            </div>

                            <div className="dispatch-package-actions">
                              <button
                                type="button"
                                className="btn btn-primary courier-touch-btn"
                                disabled={!custodyReady}
                                onClick={() =>
                                  setPodTarget({
                                    packageId: pkg.packageId,
                                    packageNumber: pkg.packageNumber,
                                  })
                                }
                                data-testid={`courier-pod-btn-${pkg.packageId}`}
                              >
                                Capture POD
                              </button>
                              <button
                                type="button"
                                className="btn btn-secondary courier-touch-btn"
                                disabled={!custodyReady}
                                onClick={() => handleQuickConfirmDelivery(pkg)}
                                data-testid={`courier-confirm-btn-${pkg.packageId}`}
                              >
                                Confirm Delivery
                              </button>
                              <button
                                type="button"
                                className="btn btn-danger-outline courier-touch-btn"
                                disabled={!custodyReady}
                                onClick={() =>
                                  setExceptionModalState({
                                    isOpen: true,
                                    mode: 'driverReport',
                                    packageId: pkg.packageId,
                                    packageNumber: pkg.packageNumber,
                                  })
                                }
                                data-testid={`courier-exception-btn-${pkg.packageId}`}
                              >
                                Report Exception
                              </button>
                            </div>
                          </div>
                        );
                      })}
                    </div>
                  </article>
                ))}
              </div>
            )}
          </div>

          <div className="outbound-card outbound-form-stack" data-testid="courier-manual-scan-card">
            <strong>Manual Parcel / Custody Scan Fallback</strong>
            <div className="form-grid-2col">
              <div className="form-field">
                <label htmlFor="manual-pkg-id">Package ID (POD / Confirm / Exception)</label>
                <div className="dispatch-package-actions">
                  <input
                    id="manual-pkg-id"
                    type="text"
                    value={manualPackageId}
                    onChange={(e) => setManualPackageId(e.target.value)}
                    placeholder="Scan or paste Package UUID"
                    data-testid="courier-manual-pkg-input"
                  />
                  <button
                    type="button"
                    className="btn btn-secondary"
                    disabled={!manualPackageId.trim()}
                    onClick={() =>
                      setPodTarget({
                        packageId: manualPackageId.trim(),
                        packageNumber: manualPackageId.trim(),
                      })
                    }
                    data-testid="courier-manual-pod-btn"
                  >
                    Open POD
                  </button>
                </div>
              </div>
              <div className="form-field">
                <label htmlFor="manual-shp-id">Shipment ID (Handover Ack)</label>
                <div className="dispatch-package-actions">
                  <input
                    id="manual-shp-id"
                    type="text"
                    value={manualShipmentId}
                    onChange={(e) => setManualShipmentId(e.target.value)}
                    placeholder="Scan or paste Shipment UUID"
                    data-testid="courier-manual-shp-input"
                  />
                  <button
                    type="button"
                    className="btn btn-secondary"
                    disabled={!manualShipmentId.trim()}
                    onClick={() => handleAcknowledgeCustody(manualShipmentId.trim())}
                  >
                    Ack Custody
                  </button>
                </div>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* =================================================================== */}
      {/* MODALS                                                              */}
      {/* =================================================================== */}
      <DriverAssignModal
        isOpen={Boolean(assignModalShipment)}
        shipment={assignModalShipment}
        eligibleDrivers={eligibleDrivers}
        onClose={() => setAssignModalShipment(null)}
        onAssigned={loadTabData}
        onFeedback={setFeedback}
      />

      <TrackingTimelineModal
        isOpen={Boolean(timelineTarget)}
        packageId={timelineTarget?.packageId || null}
        orderId={timelineTarget?.orderId || null}
        shipmentId={timelineTarget?.shipmentId || null}
        packageNumber={timelineTarget?.packageNumber}
        canManageRuns={canManageRuns}
        onClose={() => setTimelineTarget(null)}
        onSynced={loadTabData}
        onFeedback={setFeedback}
      />

      <ProofOfDeliveryModal
        isOpen={Boolean(podTarget)}
        packageId={podTarget?.packageId || null}
        packageNumber={podTarget?.packageNumber}
        canViewPod={canViewBoard}
        canExecuteDelivery={canExecuteDelivery}
        canManageRuns={canManageRuns}
        isDriver={isDriver}
        onClose={() => setPodTarget(null)}
        onCompleted={loadTabData}
        onFeedback={setFeedback}
      />

      <DeliveryExceptionActionModal
        isOpen={exceptionModalState.isOpen}
        mode={exceptionModalState.mode}
        packageId={exceptionModalState.packageId}
        packageNumber={exceptionModalState.packageNumber}
        exceptionItem={exceptionModalState.exceptionItem}
        onClose={() => setExceptionModalState((prev) => ({ ...prev, isOpen: false }))}
        onCompleted={loadTabData}
        onFeedback={setFeedback}
      />
    </div>
  );
};
