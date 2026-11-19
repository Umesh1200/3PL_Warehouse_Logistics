import React, { useState, useEffect, useCallback } from 'react';
import { useFacility } from '../../context/FacilityContext';
import { useAuth } from '../../context/AuthContext';
import { putawayApi } from '../../services/putawayApi';
import {
  PutawayTaskDto,
  PutawayTaskStatus,
  PutawayPriority,
  ConfirmPutawayTaskRequest,
  ReportExceptionRequest,
  GeneratePutawayTasksRequest,
  GetPutawayTasksQuery,
} from '../../types/inbound';

export const PutawayPage: React.FC = () => {
  const { activeFacility } = useFacility();
  const { hasPermission, user } = useAuth();
  const canManage = hasPermission('inbound:putaway:manage') || user?.roles.some((r) => r.toLowerCase() === 'superadmin');
  const canExecute = hasPermission('inbound:putaway:execute') || canManage;

  const [activeTab, setActiveTab] = useState<'queue' | 'rf'>('queue');

  // Task Queue state
  const [tasks, setTasks] = useState<PutawayTaskDto[]>([]);
  const [totalTasks, setTotalTasks] = useState(0);
  const [loadingTasks, setLoadingTasks] = useState(true);
  const [taskFilter, setTaskFilter] = useState<string>('all');
  const [searchTerm, setSearchTerm] = useState('');
  const [page, setPage] = useState(1);
  const pageSize = 10;

  // RF Next Task state
  const [nextTask, setNextTask] = useState<PutawayTaskDto | null>(null);
  const [loadingNext, setLoadingNext] = useState(false);

  // Dual-scan inputs
  const [scannedLpn, setScannedLpn] = useState('');
  const [scannedLocationBarcode, setScannedLocationBarcode] = useState('');
  const [allowOverride, setAllowOverride] = useState(false);

  // Modals & form guards
  const [showExceptionModal, setShowExceptionModal] = useState(false);
  const [exceptionReason, setExceptionReason] = useState('Obstructed Aisle');
  const [autoReRoute, setAutoReRoute] = useState(true);

  const [isGenerating, setIsGenerating] = useState(false);
  const [isConfirming, setIsConfirming] = useState(false);
  const [isReportingException, setIsReportingException] = useState(false);

  const [feedback, setFeedback] = useState<{ type: 'success' | 'error'; message: string } | null>(null);
  const [error, setError] = useState<string | null>(null);

  const loadTasks = useCallback(
    async (signal?: AbortSignal) => {
      if (!activeFacility) return;
      try {
        setLoadingTasks(true);
        setError(null);
        const query: GetPutawayTasksQuery = {
          facilityId: activeFacility.id,
          status: taskFilter !== 'all' ? Number(taskFilter) : undefined,
          searchTerm: searchTerm.trim() || undefined,
          pageNumber: page,
          pageSize,
        };
        const res = await putawayApi.getTasks(query, signal);
        setTasks(res.items);
        setTotalTasks(res.totalCount);
      } catch (err: any) {
        if (err.name !== 'AbortError') {
          setError(err.message || 'Failed to load putaway tasks');
        }
      } finally {
        setLoadingTasks(false);
      }
    },
    [activeFacility, taskFilter, searchTerm, page]
  );

  const loadNextTask = useCallback(
    async (signal?: AbortSignal) => {
      if (!activeFacility) return;
      try {
        setLoadingNext(true);
        const task = await putawayApi.getNextTask(activeFacility.id, user?.userId, signal);
        setNextTask(task);
        // Clear scan inputs when next task changes
        setScannedLpn('');
        setScannedLocationBarcode('');
        setAllowOverride(false);
      } catch (err: any) {
        if (err.name !== 'AbortError') {
          setFeedback({ type: 'error', message: err.message || 'Failed to load next putaway task' });
        }
      } finally {
        setLoadingNext(false);
      }
    },
    [activeFacility, user?.userId]
  );

  useEffect(() => {
    const ctrl = new AbortController();
    if (activeTab === 'queue') {
      loadTasks(ctrl.signal);
    } else {
      loadNextTask(ctrl.signal);
    }
    return () => ctrl.abort();
  }, [activeTab, loadTasks, loadNextTask]);

  const handleGenerateTasks = async () => {
    if (!activeFacility || isGenerating || !canManage) return;

    try {
      setIsGenerating(true);
      setFeedback(null);
      const req: GeneratePutawayTasksRequest = {
        facilityId: activeFacility.id,
      };
      const generated = await putawayApi.generateTasks(req);
      setFeedback({
        type: 'success',
        message: `Successfully generated ${generated.length} directed putaway task(s) from transient staging!`,
      });
      await loadTasks();
    } catch (err: any) {
      setFeedback({ type: 'error', message: err.message || 'Failed to generate directed putaway tasks' });
    } finally {
      setIsGenerating(false);
    }
  };

  const handleConfirmPutaway = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!nextTask || isConfirming || !canExecute) return;

    if (!scannedLpn.trim()) {
      setFeedback({ type: 'error', message: 'Scan or enter container/pallet LPN barcode.' });
      return;
    }
    if (!scannedLocationBarcode.trim()) {
      setFeedback({ type: 'error', message: 'Scan or enter target location bin barcode.' });
      return;
    }

    try {
      setIsConfirming(true);
      setFeedback(null);
      const req: ConfirmPutawayTaskRequest = {
        scannedLpn: scannedLpn.trim(),
        scannedLocationBarcode: scannedLocationBarcode.trim(),
        allowOverride,
      };

      const result = await putawayApi.confirmTask(nextTask.id, req);
      setFeedback({
        type: 'success',
        message: `Putaway confirmed! LPN ${result.lpnCode} relocated from ${result.sourceLocationCode} into ${result.actualLocationCode || result.suggestedLocationCode}.`,
      });
      // Fetch next pending task
      await loadNextTask();
    } catch (err: any) {
      setFeedback({ type: 'error', message: err.message || 'Putaway verification failed' });
    } finally {
      setIsConfirming(false);
    }
  };

  const handleReportException = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!nextTask || isReportingException || !canExecute) return;

    try {
      setIsReportingException(true);
      setFeedback(null);
      const req: ReportExceptionRequest = {
        exceptionReason,
        autoReRoute,
      };

      await putawayApi.reportException(nextTask.id, req);
      setShowExceptionModal(false);
      setFeedback({
        type: 'success',
        message: `Exception reported on task ${nextTask.id}. Task dynamically re-routed.`,
      });
      await loadNextTask();
    } catch (err: any) {
      setFeedback({ type: 'error', message: err.message || 'Failed to report exception' });
    } finally {
      setIsReportingException(false);
    }
  };

  const getPriorityBadge = (priority: PutawayPriority) => {
    switch (priority) {
      case PutawayPriority.Expedite:
        return <span className="priority-pill expedite">Expedite</span>;
      case PutawayPriority.High:
        return <span className="priority-pill high">High</span>;
      case PutawayPriority.Normal:
      default:
        return <span className="priority-pill normal">Normal</span>;
    }
  };

  const getStatusBadge = (status: PutawayTaskStatus) => {
    switch (status) {
      case PutawayTaskStatus.Pending:
        return <span className="status-badge badge-started">Pending</span>;
      case PutawayTaskStatus.Assigned:
        return <span className="status-badge badge-inprogress">Assigned</span>;
      case PutawayTaskStatus.InTransit:
        return <span className="status-badge badge-inprogress">In Transit</span>;
      case PutawayTaskStatus.Completed:
        return <span className="status-badge badge-completed">Completed</span>;
      case PutawayTaskStatus.ExceptionHold:
        return <span className="status-badge badge-flagged">Hold</span>;
      case PutawayTaskStatus.ReRouted:
        return <span className="status-badge badge-started">Re-Routed</span>;
      case PutawayTaskStatus.Cancelled:
        return <span className="status-badge badge-cancelled">Cancelled</span>;
      default:
        return <span className="status-badge">Unknown</span>;
    }
  };

  return (
    <div className="putaway-page-container" role="region" aria-label="Directed Putaway Operations">
      <div className="page-header">
        <div>
          <h1 className="page-title">Directed Putaway &amp; Storage Relocation</h1>
          <p className="page-subtitle">
            RF Dual-Scan Verification &bull; {activeFacility ? `${activeFacility.name} (${activeFacility.facilityCode})` : 'No Facility'}
          </p>
        </div>
        <div className="header-actions">
          {canManage && (
            <button
              className="btn btn-primary"
              disabled={isGenerating}
              onClick={handleGenerateTasks}
              data-testid="generate-tasks-btn"
            >
              {isGenerating ? 'Generating Tasks...' : '⚡ Generate Directed Tasks'}
            </button>
          )}
        </div>
      </div>

      {feedback && (
        <div className={`alert-banner ${feedback.type}`} role="alert" data-testid="putaway-feedback">
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

      {/* VIEW SELECTOR TABS */}
      <div className="tab-bar">
        <button
          className={`tab-btn ${activeTab === 'queue' ? 'active' : ''}`}
          onClick={() => setActiveTab('queue')}
          data-testid="tab-queue"
        >
          Putaway Task Queue ({totalTasks})
        </button>
        <button
          className={`tab-btn ${activeTab === 'rf' ? 'active' : ''}`}
          onClick={() => setActiveTab('rf')}
          data-testid="tab-rf"
        >
          📱 RF Mobile Execution {nextTask ? '• (Task Pending)' : ''}
        </button>
      </div>

      {/* TAB 1: TASK QUEUE TABLE */}
      {activeTab === 'queue' && (
        <div className="queue-view" data-testid="putaway-queue-view">
          <div className="filter-bar card">
            <div className="filter-group">
              <input
                type="text"
                className="form-input search-input"
                placeholder="Search SKU, LPN, Bin..."
                value={searchTerm}
                onChange={(e) => setSearchTerm(e.target.value)}
                data-testid="search-tasks-input"
              />
            </div>
            <div className="filter-group">
              <select
                className="form-select"
                value={taskFilter}
                onChange={(e) => setTaskFilter(e.target.value)}
                data-testid="status-filter-tasks"
              >
                <option value="all">All Statuses</option>
                <option value={PutawayTaskStatus.Pending}>Pending</option>
                <option value={PutawayTaskStatus.Assigned}>Assigned</option>
                <option value={PutawayTaskStatus.Completed}>Completed</option>
                <option value={PutawayTaskStatus.ExceptionHold}>Exception Hold</option>
                <option value={PutawayTaskStatus.ReRouted}>Re-Routed</option>
              </select>
            </div>
          </div>

          <div className="card table-card">
            {loadingTasks ? (
              <div className="table-loading" data-testid="tasks-loading">Loading putaway tasks...</div>
            ) : (
              <div className="table-responsive">
                <table className="enterprise-table" data-testid="putaway-tasks-table">
                  <thead>
                    <tr>
                      <th>Priority</th>
                      <th>LPN</th>
                      <th>SKU</th>
                      <th>Quantity</th>
                      <th>Source Lane</th>
                      <th>Suggested Bin</th>
                      <th>Status</th>
                      <th>Created At</th>
                    </tr>
                  </thead>
                  <tbody>
                    {tasks.length === 0 ? (
                      <tr>
                        <td colSpan={8} className="empty-row" data-testid="empty-tasks-row">
                          No putaway tasks found. Click "Generate Directed Tasks" to slot received staging items.
                        </td>
                      </tr>
                    ) : (
                      tasks.map((task) => (
                        <tr key={task.id} data-testid={`task-row-${task.id}`}>
                          <td>{getPriorityBadge(task.priority)}</td>
                          <td className="code-font fw-600">{task.lpnCode || '—'}</td>
                          <td className="fw-600">{task.sku}</td>
                          <td>{task.quantity}</td>
                          <td className="code-font">{task.sourceLocationCode}</td>
                          <td className="code-font highlight-bin">{task.suggestedLocationCode}</td>
                          <td>{getStatusBadge(task.status)}</td>
                          <td>{new Date(task.createdAtUtc).toLocaleTimeString()}</td>
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

      {/* TAB 2: RF DIRECTED WORK CARD */}
      {activeTab === 'rf' && (
        <div className="rf-view" data-testid="rf-execution-view">
          {loadingNext ? (
            <div className="card loading-card" data-testid="rf-loading">Fetching next directed task...</div>
          ) : !nextTask ? (
            <div className="card empty-rf-card" data-testid="rf-no-tasks">
              <h3>All Putaway Tasks Completed!</h3>
              <p>There are no pending directed putaway tasks for this facility at this time.</p>
              {canManage && (
                <button className="btn btn-primary" onClick={handleGenerateTasks} disabled={isGenerating}>
                  {isGenerating ? 'Generating...' : 'Scan Staging & Generate Tasks'}
                </button>
              )}
            </div>
          ) : (
            <div className="rf-task-container">
              <div className="rf-task-card card" data-testid="rf-task-card">
                <div className="rf-task-header">
                  <div className="rf-task-meta">
                    <span className="rf-task-label">NEXT DIRECTED TASK</span>
                    <h2 className="rf-task-sku">{nextTask.sku}</h2>
                    <span className="rf-task-desc">{nextTask.itemDescription || 'Warehouse Item'}</span>
                  </div>
                  <div className="rf-badges">
                    {getPriorityBadge(nextTask.priority)}
                    {getStatusBadge(nextTask.status)}
                  </div>
                </div>

                <div className="rf-routing-display">
                  <div className="routing-step">
                    <span className="step-label">PICK FROM SOURCE</span>
                    <div className="step-val code-font">{nextTask.sourceLocationCode}</div>
                    <span className="step-sub">Transient Staging Lane</span>
                  </div>
                  <div className="routing-arrow">&rarr;</div>
                  <div className="routing-step destination">
                    <span className="step-label">PUT TO TARGET BIN</span>
                    <div className="step-val code-font">{nextTask.suggestedLocationCode}</div>
                    <span className="step-sub code-font">Barcode: {nextTask.suggestedLocationBarcode}</span>
                  </div>
                </div>

                <div className="rf-metrics-row">
                  <div>
                    <span className="meta-label">Quantity to Relocate:</span>
                    <span className="meta-val fw-600">{nextTask.quantity} units</span>
                  </div>
                  <div>
                    <span className="meta-label">Pallet / Container LPN:</span>
                    <span className="meta-val code-font fw-600">{nextTask.lpnCode || 'None Assigned'}</span>
                  </div>
                  {nextTask.lotNumber && (
                    <div>
                      <span className="meta-label">Lot Number:</span>
                      <span className="meta-val code-font">{nextTask.lotNumber}</span>
                    </div>
                  )}
                </div>

                {/* DUAL-SCAN FORM */}
                <form onSubmit={handleConfirmPutaway} className="rf-scan-form" data-testid="dual-scan-form">
                  <h3 className="section-title">Dual-Scan Barcode Verification</h3>

                  <div className="scan-inputs-grid">
                    <div className="form-group">
                      <label htmlFor="scan-lpn-input">
                        1. Scan Container LPN Barcode *
                      </label>
                      <input
                        id="scan-lpn-input"
                        type="text"
                        className="form-input scan-input"
                        placeholder="Scan Pallet LPN"
                        value={scannedLpn}
                        onChange={(e) => setScannedLpn(e.target.value)}
                        required
                        data-testid="input-scanned-lpn"
                      />
                      <span className="input-hint">Expected: {nextTask.lpnCode || 'Any'}</span>
                    </div>

                    <div className="form-group">
                      <label htmlFor="scan-bin-input">
                        2. Scan Destination Bin Tag Barcode *
                      </label>
                      <input
                        id="scan-bin-input"
                        type="text"
                        className="form-input scan-input"
                        placeholder="Scan Shelf / Bin Tag"
                        value={scannedLocationBarcode}
                        onChange={(e) => setScannedLocationBarcode(e.target.value)}
                        required
                        data-testid="input-scanned-bin"
                      />
                      <span className="input-hint">Expected: {nextTask.suggestedLocationBarcode}</span>
                    </div>
                  </div>

                  <div className="checkbox-row">
                    <label className="checkbox-label">
                      <input
                        type="checkbox"
                        checked={allowOverride}
                        onChange={(e) => setAllowOverride(e.target.checked)}
                        data-testid="override-bin-checkbox"
                      />
                      <span>Authorize Target Bin Override (Requires Supervisor/Floor Lead Clearance)</span>
                    </label>
                  </div>

                  <div className="rf-form-actions">
                    <button
                      type="submit"
                      className="btn btn-success btn-lg"
                      disabled={isConfirming || !scannedLpn.trim() || !scannedLocationBarcode.trim()}
                      data-testid="confirm-putaway-btn"
                    >
                      {isConfirming ? 'Relocating Stock...' : '✓ Confirm Dual-Scan & Putaway'}
                    </button>
                    <button
                      type="button"
                      className="btn btn-danger"
                      onClick={() => setShowExceptionModal(true)}
                      data-testid="report-putaway-exception-btn"
                    >
                      ! Report Obstruction / Exception
                    </button>
                  </div>
                </form>
              </div>
            </div>
          )}
        </div>
      )}

      {/* REPORT EXCEPTION MODAL */}
      {showExceptionModal && nextTask && (
        <div className="modal-overlay" role="dialog" aria-modal="true" data-testid="putaway-exception-modal">
          <div className="modal-card">
            <h2 className="modal-title">Report Putaway Obstruction</h2>
            <form onSubmit={handleReportException}>
              <div className="form-group">
                <label htmlFor="exception-reason-select">Exception Reason *</label>
                <select
                  id="exception-reason-select"
                  className="form-select"
                  value={exceptionReason}
                  onChange={(e) => setExceptionReason(e.target.value)}
                  data-testid="exception-reason-select"
                >
                  <option value="Obstructed Aisle">Obstructed Aisle / Forklift Congestion</option>
                  <option value="Bin Damaged">Bin Physically Damaged / Structural Defect</option>
                  <option value="Bin Full">Bin Already Full / Capacity Exceeded</option>
                  <option value="Wrong Dimension">Product Dimensions Exceed Shelf Opening</option>
                  <option value="Quarantine Required">Packaging Compromised on Transit</option>
                </select>
              </div>

              <div className="checkbox-row">
                <label className="checkbox-label">
                  <input
                    type="checkbox"
                    checked={autoReRoute}
                    onChange={(e) => setAutoReRoute(e.target.checked)}
                  />
                  <span>Trigger Dynamic Slotting Re-route</span>
                </label>
              </div>

              <div className="modal-actions">
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={() => setShowExceptionModal(false)}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="btn btn-danger"
                  disabled={isReportingException}
                  data-testid="submit-putaway-exception-btn"
                >
                  {isReportingException ? 'Submitting...' : 'Submit Exception'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      <style>{`
        .putaway-page-container {
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
        .tab-bar {
          display: flex;
          gap: 12px;
          border-bottom: 2px solid var(--border-subtle);
          padding-bottom: 8px;
        }
        .tab-btn {
          background: none;
          border: none;
          padding: 8px 16px;
          font-size: 15px;
          font-weight: 600;
          color: var(--text-muted);
          cursor: pointer;
          border-radius: var(--radius-sm);
        }
        .tab-btn.active {
          background: #e8f0fe;
          color: #1a73e8;
        }
        .priority-pill {
          display: inline-block;
          padding: 2px 8px;
          border-radius: 12px;
          font-size: 11px;
          font-weight: 700;
          text-transform: uppercase;
        }
        .priority-pill.expedite { background: #fce8e6; color: #c5221f; }
        .priority-pill.high { background: #fef7e0; color: #b06000; }
        .priority-pill.normal { background: #e8f0fe; color: #1a73e8; }
        .highlight-bin {
          color: #137333;
          font-weight: 700;
        }
        .rf-task-container {
          max-width: 720px;
          margin: 0 auto;
        }
        .rf-task-header {
          display: flex;
          justify-content: space-between;
          align-items: flex-start;
          border-bottom: 1px solid var(--border-subtle);
          padding-bottom: 16px;
          margin-bottom: 16px;
        }
        .rf-task-label {
          font-size: 11px;
          font-weight: 700;
          color: var(--text-muted);
          text-transform: uppercase;
          letter-spacing: 0.5px;
        }
        .rf-task-sku {
          font-size: 22px;
          font-weight: 700;
          color: var(--text-main);
          margin: 4px 0;
        }
        .rf-task-desc {
          font-size: 14px;
          color: var(--text-muted);
        }
        .rf-routing-display {
          display: flex;
          align-items: center;
          justify-content: space-between;
          background: #f8fafc;
          padding: 16px;
          border-radius: var(--radius-sm);
          margin-bottom: 16px;
        }
        .routing-step {
          display: flex;
          flex-direction: column;
          gap: 2px;
        }
        .step-label {
          font-size: 11px;
          font-weight: 700;
          color: var(--text-muted);
        }
        .step-val {
          font-size: 18px;
          font-weight: 700;
        }
        .routing-step.destination .step-val {
          color: #137333;
        }
        .step-sub {
          font-size: 12px;
          color: var(--text-muted);
        }
        .routing-arrow {
          font-size: 24px;
          color: var(--text-muted);
        }
        .rf-metrics-row {
          display: grid;
          grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
          gap: 16px;
          margin-bottom: 24px;
          padding: 12px;
          background: #fafafa;
          border-radius: 4px;
        }
        .meta-label {
          display: block;
          font-size: 12px;
          color: var(--text-muted);
        }
        .meta-val {
          font-size: 14px;
        }
        .scan-inputs-grid {
          display: grid;
          grid-template-columns: 1fr 1fr;
          gap: 16px;
          margin-bottom: 16px;
        }
        .scan-input {
          font-family: monospace;
          font-size: 16px;
          font-weight: 600;
          letter-spacing: 0.5px;
        }
        .input-hint {
          font-size: 12px;
          color: var(--text-muted);
          margin-top: 4px;
        }
        .rf-form-actions {
          display: flex;
          gap: 16px;
          margin-top: 16px;
        }
        .btn-lg {
          padding: 12px 24px;
          font-size: 16px;
          font-weight: 600;
        }
        .empty-rf-card {
          text-align: center;
          padding: 48px;
        }
      `}</style>
    </div>
  );
};
