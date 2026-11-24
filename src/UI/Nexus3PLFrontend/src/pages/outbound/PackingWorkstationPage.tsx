import React, { useCallback, useEffect, useRef, useState } from 'react';
import { useAuth } from '../../context/AuthContext';
import { useFacility } from '../../context/FacilityContext';
import { UserType, normalizeUserTypeStrict } from '../../types/auth';
import {
  PackableOrderSummaryDto,
  PackagingMaterialDto,
  PackageStatus,
  PackageSummaryDto,
  PackingSessionDetailDto,
  PackingSessionStatus,
  PackingSessionSummaryDto,
  PackingStationLocationDto,
} from '../../types/outbound';
import { outboundApi } from '../../services/outboundApi';
import {
  OrderStatusBadge,
  PackageStatusBadge,
  PackingSessionStatusBadge,
  formatPackagingMaterialTypeLabel,
  generateActionIdempotencyKey,
} from '../../components/outbound/OutboundStatusBadge';
import { CartonizationPanel } from '../../components/outbound/CartonizationPanel';
import { CarrierLabelPanel } from '../../components/outbound/CarrierLabelPanel';
import { AppIcon } from '../../components/common/AppIcon';
import '../../styles/outbound.css';

export const PackingWorkstationPage: React.FC = () => {
  const { user, hasPermission, isSuperAdmin, isLoading: isAuthLoading } = useAuth();
  const { activeFacility } = useFacility();

  const userType = user ? normalizeUserTypeStrict(user.userType) : null;
  const isStaff = userType === UserType.Staff;
  const canPack = isStaff && (isSuperAdmin || hasPermission('outbound:pack'));
  const canShip = isStaff && (isSuperAdmin || hasPermission('outbound:ship'));

  const [feedback, setFeedback] = useState<{ type: 'success' | 'warning' | 'error'; message: string } | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [loadingWorkbench, setLoadingWorkbench] = useState(false);

  // Workbench Reference & Session List State (canPack)
  const [stations, setStations] = useState<PackingStationLocationDto[]>([]);
  const [materials, setMaterials] = useState<PackagingMaterialDto[]>([]);
  const [packableOrders, setPackableOrders] = useState<PackableOrderSummaryDto[]>([]);
  const [sessions, setSessions] = useState<PackingSessionSummaryDto[]>([]);

  // Start Session Inputs
  const [selectedStationId, setSelectedStationId] = useState('');
  const [selectedOrderId, setSelectedOrderId] = useState('');
  const [isStartingSession, setIsStartingSession] = useState(false);

  // Active Session Detail State
  const [activeSessionId, setActiveSessionId] = useState<string | null>(null);
  const [sessionDetail, setSessionDetail] = useState<PackingSessionDetailDto | null>(null);
  const [loadingSession, setLoadingSession] = useState(false);

  // Carton Selection & Create Inputs
  const [selectedPackageId, setSelectedPackageId] = useState<string | null>(null);
  const [newBoxMaterialId, setNewBoxMaterialId] = useState('');
  const [overrideReason, setOverrideReason] = useState('');
  const [isCreatingCarton, setIsCreatingCarton] = useState(false);

  // Pack Item Scan Inputs
  const [scannedToteLpn, setScannedToteLpn] = useState('');
  const [scannedItemBarcode, setScannedItemBarcode] = useState('');
  const [scannedLotNumber, setScannedLotNumber] = useState('');
  const [packQuantity, setPackQuantity] = useState('1');
  const [isPackingItem, setIsPackingItem] = useState(false);

  // Scale Weight / Release Tote / Complete Session State
  const [scaleWeightKg, setScaleWeightKg] = useState('');
  const [isClosingPackage, setIsClosingPackage] = useState(false);
  const [releasingToteLpn, setReleasingToteLpn] = useState<string | null>(null);
  const [isCompletingSession, setIsCompletingSession] = useState(false);

  // Ship-Only Mode State (canShip && !canPack)
  const [eligiblePackages, setEligiblePackages] = useState<PackageSummaryDto[]>([]);
  const [shipSearchTerm, setShipSearchTerm] = useState('');

  // Single-flight & Idempotency Refs
  const inFlightPackRef = useRef(false);
  const packIdempKeyRef = useRef<string | null>(null);
  const inFlightCreateCartonRef = useRef(false);

  const prevFacilityRef = useRef<string | undefined>(undefined);
  useEffect(() => {
    const nextId = activeFacility?.id;
    if (prevFacilityRef.current && nextId && prevFacilityRef.current !== nextId) {
      setActiveSessionId(null);
      setSessionDetail(null);
      setSelectedPackageId(null);
      setStations([]);
      setMaterials([]);
      setPackableOrders([]);
      setSessions([]);
      setEligiblePackages([]);
      packIdempKeyRef.current = null;
    }
    prevFacilityRef.current = nextId;
  }, [activeFacility?.id]);

  const loadPackWorkbench = useCallback(
    async (signal?: AbortSignal) => {
      if (!activeFacility || !canPack) return;
      setLoadingWorkbench(true);
      setLoadError(null);
      try {
        const [stns, mats, ordersRes, sessRes] = await Promise.all([
          outboundApi.getPackingStations(activeFacility.id, signal),
          outboundApi.getPackagingMaterials(activeFacility.id, undefined, signal),
          outboundApi.getPackableOrders(activeFacility.id, undefined, signal),
          outboundApi.getPackingSessions({ facilityId: activeFacility.id, pageSize: 25 }, signal),
        ]);
        setStations(stns);
        setMaterials(mats);
        setPackableOrders(ordersRes.items);
        setSessions(sessRes.items);

        setSelectedStationId((prev) => (prev && stns.some((s) => s.id === prev) ? prev : stns[0]?.id || ''));
        setSelectedOrderId((prev) =>
          prev && ordersRes.items.some((o) => o.id === prev) ? prev : ordersRes.items[0]?.id || ''
        );
        setNewBoxMaterialId((prev) => (prev && mats.some((m) => m.id === prev) ? prev : mats[0]?.id || ''));
      } catch (err: any) {
        if (err.name !== 'AbortError') {
          setLoadError(err.message || 'Failed to load packing workbench data.');
        }
      } finally {
        setLoadingWorkbench(false);
      }
    },
    [activeFacility?.id, canPack]
  );

  const loadShipOnlyPackages = useCallback(
    async (signal?: AbortSignal) => {
      if (!activeFacility || !canShip || canPack) return;
      setLoadingWorkbench(true);
      setLoadError(null);
      try {
        const res = await outboundApi.getEligibleShipmentPackages(
          activeFacility.id,
          shipSearchTerm.trim() || undefined,
          signal
        );
        setEligiblePackages(res.items);
        setSelectedPackageId((prev) =>
          prev && res.items.some((p) => p.id === prev) ? prev : res.items[0]?.id || null
        );
      } catch (err: any) {
        if (err.name !== 'AbortError') {
          setLoadError(err.message || 'Failed to load eligible shipping cartons.');
        }
      } finally {
        setLoadingWorkbench(false);
      }
    },
    [activeFacility?.id, canShip, canPack, shipSearchTerm]
  );

  const loadSessionDetail = useCallback(
    async (sessionId: string, signal?: AbortSignal) => {
      if (!canPack) return;
      setLoadingSession(true);
      setLoadError(null);
      try {
        const detail = await outboundApi.getPackingSessionById(sessionId, signal);
        setSessionDetail(detail);
        setSelectedPackageId((prev) => {
          if (prev && detail.packages.some((p) => p.id === prev)) return prev;
          const firstOpen = detail.packages.find((p) => p.status === PackageStatus.Open);
          return firstOpen?.id || detail.packages[0]?.id || null;
        });

        if (activeFacility) {
          const clientMats = await outboundApi.getPackagingMaterials(activeFacility.id, detail.clientId, signal);
          setMaterials(clientMats);
          setNewBoxMaterialId((prev) =>
            prev && clientMats.some((m) => m.id === prev) ? prev : clientMats[0]?.id || ''
          );
        }
      } catch (err: any) {
        if (err.name !== 'AbortError') {
          setLoadError(err.message || 'Failed to load packing session details.');
        }
      } finally {
        setLoadingSession(false);
      }
    },
    [activeFacility?.id, canPack]
  );

  useEffect(() => {
    if (isAuthLoading || !isStaff || !activeFacility) return;
    const ctrl = new AbortController();
    if (canPack) {
      loadPackWorkbench(ctrl.signal);
    } else if (canShip) {
      loadShipOnlyPackages(ctrl.signal);
    }
    return () => ctrl.abort();
  }, [isAuthLoading, isStaff, activeFacility?.id, canPack, canShip, loadPackWorkbench, loadShipOnlyPackages]);

  useEffect(() => {
    if (!activeSessionId || !canPack) return;
    const ctrl = new AbortController();
    loadSessionDetail(activeSessionId, ctrl.signal);
    return () => ctrl.abort();
  }, [activeSessionId, canPack, loadSessionDetail]);

  if (!isAuthLoading && (!isStaff || (!canPack && !canShip))) {
    return (
      <div className="outbound-page">
        <div className="outbound-forbidden-card" role="alert" data-testid="packing-forbidden-card">
          <AppIcon name="Lock" size="lg" color="danger" />
          <h2>403 Forbidden &mdash; Packing &amp; Label Execution Restricted</h2>
          <p>
            Packing bench operations and shipping label issuance are strictly restricted to authorized warehouse staff
            with <code>outbound:pack</code> or <code>outbound:ship</code> permissions.
          </p>
        </div>
      </div>
    );
  }

  const handleStartSession = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!activeFacility || !canPack || isStartingSession) return;
    if (!selectedStationId || !selectedOrderId) {
      setFeedback({ type: 'error', message: 'Select both a Packing Station and an Order to start packing.' });
      return;
    }

    setIsStartingSession(true);
    setFeedback(null);
    try {
      const res = await outboundApi.startPackingSession({
        facilityId: activeFacility.id,
        packingStationLocationId: selectedStationId,
        outboundOrderId: selectedOrderId,
      });
      setFeedback({ type: 'success', message: res.message || `Session ready for ${res.orderNumber}.` });
      setActiveSessionId(res.sessionId);
      await loadPackWorkbench();
    } catch (err: any) {
      setFeedback({ type: 'error', message: err.message || 'Unable to start packing session.' });
    } finally {
      setIsStartingSession(false);
    }
  };

  const handleCreateCarton = async (materialId?: string) => {
    if (!sessionDetail || !canPack || inFlightCreateCartonRef.current || isCreatingCarton) return;
    inFlightCreateCartonRef.current = true;
    setIsCreatingCarton(true);
    setFeedback(null);
    try {
      const chosenMaterial = materialId !== undefined ? materialId : newBoxMaterialId || undefined;
      const res = await outboundApi.createPackage({
        packingSessionId: sessionDetail.id,
        packagingMaterialId: chosenMaterial || null,
        overrideReason: overrideReason.trim() || undefined,
      });
      setOverrideReason('');
      setFeedback({
        type: 'success',
        message: `Created carton ${res.packageNumber} (LPN: ${res.handlingUnitLpn}).`,
      });
      setSelectedPackageId(res.packageId);
      await loadSessionDetail(sessionDetail.id);
    } catch (err: any) {
      setFeedback({ type: 'error', message: err.message || 'Failed to create shipping carton.' });
    } finally {
      inFlightCreateCartonRef.current = false;
      setIsCreatingCarton(false);
    }
  };

  const selectedPackage: PackageSummaryDto | null = canPack
    ? sessionDetail?.packages.find((p) => p.id === selectedPackageId) || sessionDetail?.packages[0] || null
    : eligiblePackages.find((p) => p.id === selectedPackageId) || eligiblePackages[0] || null;

  const handlePackItem = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!sessionDetail || !selectedPackage || !canPack || inFlightPackRef.current || isPackingItem) return;

    const tote = scannedToteLpn.trim();
    const itemBc = scannedItemBarcode.trim();
    const qty = Number(packQuantity);
    if (!tote || !itemBc || !Number.isFinite(qty) || qty <= 0) {
      setFeedback({
        type: 'error',
        message: 'Source tote LPN, item/SKU barcode, and positive quantity are required to pack.',
      });
      return;
    }

    if (!packIdempKeyRef.current) {
      packIdempKeyRef.current = generateActionIdempotencyKey('pack');
    }

    inFlightPackRef.current = true;
    setIsPackingItem(true);
    setFeedback(null);
    try {
      const res = await outboundApi.packItem({
        packingSessionId: sessionDetail.id,
        packageId: selectedPackage.id,
        scannedToteLpn: tote,
        scannedItemBarcode: itemBc,
        scannedLotNumber: scannedLotNumber.trim() || undefined,
        quantity: qty,
        idempotencyKey: packIdempKeyRef.current,
      });
      packIdempKeyRef.current = null;
      setScannedItemBarcode('');
      setScannedLotNumber('');
      setFeedback({
        type: 'success',
        message: `Packed ${res.quantityPacked} unit(s) of ${res.sku} into ${res.packageNumber} (Tx #${res.inventoryTransactionId}).`,
      });
      await loadSessionDetail(sessionDetail.id);
    } catch (err: any) {
      setFeedback({ type: 'error', message: err.message || 'Failed to pack scanned item into carton.' });
    } finally {
      inFlightPackRef.current = false;
      setIsPackingItem(false);
    }
  };

  const handleClosePackage = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!sessionDetail || !selectedPackage || !canPack || isClosingPackage) return;
    const gross = Number(scaleWeightKg);
    if (!Number.isFinite(gross) || gross <= 0) {
      setFeedback({ type: 'error', message: 'Enter a valid positive scale gross weight (kg) to seal carton.' });
      return;
    }

    setIsClosingPackage(true);
    setFeedback(null);
    try {
      const res = await outboundApi.closePackage(selectedPackage.id, gross);
      setScaleWeightKg('');
      setFeedback({
        type: 'success',
        message: `Sealed carton ${res.packageNumber} (${res.grossWeightKg.toFixed(2)} kg gross, ${res.volumetricWeightKg.toFixed(2)} kg dim).`,
      });
      await loadSessionDetail(sessionDetail.id);
    } catch (err: any) {
      setFeedback({ type: 'error', message: err.message || 'Failed to seal package.' });
    } finally {
      setIsClosingPackage(false);
    }
  };

  const handleReleaseTote = async (toteLpn: string) => {
    if (!activeFacility || !sessionDetail || !canPack || releasingToteLpn) return;
    setReleasingToteLpn(toteLpn);
    setFeedback(null);
    try {
      const res = await outboundApi.releaseTote(toteLpn, activeFacility.id);
      setFeedback({ type: 'success', message: res.message });
      await loadSessionDetail(sessionDetail.id);
    } catch (err: any) {
      setFeedback({ type: 'error', message: err.message || `Failed to release tote ${toteLpn}.` });
    } finally {
      setReleasingToteLpn(null);
    }
  };

  const handleCompleteSession = async () => {
    if (!sessionDetail || !canPack || isCompletingSession) return;
    setIsCompletingSession(true);
    setFeedback(null);
    try {
      const res = await outboundApi.completePackingSession(sessionDetail.id);
      setFeedback({
        type: 'success',
        message: `Packing session completed (${res.totalPackagesCount} carton(s), ${res.totalWeightKg.toFixed(2)} kg total).`,
      });
      await Promise.all([loadSessionDetail(sessionDetail.id), loadPackWorkbench()]);
    } catch (err: any) {
      setFeedback({ type: 'error', message: err.message || 'Cannot complete packing session.' });
    } finally {
      setIsCompletingSession(false);
    }
  };

  const totalOrderAllocated = sessionDetail
    ? sessionDetail.orderLines.reduce((sum, l) => sum + l.quantityAllocated, 0)
    : 0;
  const totalOrderPacked = sessionDetail
    ? sessionDetail.orderLines.reduce((sum, l) => sum + l.quantityPacked, 0)
    : 0;
  const packPercent = totalOrderAllocated > 0 ? Math.min(100, Math.round((totalOrderPacked / totalOrderAllocated) * 100)) : 0;

  return (
    <div className="outbound-page">
      <div className="outbound-header">
        <div>
          <h2 className="page-section-heading">Packing Bench &amp; Carrier Label Issuance</h2>
          <p className="outbound-subtitle">
            {activeFacility ? `${activeFacility.name} (${activeFacility.facilityCode})` : 'Select an active facility'}
            {canPack && !canShip ? ' • Packing Bench Only' : !canPack && canShip ? ' • Shipping Label Desk' : ''}
          </p>
        </div>
        <div className="outbound-header-actions">
          {activeSessionId && canPack && (
            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => {
                setActiveSessionId(null);
                setSessionDetail(null);
              }}
              data-testid="back-to-workbench-btn"
            >
              &larr; All Sessions
            </button>
          )}
          <button
            type="button"
            className="btn btn-secondary"
            onClick={() => {
              if (activeSessionId && canPack) loadSessionDetail(activeSessionId);
              else if (canPack) loadPackWorkbench();
              else if (canShip) loadShipOnlyPackages();
            }}
            data-testid="packing-refresh-btn"
          >
            <AppIcon name="RefreshCw" size="sm" />
            <span>Refresh</span>
          </button>
        </div>
      </div>

      {feedback && (
        <div className={`outbound-alert ${feedback.type}`} role="status" data-testid="packing-feedback-banner">
          <span>{feedback.message}</span>
          <button type="button" className="btn btn-secondary btn-sm" onClick={() => setFeedback(null)}>
            Dismiss
          </button>
        </div>
      )}

      {loadError && (
        <div className="outbound-alert error" role="alert" data-testid="packing-load-error">
          <span>{loadError}</span>
          <button
            type="button"
            className="btn btn-secondary btn-sm"
            onClick={() => {
              if (activeSessionId && canPack) loadSessionDetail(activeSessionId);
              else if (canPack) loadPackWorkbench();
              else if (canShip) loadShipOnlyPackages();
            }}
            data-testid="packing-retry-btn"
          >
            Retry
          </button>
        </div>
      )}

      {/* MODE A: Ship-Only Desk (strictly separated from /packing/* endpoints) */}
      {!canPack && canShip && (
        <div className="packing-workbench-stack" data-testid="ship-only-label-desk">
          <section className="card packing-section-card">
            <div className="packing-section-header">
              <div>
                <h3>Eligible Packed Cartons</h3>
                <p className="outbound-subtitle">
                  Cartons ready for carrier rating, shipping label generation, or void/re-rate adjustments.
                </p>
              </div>
              <div className="packing-search-row">
                <input
                  type="text"
                  placeholder="Filter Package #, Order #, or Tracking..."
                  value={shipSearchTerm}
                  onChange={(e) => setShipSearchTerm(e.target.value)}
                  data-testid="ship-only-search-input"
                />
              </div>
            </div>

            {loadingWorkbench ? (
              <p className="outbound-subtitle">Loading eligible cartons...</p>
            ) : eligiblePackages.length === 0 ? (
              <div className="empty-state" data-testid="ship-only-empty-state">
                <p>No packed cartons currently await carrier labeling.</p>
              </div>
            ) : (
              <>
                <p className="table-scroll-hint">Swipe horizontally to inspect carton dimensions &amp; tracking &rarr;</p>
                <div className="outbound-table-scroll">
                  <table className="data-table">
                    <thead>
                      <tr>
                        <th>Package #</th>
                        <th>Order #</th>
                        <th>Weight / Dim</th>
                        <th>Status</th>
                        <th>Carrier / Tracking</th>
                        <th>Select</th>
                      </tr>
                    </thead>
                    <tbody>
                      {eligiblePackages.map((p) => (
                        <tr key={p.id} className={selectedPackage?.id === p.id ? 'selected-row' : ''}>
                          <td className="code-font">
                            <strong>{p.packageNumber}</strong>
                          </td>
                          <td className="code-font">{p.orderNumber}</td>
                          <td>{p.grossWeightKg.toFixed(2)} kg</td>
                          <td>
                            <PackageStatusBadge status={p.status} />
                          </td>
                          <td className="code-font">{p.trackingNumber ? `${p.carrierCode} • ${p.trackingNumber}` : 'Unlabeled'}</td>
                          <td>
                            <button
                              type="button"
                              className={`btn btn-sm ${selectedPackage?.id === p.id ? 'btn-primary' : 'btn-secondary'}`}
                              onClick={() => setSelectedPackageId(p.id)}
                              data-testid={`select-pkg-${p.id}`}
                            >
                              {selectedPackage?.id === p.id ? 'Selected' : 'Manage Label'}
                            </button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </>
            )}
          </section>

          <CarrierLabelPanel
            pkg={selectedPackage}
            canShip={canShip}
            onLabelUpdated={() => loadShipOnlyPackages()}
            onFeedback={setFeedback}
          />
        </div>
      )}

      {/* MODE B: Pack Workbench Home (Start Session + Resume Recent Sessions) */}
      {canPack && !activeSessionId && (
        <div className="packing-workbench-stack" data-testid="packing-workbench-home">
          <section className="card packing-section-card">
            <h3>Start or Resume Packing Session</h3>
            <p className="outbound-subtitle">
              Lock an allocated/picked outbound order to a physical packing station before cartonization and scanning.
            </p>
            <form onSubmit={handleStartSession} className="outbound-form-stack" data-testid="start-packing-session-form">
              <div className="outbound-form-grid">
                <div className="form-field">
                  <label htmlFor="pack-station-select">Packing Station *</label>
                  <select
                    id="pack-station-select"
                    value={selectedStationId}
                    onChange={(e) => setSelectedStationId(e.target.value)}
                    disabled={loadingWorkbench}
                    data-testid="packing-station-select"
                  >
                    <option value="">-- Select Packing Station --</option>
                    {stations.map((s) => (
                      <option key={s.id} value={s.id}>
                        {s.locationCode} ({s.barcode})
                      </option>
                    ))}
                  </select>
                </div>

                <div className="form-field">
                  <label htmlFor="pack-order-select">Packable Outbound Order *</label>
                  <select
                    id="pack-order-select"
                    value={selectedOrderId}
                    onChange={(e) => setSelectedOrderId(e.target.value)}
                    disabled={loadingWorkbench}
                    data-testid="packable-order-select"
                  >
                    <option value="">-- Select Order --</option>
                    {packableOrders.map((o) => (
                      <option key={o.id} value={o.id}>
                        {o.orderNumber} &mdash; {o.customerName} ({o.totalPickedUnits}/{o.totalAllocatedUnits} picked)
                      </option>
                    ))}
                  </select>
                </div>
              </div>

              <div className="modal-inline-actions">
                <button
                  type="submit"
                  className="btn btn-primary btn-rf-lg"
                  disabled={loadingWorkbench || isStartingSession || !selectedStationId || !selectedOrderId}
                  data-testid="start-packing-session-btn"
                >
                  <AppIcon name="Package" size="sm" />
                  <span>{isStartingSession ? 'Starting Bench Session...' : 'Start / Resume Packing Session'}</span>
                </button>
              </div>
            </form>
          </section>

          <section className="card packing-section-card" data-testid="packing-sessions-list">
            <h3>Facility Packing Sessions</h3>
            {loadingWorkbench ? (
              <p className="outbound-subtitle">Loading packing sessions...</p>
            ) : sessions.length === 0 ? (
              <div className="empty-state" data-testid="packing-sessions-empty">
                <p>No packing sessions recorded for this facility yet.</p>
              </div>
            ) : (
              <>
                <p className="table-scroll-hint">Swipe horizontally to inspect session columns &rarr;</p>
                <div className="outbound-table-scroll">
                  <table className="data-table">
                    <thead>
                      <tr>
                        <th>Order #</th>
                        <th>Station</th>
                        <th>Operator</th>
                        <th>Status</th>
                        <th>Packages</th>
                        <th>Action</th>
                      </tr>
                    </thead>
                    <tbody>
                      {sessions.map((s) => (
                        <tr key={s.id}>
                          <td className="code-font">
                            <strong>{s.orderNumber}</strong>
                          </td>
                          <td className="code-font">{s.packingStationLocationCode}</td>
                          <td>{s.operatorEmail || s.operatorUserId}</td>
                          <td>
                            <PackingSessionStatusBadge status={s.status} />
                          </td>
                          <td>{s.activePackagesCount} carton(s)</td>
                          <td>
                            <button
                              type="button"
                              className="btn btn-secondary btn-sm"
                              onClick={() => setActiveSessionId(s.id)}
                              data-testid={`open-session-btn-${s.id}`}
                            >
                              {s.status === PackingSessionStatus.Completed ? 'Inspect' : 'Open Bench'}
                            </button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </>
            )}
          </section>
        </div>
      )}

      {/* MODE C: Active Session Workbench */}
      {canPack && activeSessionId && (
        <div className="packing-workbench-stack" data-testid="packing-session-workbench">
          {loadingSession && !sessionDetail ? (
            <div className="card">Loading packing session workspace...</div>
          ) : sessionDetail ? (
            <>
              <section className="card rf-pick-card" data-testid="session-header-card">
                <div className="rf-pick-header">
                  <div>
                    <span className="rf-eyebrow">
                      PACKING STATION {sessionDetail.packingStationLocationCode} &bull; ORDER {sessionDetail.orderNumber}
                    </span>
                    <h2 className="rf-sku-heading">{sessionDetail.orderNumber}</h2>
                    <p className="outbound-subtitle">
                      Operator: {sessionDetail.operatorEmail || sessionDetail.operatorUserId} &bull;{' '}
                      {sessionDetail.packages.length} carton(s)
                    </p>
                  </div>

                  <div className="rf-header-badges">
                    <PackingSessionStatusBadge status={sessionDetail.status} />
                    {sessionDetail.status === PackingSessionStatus.Active && (
                      <button
                        type="button"
                        className="btn btn-primary"
                        disabled={isCompletingSession}
                        onClick={handleCompleteSession}
                        data-testid="complete-session-btn"
                      >
                        <AppIcon name="CheckCircle2" size="sm" />
                        <span>{isCompletingSession ? 'Completing...' : 'Complete Packing Session'}</span>
                      </button>
                    )}
                  </div>
                </div>

                <div className="allocation-bar-wrap" style={{ marginTop: '4px' }}>
                  <div className="allocation-bar-track">
                    <div className="allocation-bar-fill" style={{ width: `${packPercent}%` }} />
                  </div>
                  <span className="allocation-bar-label">
                    {totalOrderPacked} / {totalOrderAllocated} packed ({packPercent}%)
                  </span>
                </div>
              </section>

              {/* Order Lines + Picked Totes Split */}
              <div className="packing-two-col">
                <section className="card packing-section-card" data-testid="session-order-lines">
                  <h3>Order Line Pack Progress</h3>
                  <p className="table-scroll-hint">Swipe horizontally to inspect line counts &rarr;</p>
                  <div className="outbound-table-scroll">
                    <table className="data-table">
                      <thead>
                        <tr>
                          <th>SKU</th>
                          <th>Picked</th>
                          <th>Packed</th>
                          <th>Remaining</th>
                          <th>Status</th>
                        </tr>
                      </thead>
                      <tbody>
                        {sessionDetail.orderLines.map((line) => (
                          <tr key={line.orderLineId}>
                            <td>
                              <strong className="code-font">{line.sku}</strong>
                              <div className="outbound-subtitle">{line.description}</div>
                            </td>
                            <td>{line.quantityPicked}</td>
                            <td>
                              <strong>{line.quantityPacked}</strong>
                            </td>
                            <td>{line.remainingToPack}</td>
                            <td>
                              <OrderStatusBadge status={line.lineStatus as any} />
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </section>

                <section className="card packing-section-card" data-testid="session-picked-totes">
                  <h3>Picked Totes at Station</h3>
                  {sessionDetail.pickedTotes.length === 0 ? (
                    <p className="outbound-subtitle">No active totes currently linked to this order.</p>
                  ) : (
                    <div className="totes-stack">
                      {sessionDetail.pickedTotes.map((tote) => (
                        <div key={tote.handlingUnitId} className="tote-chip-row" data-testid={`tote-row-${tote.lpnCode}`}>
                          <div>
                            <strong className="code-font">{tote.lpnCode}</strong>
                            <div className="outbound-subtitle">
                              {tote.status} &bull; <strong>{tote.totalQuantity}</strong> unit(s) remaining
                            </div>
                          </div>
                          <div className="row-actions-group">
                            {tote.totalQuantity > 0 && sessionDetail.status === PackingSessionStatus.Active && (
                              <button
                                type="button"
                                className="btn btn-secondary btn-sm"
                                onClick={() => setScannedToteLpn(tote.lpnCode)}
                                data-testid={`use-tote-btn-${tote.lpnCode}`}
                              >
                                Scan Tote
                              </button>
                            )}
                            {tote.totalQuantity === 0 && tote.status !== 'Available' && (
                              <button
                                type="button"
                                className="btn btn-primary btn-sm"
                                disabled={releasingToteLpn === tote.lpnCode}
                                onClick={() => handleReleaseTote(tote.lpnCode)}
                                data-testid={`release-tote-btn-${tote.lpnCode}`}
                              >
                                {releasingToteLpn === tote.lpnCode ? 'Releasing...' : 'Release Empty Tote'}
                              </button>
                            )}
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </section>
              </div>

              {/* 3D Cartonization Recommendation */}
              <CartonizationPanel
                clientId={sessionDetail.clientId}
                facilityId={sessionDetail.facilityId}
                outboundOrderId={sessionDetail.outboundOrderId}
                materials={materials}
                isSessionActive={sessionDetail.status === PackingSessionStatus.Active}
                isCreatingCarton={isCreatingCarton}
                onApplyRecommendedBox={(matId) => handleCreateCarton(matId)}
              />

              {/* Cartons List + Create Carton + Scan to Carton + Seal Carton */}
              <section className="card packing-section-card" data-testid="cartons-bench-section">
                <div className="packing-section-header">
                  <div>
                    <h3>Shipping Cartons ({sessionDetail.packages.length})</h3>
                    <p className="outbound-subtitle">
                      Create cartons paired 1:1 with shipping handling units, scan items from tote to carton, and record
                      scale weight to seal.
                    </p>
                  </div>
                </div>

                {sessionDetail.status === PackingSessionStatus.Active && (
                  <div className="create-carton-bar" data-testid="create-carton-controls">
                    <div className="form-field">
                      <label htmlFor="manual-box-select">Packaging Material</label>
                      <select
                        id="manual-box-select"
                        value={newBoxMaterialId}
                        onChange={(e) => setNewBoxMaterialId(e.target.value)}
                        data-testid="create-carton-material-select"
                      >
                        <option value="">Default / Custom Carton</option>
                        {materials.map((m) => (
                          <option key={m.id} value={m.id}>
                            {m.code} &mdash; {m.name} ({formatPackagingMaterialTypeLabel(m.materialType)}, {m.tareWeightKg}kg tare)
                          </option>
                        ))}
                      </select>
                    </div>
                    <div className="form-field">
                      <label htmlFor="carton-override-reason">Override Reason (Optional)</label>
                      <input
                        id="carton-override-reason"
                        type="text"
                        placeholder="Optional reason if overriding carton recommendation"
                        value={overrideReason}
                        onChange={(e) => setOverrideReason(e.target.value)}
                        data-testid="create-carton-override-input"
                      />
                    </div>
                    <button
                      type="button"
                      className="btn btn-primary"
                      disabled={isCreatingCarton}
                      onClick={() => handleCreateCarton()}
                      data-testid="create-carton-btn"
                    >
                      <AppIcon name="Plus" size="sm" />
                      <span>{isCreatingCarton ? 'Creating Carton...' : 'Create Carton'}</span>
                    </button>
                  </div>
                )}

                {sessionDetail.packages.length > 0 && (
                  <div className="carton-pills-row" data-testid="carton-selector-list">
                    {sessionDetail.packages.map((p) => (
                      <button
                        key={p.id}
                        type="button"
                        className={`carton-pill-btn ${selectedPackage?.id === p.id ? 'active' : ''}`}
                        onClick={() => setSelectedPackageId(p.id)}
                        data-testid={`carton-tab-${p.packageNumber}`}
                      >
                        <span className="code-font">
                          <strong>{p.packageNumber}</strong>
                        </span>
                        <PackageStatusBadge status={p.status} />
                      </button>
                    ))}
                  </div>
                )}

                {selectedPackage && (
                  <div className="selected-carton-workspace" data-testid="selected-carton-workspace">
                    <div className="rf-routing-banner">
                      <div className="rf-route-node">
                        <span className="rf-route-label">PACKAGE / CARTON LPN</span>
                        <strong className="rf-route-code code-font">{selectedPackage.packageNumber}</strong>
                        <span className="outbound-subtitle code-font">{selectedPackage.handlingUnitLpn}</span>
                      </div>
                      <div className="rf-route-node">
                        <span className="rf-route-label">DIMENSIONS &amp; TARE</span>
                        <strong className="rf-route-sub code-font">
                          {selectedPackage.lengthCm}&times;{selectedPackage.widthCm}&times;{selectedPackage.heightCm} cm
                        </strong>
                        <span className="outbound-subtitle">Tare: {selectedPackage.tareWeightKg.toFixed(2)} kg</span>
                      </div>
                      <div className="rf-route-node">
                        <span className="rf-route-label">SEALED GROSS / DIM</span>
                        <strong className="rf-route-code">{selectedPackage.grossWeightKg.toFixed(2)} kg</strong>
                        <span className="outbound-subtitle">Volumetric: {selectedPackage.volumetricWeightKg.toFixed(2)} kg</span>
                      </div>
                    </div>

                    {sessionDetail.status === PackingSessionStatus.Active &&
                      selectedPackage.status === PackageStatus.Open && (
                        <div className="packing-two-col">
                          {/* Pack Item Scan Form */}
                          <form onSubmit={handlePackItem} className="outbound-form-stack card" data-testid="pack-item-form">
                            <h4>Tote-to-Carton Scan ({selectedPackage.packageNumber})</h4>
                            <div className="form-field">
                              <label htmlFor="pack-scan-tote">1. Source Tote LPN *</label>
                              <input
                                id="pack-scan-tote"
                                type="text"
                                className="rf-scan-input code-font"
                                placeholder="Scan tote barcode..."
                                value={scannedToteLpn}
                                onChange={(e) => {
                                  setScannedToteLpn(e.target.value);
                                  packIdempKeyRef.current = null;
                                }}
                                disabled={isPackingItem}
                                autoComplete="off"
                                data-testid="pack-scan-tote"
                              />
                            </div>

                            <div className="form-field">
                              <label htmlFor="pack-scan-item">2. Item / SKU Barcode *</label>
                              <input
                                id="pack-scan-item"
                                type="text"
                                className="rf-scan-input code-font"
                                placeholder="Scan SKU or UPC barcode..."
                                value={scannedItemBarcode}
                                onChange={(e) => {
                                  setScannedItemBarcode(e.target.value);
                                  packIdempKeyRef.current = null;
                                }}
                                disabled={isPackingItem}
                                autoComplete="off"
                                data-testid="pack-scan-item"
                              />
                            </div>

                            <div className="outbound-form-grid">
                              <div className="form-field">
                                <label htmlFor="pack-scan-lot">3. Lot # (If Tracked)</label>
                                <input
                                  id="pack-scan-lot"
                                  type="text"
                                  className="rf-scan-input code-font"
                                  placeholder="Optional lot barcode"
                                  value={scannedLotNumber}
                                  onChange={(e) => setScannedLotNumber(e.target.value)}
                                  disabled={isPackingItem}
                                  autoComplete="off"
                                  data-testid="pack-scan-lot"
                                />
                              </div>

                              <div className="form-field">
                                <label htmlFor="pack-qty-input">4. Quantity *</label>
                                <input
                                  id="pack-qty-input"
                                  type="number"
                                  step="any"
                                  min="0.01"
                                  className="rf-scan-input"
                                  value={packQuantity}
                                  onChange={(e) => {
                                    setPackQuantity(e.target.value);
                                    packIdempKeyRef.current = null;
                                  }}
                                  disabled={isPackingItem}
                                  data-testid="pack-qty-input"
                                />
                              </div>
                            </div>

                            <button
                              type="submit"
                              className="btn btn-primary btn-rf-lg"
                              disabled={isPackingItem}
                              data-testid="pack-item-btn"
                            >
                              <AppIcon name="PackageCheck" size="sm" />
                              <span>{isPackingItem ? 'Packing Item...' : 'Pack Item into Carton'}</span>
                            </button>
                          </form>

                          {/* Close & Scale Weigh Carton Form */}
                          <form onSubmit={handleClosePackage} className="outbound-form-stack card" data-testid="close-package-form">
                            <h4>Scale Verification &amp; Seal Carton</h4>
                            <p className="outbound-subtitle">
                              Place packed carton on bench scale and confirm gross weight (kg) before rating or labeling.
                            </p>
                            <div className="form-field">
                              <label htmlFor="close-pkg-weight">Scale Gross Weight (kg) *</label>
                              <input
                                id="close-pkg-weight"
                                type="number"
                                step="0.01"
                                min="0.01"
                                className="rf-scan-input"
                                placeholder="e.g., 6.45"
                                value={scaleWeightKg}
                                onChange={(e) => setScaleWeightKg(e.target.value)}
                                disabled={isClosingPackage}
                                data-testid="close-package-weight-input"
                              />
                            </div>

                            <button
                              type="submit"
                              className="btn btn-primary btn-rf-lg"
                              disabled={isClosingPackage}
                              data-testid="close-package-btn"
                            >
                              <AppIcon name="CheckCircle2" size="sm" />
                              <span>{isClosingPackage ? 'Sealing Carton...' : 'Seal & Close Carton'}</span>
                            </button>
                          </form>
                        </div>
                      )}
                  </div>
                )}
              </section>

              {/* Carrier Rate Quote & Label Workbench */}
              <CarrierLabelPanel
                pkg={selectedPackage}
                canShip={canShip}
                onLabelUpdated={() => loadSessionDetail(sessionDetail.id)}
                onFeedback={setFeedback}
              />
            </>
          ) : null}
        </div>
      )}
    </div>
  );
};
