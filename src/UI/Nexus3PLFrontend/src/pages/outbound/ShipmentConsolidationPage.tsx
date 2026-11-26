import React, { useCallback, useEffect, useRef, useState } from 'react';
import { useAuth } from '../../context/AuthContext';
import { useFacility } from '../../context/FacilityContext';
import { ClientDto, UserType, normalizeUserTypeStrict } from '../../types/auth';
import {
  CarrierServiceLevel,
  EligibleShipmentDriverDto,
  PackageStatus,
  PackageSummaryDto,
  ShipmentDto,
  ShipmentStagingLocationDto,
  ShipmentStatus,
} from '../../types/outbound';
import { outboundApi } from '../../services/outboundApi';
import { formatDateTime } from '../../utils/formatters';
import {
  PackageStatusBadge,
  ShipmentCustodyBadge,
  ShipmentStatusBadge,
  formatServiceLevelLabel,
} from '../../components/outbound/OutboundStatusBadge';
import { BillOfLadingModal } from '../../components/outbound/BillOfLadingModal';
import { DriverCustodyHandoverModal } from '../../components/outbound/DriverCustodyHandoverModal';
import { OutboundTableRegion } from '../../components/outbound/OutboundTableRegion';
import { AppIcon } from '../../components/common/AppIcon';
import '../../styles/outbound.css';

const CARRIERS = ['SIM-FEDEX', 'SIM-UPS', 'SIM-USPS', 'SIM-FREIGHT'];

export const ShipmentConsolidationPage: React.FC = () => {
  const { user, hasPermission, isSuperAdmin, isLoading: isAuthLoading } = useAuth();
  const { activeFacility } = useFacility();

  const userType = user ? normalizeUserTypeStrict(user.userType) : null;
  const isStaff = userType === UserType.Staff;
  const canShip = isStaff && (isSuperAdmin || hasPermission('outbound:ship'));

  const [feedback, setFeedback] = useState<{ type: 'success' | 'warning' | 'error'; message: string } | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [loadingDesk, setLoadingDesk] = useState(false);

  // Reference Data
  const [clients, setClients] = useState<ClientDto[]>([]);
  const [stagingLocations, setStagingLocations] = useState<ShipmentStagingLocationDto[]>([]);
  const [eligibleDrivers, setEligibleDrivers] = useState<EligibleShipmentDriverDto[]>([]);

  // Shipments Queue & Filters
  const [shipments, setShipments] = useState<ShipmentDto[]>([]);
  const [statusFilter, setStatusFilter] = useState<string>('');
  const [searchTerm, setSearchTerm] = useState('');

  // Active Shipment Detail & Eligible Packages
  const [selectedShipmentId, setSelectedShipmentId] = useState<string | null>(null);
  const [activeShipment, setActiveShipment] = useState<ShipmentDto | null>(null);
  const [loadingDetail, setLoadingDetail] = useState(false);
  const [eligiblePackages, setEligiblePackages] = useState<PackageSummaryDto[]>([]);
  const [eligibleSearch, setEligibleSearch] = useState('');

  // Create Shipment Form
  const [showCreateBuilder, setShowCreateBuilder] = useState(false);
  const [createClientId, setCreateClientId] = useState('');
  const [createCarrierCode, setCreateCarrierCode] = useState('SIM-FEDEX');
  const [createServiceLevel, setCreateServiceLevel] = useState<CarrierServiceLevel>(
    CarrierServiceLevel.StandardGround
  );
  const [createStagingLocationId, setCreateStagingLocationId] = useState('');
  const [isCreatingShipment, setIsCreatingShipment] = useState(false);

  // Dock Staging & Loading Controls
  const [selectedStagingLocId, setSelectedStagingLocId] = useState('');
  const [trailerNumberInput, setTrailerNumberInput] = useState('');
  const [cartonScanInput, setCartonScanInput] = useState('');
  const [mutatingPackageId, setMutatingPackageId] = useState<string | null>(null);

  // Inline Remove Package Confirmation
  const [removingPackageId, setRemovingPackageId] = useState<string | null>(null);
  const [removeReason, setRemoveReason] = useState('');

  // Modals
  const [bolModalOpen, setBolModalOpen] = useState(false);
  const [handoverModalOpen, setHandoverModalOpen] = useState(false);

  const prevFacilityRef = useRef<string | undefined>(undefined);
  useEffect(() => {
    const nextId = activeFacility?.id;
    if (prevFacilityRef.current && nextId && prevFacilityRef.current !== nextId) {
      setSelectedShipmentId(null);
      setActiveShipment(null);
      setShipments([]);
      setEligiblePackages([]);
      setClients([]);
      setStagingLocations([]);
      setEligibleDrivers([]);
      setRemovingPackageId(null);
      setTrailerNumberInput('');
    }
    prevFacilityRef.current = nextId;
  }, [activeFacility?.id]);

  useEffect(() => {
    setTrailerNumberInput('');
  }, [selectedShipmentId]);

  const loadWorkspace = useCallback(
    async (signal?: AbortSignal) => {
      if (!activeFacility || !canShip) return;
      setLoadingDesk(true);
      setLoadError(null);
      try {
        const [shipmentsRes, eligibleRes, locsRes, clientsRes, driversRes] = await Promise.all([
          outboundApi.getShipments(
            {
              facilityId: activeFacility.id,
              status: statusFilter ? (statusFilter as ShipmentStatus) : undefined,
              searchTerm: searchTerm || undefined,
              pageSize: 30,
            },
            signal
          ),
          outboundApi.getEligibleShipmentPackages(
            {
              facilityId: activeFacility.id,
              searchTerm: eligibleSearch || undefined,
              pageSize: 40,
            },
            undefined,
            signal
          ),
          outboundApi.getShipmentStagingLocations(activeFacility.id, signal),
          outboundApi.getShipmentReferenceClients(activeFacility.id, signal),
          outboundApi.getEligibleShipmentDrivers(activeFacility.id, undefined, signal),
        ]);

        setShipments(shipmentsRes.items);
        setEligiblePackages(eligibleRes.items);
        setStagingLocations(locsRes);
        setClients(clientsRes.items);
        setEligibleDrivers(driversRes);

        setCreateClientId((prev) =>
          prev && clientsRes.items.some((c) => c.id === prev) ? prev : clientsRes.items[0]?.id || ''
        );
        setCreateStagingLocationId((prev) =>
          prev && locsRes.some((l) => l.id === prev) ? prev : locsRes[0]?.id || ''
        );
        setSelectedStagingLocId((prev) =>
          prev && locsRes.some((l) => l.id === prev) ? prev : locsRes[0]?.id || ''
        );

        setSelectedShipmentId((prev) =>
          prev && shipmentsRes.items.some((s) => s.id === prev)
            ? prev
            : shipmentsRes.items[0]?.id || null
        );
      } catch (err: any) {
        if (err.name !== 'AbortError') {
          setLoadError(err.message || 'Failed to load shipment consolidation workbench.');
        }
      } finally {
        setLoadingDesk(false);
      }
    },
    [activeFacility?.id, canShip, statusFilter, searchTerm, eligibleSearch]
  );

  useEffect(() => {
    if (!activeFacility || !canShip) return;
    const controller = new AbortController();
    loadWorkspace(controller.signal);
    return () => controller.abort();
  }, [loadWorkspace, activeFacility?.id, canShip]);

  const loadShipmentDetail = useCallback(
    async (shipmentId: string, signal?: AbortSignal) => {
      if (!canShip) return;
      setLoadingDetail(true);
      try {
        const detail = await outboundApi.getShipmentById(shipmentId, signal);
        setActiveShipment(detail);
        if (detail.stagingLocationId) {
          setSelectedStagingLocId(detail.stagingLocationId);
        }
        setTrailerNumberInput((prev) => detail.trailerNumber || prev);
      } catch (err: any) {
        if (err.name !== 'AbortError') {
          setFeedback({ type: 'error', message: err.message || 'Failed to load shipment details.' });
        }
      } finally {
        setLoadingDetail(false);
      }
    },
    [canShip]
  );

  useEffect(() => {
    if (!selectedShipmentId || !canShip) {
      setActiveShipment(null);
      return;
    }
    const controller = new AbortController();
    loadShipmentDetail(selectedShipmentId, controller.signal);
    return () => controller.abort();
  }, [selectedShipmentId, loadShipmentDetail, canShip]);

  const refreshAll = async (focusShipmentId?: string) => {
    const targetId = focusShipmentId || selectedShipmentId;
    await loadWorkspace();
    if (targetId) {
      setSelectedShipmentId(targetId);
      await loadShipmentDetail(targetId);
    }
  };

  if (isAuthLoading) {
    return (
      <div className="outbound-page">
        <div className="card outbound-state-card">Loading operational session...</div>
      </div>
    );
  }

  if (!isStaff || !canShip) {
    return (
      <div className="outbound-page">
        <div className="card outbound-state-card" role="alert" data-testid="shipping-access-denied">
          <AppIcon name="Lock" size="md" color="danger" />
          <h3>Shipment Consolidation &amp; Dispatch Restricted</h3>
          <p className="outbound-subtitle">
            Only authorized warehouse staff holding <code>outbound:ship</code> may consolidate freight shipments, issue Bills of Lading, or transfer carrier custody. Client and Courier accounts are excluded from dock dispatch execution.
          </p>
        </div>
      </div>
    );
  }

  if (!activeFacility) {
    return (
      <div className="outbound-page">
        <div className="card outbound-state-card">
          <h3>No Active Facility Selected</h3>
          <p className="outbound-subtitle">Select an authorized warehouse facility from the top bar.</p>
        </div>
      </div>
    );
  }

  const handleCreateShipment = async (e: React.FormEvent) => {
    e.preventDefault();
    if (isCreatingShipment || !createClientId) return;
    setIsCreatingShipment(true);
    setFeedback(null);
    try {
      const created = await outboundApi.createShipment({
        clientId: createClientId,
        facilityId: activeFacility.id,
        carrierCode: createCarrierCode,
        serviceLevel: createServiceLevel,
        stagingLocationId: createStagingLocationId || null,
      });
      setShowCreateBuilder(false);
      setFeedback({
        type: 'success',
        message: `Created Draft consolidation shipment ${created.shipmentNumber}.`,
      });
      await refreshAll(created.shipmentId);
    } catch (err: any) {
      setFeedback({ type: 'error', message: err.message || 'Failed to create shipment.' });
    } finally {
      setIsCreatingShipment(false);
    }
  };

  const handleAddPackage = async (pkg: PackageSummaryDto) => {
    if (!activeShipment || mutatingPackageId) return;
    setMutatingPackageId(pkg.id);
    setFeedback(null);
    try {
      const res = await outboundApi.addPackageToShipment(activeShipment.id, pkg.id);
      setFeedback({
        type: 'success',
        message: `Added ${res.packageNumber} to ${activeShipment.shipmentNumber} (${res.totalPackagesCount} cartons, ${res.totalGrossWeightKg.toFixed(2)} kg).`,
      });
      await refreshAll(activeShipment.id);
    } catch (err: any) {
      setFeedback({ type: 'error', message: err.message || 'Failed to add carton to shipment.' });
    } finally {
      setMutatingPackageId(null);
    }
  };

  const handleRemovePackage = async (packageId: string) => {
    if (!activeShipment || mutatingPackageId) return;
    if (!removeReason.trim()) {
      setFeedback({ type: 'error', message: 'A removal reason is required for shipment audit compliance.' });
      return;
    }
    setMutatingPackageId(packageId);
    setFeedback(null);
    try {
      await outboundApi.removePackageFromShipment(activeShipment.id, packageId, removeReason.trim());
      setRemovingPackageId(null);
      setRemoveReason('');
      setFeedback({
        type: 'success',
        message: 'Removed carton from shipment and preserved removal audit reason.',
      });
      await refreshAll(activeShipment.id);
    } catch (err: any) {
      setFeedback({ type: 'error', message: err.message || 'Failed to remove carton from shipment.' });
    } finally {
      setMutatingPackageId(null);
    }
  };

  const handleStagePackage = async (packageId: string) => {
    if (!activeShipment || mutatingPackageId) return;
    if (!selectedStagingLocId) {
      setFeedback({ type: 'error', message: 'Select a Staging Lane or Dock Door before staging cartons.' });
      return;
    }
    setMutatingPackageId(packageId);
    setFeedback(null);
    try {
      const res = await outboundApi.stagePackage(packageId, selectedStagingLocId);
      setFeedback({
        type: 'success',
        message: `Staged ${res.packageNumber} at ${res.stagingLocationCode}.`,
      });
      await refreshAll(activeShipment.id);
    } catch (err: any) {
      setFeedback({ type: 'error', message: err.message || 'Failed to stage package.' });
    } finally {
      setMutatingPackageId(null);
    }
  };

  const handleLoadPackage = async (packageId: string) => {
    if (!activeShipment || mutatingPackageId) return;
    setMutatingPackageId(packageId);
    setFeedback(null);
    try {
      const res = await outboundApi.loadPackage(packageId, {
        shipmentId: activeShipment.id,
        trailerNumber: trailerNumberInput.trim() || null,
        dockLocationId: selectedStagingLocId || null,
      });
      setFeedback({
        type: 'success',
        message:
          res.shipmentStatus === ShipmentStatus.ReadyForDispatch
            ? `Loaded ${res.packageNumber}. All cartons loaded — shipment ${activeShipment.shipmentNumber} is now ReadyForDispatch.`
            : `Loaded ${res.packageNumber} onto trailer.`,
      });
      await refreshAll(activeShipment.id);
    } catch (err: any) {
      setFeedback({ type: 'error', message: err.message || 'Failed to load package onto trailer.' });
    } finally {
      setMutatingPackageId(null);
    }
  };

  const handleQuickCartonScan = async (mode: 'stage' | 'load') => {
    if (!activeShipment) return;
    const token = cartonScanInput.trim().toLowerCase();
    if (!token) {
      setFeedback({ type: 'warning', message: 'Scan or enter a consolidated carton number or tracking #.' });
      return;
    }
    const target = activeShipment.packages.find(
      (p) =>
        p.packageNumber.toLowerCase() === token ||
        (p.trackingNumber && p.trackingNumber.toLowerCase() === token)
    );
    if (!target) {
      setFeedback({
        type: 'error',
        message: `Carton '${cartonScanInput.trim()}' was not found in shipment ${activeShipment.shipmentNumber}.`,
      });
      return;
    }
    setCartonScanInput('');
    if (mode === 'stage') {
      await handleStagePackage(target.packageId);
    } else {
      await handleLoadPackage(target.packageId);
    }
  };

  const draftCount = shipments.filter((s) => s.status === ShipmentStatus.Draft).length;
  const readyCount = shipments.filter((s) => s.status === ShipmentStatus.ReadyForDispatch).length;
  const dispatchedCount = shipments.filter((s) => s.status === ShipmentStatus.Dispatched).length;

  const allPackagesLoaded =
    Boolean(activeShipment) &&
    activeShipment!.packages.length > 0 &&
    activeShipment!.packages.every((p) => p.status === PackageStatus.Loaded);

  return (
    <div className="outbound-page">
      <div className="outbound-header">
        <div>
          <h2 className="outbound-title">Shipment Consolidation, BOL &amp; Dock Dispatch</h2>
          <p className="outbound-subtitle">
            Group labeled cartons by client and carrier, scan dock staging &amp; trailer loading, issue immutable Bills of Lading, and confirm driver custody handover for{' '}
            <strong>{activeFacility.name}</strong> ({activeFacility.facilityCode}).
          </p>
        </div>
        <div className="outbound-header-actions">
          <button
            type="button"
            className="btn btn-secondary"
            onClick={() => refreshAll()}
            disabled={loadingDesk}
          >
            <AppIcon name="RefreshCw" size="xs" /> Refresh Dock
          </button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={() => setShowCreateBuilder((prev) => !prev)}
            data-testid="toggle-create-shipment-btn"
          >
            <AppIcon name="Plus" size="xs" /> {showCreateBuilder ? 'Hide Builder' : 'New Shipment'}
          </button>
        </div>
      </div>

      {/* KPI Strip */}
      <div className="outbound-kpi-grid">
        <div className="card kpi-mini-card">
          <span className="kpi-mini-label">Draft Consolidations</span>
          <strong className="kpi-mini-value">{draftCount}</strong>
        </div>
        <div className="card kpi-mini-card">
          <span className="kpi-mini-label">Ready for Dispatch</span>
          <strong className="kpi-mini-value">{readyCount}</strong>
        </div>
        <div className="card kpi-mini-card">
          <span className="kpi-mini-label">Dispatched Manifests</span>
          <strong className="kpi-mini-value">{dispatchedCount}</strong>
        </div>
        <div className="card kpi-mini-card">
          <span className="kpi-mini-label">Eligible Labeled Cartons</span>
          <strong className="kpi-mini-value">{eligiblePackages.length}</strong>
        </div>
      </div>

      {feedback && (
        <div
          className={`outbound-alert ${feedback.type}`}
          role="status"
          data-testid="shipping-feedback-banner"
        >
          <span>{feedback.message}</span>
        </div>
      )}

      {loadError && (
        <div className="outbound-alert error" role="alert">
          <span>{loadError}</span>
        </div>
      )}

      {showCreateBuilder && (
        <form
          onSubmit={handleCreateShipment}
          className="card outbound-card shipping-builder-card"
          data-testid="create-shipment-form"
        >
          <div className="packing-section-header">
            <div>
              <h3>Create Freight Consolidation Shipment</h3>
              <p className="outbound-subtitle">
                Requires an active Client-to-Facility authorization and Staging Lane / Dock Door assignment.
              </p>
            </div>
          </div>

          <div className="shipping-builder-grid">
            <div className="form-field">
              <label htmlFor="create-shipment-client">Client Tenant *</label>
              <select
                id="create-shipment-client"
                value={createClientId}
                onChange={(e) => setCreateClientId(e.target.value)}
                required
                data-testid="create-shipment-client"
              >
                <option value="">Select authorized client...</option>
                {clients.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.companyName} ({c.accountCode})
                  </option>
                ))}
              </select>
            </div>

            <div className="form-field">
              <label htmlFor="create-shipment-carrier">Carrier *</label>
              <select
                id="create-shipment-carrier"
                value={createCarrierCode}
                onChange={(e) => setCreateCarrierCode(e.target.value)}
                data-testid="create-shipment-carrier"
              >
                {CARRIERS.map((c) => (
                  <option key={c} value={c}>
                    {c}
                  </option>
                ))}
              </select>
            </div>

            <div className="form-field">
              <label htmlFor="create-shipment-service">Service Level</label>
              <select
                id="create-shipment-service"
                value={createServiceLevel}
                onChange={(e) => setCreateServiceLevel(e.target.value as CarrierServiceLevel)}
                data-testid="create-shipment-service"
              >
                {Object.values(CarrierServiceLevel).map((lvl) => (
                  <option key={lvl} value={lvl}>
                    {formatServiceLevelLabel(lvl)}
                  </option>
                ))}
              </select>
            </div>

            <div className="form-field">
              <label htmlFor="create-shipment-staging">Default Staging Lane / Dock</label>
              <select
                id="create-shipment-staging"
                value={createStagingLocationId}
                onChange={(e) => setCreateStagingLocationId(e.target.value)}
                data-testid="create-shipment-staging"
              >
                <option value="">Assign later</option>
                {stagingLocations.map((loc) => (
                  <option key={loc.id} value={loc.id}>
                    {loc.locationCode} ({loc.locationType})
                  </option>
                ))}
              </select>
            </div>
          </div>

          <div className="shipping-builder-actions">
            <button
              type="submit"
              className="btn btn-primary"
              disabled={isCreatingShipment || !createClientId}
              data-testid="submit-create-shipment-btn"
            >
              {isCreatingShipment ? 'Creating Shipment...' : 'Create Draft Shipment'}
            </button>
          </div>
        </form>
      )}

      {/* Main Two-Column Consolidation Desk */}
      <div className="shipping-workspace-grid">
        {/* Left: Shipments Queue */}
        <section className="card outbound-card" aria-label="Shipments Queue">
          <div className="packing-section-header">
            <h3>Dock Shipments</h3>
            <span className="badge badge-info">{shipments.length} shown</span>
          </div>

          <div className="shipping-queue-filters">
            <input
              type="text"
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              placeholder="Search shipment #, BOL #, trailer..."
              aria-label="Search shipments"
            />
            <select
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value)}
              aria-label="Filter shipments by status"
              data-testid="shipment-status-filter"
            >
              <option value="">All Statuses</option>
              <option value={ShipmentStatus.Draft}>Draft</option>
              <option value={ShipmentStatus.ReadyForDispatch}>ReadyForDispatch</option>
              <option value={ShipmentStatus.Dispatched}>Dispatched</option>
              <option value={ShipmentStatus.Cancelled}>Cancelled</option>
            </select>
          </div>

          {shipments.length === 0 ? (
            <div className="empty-inline-state" data-testid="shipments-empty">
              No shipments match the active filter. Create a Draft consolidation shipment to begin.
            </div>
          ) : (
            <div className="shipment-list" data-testid="shipments-list">
              {shipments.map((s) => {
                const isSelected = s.id === selectedShipmentId;
                return (
                  <button
                    key={s.id}
                    type="button"
                    className={`shipment-queue-item ${isSelected ? 'active' : ''}`}
                    onClick={() => setSelectedShipmentId(s.id)}
                    data-testid={`shipment-item-${s.shipmentNumber}`}
                  >
                    <div className="shipment-queue-top">
                      <strong className="mono-cell">{s.shipmentNumber}</strong>
                      <ShipmentStatusBadge status={s.status} />
                    </div>
                    <div className="shipment-queue-meta">
                      <span>{s.clientName || 'Client Tenant'}</span>
                      <span>
                        {s.carrierCode} &bull; {s.totalPackagesCount} cartons &bull;{' '}
                        {s.totalWeightKg.toFixed(2)} kg
                      </span>
                    </div>
                    <div className="shipment-queue-bottom">
                      <span className="mono-cell">
                        {s.billOfLadingNumber ? `BOL: ${s.billOfLadingNumber}` : 'No BOL yet'}
                      </span>
                      <ShipmentCustodyBadge status={s.custodyStatus} />
                    </div>
                  </button>
                );
              })}
            </div>
          )}
        </section>

        {/* Right: Active Consolidation, Staging, Load & Handover */}
        <section
          className="card outbound-card"
          aria-label="Active Consolidation Workspace"
          data-testid="shipment-detail-panel"
        >
          {!activeShipment ? (
            <div className="empty-inline-state">
              {loadingDetail ? 'Loading shipment details...' : 'Select a shipment from the queue or create a new Draft shipment.'}
            </div>
          ) : (
            <div className="shipping-detail-stack">
              <div className="shipping-detail-header">
                <div>
                  <div className="shipping-detail-title-row">
                    <h3 className="mono-cell">{activeShipment.shipmentNumber}</h3>
                    <ShipmentStatusBadge status={activeShipment.status} />
                    <ShipmentCustodyBadge status={activeShipment.custodyStatus} />
                  </div>
                  <p className="outbound-subtitle">
                    {activeShipment.clientName || 'Client'} &bull; {activeShipment.carrierCode} (
                    {formatServiceLevelLabel(activeShipment.carrierServiceLevel)}) &bull;{' '}
                    <strong>{activeShipment.totalPackagesCount}</strong> cartons &bull;{' '}
                    <strong>{activeShipment.totalWeightKg.toFixed(2)} kg</strong>
                    {activeShipment.stagingLocationCode ? ` • Lane: ${activeShipment.stagingLocationCode}` : ''}
                  </p>
                </div>

                <div className="shipping-header-cta-group">
                  <button
                    type="button"
                    className="btn btn-secondary btn-sm"
                    onClick={() => setBolModalOpen(true)}
                    disabled={
                      activeShipment.totalPackagesCount === 0 ||
                      (!activeShipment.billOfLadingId &&
                        activeShipment.status !== ShipmentStatus.ReadyForDispatch)
                    }
                    data-testid="open-bol-modal-btn"
                  >
                    <AppIcon name="Receipt" size="xs" />{' '}
                    {activeShipment.billOfLadingId ? 'View Immutable BOL' : 'Issue BOL Snapshot'}
                  </button>
                  <button
                    type="button"
                    className="btn btn-primary btn-sm"
                    onClick={() => setHandoverModalOpen(true)}
                    disabled={
                      activeShipment.status !== ShipmentStatus.ReadyForDispatch || !allPackagesLoaded
                    }
                    title={
                      activeShipment.status !== ShipmentStatus.ReadyForDispatch
                        ? 'Stage and load every consolidated carton onto trailer before driver custody handover.'
                        : 'Transfer custody to carrier driver'
                    }
                    data-testid="open-handover-modal-btn"
                  >
                    <AppIcon name="Truck" size="xs" /> Driver Custody Handover
                  </button>
                </div>
              </div>

              {activeShipment.status === ShipmentStatus.Dispatched && (
                <div className="outbound-alert success" data-testid="shipment-dispatched-banner">
                  <span>
                    <strong>Dispatched</strong>{' '}
                    {activeShipment.dispatchedAtUtc
                      ? `at ${formatDateTime(activeShipment.dispatchedAtUtc, { timeZone: activeFacility?.timezone, countryCode: activeFacility?.countryCode })}`
                      : ''}{' '}
                    &bull; Trailer: <code>{activeShipment.trailerNumber || 'N/A'}</code> &bull; Seal:{' '}
                    <code>{activeShipment.sealNumber || 'N/A'}</code> &bull; Zero secondary stock mutation executed.
                  </span>
                </div>
              )}

              {/* Dock Staging & Trailer Loading Controls */}
              {(activeShipment.status === ShipmentStatus.Draft ||
                activeShipment.status === ShipmentStatus.ReadyForDispatch) && (
                <div className="dock-scan-toolbar" data-testid="dock-scan-toolbar">
                  <div className="form-field">
                    <label htmlFor="dock-staging-loc-select">Staging Lane / Dock Door</label>
                    <select
                      id="dock-staging-loc-select"
                      value={selectedStagingLocId}
                      onChange={(e) => setSelectedStagingLocId(e.target.value)}
                      data-testid="dock-staging-loc-select"
                    >
                      <option value="">Select dock lane...</option>
                      {stagingLocations.map((loc) => (
                        <option key={loc.id} value={loc.id}>
                          {loc.locationCode} ({loc.locationType})
                        </option>
                      ))}
                    </select>
                  </div>

                  <div className="form-field">
                    <label htmlFor="dock-trailer-input">Trailer #</label>
                    <input
                      id="dock-trailer-input"
                      type="text"
                      value={trailerNumberInput}
                      onChange={(e) => setTrailerNumberInput(e.target.value)}
                      placeholder="e.g. TRL-744-01"
                      data-testid="dock-trailer-input"
                    />
                  </div>

                  <div className="form-field">
                    <label htmlFor="dock-carton-scan">Carton / Tracking Scan</label>
                    <div className="dock-scan-input-group">
                      <input
                        id="dock-carton-scan"
                        type="text"
                        value={cartonScanInput}
                        onChange={(e) => setCartonScanInput(e.target.value)}
                        placeholder="Scan PKG-# or TRK-#"
                        data-testid="dock-carton-scan-input"
                      />
                      <button
                        type="button"
                        className="btn btn-secondary btn-sm"
                        onClick={() => handleQuickCartonScan('stage')}
                        data-testid="quick-stage-btn"
                      >
                        Stage
                      </button>
                      <button
                        type="button"
                        className="btn btn-secondary btn-sm"
                        onClick={() => handleQuickCartonScan('load')}
                        data-testid="quick-load-btn"
                      >
                        Load
                      </button>
                    </div>
                  </div>
                </div>
              )}

              {/* Consolidated Cartons Table */}
              <div>
                <div className="packing-section-header">
                  <h4>Consolidated Cartons ({activeShipment.packages.length})</h4>
                  <span className="table-scroll-hint">Swipe horizontally for row actions</span>
                </div>

                {activeShipment.packages.length === 0 ? (
                  <div className="empty-inline-state" data-testid="consolidated-packages-empty">
                    No cartons consolidated yet. Select eligible labeled cartons below to build this shipment.
                  </div>
                ) : (
                  <div className="outbound-table-scroll">
                    <table className="data-table data-table-compact" data-testid="consolidated-packages-table">
                      <thead>
                        <tr>
                          <th>Carton #</th>
                          <th>Order #</th>
                          <th>Tracking #</th>
                          <th>Weight</th>
                          <th>Location</th>
                          <th>Status</th>
                          <th>Dock Execution</th>
                        </tr>
                      </thead>
                      <tbody>
                        {activeShipment.packages.map((pkg) => {
                          const canStage =
                            pkg.status === PackageStatus.Packed ||
                            pkg.status === PackageStatus.LabelPrinted;
                          const canLoad = pkg.status === PackageStatus.Staged;
                          const canRemove =
                            activeShipment.status !== ShipmentStatus.Dispatched &&
                            activeShipment.status !== ShipmentStatus.Cancelled;
                          const isBusy = mutatingPackageId === pkg.packageId;

                          return (
                            <React.Fragment key={pkg.packageId}>
                              <tr data-testid={`consolidated-row-${pkg.packageNumber}`}>
                                <td className="mono-cell">{pkg.packageNumber}</td>
                                <td className="mono-cell">{pkg.orderNumber || '—'}</td>
                                <td className="mono-cell">{pkg.trackingNumber || '—'}</td>
                                <td>{pkg.grossWeightKg.toFixed(2)} kg</td>
                                <td>{pkg.currentLocationCode || 'Pack Bench'}</td>
                                <td>
                                  <PackageStatusBadge status={pkg.status} />
                                </td>
                                <td>
                                  <div className="row-action-cluster">
                                    {canStage && (
                                      <button
                                        type="button"
                                        className="btn btn-secondary btn-xs"
                                        disabled={isBusy || !selectedStagingLocId}
                                        onClick={() => handleStagePackage(pkg.packageId)}
                                        data-testid={`stage-pkg-${pkg.packageNumber}`}
                                      >
                                        Stage
                                      </button>
                                    )}
                                    <button
                                      type="button"
                                      className="btn btn-primary btn-xs"
                                      disabled={isBusy || !canLoad}
                                      title={
                                        canLoad
                                          ? 'Confirm trailer load scan'
                                          : pkg.status === PackageStatus.Loaded
                                          ? 'Already loaded on trailer'
                                          : 'Package must be Staged before trailer loading'
                                      }
                                      onClick={() => handleLoadPackage(pkg.packageId)}
                                      data-testid={`load-pkg-${pkg.packageNumber}`}
                                    >
                                      {pkg.status === PackageStatus.Loaded ? 'Loaded' : 'Load'}
                                    </button>
                                    {canRemove && (
                                      <button
                                        type="button"
                                        className="btn btn-danger-outline btn-xs"
                                        disabled={isBusy}
                                        onClick={() => {
                                          setRemovingPackageId(pkg.packageId);
                                          setRemoveReason('');
                                        }}
                                        data-testid={`remove-pkg-${pkg.packageNumber}`}
                                      >
                                        Remove
                                      </button>
                                    )}
                                  </div>
                                </td>
                              </tr>
                              {removingPackageId === pkg.packageId && (
                                <tr data-testid={`remove-reason-row-${pkg.packageNumber}`}>
                                  <td colSpan={7}>
                                    <div className="inline-remove-bar">
                                      <input
                                        type="text"
                                        value={removeReason}
                                        onChange={(e) => setRemoveReason(e.target.value)}
                                        placeholder="Mandatory removal audit reason..."
                                        aria-label="Removal reason"
                                        data-testid="remove-package-reason-input"
                                      />
                                      <button
                                        type="button"
                                        className="btn btn-danger btn-xs"
                                        disabled={isBusy}
                                        onClick={() => handleRemovePackage(pkg.packageId)}
                                        data-testid="confirm-remove-package-btn"
                                      >
                                        Confirm Remove
                                      </button>
                                      <button
                                        type="button"
                                        className="btn btn-secondary btn-xs"
                                        onClick={() => setRemovingPackageId(null)}
                                      >
                                        Cancel
                                      </button>
                                    </div>
                                  </td>
                                </tr>
                              )}
                            </React.Fragment>
                          );
                        })}
                      </tbody>
                    </table>
                  </div>
                )}
              </div>

              {/* Eligible Unassigned Packages Picker (Draft only) */}
              {activeShipment.status === ShipmentStatus.Draft && (
                <div className="eligible-picker-section" data-testid="eligible-packages-section">
                  <div className="packing-section-header">
                    <div>
                      <h4>Eligible Unassigned Cartons</h4>
                      <p className="outbound-subtitle">
                        Add labeled cartons matching client &amp; carrier ({activeShipment.carrierCode}) to this Draft shipment.
                      </p>
                    </div>
                    <input
                      type="text"
                      value={eligibleSearch}
                      onChange={(e) => setEligibleSearch(e.target.value)}
                      placeholder="Filter carton #, tracking #, order #..."
                      aria-label="Filter eligible cartons"
                    />
                  </div>

                  {eligiblePackages.length === 0 ? (
                    <div className="empty-inline-state" data-testid="eligible-packages-empty">
                      No unassigned Packed / LabelPrinted cartons available in {activeFacility.facilityCode}.
                    </div>
                  ) : (
                    <OutboundTableRegion
                      ariaLabel="Eligible labeled cartons table"
                      hintText="Swipe or scroll horizontally to consolidate cartons →"
                      scrollClassName="outbound-table-scroll"
                      hintTestId="eligible-packages-scroll-hint"
                      regionTestId="eligible-packages-scroll-region"
                    >
                      <table className="data-table data-table-compact" data-testid="eligible-packages-table">
                        <thead>
                          <tr>
                            <th>Carton #</th>
                            <th>Order #</th>
                            <th>Carrier / Tracking</th>
                            <th>Weight</th>
                            <th>Status</th>
                            <th>Consolidate</th>
                          </tr>
                        </thead>
                        <tbody>
                          {eligiblePackages.map((pkg) => {
                            const sameClient = pkg.clientId === activeShipment.clientId;
                            const carrierOk =
                              !pkg.carrierCode ||
                              pkg.carrierCode.toUpperCase() ===
                                activeShipment.carrierCode.toUpperCase();
                            const labelOk = pkg.hasActiveLabel && Boolean(pkg.trackingNumber);
                            const canAdd = sameClient && carrierOk && labelOk;
                            let blockReason = '';
                            if (!sameClient) blockReason = 'Different client tenant';
                            else if (!labelOk) blockReason = 'Active shipping label required';
                            else if (!carrierOk) blockReason = `Carrier ${pkg.carrierCode} mismatch`;

                            return (
                              <tr key={pkg.id} data-testid={`eligible-row-${pkg.packageNumber}`}>
                                <td className="mono-cell">{pkg.packageNumber}</td>
                                <td className="mono-cell">{pkg.orderNumber || '—'}</td>
                                <td>
                                  <div>{pkg.carrierCode || 'Unlabeled'}</div>
                                  <div className="mono-cell outbound-subtitle">
                                    {pkg.trackingNumber || 'No tracking'}
                                  </div>
                                </td>
                                <td>{pkg.grossWeightKg.toFixed(2)} kg</td>
                                <td>
                                  <PackageStatusBadge status={pkg.status} />
                                </td>
                                <td>
                                  <button
                                    type="button"
                                    className="btn btn-primary btn-xs"
                                    disabled={!canAdd || mutatingPackageId === pkg.id}
                                    title={blockReason || 'Consolidate carton into shipment'}
                                    onClick={() => handleAddPackage(pkg)}
                                    data-testid={`add-eligible-pkg-${pkg.packageNumber}`}
                                  >
                                    {canAdd ? '+ Consolidate' : blockReason}
                                  </button>
                                </td>
                              </tr>
                            );
                          })}
                        </tbody>
                      </table>
                    </OutboundTableRegion>
                  )}
                </div>
              )}
            </div>
          )}
        </section>
      </div>

      <BillOfLadingModal
        isOpen={bolModalOpen}
        shipment={activeShipment}
        canShip={canShip}
        onClose={() => setBolModalOpen(false)}
        onBolIssued={() => refreshAll(activeShipment?.id)}
        onFeedback={setFeedback}
      />

      <DriverCustodyHandoverModal
        isOpen={handoverModalOpen}
        shipment={activeShipment}
        eligibleDrivers={eligibleDrivers}
        canShip={canShip}
        onClose={() => setHandoverModalOpen(false)}
        onDispatched={() => refreshAll(activeShipment?.id)}
        onFeedback={setFeedback}
      />
    </div>
  );
};
