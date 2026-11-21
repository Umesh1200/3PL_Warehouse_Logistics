import React, { useState, useEffect, useCallback, useRef } from 'react';
import { useFacility } from '../../context/FacilityContext';
import { useAuth } from '../../context/AuthContext';
import { receivingApi, ReceivingSessionQueryParams } from '../../services/receivingApi';
import { AppIcon } from '../../components/common/AppIcon';
import {
  ReceivingSessionDto,
  ReceivingStatus,
  ReceiptConditionStatus,
  DiscrepancyType,
  DiscrepancyResolutionStatus,
  StartReceivingSessionRequest,
  ScanReceiptLineRequest,
  RecordReceivingDiscrepancyRequest,
} from '../../types/inbound';

export const ReceivingPage: React.FC = () => {
  const { activeFacility } = useFacility();
  const { hasPermission } = useAuth();
  const canReceive = hasPermission('inbound:receive');
  const canApprove = hasPermission('inbound:receive:approve');
  const canResolveDiscrepancy = hasPermission('inbound:discrepancy:resolve');

  const [sessions, setSessions] = useState<ReceivingSessionDto[]>([]);
  const [, setTotalSessions] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<{ type: 'success' | 'error'; message: string } | null>(null);

  // Filters
  const [searchTerm, setSearchTerm] = useState('');
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [page] = useState(1);
  const pageSize = 10;

  // Selected session for detailed execution
  const [selectedSessionId, setSelectedSessionId] = useState<string | null>(null);
  const [sessionDetail, setSessionDetail] = useState<ReceivingSessionDto | null>(null);
  const [, setLoadingDetail] = useState(false);

  // Modals
  const [showStartModal, setShowStartModal] = useState(false);
  const [showDiscrepancyModal, setShowDiscrepancyModal] = useState(false);
  const [showCompleteConfirm, setShowCompleteConfirm] = useState(false);

  // Form states with submission guards
  const [isStarting, setIsStarting] = useState(false);
  const [isScanning, setIsScanning] = useState(false);
  const [isCompleting, setIsCompleting] = useState(false);
  const [isRecordingDiscrepancy, setIsRecordingDiscrepancy] = useState(false);
  const [isApproving, setIsApproving] = useState(false);
  const [resolvingDiscrepancyId, setResolvingDiscrepancyId] = useState<string | null>(null);

  // Single-flight refs to guard against rapid hardware scanner keyboard-wedge double-submits
  const inFlightScanRef = useRef(false);
  const inFlightCompleteRef = useRef(false);

  // Focus management refs
  const scanItemIdInputRef = useRef<HTMLInputElement>(null);
  const startModalRef = useRef<HTMLDivElement>(null);
  const discrepancyModalRef = useRef<HTMLDivElement>(null);
  const completeModalRef = useRef<HTMLDivElement>(null);

  // Start Session Form
  const [startForm, setStartForm] = useState<Partial<StartReceivingSessionRequest>>({
    dockDoorId: '',
    yardVisitId: '',
    asnId: '',
    purchaseOrderId: '',
    sealNumber: '',
    sealIntact: true,
    isBlindReceiving: false,
    notes: '',
  });

  // Scan Line Form
  const [scanForm, setScanForm] = useState<{
    itemId: string;
    quantityReceived: number | string;
    lpn: string;
    lotNumber: string;
    expirationDate: string;
    condition: ReceiptConditionStatus;
    notes: string;
  }>({
    itemId: '',
    quantityReceived: 1,
    lpn: '',
    lotNumber: '',
    expirationDate: '',
    condition: ReceiptConditionStatus.Sound,
    notes: '',
  });

  // Discrepancy Form
  const [discrepancyForm, setDiscrepancyForm] = useState<{
    type: DiscrepancyType;
    discrepantQuantity: number;
    notes: string;
  }>({
    type: DiscrepancyType.QuantityOverage,
    discrepantQuantity: 1,
    notes: '',
  });

  // Load Sessions
  const loadSessions = useCallback(
    async (signal?: AbortSignal) => {
      if (!activeFacility) return;
      try {
        setLoading(true);
        setError(null);
        const query: ReceivingSessionQueryParams = {
          facilityId: activeFacility.id,
          searchTerm: searchTerm.trim() || undefined,
          status: statusFilter !== 'all' ? statusFilter : undefined,
          pageNumber: page,
          pageSize,
        };
        const res = await receivingApi.getSessions(query, signal);
        setSessions(res.items);
        setTotalSessions(res.totalCount);
      } catch (err: any) {
        if (err.name !== 'AbortError') {
          setError(err.message || 'Failed to load receiving sessions');
        }
      } finally {
        setLoading(false);
      }
    },
    [activeFacility, searchTerm, statusFilter, page]
  );

  // Load Session Detail
  const loadSessionDetail = useCallback(async (id: string, signal?: AbortSignal) => {
    try {
      setLoadingDetail(true);
      const res = await receivingApi.getSessionById(id, signal);
      setSessionDetail(res);
    } catch (err: any) {
      if (err.name !== 'AbortError') {
        setFeedback({ type: 'error', message: err.message || 'Failed to load session details' });
      }
    } finally {
      setLoadingDetail(false);
    }
  }, []);

  useEffect(() => {
    const ctrl = new AbortController();
    loadSessions(ctrl.signal);
    return () => ctrl.abort();
  }, [loadSessions]);

  useEffect(() => {
    if (selectedSessionId) {
      const ctrl = new AbortController();
      loadSessionDetail(selectedSessionId, ctrl.signal);
      return () => ctrl.abort();
    } else {
      setSessionDetail(null);
    }
  }, [selectedSessionId, loadSessionDetail]);

  // Focus restoration to scanner input when active session details load
  useEffect(() => {
    if (selectedSessionId && sessionDetail && canReceive) {
      const timer = setTimeout(() => {
        scanItemIdInputRef.current?.focus();
      }, 100);
      return () => clearTimeout(timer);
    }
  }, [selectedSessionId, sessionDetail?.id, canReceive]);

  // Accessible Modal Keyboard Trapping & Escape Dismissal
  useEffect(() => {
    const activeModal = showStartModal
      ? startModalRef.current
      : showDiscrepancyModal
      ? discrepancyModalRef.current
      : showCompleteConfirm
      ? completeModalRef.current
      : null;

    if (!activeModal) return;

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        if (showStartModal) setShowStartModal(false);
        if (showDiscrepancyModal) setShowDiscrepancyModal(false);
        if (showCompleteConfirm) setShowCompleteConfirm(false);
        return;
      }

      if (e.key === 'Tab') {
        const focusable = activeModal.querySelectorAll<HTMLElement>(
          'button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
        );
        if (focusable.length === 0) return;

        const first = focusable[0];
        const last = focusable[focusable.length - 1];

        if (e.shiftKey) {
          if (document.activeElement === first) {
            e.preventDefault();
            last.focus();
          }
        } else {
          if (document.activeElement === last) {
            e.preventDefault();
            first.focus();
          }
        }
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [showStartModal, showDiscrepancyModal, showCompleteConfirm]);

  const handleStartSession = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!activeFacility || isStarting) return;

    try {
      setIsStarting(true);
      setFeedback(null);
      const payload: StartReceivingSessionRequest = {
        facilityId: activeFacility.id,
        dockDoorId: startForm.dockDoorId || '',
        yardVisitId: startForm.yardVisitId || '',
        asnId: startForm.asnId || undefined,
        purchaseOrderId: startForm.purchaseOrderId || undefined,
        sealNumber: startForm.sealNumber || undefined,
        sealIntact: startForm.sealIntact,
        isBlindReceiving: startForm.isBlindReceiving || false,
        notes: startForm.notes || undefined,
      };

      const newSession = await receivingApi.startSession(payload);
      setShowStartModal(false);
      setStartForm({
        dockDoorId: '',
        yardVisitId: '',
        asnId: '',
        purchaseOrderId: '',
        sealNumber: '',
        sealIntact: true,
        isBlindReceiving: false,
        notes: '',
      });
      setFeedback({ type: 'success', message: `Receiving session ${newSession.sessionNumber} started successfully!` });
      await loadSessions();
      setSelectedSessionId(newSession.id);
    } catch (err: any) {
      setFeedback({ type: 'error', message: err.message || 'Failed to start receiving session' });
    } finally {
      setIsStarting(false);
    }
  };

  const handleScanLine = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!sessionDetail || isScanning || inFlightScanRef.current || !canReceive) return;

    const trimmedItemId = scanForm.itemId.trim();
    if (!trimmedItemId) {
      setFeedback({ type: 'error', message: 'Item ID (GUID) is required to scan receipt line.' });
      scanItemIdInputRef.current?.focus();
      return;
    }

    const qty = Number(scanForm.quantityReceived);
    if (isNaN(qty) || qty <= 0) {
      setFeedback({ type: 'error', message: 'Quantity received must be a positive number greater than zero.' });
      return;
    }

    try {
      inFlightScanRef.current = true;
      setIsScanning(true);
      setFeedback(null);

      const req: ScanReceiptLineRequest = {
        itemId: trimmedItemId,
        quantityReceived: qty,
        lpn: scanForm.lpn.trim() || undefined,
        lotNumber: scanForm.lotNumber.trim() || undefined,
        expirationDate: scanForm.expirationDate ? new Date(scanForm.expirationDate).toISOString() : undefined,
        condition: scanForm.condition,
        notes: scanForm.notes.trim() || undefined,
      };

      await receivingApi.scanLine(sessionDetail.id, req);
      setScanForm({
        itemId: '',
        quantityReceived: 1,
        lpn: '',
        lotNumber: '',
        expirationDate: '',
        condition: ReceiptConditionStatus.Sound,
        notes: '',
      });
      setFeedback({ type: 'success', message: 'Line item scanned and added to intake session.' });
      await loadSessionDetail(sessionDetail.id);

      // Restore focus predictably after scan for hardware wedge scanner continuity
      setTimeout(() => {
        scanItemIdInputRef.current?.focus();
      }, 50);
    } catch (err: any) {
      setFeedback({ type: 'error', message: err.message || 'Failed to scan receipt line' });
    } finally {
      inFlightScanRef.current = false;
      setIsScanning(false);
    }
  };

  const handleRecordDiscrepancy = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!sessionDetail || isRecordingDiscrepancy || !canReceive) return;

    try {
      setIsRecordingDiscrepancy(true);
      setFeedback(null);
      const req: RecordReceivingDiscrepancyRequest = {
        type: discrepancyForm.type,
        discrepantQuantity: Number(discrepancyForm.discrepantQuantity),
        notes: discrepancyForm.notes,
      };

      await receivingApi.recordDiscrepancy(sessionDetail.id, req);
      setShowDiscrepancyModal(false);
      setDiscrepancyForm({
        type: DiscrepancyType.QuantityOverage,
        discrepantQuantity: 1,
        notes: '',
      });
      setFeedback({ type: 'success', message: 'Discrepancy recorded and flagged for review.' });
      await loadSessionDetail(sessionDetail.id);
    } catch (err: any) {
      setFeedback({ type: 'error', message: err.message || 'Failed to record discrepancy' });
    } finally {
      setIsRecordingDiscrepancy(false);
    }
  };

  const handleCompleteSession = async () => {
    if (!sessionDetail || isCompleting || inFlightCompleteRef.current || !canReceive) return;

    try {
      inFlightCompleteRef.current = true;
      setIsCompleting(true);
      setFeedback(null);
      const updated = await receivingApi.completeSession(sessionDetail.id);
      setShowCompleteConfirm(false);
      setSessionDetail(updated);
      setFeedback({
        type: 'success',
        message: `Session ${updated.sessionNumber} finalized. Stock-on-hand ledger updated in staging locations.`,
      });
      await loadSessions();
    } catch (err: any) {
      setFeedback({ type: 'error', message: err.message || 'Failed to complete receiving session' });
    } finally {
      inFlightCompleteRef.current = false;
      setIsCompleting(false);
    }
  };

  const handleApproveSession = async () => {
    if (!sessionDetail || isApproving || !canApprove) return;

    try {
      setIsApproving(true);
      setFeedback(null);
      const updated = await receivingApi.approveSession(sessionDetail.id);
      setSessionDetail(updated);
      setFeedback({
        type: 'success',
        message: `Receiving session ${updated.sessionNumber} approved by supervisor. Ready for intake and completion.`,
      });
      await loadSessions();
    } catch (err: any) {
      setFeedback({ type: 'error', message: err.message || 'Failed to approve receiving session' });
    } finally {
      setIsApproving(false);
    }
  };

  const handleResolveDiscrepancy = async (
    discrepancyId: string,
    resolution: DiscrepancyResolutionStatus,
    notes?: string
  ) => {
    if (!sessionDetail || resolvingDiscrepancyId || !canResolveDiscrepancy) return;

    try {
      setResolvingDiscrepancyId(discrepancyId);
      setFeedback(null);
      await receivingApi.resolveDiscrepancy(sessionDetail.id, discrepancyId, {
        resolution,
        notes: notes || `Discrepancy resolved as ${resolution}`,
      });
      setFeedback({
        type: 'success',
        message: `Discrepancy successfully resolved as ${resolution}.`,
      });
      await loadSessionDetail(sessionDetail.id);
    } catch (err: any) {
      setFeedback({ type: 'error', message: err.message || 'Failed to resolve discrepancy' });
    } finally {
      setResolvingDiscrepancyId(null);
    }
  };

  const getStatusBadge = (status: ReceivingStatus) => {
    switch (status) {
      case ReceivingStatus.Started:
        return <span className="status-badge badge-started">Started</span>;
      case ReceivingStatus.InProgress:
        return <span className="status-badge badge-inprogress">In Progress</span>;
      case ReceivingStatus.Completed:
        return <span className="status-badge badge-completed">Completed</span>;
      case ReceivingStatus.Flagged:
        return <span className="status-badge badge-flagged">Flagged</span>;
      case ReceivingStatus.Cancelled:
        return <span className="status-badge badge-cancelled">Cancelled</span>;
      default:
        return <span className="status-badge">Unknown</span>;
    }
  };

  const getConditionBadge = (condition: ReceiptConditionStatus) => {
    switch (condition) {
      case ReceiptConditionStatus.Sound:
        return <span className="condition-badge sound">Sound</span>;
      case ReceiptConditionStatus.Damaged:
        return <span className="condition-badge damaged">Damaged</span>;
      case ReceiptConditionStatus.Defective:
        return <span className="condition-badge defective">Defective</span>;
      default:
        return <span className="condition-badge">Unknown</span>;
    }
  };

  return (
    <div className="receiving-page-container" role="region" aria-label="Goods Receiving Management">
      {/* 1. SESSION OVERVIEW HEADER */}
      <div className="page-header">
        <div className="page-header-text">
          <div className="header-icon-title">
            <AppIcon name="ArrowDownToLine" size="lg" color="primary" />
            <h1 className="page-title">Inbound Goods Receiving</h1>
          </div>
          <p className="page-subtitle">
            Facility Floor Ingestion &bull; {activeFacility ? `${activeFacility.name} (${activeFacility.facilityCode})` : 'No Facility Selected'}
          </p>
        </div>
        {canReceive && !selectedSessionId && (
          <button
            type="button"
            className="btn btn-primary btn-action-lg"
            onClick={() => setShowStartModal(true)}
            data-testid="start-session-btn"
          >
            <AppIcon name="Plus" size="sm" />
            <span>Start Receiving Session</span>
          </button>
        )}
      </div>

      {feedback && (
        <div
          className={`alert-banner ${feedback.type}`}
          role="alert"
          data-testid="feedback-banner"
        >
          <div className="alert-content">
            <AppIcon
              name={feedback.type === 'success' ? 'CheckCircle2' : 'AlertCircle'}
              size="md"
              color={feedback.type === 'success' ? 'success' : 'danger'}
            />
            <span>{feedback.message}</span>
          </div>
          <button
            type="button"
            className="alert-close"
            onClick={() => setFeedback(null)}
            aria-label="Dismiss alert"
          >
            <AppIcon name="X" size="sm" />
          </button>
        </div>
      )}

      {error && (
        <div className="alert-banner error" role="alert">
          <div className="alert-content">
            <AppIcon name="AlertTriangle" size="md" color="danger" />
            <span>{error}</span>
          </div>
        </div>
      )}

      {/* VIEW: SESSION DETAIL WORKSPACE */}
      {selectedSessionId && sessionDetail ? (
        <div className="session-detail-view" data-testid="session-detail-view">
          {/* 2. SELECTED DOCK/SESSION CONTEXT BANNER */}
          <div className="detail-top-nav">
            <button
              type="button"
              className="btn btn-secondary btn-nav-back"
              onClick={() => setSelectedSessionId(null)}
              data-testid="back-to-sessions-btn"
            >
              <AppIcon name="ChevronLeft" size="sm" />
              <span>Back to Sessions List</span>
            </button>
            <div className="session-id-tag">
              <span className="session-number-label code-font">{sessionDetail.sessionNumber}</span>
              {getStatusBadge(sessionDetail.status)}
            </div>
          </div>

          <div className="session-summary-card card" data-testid="session-summary-card">
            <div className="summary-grid">
              <div className="summary-col">
                <span className="summary-label">
                  <AppIcon name="Building2" size="sm" /> Dock Door
                </span>
                <span className="summary-val code-font">{sessionDetail.doorNumber || 'Unassigned'}</span>
              </div>
              <div className="summary-col">
                <span className="summary-label">
                  <AppIcon name="Truck" size="sm" /> Trailer / Visit
                </span>
                <span className="summary-val code-font">
                  {sessionDetail.trailerNumber || sessionDetail.visitNumber || 'N/A'}
                </span>
              </div>
              <div className="summary-col">
                <span className="summary-label">
                  <AppIcon name="Receipt" size="sm" /> ASN / PO Link
                </span>
                <span className="summary-val code-font">
                  {sessionDetail.asnNumber || sessionDetail.poNumber || 'Blind Receiving'}
                </span>
              </div>
              <div className="summary-col">
                <span className="summary-label">
                  <AppIcon name="Clock" size="sm" /> Started At
                </span>
                <span className="summary-val">{new Date(sessionDetail.startedAtUtc).toLocaleTimeString()}</span>
              </div>
              <div className="summary-col">
                <span className="summary-label">
                  <AppIcon name="Layers" size="sm" /> Total Lines
                </span>
                <span className="summary-val" data-testid="lines-count-val">{sessionDetail.lines.length}</span>
              </div>
              <div className="summary-col">
                <span className="summary-label">
                  <AppIcon name="ShieldCheck" size="sm" /> Supervisor Status
                </span>
                <span className="summary-val">
                  {sessionDetail.requiresSupervisorApproval ? (
                    sessionDetail.isSupervisorApproved ? (
                      <span className="badge-approved">Approved</span>
                    ) : (
                      <span className="badge-pending-approval">Pending Approval</span>
                    )
                  ) : (
                    <span className="text-muted">Standard</span>
                  )}
                </span>
              </div>
            </div>

            {sessionDetail.requiresSupervisorApproval && !sessionDetail.isSupervisorApproved && (
              <div className="supervisor-warning-banner" role="alert" data-testid="supervisor-warning-banner">
                <div className="warning-content">
                  <AppIcon name="AlertTriangle" size="md" color="warning" />
                  <div>
                    <strong>Supervisor Sign-Off Required:</strong> This session involves blind receiving or flagged discrepancies. Authorization is required prior to completion.
                  </div>
                </div>
                {canApprove && (
                  <button
                    type="button"
                    className="btn btn-warning btn-sm"
                    onClick={handleApproveSession}
                    disabled={isApproving}
                    data-testid="approve-session-btn"
                  >
                    <AppIcon name="ShieldCheck" size="sm" />
                    <span>{isApproving ? 'Approving...' : 'Approve Session (Supervisor)'}</span>
                  </button>
                )}
              </div>
            )}
          </div>

          {/* 3. PROMINENT INTAKE SCANNER WORKSTATION */}
          {sessionDetail.status !== ReceivingStatus.Completed &&
            sessionDetail.status !== ReceivingStatus.Cancelled &&
            canReceive && (
              <div className="scanner-card card" data-testid="line-scanner-card">
                <div className="scanner-header">
                  <div className="scanner-title-group">
                    <AppIcon name="Barcode" size="md" color="primary" />
                    <div>
                      <h2 className="card-title">RF Intake Line Scanner</h2>
                      <p className="scanner-subtitle">
                        Scan or key catalog Item GUID, quantity, and container license plate. Keyboard-wedge scanners supported.
                      </p>
                    </div>
                  </div>
                  <span className="scanner-station-pill">WORKSTATION ACTIVE</span>
                </div>

                <form onSubmit={handleScanLine} className="scanner-form" noValidate>
                  {/* Group 1: Item Identification & Quantity */}
                  <div className="scanner-section">
                    <div className="section-label-bar">
                      <span className="section-step-num">1</span>
                      <span className="section-step-title">Item Identification &amp; Quantity</span>
                    </div>
                    <div className="scanner-grid-primary">
                      <div className="form-group flex-2">
                        <label htmlFor="scan-item-id" className="form-label">
                          Item ID (Catalog GUID) *
                        </label>
                        <div className="input-with-icon">
                          <input
                            ref={scanItemIdInputRef}
                            id="scan-item-id"
                            type="text"
                            className="form-input scan-barcode-input"
                            placeholder="Scan or enter authoritative Item GUID"
                            value={scanForm.itemId}
                            onChange={(e) => setScanForm({ ...scanForm, itemId: e.target.value })}
                            required
                            autoComplete="off"
                            data-testid="scan-item-id-input"
                          />
                        </div>
                        <span className="input-helper-text">
                          Authoritative Item GUID required by backend contract. Scanners must emit catalog Item GUID.
                        </span>
                      </div>
                      <div className="form-group flex-1">
                        <label htmlFor="scan-qty" className="form-label">
                          Quantity Received *
                        </label>
                        <input
                          id="scan-qty"
                          type="number"
                          min="1"
                          step="1"
                          className="form-input scan-barcode-input scan-qty-input"
                          value={scanForm.quantityReceived}
                          onChange={(e) => setScanForm({ ...scanForm, quantityReceived: e.target.value === '' ? '' : Number(e.target.value) })}
                          required
                          data-testid="scan-qty-input"
                        />
                        <span className="input-helper-text">Positive physical count at dock</span>
                      </div>
                    </div>
                  </div>

                  {/* Group 2: Container LPN & Lot Tracking */}
                  <div className="scanner-section">
                    <div className="section-label-bar">
                      <span className="section-step-num">2</span>
                      <span className="section-step-title">Container Pallet LPN &amp; Lot Traceability</span>
                    </div>
                    <div className="scanner-grid-secondary">
                      <div className="form-group">
                        <label htmlFor="scan-lpn" className="form-label">
                          Container / Pallet LPN
                        </label>
                        <input
                          id="scan-lpn"
                          type="text"
                          className="form-input scan-barcode-input"
                          placeholder="Scan Pallet LPN (auto-generated if empty)"
                          value={scanForm.lpn}
                          onChange={(e) => setScanForm({ ...scanForm, lpn: e.target.value })}
                          autoComplete="off"
                          data-testid="scan-lpn-input"
                        />
                        <span className="input-helper-text">Pallet license plate barcode</span>
                      </div>
                      <div className="form-group">
                        <label htmlFor="scan-lot" className="form-label">
                          Lot Number
                        </label>
                        <input
                          id="scan-lot"
                          type="text"
                          className="form-input scan-secondary-input"
                          placeholder="e.g. LOT-2026-X"
                          value={scanForm.lotNumber}
                          onChange={(e) => setScanForm({ ...scanForm, lotNumber: e.target.value })}
                          data-testid="scan-lot-input"
                        />
                        <span className="input-helper-text">Manufacturer lot code</span>
                      </div>
                      <div className="form-group">
                        <label htmlFor="scan-exp" className="form-label">
                          Expiration Date
                        </label>
                        <input
                          id="scan-exp"
                          type="date"
                          className="form-input scan-secondary-input"
                          value={scanForm.expirationDate}
                          onChange={(e) => setScanForm({ ...scanForm, expirationDate: e.target.value })}
                        />
                        <span className="input-helper-text">Perishable shelf-life</span>
                      </div>
                    </div>
                  </div>

                  {/* Group 3: Condition Status & Actions */}
                  <div className="scanner-section">
                    <div className="section-label-bar">
                      <span className="section-step-num">3</span>
                      <span className="section-step-title">Condition &amp; Ingestion Actions</span>
                    </div>
                    <div className="scanner-grid-tertiary">
                      <div className="form-group">
                        <label htmlFor="scan-condition" className="form-label">
                          Inspection Condition *
                        </label>
                        <select
                          id="scan-condition"
                          className="form-select scan-select-input"
                          value={scanForm.condition}
                          onChange={(e) =>
                            setScanForm({ ...scanForm, condition: e.target.value as ReceiptConditionStatus })
                          }
                          data-testid="scan-condition-select"
                        >
                          <option value={ReceiptConditionStatus.Sound}>Sound (Standard Ingestion)</option>
                          <option value={ReceiptConditionStatus.Damaged}>Damaged (Damaged Hold)</option>
                          <option value={ReceiptConditionStatus.Defective}>Defective (Quarantine Staging)</option>
                        </select>
                      </div>
                      <div className="form-group flex-2">
                        <label htmlFor="scan-notes" className="form-label">
                          Inspection Notes
                        </label>
                        <input
                          id="scan-notes"
                          type="text"
                          className="form-input scan-secondary-input"
                          placeholder="Dock observations, outer carton notes..."
                          value={scanForm.notes}
                          onChange={(e) => setScanForm({ ...scanForm, notes: e.target.value })}
                        />
                      </div>
                    </div>
                  </div>

                  {/* Operational Action Buttons (>=44px touch targets) */}
                  <div className="scanner-actions-bar">
                    <button
                      type="submit"
                      className="btn btn-primary btn-action-lg"
                      disabled={isScanning}
                      data-testid="submit-line-btn"
                    >
                      <AppIcon name="PackageCheck" size="md" />
                      <span>{isScanning ? 'Adding Line...' : '+ Scan & Ingest Line'}</span>
                    </button>
                    <button
                      type="button"
                      className="btn btn-secondary btn-action-lg"
                      onClick={() => setShowDiscrepancyModal(true)}
                      data-testid="record-discrepancy-btn"
                    >
                      <AppIcon name="AlertTriangle" size="md" color="warning" />
                      <span>Report Discrepancy</span>
                    </button>
                  </div>
                </form>
              </div>
            )}

          {/* 4. RECEIVED INVENTORY LINES TABLE */}
          <div className="card lines-table-card">
            <div className="table-header-row">
              <div className="table-header-title">
                <AppIcon name="Boxes" size="md" color="primary" />
                <h2 className="card-title">Received Inventory Lines ({sessionDetail.lines.length})</h2>
              </div>
              <span className="table-sub-count">Auto-staged in transient receiving dock buffers</span>
            </div>
            <div className="table-responsive">
              <table className="enterprise-table" data-testid="received-lines-table">
                <thead>
                  <tr>
                    <th>LPN</th>
                    <th>SKU</th>
                    <th>Description</th>
                    <th>Condition</th>
                    <th>Quantity</th>
                    <th>Lot Number</th>
                    <th>Staging Location</th>
                    <th>Scanned At</th>
                  </tr>
                </thead>
                <tbody>
                  {sessionDetail.lines.length === 0 ? (
                    <tr>
                      <td colSpan={8} className="empty-row">
                        No receipt lines scanned yet. Position cursor in the intake scanner workstation above.
                      </td>
                    </tr>
                  ) : (
                    sessionDetail.lines.map((line) => (
                      <tr key={line.id} data-testid={`line-row-${line.id}`}>
                        <td className="code-font fw-600">{line.lpn || 'N/A'}</td>
                        <td className="fw-600">{line.sku}</td>
                        <td className="text-muted">{line.itemDescription || '—'}</td>
                        <td>{getConditionBadge(line.condition)}</td>
                        <td className="fw-700">{line.quantityReceived}</td>
                        <td className="code-font">{line.lotNumber || '—'}</td>
                        <td className="code-font highlight-staging">{line.stagingLocationCode || 'Auto Staging'}</td>
                        <td className="text-muted">{new Date(line.scannedAtUtc).toLocaleTimeString()}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>

            {/* 5. DISCREPANCIES & SUPERVISOR APPROVAL */}
            {sessionDetail.discrepancies.length > 0 && (
              <div className="discrepancies-table-section" data-testid="discrepancies-table-card">
                <div className="table-header-row">
                  <div className="table-header-title">
                    <AppIcon name="AlertCircle" size="md" color="danger" />
                    <h2 className="card-title">Recorded Discrepancies &amp; Quality Holds ({sessionDetail.discrepancies.length})</h2>
                  </div>
                  <span className="table-sub-count">Holds prevent session closure until resolved</span>
                </div>
                <div className="table-responsive">
                  <table className="enterprise-table" data-testid="discrepancies-table">
                    <thead>
                      <tr>
                        <th>Type</th>
                        <th>Quantity</th>
                        <th>Notes</th>
                        <th>Reported At</th>
                        <th>Status</th>
                        <th>Resolution Notes</th>
                        {canResolveDiscrepancy && <th>Supervisor Actions</th>}
                      </tr>
                    </thead>
                    <tbody>
                      {sessionDetail.discrepancies.map((d) => (
                        <tr key={d.id} data-testid={`discrepancy-row-${d.id}`}>
                          <td className="fw-600 text-danger">{d.type}</td>
                          <td className="fw-600">{d.discrepantQuantity}</td>
                          <td>{d.notes || '—'}</td>
                          <td className="text-muted">{new Date(d.reportedAtUtc).toLocaleTimeString()}</td>
                          <td>
                            <span className={`status-badge badge-${d.status.toLowerCase()}`}>
                              {d.status}
                            </span>
                          </td>
                          <td className="text-muted">{d.resolutionNotes || (d.resolvedAtUtc ? `Resolved at ${new Date(d.resolvedAtUtc).toLocaleTimeString()}` : '—')}</td>
                          {canResolveDiscrepancy && (
                            <td>
                              {d.status === DiscrepancyResolutionStatus.PendingReview ? (
                                <div className="supervisor-action-buttons">
                                  <button
                                    type="button"
                                    className="btn btn-sm btn-success"
                                    onClick={() => handleResolveDiscrepancy(d.id, DiscrepancyResolutionStatus.Approved)}
                                    disabled={resolvingDiscrepancyId === d.id}
                                    data-testid={`resolve-approve-${d.id}`}
                                  >
                                    Approve
                                  </button>
                                  <button
                                    type="button"
                                    className="btn btn-sm btn-warning"
                                    onClick={() => handleResolveDiscrepancy(d.id, DiscrepancyResolutionStatus.Quarantined)}
                                    disabled={resolvingDiscrepancyId === d.id}
                                    data-testid={`resolve-quarantine-${d.id}`}
                                  >
                                    Quarantine
                                  </button>
                                  <button
                                    type="button"
                                    className="btn btn-sm btn-danger"
                                    onClick={() => handleResolveDiscrepancy(d.id, DiscrepancyResolutionStatus.Rejected)}
                                    disabled={resolvingDiscrepancyId === d.id}
                                    data-testid={`resolve-reject-${d.id}`}
                                  >
                                    Reject
                                  </button>
                                </div>
                              ) : (
                                <span className="text-muted">Resolved</span>
                              )}
                            </td>
                          )}
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            )}

            {/* 6. SESSION COMPLETION ACTION BAR */}
            {sessionDetail.status !== ReceivingStatus.Completed &&
              sessionDetail.status !== ReceivingStatus.Cancelled &&
              canReceive && (
                <div className="complete-action-bar">
                  <div className="action-hint">
                    <AppIcon name="Info" size="sm" color="primary" />
                    <span>
                      Completing this session reconciles ASN lines and posts inventory stock into transient staging lanes.
                    </span>
                  </div>
                  <button
                    type="button"
                    className="btn btn-success btn-action-lg"
                    disabled={
                      isCompleting ||
                      sessionDetail.lines.length === 0 ||
                      (sessionDetail.requiresSupervisorApproval && !sessionDetail.isSupervisorApproved) ||
                      sessionDetail.discrepancies.some((d) => d.status === DiscrepancyResolutionStatus.PendingReview)
                    }
                    onClick={() => setShowCompleteConfirm(true)}
                    data-testid="complete-session-action-btn"
                  >
                    <AppIcon name="CheckCircle2" size="md" />
                    <span>{isCompleting ? 'Finalizing Session...' : '✓ Complete Receiving Session'}</span>
                  </button>
                </div>
              )}
          </div>
        </div>
      ) : (
        /* VIEW: SESSIONS LIST */
        <div className="sessions-list-view">
          <div className="filter-bar card">
            <div className="filter-group flex-2">
              <input
                type="text"
                className="form-input search-input"
                placeholder="Search session #, trailer, door, ASN..."
                value={searchTerm}
                onChange={(e) => setSearchTerm(e.target.value)}
                data-testid="search-sessions-input"
              />
            </div>
            <div className="filter-group flex-1">
              <select
                className="form-select status-select"
                value={statusFilter}
                onChange={(e) => setStatusFilter(e.target.value)}
                data-testid="status-filter-select"
              >
                <option value="all">All Statuses</option>
                <option value={ReceivingStatus.Started}>Started</option>
                <option value={ReceivingStatus.InProgress}>In Progress</option>
                <option value={ReceivingStatus.Completed}>Completed</option>
                <option value={ReceivingStatus.Flagged}>Flagged</option>
                <option value={ReceivingStatus.Cancelled}>Cancelled</option>
              </select>
            </div>
          </div>

          <div className="card table-card">
            {loading ? (
              <div className="table-loading" data-testid="table-loading">
                <AppIcon name="RefreshCw" size="md" className="spin-icon" />
                <span>Loading receiving sessions...</span>
              </div>
            ) : (
              <div className="table-responsive">
                <table className="enterprise-table" data-testid="sessions-table">
                  <thead>
                    <tr>
                      <th>Session Number</th>
                      <th>Door</th>
                      <th>Trailer</th>
                      <th>ASN / PO</th>
                      <th>Status</th>
                      <th>Started At</th>
                      <th>Lines</th>
                      <th>Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {sessions.length === 0 ? (
                      <tr>
                        <td colSpan={8} className="empty-row" data-testid="empty-sessions-row">
                          No receiving sessions found matching the criteria. Click &quot;Start Receiving Session&quot; to begin.
                        </td>
                      </tr>
                    ) : (
                      sessions.map((s) => (
                        <tr key={s.id} data-testid={`session-row-${s.id}`}>
                          <td className="fw-600 code-font">{s.sessionNumber}</td>
                          <td>{s.doorNumber || '—'}</td>
                          <td>{s.trailerNumber || s.visitNumber || '—'}</td>
                          <td>{s.asnNumber || s.poNumber || 'Blind'}</td>
                          <td>{getStatusBadge(s.status)}</td>
                          <td className="text-muted">{new Date(s.startedAtUtc).toLocaleDateString()}</td>
                          <td className="fw-600">{s.lines?.length || 0}</td>
                          <td>
                            <button
                              type="button"
                              className="btn btn-secondary btn-sm btn-open-session"
                              onClick={() => setSelectedSessionId(s.id)}
                              data-testid={`open-session-${s.id}`}
                            >
                              <AppIcon name={s.status === ReceivingStatus.Completed ? 'Eye' : 'PackageOpen'} size="sm" />
                              <span>{s.status === ReceivingStatus.Completed ? 'View Details' : 'Open Intake'}</span>
                            </button>
                          </td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </div>
      )}

      {/* START SESSION MODAL */}
      {showStartModal && (
        <div
          ref={startModalRef}
          className="modal-overlay"
          role="dialog"
          aria-modal="true"
          aria-labelledby="start-session-title"
          data-testid="start-session-modal"
        >
          <div className="modal-card">
            <div className="modal-header">
              <div className="modal-title-group">
                <AppIcon name="ArrowDownToLine" size="md" color="primary" />
                <h2 id="start-session-title" className="modal-title">Start New Goods Receiving Session</h2>
              </div>
              <button
                type="button"
                className="modal-close-btn"
                onClick={() => setShowStartModal(false)}
                aria-label="Close modal dialog"
              >
                <AppIcon name="X" size="sm" />
              </button>
            </div>
            <form onSubmit={handleStartSession}>
              <div className="modal-body">
                <div className="form-group">
                  <label htmlFor="start-dock-door">Dock Door ID *</label>
                  <input
                    id="start-dock-door"
                    type="text"
                    className="form-input"
                    placeholder="Dock Door GUID"
                    value={startForm.dockDoorId || ''}
                    onChange={(e) => setStartForm({ ...startForm, dockDoorId: e.target.value })}
                    required
                    data-testid="start-dockdoor-input"
                  />
                </div>

                <div className="form-group">
                  <label htmlFor="start-yard-visit">Yard Visit ID *</label>
                  <input
                    id="start-yard-visit"
                    type="text"
                    className="form-input"
                    placeholder="Yard Visit GUID"
                    value={startForm.yardVisitId || ''}
                    onChange={(e) => setStartForm({ ...startForm, yardVisitId: e.target.value })}
                    required
                    data-testid="start-yardvisit-input"
                  />
                </div>

                <div className="form-group">
                  <label htmlFor="start-asn">ASN ID (Optional)</label>
                  <input
                    id="start-asn"
                    type="text"
                    className="form-input"
                    placeholder="Advance Shipping Notice GUID"
                    value={startForm.asnId || ''}
                    onChange={(e) => setStartForm({ ...startForm, asnId: e.target.value })}
                    data-testid="start-asn-input"
                  />
                </div>

                <div className="form-group">
                  <label htmlFor="start-seal">Seal Number</label>
                  <input
                    id="start-seal"
                    type="text"
                    className="form-input"
                    placeholder="SEAL-XXXX"
                    value={startForm.sealNumber || ''}
                    onChange={(e) => setStartForm({ ...startForm, sealNumber: e.target.value })}
                  />
                </div>

                <div className="checkbox-row">
                  <label className="checkbox-label">
                    <input
                      type="checkbox"
                      checked={startForm.sealIntact ?? true}
                      onChange={(e) => setStartForm({ ...startForm, sealIntact: e.target.checked })}
                    />
                    <span>Seal Intact &amp; Verified</span>
                  </label>
                </div>

                <div className="checkbox-row">
                  <label className="checkbox-label">
                    <input
                      type="checkbox"
                      checked={startForm.isBlindReceiving}
                      onChange={(e) => setStartForm({ ...startForm, isBlindReceiving: e.target.checked })}
                      data-testid="start-blind-checkbox"
                    />
                    <span>Blind Receiving (Requires Supervisor Approval)</span>
                  </label>
                </div>
              </div>

              <div className="modal-actions">
                <button
                  type="button"
                  className="btn btn-secondary btn-action-md"
                  onClick={() => setShowStartModal(false)}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="btn btn-primary btn-action-md"
                  disabled={isStarting}
                  data-testid="submit-start-session-btn"
                >
                  {isStarting ? 'Starting...' : 'Start Session'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* COMPLETE CONFIRMATION MODAL */}
      {showCompleteConfirm && sessionDetail && (
        <div
          ref={completeModalRef}
          className="modal-overlay"
          role="dialog"
          aria-modal="true"
          aria-labelledby="complete-session-title"
          data-testid="complete-confirm-modal"
        >
          <div className="modal-card">
            <div className="modal-header">
              <div className="modal-title-group">
                <AppIcon name="CheckCircle2" size="md" color="success" />
                <h2 id="complete-session-title" className="modal-title">Finalize Receiving Session?</h2>
              </div>
              <button
                type="button"
                className="modal-close-btn"
                onClick={() => setShowCompleteConfirm(false)}
                aria-label="Close modal dialog"
              >
                <AppIcon name="X" size="sm" />
              </button>
            </div>
            <div className="modal-body">
              <p className="modal-body-text">
                Are you sure you want to complete session <strong className="code-font">{sessionDetail.sessionNumber}</strong>?
              </p>
              <p className="modal-subtext">
                This will lock all <strong>{sessionDetail.lines.length}</strong> scanned intake lines and atomically update the warehouse
                stock-on-hand ledger into transient staging lanes. This action cannot be reversed.
              </p>
            </div>
            <div className="modal-actions">
              <button
                type="button"
                className="btn btn-secondary btn-action-md"
                onClick={() => setShowCompleteConfirm(false)}
                disabled={isCompleting}
              >
                Cancel
              </button>
              <button
                type="button"
                className="btn btn-success btn-action-md"
                onClick={handleCompleteSession}
                disabled={isCompleting}
                data-testid="confirm-complete-btn"
              >
                {isCompleting ? 'Updating Stock...' : 'Confirm Completion'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* RECORD DISCREPANCY MODAL */}
      {showDiscrepancyModal && (
        <div
          ref={discrepancyModalRef}
          className="modal-overlay"
          role="dialog"
          aria-modal="true"
          aria-labelledby="discrepancy-modal-title"
          data-testid="discrepancy-modal"
        >
          <div className="modal-card">
            <div className="modal-header">
              <div className="modal-title-group">
                <AppIcon name="AlertTriangle" size="md" color="warning" />
                <h2 id="discrepancy-modal-title" className="modal-title">Record Receiving Discrepancy</h2>
              </div>
              <button
                type="button"
                className="modal-close-btn"
                onClick={() => setShowDiscrepancyModal(false)}
                aria-label="Close modal dialog"
              >
                <AppIcon name="X" size="sm" />
              </button>
            </div>
            <form onSubmit={handleRecordDiscrepancy}>
              <div className="modal-body">
                <div className="form-group">
                  <label htmlFor="disc-type">Discrepancy Type *</label>
                  <select
                    id="disc-type"
                    className="form-select"
                    value={discrepancyForm.type}
                    onChange={(e) =>
                      setDiscrepancyForm({ ...discrepancyForm, type: e.target.value as DiscrepancyType })
                    }
                    data-testid="disc-type-select"
                  >
                    <option value={DiscrepancyType.QuantityOverage}>Quantity Overage</option>
                    <option value={DiscrepancyType.QuantityShortage}>Quantity Shortage</option>
                    <option value={DiscrepancyType.DamagedGoods}>Damaged Goods</option>
                    <option value={DiscrepancyType.TempExcursion}>Temperature Excursion</option>
                    <option value={DiscrepancyType.UnexpectedSKU}>Unexpected SKU</option>
                  </select>
                </div>

                <div className="form-group">
                  <label htmlFor="disc-qty">Discrepant Quantity *</label>
                  <input
                    id="disc-qty"
                    type="number"
                    min="1"
                    className="form-input"
                    value={discrepancyForm.discrepantQuantity}
                    onChange={(e) =>
                      setDiscrepancyForm({ ...discrepancyForm, discrepantQuantity: Number(e.target.value) })
                    }
                    required
                    data-testid="disc-qty-input"
                  />
                </div>

                <div className="form-group">
                  <label htmlFor="disc-notes">Notes / Observations *</label>
                  <textarea
                    id="disc-notes"
                    className="form-textarea"
                    rows={3}
                    placeholder="Describe damage, seal integrity, or variance"
                    value={discrepancyForm.notes}
                    onChange={(e) => setDiscrepancyForm({ ...discrepancyForm, notes: e.target.value })}
                    required
                    data-testid="disc-notes-input"
                  />
                </div>
              </div>

              <div className="modal-actions">
                <button
                  type="button"
                  className="btn btn-secondary btn-action-md"
                  onClick={() => setShowDiscrepancyModal(false)}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="btn btn-danger btn-action-md"
                  disabled={isRecordingDiscrepancy}
                  data-testid="submit-discrepancy-btn"
                >
                  {isRecordingDiscrepancy ? 'Submitting...' : 'Record Discrepancy'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      <style>{`
        .receiving-page-container {
          display: flex;
          flex-direction: column;
          gap: 20px;
          max-width: 100%;
          box-sizing: border-box;
        }
        .page-header {
          display: flex;
          justify-content: space-between;
          align-items: center;
          gap: 16px;
          flex-wrap: wrap;
        }
        .page-header-text {
          display: flex;
          flex-direction: column;
          gap: 4px;
        }
        .header-icon-title {
          display: flex;
          align-items: center;
          gap: 10px;
        }
        .page-title {
          font-size: 22px;
          font-weight: 700;
          color: var(--text-main);
          margin: 0;
        }
        .page-subtitle {
          font-size: 13px;
          color: var(--text-muted);
          margin: 0;
        }
        .alert-banner {
          padding: 12px 16px;
          border-radius: var(--radius-md);
          display: flex;
          justify-content: space-between;
          align-items: center;
          font-size: 14px;
          font-weight: 500;
          gap: 12px;
        }
        .alert-content {
          display: flex;
          align-items: center;
          gap: 10px;
        }
        .alert-banner.success {
          background: #e6f4ea;
          color: #137333;
          border: 1px solid #ceead6;
        }
        .alert-banner.error {
          background: #fce8e6;
          color: #c5221f;
          border: 1px solid #fad2cf;
        }
        .alert-close {
          background: none;
          border: none;
          padding: 6px;
          cursor: pointer;
          color: inherit;
          display: flex;
          align-items: center;
          justify-content: center;
          min-width: 32px;
          min-height: 32px;
          border-radius: 4px;
        }
        .alert-close:hover {
          background: rgba(0, 0, 0, 0.05);
        }
        .card {
          background: #ffffff;
          border: 1px solid var(--border-subtle);
          border-radius: var(--radius-md);
          padding: 20px;
          box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
        }
        .card-title {
          font-size: 16px;
          font-weight: 600;
          color: var(--text-main);
          margin: 0;
        }
        .detail-top-nav {
          display: flex;
          justify-content: space-between;
          align-items: center;
          margin-bottom: 16px;
          gap: 12px;
          flex-wrap: wrap;
        }
        .btn-nav-back {
          display: inline-flex;
          align-items: center;
          gap: 6px;
          min-height: 40px;
          padding: 8px 14px;
        }
        .session-id-tag {
          display: flex;
          align-items: center;
          gap: 10px;
        }
        .session-number-label {
          font-family: var(--font-mono, monospace);
          font-weight: 700;
          font-size: 16px;
          color: var(--text-main);
        }
        .summary-grid {
          display: grid;
          grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
          gap: 16px;
        }
        .summary-col {
          display: flex;
          flex-direction: column;
          gap: 4px;
        }
        .summary-label {
          display: flex;
          align-items: center;
          gap: 5px;
          font-size: 11px;
          text-transform: uppercase;
          letter-spacing: 0.5px;
          color: var(--text-muted);
          font-weight: 600;
        }
        .summary-val {
          font-size: 14px;
          font-weight: 600;
          color: var(--text-main);
        }
        .supervisor-warning-banner {
          margin-top: 16px;
          padding: 12px 16px;
          background: #fff8e1;
          color: #b78103;
          border-left: 4px solid #f9ab00;
          border-radius: 4px;
          font-size: 13px;
          display: flex;
          justify-content: space-between;
          align-items: center;
          gap: 12px;
          flex-wrap: wrap;
        }
        .warning-content {
          display: flex;
          align-items: center;
          gap: 10px;
          flex: 1;
        }
        /* INTAKE WORKSTATION STYLES */
        .scanner-card {
          margin-top: 16px;
          border: 2px solid #2563eb;
          background: #fcfdfe;
        }
        .scanner-header {
          display: flex;
          justify-content: space-between;
          align-items: center;
          border-bottom: 1px solid var(--border-subtle);
          padding-bottom: 12px;
          margin-bottom: 16px;
          gap: 12px;
          flex-wrap: wrap;
        }
        .scanner-title-group {
          display: flex;
          align-items: center;
          gap: 10px;
        }
        .scanner-subtitle {
          font-size: 12px;
          color: var(--text-muted);
          margin: 2px 0 0 0;
        }
        .scanner-station-pill {
          display: inline-block;
          background: #eff6ff;
          color: #1d4ed8;
          border: 1px solid #bfdbfe;
          font-size: 11px;
          font-weight: 700;
          letter-spacing: 0.5px;
          padding: 4px 10px;
          border-radius: 12px;
        }
        .scanner-form {
          display: flex;
          flex-direction: column;
          gap: 16px;
        }
        .scanner-section {
          background: #ffffff;
          border: 1px solid var(--border-subtle);
          border-radius: var(--radius-sm);
          padding: 14px;
          display: flex;
          flex-direction: column;
          gap: 12px;
        }
        .section-label-bar {
          display: flex;
          align-items: center;
          gap: 8px;
          font-size: 12px;
          font-weight: 700;
          color: var(--text-muted);
          text-transform: uppercase;
          letter-spacing: 0.5px;
        }
        .section-step-num {
          display: inline-flex;
          align-items: center;
          justify-content: center;
          width: 20px;
          height: 20px;
          border-radius: 50%;
          background: #2563eb;
          color: #ffffff;
          font-size: 11px;
          font-weight: 700;
        }
        .scanner-grid-primary {
          display: flex;
          gap: 16px;
          flex-wrap: wrap;
        }
        .scanner-grid-secondary {
          display: grid;
          grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
          gap: 14px;
        }
        .scanner-grid-tertiary {
          display: flex;
          gap: 14px;
          flex-wrap: wrap;
        }
        .flex-1 { flex: 1; min-width: 140px; }
        .flex-2 { flex: 2; min-width: 220px; }
        .form-group {
          display: flex;
          flex-direction: column;
          gap: 4px;
        }
        .form-label {
          font-size: 12px;
          font-weight: 600;
          color: var(--text-main);
        }
        /* SCANNER INPUTS: >= 52px height, ~18px monospace readable font */
        .scan-barcode-input {
          min-height: 52px;
          font-size: 18px;
          font-family: var(--font-mono, monospace);
          font-weight: 600;
          letter-spacing: 0.5px;
          padding: 10px 14px;
          border: 2px solid #cbd5e1;
          border-radius: var(--radius-sm);
          background: #ffffff;
          color: var(--text-main);
          box-sizing: border-box;
          width: 100%;
          transition: border-color 0.15s ease, box-shadow 0.15s ease;
        }
        .scan-barcode-input:focus {
          border-color: #2563eb;
          box-shadow: 0 0 0 3px rgba(37, 99, 235, 0.2);
          outline: none;
        }
        .scan-qty-input {
          text-align: center;
          font-size: 20px;
          font-weight: 700;
        }
        .scan-secondary-input, .scan-select-input {
          min-height: 44px;
          padding: 8px 12px;
          border: 1px solid var(--border-subtle);
          border-radius: var(--radius-sm);
          font-size: 14px;
          box-sizing: border-box;
          width: 100%;
        }
        .input-helper-text {
          font-size: 11px;
          color: var(--text-muted);
          margin-top: 2px;
        }
        .scanner-actions-bar {
          display: flex;
          gap: 12px;
          margin-top: 4px;
          flex-wrap: wrap;
        }
        .btn-action-lg {
          min-height: 48px;
          padding: 10px 20px;
          font-size: 15px;
          font-weight: 600;
          display: inline-flex;
          align-items: center;
          justify-content: center;
          gap: 8px;
          cursor: pointer;
        }
        .btn-action-md {
          min-height: 44px;
          padding: 8px 16px;
          font-size: 14px;
          font-weight: 600;
        }
        .btn-open-session {
          display: inline-flex;
          align-items: center;
          gap: 6px;
          min-height: 36px;
        }
        /* LINES & DISCREPANCIES TABLE */
        .lines-table-card {
          margin-top: 16px;
        }
        .table-header-row {
          display: flex;
          justify-content: space-between;
          align-items: center;
          margin-bottom: 12px;
          gap: 12px;
          flex-wrap: wrap;
        }
        .table-header-title {
          display: flex;
          align-items: center;
          gap: 8px;
        }
        .table-sub-count {
          font-size: 12px;
          color: var(--text-muted);
        }
        .table-responsive {
          width: 100%;
          overflow-x: auto;
          -webkit-overflow-scrolling: touch;
        }
        .enterprise-table {
          width: 100%;
          border-collapse: collapse;
          font-size: 13px;
        }
        .enterprise-table th {
          text-align: left;
          padding: 10px 12px;
          background: #f8fafc;
          border-bottom: 2px solid var(--border-subtle);
          color: var(--text-muted);
          font-size: 11px;
          text-transform: uppercase;
          letter-spacing: 0.5px;
          white-space: nowrap;
        }
        .enterprise-table td {
          padding: 10px 12px;
          border-bottom: 1px solid var(--border-subtle);
          vertical-align: middle;
        }
        .code-font {
          font-family: var(--font-mono, monospace);
        }
        .fw-600 { font-weight: 600; }
        .fw-700 { font-weight: 700; }
        .text-muted { color: var(--text-muted); }
        .text-danger { color: #c5221f; }
        .highlight-staging {
          color: #0284c7;
          font-weight: 600;
        }
        .empty-row {
          text-align: center;
          padding: 32px 16px;
          color: var(--text-muted);
          font-style: italic;
        }
        .discrepancies-table-section {
          margin-top: 24px;
          padding-top: 16px;
          border-top: 1px solid var(--border-subtle);
        }
        .supervisor-action-buttons {
          display: flex;
          gap: 6px;
          flex-wrap: wrap;
        }
        .complete-action-bar {
          margin-top: 20px;
          padding-top: 16px;
          border-top: 1px solid var(--border-subtle);
          display: flex;
          justify-content: space-between;
          align-items: center;
          gap: 16px;
          flex-wrap: wrap;
        }
        .action-hint {
          display: flex;
          align-items: center;
          gap: 8px;
          font-size: 13px;
          color: var(--text-muted);
        }
        /* SESSIONS LIST FILTER BAR */
        .filter-bar {
          display: flex;
          gap: 12px;
          padding: 14px;
          flex-wrap: wrap;
        }
        .filter-group {
          display: flex;
          flex-direction: column;
        }
        .search-input {
          min-height: 44px;
        }
        .status-select {
          min-height: 44px;
        }
        .table-loading {
          display: flex;
          align-items: center;
          justify-content: center;
          gap: 10px;
          padding: 32px;
          color: var(--text-muted);
        }
        .spin-icon {
          animation: spin 1s linear infinite;
        }
        @keyframes spin {
          100% { transform: rotate(360deg); }
        }
        /* BADGES */
        .status-badge {
          display: inline-block;
          padding: 3px 8px;
          border-radius: 12px;
          font-size: 11px;
          font-weight: 600;
          text-transform: uppercase;
        }
        .badge-started { background: #e8f0fe; color: #1a73e8; }
        .badge-inprogress { background: #fef7e0; color: #b06000; }
        .badge-completed { background: #e6f4ea; color: #137333; }
        .badge-flagged { background: #fce8e6; color: #c5221f; }
        .badge-cancelled { background: #f1f3f4; color: #5f6368; }
        .badge-approved { color: #137333; font-weight: 600; }
        .badge-pending-approval { color: #b06000; font-weight: 600; }
        .condition-badge {
          display: inline-block;
          padding: 2px 6px;
          border-radius: 4px;
          font-size: 11px;
          font-weight: 600;
        }
        .condition-badge.sound { background: #e6f4ea; color: #137333; }
        .condition-badge.damaged { background: #fce8e6; color: #c5221f; }
        .condition-badge.defective { background: #fef7e0; color: #b06000; }
        /* ACCESSIBLE MODALS */
        .modal-overlay {
          position: fixed;
          inset: 0;
          background: rgba(15, 23, 42, 0.6);
          backdrop-filter: blur(2px);
          display: flex;
          align-items: center;
          justify-content: center;
          z-index: 1000;
          padding: 16px;
          box-sizing: border-box;
        }
        .modal-card {
          background: #ffffff;
          border-radius: var(--radius-md);
          width: 100%;
          max-width: 520px;
          max-height: calc(100vh - 40px);
          overflow-y: auto;
          box-shadow: 0 20px 40px rgba(15, 23, 42, 0.2);
          border: 1px solid var(--border-subtle);
          display: flex;
          flex-direction: column;
        }
        .modal-header {
          display: flex;
          justify-content: space-between;
          align-items: center;
          padding: 16px 20px;
          border-bottom: 1px solid var(--border-subtle);
          background: #fafbfc;
        }
        .modal-title-group {
          display: flex;
          align-items: center;
          gap: 8px;
        }
        .modal-title {
          font-size: 16px;
          font-weight: 700;
          color: var(--text-main);
          margin: 0;
        }
        .modal-close-btn {
          background: none;
          border: none;
          padding: 6px;
          cursor: pointer;
          color: var(--text-muted);
          min-width: 36px;
          min-height: 36px;
          display: flex;
          align-items: center;
          justify-content: center;
          border-radius: 4px;
        }
        .modal-close-btn:hover {
          background: #f1f5f9;
          color: var(--text-main);
        }
        .modal-body {
          padding: 20px;
          display: flex;
          flex-direction: column;
          gap: 14px;
        }
        .modal-body-text {
          font-size: 14px;
          color: var(--text-main);
          margin: 0 0 8px 0;
        }
        .modal-subtext {
          font-size: 13px;
          color: var(--text-muted);
          line-height: 1.5;
          margin: 0;
        }
        .checkbox-row {
          margin: 4px 0;
        }
        .checkbox-label {
          display: flex;
          align-items: center;
          gap: 8px;
          font-size: 13px;
          cursor: pointer;
          user-select: none;
        }
        .modal-actions {
          display: flex;
          justify-content: flex-end;
          gap: 12px;
          padding: 16px 20px;
          border-top: 1px solid var(--border-subtle);
          background: #fafbfc;
        }
        @media (max-width: 768px) {
          .scanner-grid-primary {
            flex-direction: column;
          }
          .scanner-grid-tertiary {
            flex-direction: column;
          }
          .complete-action-bar {
            flex-direction: column;
            align-items: stretch;
          }
        }
      `}</style>
    </div>
  );
};
