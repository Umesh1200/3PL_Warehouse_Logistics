import React, { useState, useEffect, useCallback } from 'react';
import { useFacility } from '../../context/FacilityContext';
import { useAuth } from '../../context/AuthContext';
import { receivingApi, ReceivingSessionQueryParams } from '../../services/receivingApi';
import {
  ReceivingSessionDto,
  ReceiptLineDto,
  ReceivingStatus,
  ReceiptConditionStatus,
  DiscrepancyType,
  StartReceivingSessionRequest,
  ScanReceiptLineRequest,
  RecordReceivingDiscrepancyRequest,
} from '../../types/inbound';

export const ReceivingPage: React.FC = () => {
  const { activeFacility } = useFacility();
  const { hasPermission } = useAuth();
  const canReceive = hasPermission('inbound:receive');

  const [sessions, setSessions] = useState<ReceivingSessionDto[]>([]);
  const [totalSessions, setTotalSessions] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<{ type: 'success' | 'error'; message: string } | null>(null);

  // Filters
  const [searchTerm, setSearchTerm] = useState('');
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [page, setPage] = useState(1);
  const pageSize = 10;

  // Selected session for detailed execution
  const [selectedSessionId, setSelectedSessionId] = useState<string | null>(null);
  const [sessionDetail, setSessionDetail] = useState<ReceivingSessionDto | null>(null);
  const [loadingDetail, setLoadingDetail] = useState(false);

  // Modals
  const [showStartModal, setShowStartModal] = useState(false);
  const [showDiscrepancyModal, setShowDiscrepancyModal] = useState(false);
  const [showCompleteConfirm, setShowCompleteConfirm] = useState(false);

  // Form states with submission guards
  const [isStarting, setIsStarting] = useState(false);
  const [isScanning, setIsScanning] = useState(false);
  const [isCompleting, setIsCompleting] = useState(false);
  const [isRecordingDiscrepancy, setIsRecordingDiscrepancy] = useState(false);

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
    sku: string;
    quantityReceived: number;
    lpn: string;
    lotNumber: string;
    expirationDate: string;
    condition: ReceiptConditionStatus;
    notes: string;
  }>({
    itemId: '',
    sku: '',
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

  const loadSessions = useCallback(
    async (signal?: AbortSignal) => {
      if (!activeFacility) return;
      try {
        setLoading(true);
        setError(null);
        const query: ReceivingSessionQueryParams = {
          facilityId: activeFacility.id,
          searchTerm: searchTerm.trim() || undefined,
          status: statusFilter !== 'all' ? Number(statusFilter) : undefined,
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

  const handleStartSession = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!activeFacility || isStarting) return;

    try {
      setIsStarting(true);
      setFeedback(null);
      const payload: StartReceivingSessionRequest = {
        facilityId: activeFacility.id,
        dockDoorId: startForm.dockDoorId || undefined,
        yardVisitId: startForm.yardVisitId || undefined,
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
    if (!sessionDetail || isScanning || !canReceive) return;
    if (!scanForm.itemId) {
      setFeedback({ type: 'error', message: 'Item ID / SKU is required to scan receipt line.' });
      return;
    }

    try {
      setIsScanning(true);
      setFeedback(null);
      const req: ScanReceiptLineRequest = {
        itemId: scanForm.itemId,
        quantityReceived: Number(scanForm.quantityReceived),
        lpn: scanForm.lpn.trim() || undefined,
        lotNumber: scanForm.lotNumber.trim() || undefined,
        expirationDate: scanForm.expirationDate ? new Date(scanForm.expirationDate).toISOString() : undefined,
        condition: scanForm.condition,
        notes: scanForm.notes.trim() || undefined,
      };

      await receivingApi.scanLine(sessionDetail.id, req);
      setScanForm({
        itemId: '',
        sku: '',
        quantityReceived: 1,
        lpn: '',
        lotNumber: '',
        expirationDate: '',
        condition: ReceiptConditionStatus.Sound,
        notes: '',
      });
      setFeedback({ type: 'success', message: 'Line item scanned and added to intake session.' });
      await loadSessionDetail(sessionDetail.id);
    } catch (err: any) {
      setFeedback({ type: 'error', message: err.message || 'Failed to scan receipt line' });
    } finally {
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
    if (!sessionDetail || isCompleting || !canReceive) return;

    try {
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
      setIsCompleting(false);
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
      <div className="page-header">
        <div>
          <h1 className="page-title">Inbound Goods Receiving</h1>
          <p className="page-subtitle">
            Facility Floor Ingestion &bull; {activeFacility ? `${activeFacility.name} (${activeFacility.facilityCode})` : 'No Facility Selected'}
          </p>
        </div>
        {canReceive && !selectedSessionId && (
          <button
            className="btn btn-primary"
            onClick={() => setShowStartModal(true)}
            data-testid="start-session-btn"
          >
            + Start Receiving Session
          </button>
        )}
      </div>

      {feedback && (
        <div
          className={`alert-banner ${feedback.type}`}
          role="alert"
          data-testid="feedback-banner"
        >
          <span>{feedback.message}</span>
          <button className="alert-close" onClick={() => setFeedback(null)} aria-label="Dismiss alert">
            &times;
          </button>
        </div>
      )}

      {error && (
        <div className="alert-banner error" role="alert">
          <span>{error}</span>
        </div>
      )}

      {/* VIEW: SESSION DETAIL WORKSPACE */}
      {selectedSessionId && sessionDetail ? (
        <div className="session-detail-view" data-testid="session-detail-view">
          <div className="detail-top-nav">
            <button
              className="btn btn-secondary btn-sm"
              onClick={() => setSelectedSessionId(null)}
              data-testid="back-to-sessions-btn"
            >
              &larr; Back to Sessions List
            </button>
            <div className="session-id-tag">
              <span className="session-number-label">{sessionDetail.sessionNumber}</span>
              {getStatusBadge(sessionDetail.status)}
            </div>
          </div>

          <div className="session-summary-card card">
            <div className="summary-grid">
              <div>
                <span className="summary-label">Dock Door:</span>
                <span className="summary-val">{sessionDetail.doorNumber || 'Unassigned'}</span>
              </div>
              <div>
                <span className="summary-label">Trailer / Visit:</span>
                <span className="summary-val">
                  {sessionDetail.trailerNumber || sessionDetail.visitNumber || 'N/A'}
                </span>
              </div>
              <div>
                <span className="summary-label">ASN / PO Link:</span>
                <span className="summary-val">
                  {sessionDetail.asnNumber || sessionDetail.poNumber || 'Blind Receiving'}
                </span>
              </div>
              <div>
                <span className="summary-label">Started At:</span>
                <span className="summary-val">{new Date(sessionDetail.startedAtUtc).toLocaleString()}</span>
              </div>
              <div>
                <span className="summary-label">Total Lines Scanned:</span>
                <span className="summary-val" data-testid="lines-count-val">{sessionDetail.lines.length}</span>
              </div>
              <div>
                <span className="summary-label">Supervisor Approval:</span>
                <span className="summary-val">
                  {sessionDetail.requiresSupervisorApproval ? (
                    sessionDetail.isSupervisorApproved ? (
                      <span className="badge-approved">Approved</span>
                    ) : (
                      <span className="badge-pending-approval">Pending Approval</span>
                    )
                  ) : (
                    'Not Required'
                  )}
                </span>
              </div>
            </div>

            {sessionDetail.requiresSupervisorApproval && !sessionDetail.isSupervisorApproved && (
              <div className="supervisor-warning-banner" role="alert">
                <strong>Attention:</strong> This session requires supervisor sign-off before completion due to blind receiving or flagged discrepancies.
              </div>
            )}
          </div>

          {/* LINE SCANNER FORM (Only when session is active and user can receive) */}
          {sessionDetail.status !== ReceivingStatus.Completed &&
            sessionDetail.status !== ReceivingStatus.Cancelled &&
            canReceive && (
              <div className="scanner-card card" data-testid="line-scanner-card">
                <h3 className="card-title">RF Intake Line Scanner</h3>
                <form onSubmit={handleScanLine} className="scanner-form">
                  <div className="form-row">
                    <div className="form-group">
                      <label htmlFor="scan-item-id">Item ID / SKU GUID *</label>
                      <input
                        id="scan-item-id"
                        type="text"
                        className="form-input"
                        placeholder="Scan or enter item ID"
                        value={scanForm.itemId}
                        onChange={(e) => setScanForm({ ...scanForm, itemId: e.target.value })}
                        required
                        data-testid="scan-item-id-input"
                      />
                    </div>
                    <div className="form-group">
                      <label htmlFor="scan-qty">Quantity Received *</label>
                      <input
                        id="scan-qty"
                        type="number"
                        min="1"
                        step="1"
                        className="form-input"
                        value={scanForm.quantityReceived}
                        onChange={(e) => setScanForm({ ...scanForm, quantityReceived: Number(e.target.value) })}
                        required
                        data-testid="scan-qty-input"
                      />
                    </div>
                    <div className="form-group">
                      <label htmlFor="scan-lpn">LPN Pallet Barcode</label>
                      <input
                        id="scan-lpn"
                        type="text"
                        className="form-input"
                        placeholder="Auto-generated if empty"
                        value={scanForm.lpn}
                        onChange={(e) => setScanForm({ ...scanForm, lpn: e.target.value })}
                        data-testid="scan-lpn-input"
                      />
                    </div>
                  </div>

                  <div className="form-row">
                    <div className="form-group">
                      <label htmlFor="scan-condition">Condition</label>
                      <select
                        id="scan-condition"
                        className="form-select"
                        value={scanForm.condition}
                        onChange={(e) =>
                          setScanForm({ ...scanForm, condition: Number(e.target.value) as ReceiptConditionStatus })
                        }
                        data-testid="scan-condition-select"
                      >
                        <option value={ReceiptConditionStatus.Sound}>Sound (Standard Storage)</option>
                        <option value={ReceiptConditionStatus.Damaged}>Damaged (Damaged Hold)</option>
                        <option value={ReceiptConditionStatus.Defective}>Defective (Quarantine)</option>
                      </select>
                    </div>
                    <div className="form-group">
                      <label htmlFor="scan-lot">Lot Number</label>
                      <input
                        id="scan-lot"
                        type="text"
                        className="form-input"
                        placeholder="LOT-XXX"
                        value={scanForm.lotNumber}
                        onChange={(e) => setScanForm({ ...scanForm, lotNumber: e.target.value })}
                        data-testid="scan-lot-input"
                      />
                    </div>
                    <div className="form-group">
                      <label htmlFor="scan-exp">Expiration Date</label>
                      <input
                        id="scan-exp"
                        type="date"
                        className="form-input"
                        value={scanForm.expirationDate}
                        onChange={(e) => setScanForm({ ...scanForm, expirationDate: e.target.value })}
                      />
                    </div>
                  </div>

                  <div className="form-actions">
                    <button
                      type="submit"
                      className="btn btn-primary"
                      disabled={isScanning}
                      data-testid="submit-line-btn"
                    >
                      {isScanning ? 'Adding Line...' : '+ Scan & Ingest Line'}
                    </button>
                    <button
                      type="button"
                      className="btn btn-secondary"
                      onClick={() => setShowDiscrepancyModal(true)}
                      data-testid="record-discrepancy-btn"
                    >
                      ! Report Discrepancy
                    </button>
                  </div>
                </form>
              </div>
            )}

          {/* SCANNED LINES TABLE */}
          <div className="card lines-table-card">
            <h3 className="card-title">Received Inventory Lines ({sessionDetail.lines.length})</h3>
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
                        No receipt lines scanned yet. Use the intake scanner above.
                      </td>
                    </tr>
                  ) : (
                    sessionDetail.lines.map((line) => (
                      <tr key={line.id} data-testid={`line-row-${line.id}`}>
                        <td className="code-font">{line.lpn || 'N/A'}</td>
                        <td className="fw-600">{line.sku}</td>
                        <td>{line.itemDescription || '—'}</td>
                        <td>{getConditionBadge(line.condition)}</td>
                        <td className="fw-600">{line.quantityReceived}</td>
                        <td>{line.lotNumber || '—'}</td>
                        <td>{line.stagingLocationCode || 'Auto Staging'}</td>
                        <td>{new Date(line.scannedAtUtc).toLocaleTimeString()}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>

            {/* COMPLETION ACTION BAR */}
            {sessionDetail.status !== ReceivingStatus.Completed &&
              sessionDetail.status !== ReceivingStatus.Cancelled &&
              canReceive && (
                <div className="complete-action-bar">
                  <div className="action-hint">
                    Completing this session reconciles ASN lines and posts inventory stock into transient staging lanes.
                  </div>
                  <button
                    className="btn btn-success"
                    disabled={
                      isCompleting ||
                      sessionDetail.lines.length === 0 ||
                      (sessionDetail.requiresSupervisorApproval && !sessionDetail.isSupervisorApproved)
                    }
                    onClick={() => setShowCompleteConfirm(true)}
                    data-testid="complete-session-action-btn"
                  >
                    {isCompleting ? 'Finalizing Session...' : '✓ Complete Receiving Session'}
                  </button>
                </div>
              )}
          </div>
        </div>
      ) : (
        /* VIEW: SESSIONS LIST */
        <div className="sessions-list-view">
          <div className="filter-bar card">
            <div className="filter-group">
              <input
                type="text"
                className="form-input search-input"
                placeholder="Search session #, trailer, door, ASN..."
                value={searchTerm}
                onChange={(e) => setSearchTerm(e.target.value)}
                data-testid="search-sessions-input"
              />
            </div>
            <div className="filter-group">
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
                Loading receiving sessions...
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
                          No receiving sessions found matching the criteria.
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
                          <td>{new Date(s.startedAtUtc).toLocaleDateString()}</td>
                          <td>{s.lines?.length || 0}</td>
                          <td>
                            <button
                              className="btn btn-secondary btn-sm"
                              onClick={() => setSelectedSessionId(s.id)}
                              data-testid={`open-session-${s.id}`}
                            >
                              {s.status === ReceivingStatus.Completed ? 'View Details' : 'Open Intake'}
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
        <div className="modal-overlay" role="dialog" aria-modal="true" data-testid="start-session-modal">
          <div className="modal-card">
            <h2 className="modal-title">Start New Goods Receiving Session</h2>
            <form onSubmit={handleStartSession}>
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

              <div className="modal-actions">
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={() => setShowStartModal(false)}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="btn btn-primary"
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
        <div className="modal-overlay" role="dialog" aria-modal="true" data-testid="complete-confirm-modal">
          <div className="modal-card">
            <h2 className="modal-title">Finalize Receiving Session?</h2>
            <p className="modal-body-text">
              Are you sure you want to complete session <strong>{sessionDetail.sessionNumber}</strong>?
            </p>
            <p className="modal-subtext">
              This will lock all {sessionDetail.lines.length} scanned intake lines and atomically update the warehouse
              stock-on-hand ledger into transient staging lanes. This action cannot be reversed.
            </p>
            <div className="modal-actions">
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => setShowCompleteConfirm(false)}
                disabled={isCompleting}
              >
                Cancel
              </button>
              <button
                type="button"
                className="btn btn-success"
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
        <div className="modal-overlay" role="dialog" aria-modal="true" data-testid="discrepancy-modal">
          <div className="modal-card">
            <h2 className="modal-title">Record Receiving Discrepancy</h2>
            <form onSubmit={handleRecordDiscrepancy}>
              <div className="form-group">
                <label htmlFor="disc-type">Discrepancy Type *</label>
                <select
                  id="disc-type"
                  className="form-select"
                  value={discrepancyForm.type}
                  onChange={(e) =>
                    setDiscrepancyForm({ ...discrepancyForm, type: Number(e.target.value) as DiscrepancyType })
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

              <div className="modal-actions">
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={() => setShowDiscrepancyModal(false)}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="btn btn-danger"
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
          gap: 24px;
        }
        .page-header {
          display: flex;
          justify-content: space-between;
          align-items: center;
        }
        .page-title {
          font-size: 24px;
          font-weight: 700;
          color: var(--text-main);
          margin-bottom: 4px;
        }
        .page-subtitle {
          font-size: 14px;
          color: var(--text-muted);
        }
        .alert-banner {
          padding: 12px 16px;
          border-radius: var(--radius-md);
          display: flex;
          justify-content: space-between;
          align-items: center;
          font-size: 14px;
          font-weight: 500;
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
          font-size: 18px;
          cursor: pointer;
          color: inherit;
        }
        .card {
          background: #ffffff;
          border: 1px solid var(--border-subtle);
          border-radius: var(--radius-md);
          padding: 24px;
        }
        .card-title {
          font-size: 16px;
          font-weight: 600;
          color: var(--text-main);
          margin-bottom: 16px;
        }
        .detail-top-nav {
          display: flex;
          justify-content: space-between;
          align-items: center;
          margin-bottom: 16px;
        }
        .session-id-tag {
          display: flex;
          align-items: center;
          gap: 12px;
        }
        .session-number-label {
          font-family: monospace;
          font-weight: 700;
          font-size: 18px;
        }
        .summary-grid {
          display: grid;
          grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
          gap: 16px;
        }
        .summary-label {
          display: block;
          font-size: 12px;
          text-transform: uppercase;
          color: var(--text-muted);
          margin-bottom: 4px;
        }
        .summary-val {
          font-size: 14px;
          font-weight: 600;
          color: var(--text-main);
        }
        .supervisor-warning-banner {
          margin-top: 16px;
          padding: 12px;
          background: #fff8e1;
          color: #b78103;
          border-left: 4px solid #f9ab00;
          border-radius: 4px;
          font-size: 13px;
        }
        .scanner-form {
          display: flex;
          flex-direction: column;
          gap: 16px;
        }
        .form-row {
          display: grid;
          grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
          gap: 16px;
        }
        .form-group {
          display: flex;
          flex-direction: column;
          gap: 6px;
        }
        .form-group label {
          font-size: 13px;
          font-weight: 600;
          color: var(--text-main);
        }
        .form-input, .form-select, .form-textarea {
          padding: 8px 12px;
          border: 1px solid var(--border-subtle);
          border-radius: var(--radius-sm);
          font-size: 14px;
        }
        .form-actions {
          display: flex;
          gap: 12px;
          margin-top: 8px;
        }
        .complete-action-bar {
          margin-top: 24px;
          padding-top: 16px;
          border-top: 1px solid var(--border-subtle);
          display: flex;
          justify-content: space-between;
          align-items: center;
        }
        .action-hint {
          font-size: 13px;
          color: var(--text-muted);
        }
        .filter-bar {
          display: flex;
          gap: 16px;
          padding: 16px;
        }
        .enterprise-table {
          width: 100%;
          border-collapse: collapse;
          font-size: 14px;
        }
        .enterprise-table th {
          text-align: left;
          padding: 12px;
          background: #f8fafc;
          border-bottom: 2px solid var(--border-subtle);
          color: var(--text-muted);
          font-size: 12px;
          text-transform: uppercase;
        }
        .enterprise-table td {
          padding: 12px;
          border-bottom: 1px solid var(--border-subtle);
        }
        .code-font {
          font-family: monospace;
        }
        .fw-600 {
          font-weight: 600;
        }
        .empty-row {
          text-align: center;
          padding: 32px;
          color: var(--text-muted);
        }
        .status-badge {
          display: inline-block;
          padding: 4px 8px;
          border-radius: 12px;
          font-size: 12px;
          font-weight: 600;
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
          font-size: 12px;
          font-weight: 600;
        }
        .condition-badge.sound { background: #e6f4ea; color: #137333; }
        .condition-badge.damaged { background: #fce8e6; color: #c5221f; }
        .condition-badge.defective { background: #fef7e0; color: #b06000; }
        .modal-overlay {
          position: fixed;
          inset: 0;
          background: rgba(0, 0, 0, 0.5);
          display: flex;
          align-items: center;
          justify-content: center;
          z-index: 1000;
        }
        .modal-card {
          background: #ffffff;
          border-radius: var(--radius-md);
          padding: 24px;
          width: 100%;
          max-width: 500px;
          max-height: calc(100dvh - 40px);
          overflow-y: auto;
        }
        .modal-title {
          font-size: 18px;
          font-weight: 700;
          margin-bottom: 16px;
        }
        .modal-body-text {
          font-size: 14px;
          margin-bottom: 8px;
        }
        .modal-subtext {
          font-size: 12px;
          color: var(--text-muted);
          margin-bottom: 16px;
        }
        .checkbox-row {
          margin: 12px 0;
        }
        .checkbox-label {
          display: flex;
          align-items: center;
          gap: 8px;
          font-size: 14px;
          cursor: pointer;
        }
        .modal-actions {
          display: flex;
          justify-content: flex-end;
          gap: 12px;
          margin-top: 24px;
        }
      `}</style>
    </div>
  );
};
