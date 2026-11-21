import React, { useState, useEffect, useCallback, useRef } from 'react';
import { useFacility } from '../../context/FacilityContext';
import { useAuth } from '../../context/AuthContext';
import { putawayApi } from '../../services/putawayApi';
import { AppIcon } from '../../components/common/AppIcon';
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
  const { hasPermission, user, isLoading: isAuthLoading } = useAuth();
  const canManage = hasPermission('inbound:putaway:manage');
  const canExecute = hasPermission('inbound:putaway:execute');
  const canViewQueue = hasPermission('inbound:view');

  // Execution-only operators default directly to RF mode once auth is resolved
  const [activeTab, setActiveTab] = useState<'queue' | 'rf'>('queue');
  const tabInitializedRef = useRef<boolean>(false);

  useEffect(() => {
    if (!isAuthLoading && !tabInitializedRef.current) {
      if (!canViewQueue && canExecute) {
        tabInitializedRef.current = true;
        setActiveTab('rf');
      }
    }
  }, [isAuthLoading, canViewQueue, canExecute]);

  // Task Queue state
  const [tasks, setTasks] = useState<PutawayTaskDto[]>([]);
  const [totalTasks, setTotalTasks] = useState(0);
  const [loadingTasks, setLoadingTasks] = useState(true);
  const [taskFilter, setTaskFilter] = useState<string>('all');
  const [searchTerm, setSearchTerm] = useState('');
  const [page] = useState(1);
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

  // Single-flight ref guards
  const inFlightConfirmRef = useRef(false);
  const inFlightGenerateRef = useRef(false);

  // Focus ref for RF barcode scanner input
  const scanLpnInputRef = useRef<HTMLInputElement>(null);
  const exceptionModalRef = useRef<HTMLDivElement>(null);

  const [feedback, setFeedback] = useState<{ type: 'success' | 'error'; message: string } | null>(null);
  const [error, setError] = useState<string | null>(null);

  const loadTasks = useCallback(
    async (signal?: AbortSignal) => {
      if (!activeFacility || !canViewQueue) return;
      try {
        setLoadingTasks(true);
        setError(null);
        const query: GetPutawayTasksQuery = {
          facilityId: activeFacility.id,
          status: taskFilter !== 'all' ? (taskFilter as PutawayTaskStatus) : undefined,
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
    [activeFacility, canViewQueue, taskFilter, searchTerm, page]
  );

  const loadNextTask = useCallback(
    async (signal?: AbortSignal) => {
      if (!activeFacility || !canExecute) return;
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
    [activeFacility, canExecute, user?.userId]
  );

  useEffect(() => {
    if (isAuthLoading) return;
    const ctrl = new AbortController();
    if (activeTab === 'queue' && canViewQueue) {
      loadTasks(ctrl.signal);
    } else if (activeTab === 'rf' && canExecute) {
      loadNextTask(ctrl.signal);
    }
    return () => ctrl.abort();
  }, [isAuthLoading, activeTab, canViewQueue, canExecute, loadTasks, loadNextTask]);

  // Predictable focus restoration to LPN input when task is loaded
  useEffect(() => {
    if (activeTab === 'rf' && nextTask && canExecute) {
      const timer = setTimeout(() => {
        scanLpnInputRef.current?.focus();
      }, 100);
      return () => clearTimeout(timer);
    }
  }, [activeTab, nextTask?.id, canExecute]);

  // Modal keyboard trapping and Escape key dismissal
  useEffect(() => {
    if (!showExceptionModal || !exceptionModalRef.current) return;

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setShowExceptionModal(false);
        return;
      }

      if (e.key === 'Tab' && exceptionModalRef.current) {
        const focusable = exceptionModalRef.current.querySelectorAll<HTMLElement>(
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
  }, [showExceptionModal]);

  const handleGenerateTasks = async () => {
    if (!activeFacility || isGenerating || inFlightGenerateRef.current || !canManage) return;

    try {
      inFlightGenerateRef.current = true;
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
      if (canViewQueue) {
        await loadTasks();
      }
      if (canExecute && !nextTask) {
        await loadNextTask();
      }
    } catch (err: any) {
      setFeedback({ type: 'error', message: err.message || 'Failed to generate directed putaway tasks' });
    } finally {
      inFlightGenerateRef.current = false;
      setIsGenerating(false);
    }
  };

  const handleConfirmPutaway = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!nextTask || isConfirming || inFlightConfirmRef.current || !canExecute) return;

    if (!scannedLpn.trim()) {
      setFeedback({ type: 'error', message: 'Scan or enter container/pallet LPN barcode.' });
      scanLpnInputRef.current?.focus();
      return;
    }
    if (!scannedLocationBarcode.trim()) {
      setFeedback({ type: 'error', message: 'Scan or enter target location bin barcode.' });
      return;
    }

    try {
      inFlightConfirmRef.current = true;
      setIsConfirming(true);
      setFeedback(null);
      const req: ConfirmPutawayTaskRequest = {
        scannedLpn: scannedLpn.trim(),
        scannedLocationBarcode: scannedLocationBarcode.trim(),
        allowOverride,
      };

      const result = await putawayApi.confirmTask(nextTask.id, req);

      // Device vibration feedback when supported and available
      if (typeof window !== 'undefined' && 'vibrate' in navigator) {
        try {
          navigator.vibrate([100, 50, 100]);
        } catch (_) {
          // Device policy or unsupported hardware - ignore safely
        }
      }

      setFeedback({
        type: 'success',
        message: `Putaway confirmed! LPN ${result.lpnCode} relocated from ${result.sourceLocationCode} into ${result.actualLocationCode || result.suggestedLocationCode}.`,
      });

      // Fetch next pending task
      await loadNextTask();
    } catch (err: any) {
      setFeedback({ type: 'error', message: err.message || 'Putaway verification failed' });
    } finally {
      inFlightConfirmRef.current = false;
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
        <div className="page-header-text">
          <div className="header-icon-title">
            <AppIcon name="ArrowDownRight" size="lg" color="primary" />
            <h1 className="page-title">Directed Putaway &amp; Storage Relocation</h1>
          </div>
          <p className="page-subtitle">
            RF Dual-Scan Verification &bull; {activeFacility ? `${activeFacility.name} (${activeFacility.facilityCode})` : 'No Facility'}
          </p>
        </div>

        <div className="header-actions">
          {canManage && (
            <button
              type="button"
              className="btn btn-primary btn-action-lg"
              disabled={isGenerating}
              onClick={handleGenerateTasks}
              data-testid="generate-tasks-btn"
            >
              <AppIcon name="Sparkles" size="sm" />
              <span>{isGenerating ? 'Generating Tasks...' : '⚡ Generate Directed Tasks'}</span>
            </button>
          )}
        </div>
      </div>

      {feedback && (
        <div className={`alert-banner ${feedback.type}`} role="alert" data-testid="putaway-feedback">
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

      {/* VIEW SELECTOR TABS */}
      <div className="tab-bar">
        {canViewQueue && (
          <button
            type="button"
            className={`tab-btn ${activeTab === 'queue' ? 'active' : ''}`}
            onClick={() => setActiveTab('queue')}
            data-testid="tab-queue"
          >
            <AppIcon name="Boxes" size="sm" />
            <span>Putaway Task Queue ({totalTasks})</span>
          </button>
        )}
        {canExecute && (
          <button
            type="button"
            className={`tab-btn ${activeTab === 'rf' ? 'active' : ''}`}
            onClick={() => setActiveTab('rf')}
            data-testid="tab-rf"
          >
            <AppIcon name="Barcode" size="sm" />
            <span>RF Mobile Execution {nextTask ? '• (Task Pending)' : ''}</span>
          </button>
        )}
      </div>

      {/* MANAGE-ONLY WORKSTATION (Fix for Requirement D: when user has canManage only) */}
      {canManage && !canViewQueue && !canExecute && (
        <div className="card putaway-management-workstation" data-testid="putaway-manage-workstation">
          <div className="manage-workstation-header">
            <div className="manage-icon-badge">
              <AppIcon name="Sliders" size="lg" color="primary" />
            </div>
            <div>
              <h2 className="manage-workstation-title">Putaway Task Management Workstation</h2>
              <p className="manage-workstation-desc">
                Supervise and generate directed putaway tasks for transient dock staging inventory across{' '}
                <strong>{activeFacility?.name || 'this facility'}</strong>.
              </p>
            </div>
          </div>

          <div className="manage-workstation-body">
            <div className="manage-action-card">
              <div className="manage-action-info">
                <h3 className="manage-box-title">Directed Task Slotting Generation</h3>
                <p className="manage-box-desc">
                  Scans all active staging lanes and calculates optimal destination storage locations based on turnover velocity, item dimensions, and warehouse storage zones.
                </p>
              </div>
              <button
                type="button"
                className="btn btn-primary btn-action-lg"
                disabled={isGenerating}
                onClick={handleGenerateTasks}
                data-testid="generate-tasks-btn-manage-workstation"
              >
                <AppIcon name="Sparkles" size="md" />
                <span>{isGenerating ? 'Generating Tasks...' : '⚡ Generate Directed Tasks'}</span>
              </button>
            </div>

            <div className="manage-perm-note">
              <AppIcon name="Info" size="sm" color="primary" />
              <span>
                Task queue inspection requires <code>inbound:view</code> permission. Handheld RF mobile execution requires <code>inbound:putaway:execute</code> permission.
              </span>
            </div>
          </div>
        </div>
      )}

      {/* ACCESS RESTRICTION BANNER ONLY IF USER HAS NEITHER CAN_EXECUTE, CAN_VIEW_QUEUE, NOR CAN_MANAGE */}
      {!canExecute && !canViewQueue && !canManage && (
        <div className="card authorization-required-card">
          <AppIcon name="Lock" size="lg" color="muted" />
          <h3 className="auth-card-title">Directed Putaway Authorization Required</h3>
          <p className="auth-card-desc">
            Your account requires <code>inbound:putaway:execute</code>, <code>inbound:putaway:manage</code>, or <code>inbound:view</code> permissions to operate this module.
          </p>
        </div>
      )}

      {/* TAB 1: TASK QUEUE TABLE */}
      {activeTab === 'queue' && canViewQueue && (
        <div className="queue-view" data-testid="putaway-queue-view">
          <div className="filter-bar card">
            <div className="filter-group flex-2">
              <input
                type="text"
                className="form-input search-input"
                placeholder="Search SKU, LPN, Bin..."
                value={searchTerm}
                onChange={(e) => setSearchTerm(e.target.value)}
                data-testid="search-tasks-input"
              />
            </div>
            <div className="filter-group flex-1">
              <select
                className="form-select status-select"
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
              <div className="table-loading" data-testid="tasks-loading">
                <AppIcon name="RefreshCw" size="md" className="spin-icon" />
                <span>Loading putaway tasks...</span>
              </div>
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
                          No putaway tasks found. Click &quot;Generate Directed Tasks&quot; to slot received staging items.
                        </td>
                      </tr>
                    ) : (
                      tasks.map((task) => (
                        <tr key={task.id} data-testid={`task-row-${task.id}`}>
                          <td>{getPriorityBadge(task.priority)}</td>
                          <td className="code-font fw-600">{task.lpnCode || '—'}</td>
                          <td className="fw-600">{task.sku}</td>
                          <td className="fw-700">{task.quantity}</td>
                          <td className="code-font">{task.sourceLocationCode}</td>
                          <td className="code-font highlight-bin">{task.suggestedLocationCode}</td>
                          <td>{getStatusBadge(task.status)}</td>
                          <td className="text-muted">{new Date(task.createdAtUtc).toLocaleTimeString()}</td>
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
      {activeTab === 'rf' && canExecute && (
        <div className="rf-view" data-testid="rf-execution-view">
          {loadingNext ? (
            <div className="card loading-card" data-testid="rf-loading">
              <AppIcon name="RefreshCw" size="lg" className="spin-icon" color="primary" />
              <span>Fetching next directed task...</span>
            </div>
          ) : !nextTask ? (
            <div className="card empty-rf-card" data-testid="rf-no-tasks">
              <div className="empty-icon-wrapper">
                <AppIcon name="CheckCircle2" size="lg" color="success" />
              </div>
              <h2 className="empty-title">All Putaway Tasks Completed!</h2>
              <p className="empty-desc">There are no pending directed putaway tasks for this facility at this time.</p>
              {canManage && (
                <button
                  type="button"
                  className="btn btn-primary btn-action-lg"
                  onClick={handleGenerateTasks}
                  disabled={isGenerating}
                >
                  <AppIcon name="Sparkles" size="md" />
                  <span>{isGenerating ? 'Generating...' : 'Scan Staging & Generate Tasks'}</span>
                </button>
              )}
            </div>
          ) : (
            <div className="rf-task-container">
              <div className="rf-task-card card" data-testid="rf-task-card">
                {/* HIGH LEGIBILITY TASK HEADER */}
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

                {/* VISUAL ROUTING DISPLAY: STRONG SEPARATION BETWEEN SOURCE AND DESTINATION */}
                <div className="rf-routing-display">
                  <div className="routing-step source-step">
                    <span className="step-label">
                      <AppIcon name="ArrowDownToLine" size="sm" /> PICK FROM SOURCE
                    </span>
                    <div className="step-val code-font">{nextTask.sourceLocationCode}</div>
                    <span className="step-sub">Transient Staging Lane</span>
                  </div>
                  <div className="routing-arrow" aria-hidden="true">&rarr;</div>
                  <div className="routing-step destination-step">
                    <span className="step-label">
                      <AppIcon name="MapPin" size="sm" /> PUT TO TARGET BIN
                    </span>
                    <div className="step-val code-font">{nextTask.suggestedLocationCode}</div>
                    <span className="step-sub barcode-hint code-font">
                      Barcode: <strong>{nextTask.suggestedLocationBarcode}</strong>
                    </span>
                  </div>
                </div>

                <div className="rf-metrics-row">
                  <div className="metric-cell">
                    <span className="meta-label">Quantity to Relocate:</span>
                    <span className="meta-val fw-700">{nextTask.quantity} units</span>
                  </div>
                  <div className="metric-cell">
                    <span className="meta-label">Pallet / Container LPN:</span>
                    <span className="meta-val code-font fw-700">{nextTask.lpnCode || 'None Assigned'}</span>
                  </div>
                  {nextTask.lotNumber && (
                    <div className="metric-cell">
                      <span className="meta-label">Lot Number:</span>
                      <span className="meta-val code-font">{nextTask.lotNumber}</span>
                    </div>
                  )}
                </div>

                {/* NUMBERED TWO-STEP SCAN GUIDANCE FORM */}
                <form onSubmit={handleConfirmPutaway} className="rf-scan-form" data-testid="dual-scan-form">
                  <div className="scan-section-header">
                    <h3 className="section-title">Dual-Scan Barcode Verification</h3>
                    <p className="section-subtitle">
                      Scan container LPN tag followed by target shelf bin tag. Dual matching guarantees stock accuracy.
                    </p>
                  </div>

                  <div className="scan-inputs-grid">
                    {/* STEP 1: LPN SCANNER INPUT */}
                    <div className="scan-step-box">
                      <div className="step-badge-row">
                        <span className="scan-step-number">Step 1</span>
                        <label htmlFor="scan-lpn-input" className="scan-step-title">
                          Scan Container LPN Barcode *
                        </label>
                      </div>
                      <input
                        ref={scanLpnInputRef}
                        id="scan-lpn-input"
                        type="text"
                        className="form-input scan-input"
                        placeholder="Scan Pallet LPN"
                        value={scannedLpn}
                        onChange={(e) => setScannedLpn(e.target.value)}
                        required
                        autoComplete="off"
                        data-testid="input-scanned-lpn"
                      />
                      <span className="input-hint">
                        Expected: {nextTask.lpnCode || 'Any'}
                      </span>
                    </div>

                    {/* STEP 2: BIN SCANNER INPUT */}
                    <div className="scan-step-box target-step-box">
                      <div className="step-badge-row">
                        <span className="scan-step-number target-step-badge">Step 2</span>
                        <label htmlFor="scan-bin-input" className="scan-step-title">
                          Scan Destination Bin Tag Barcode *
                        </label>
                      </div>
                      <input
                        id="scan-bin-input"
                        type="text"
                        className="form-input scan-input target-bin-input"
                        placeholder="Scan Shelf / Bin Tag"
                        value={scannedLocationBarcode}
                        onChange={(e) => setScannedLocationBarcode(e.target.value)}
                        required
                        autoComplete="off"
                        data-testid="input-scanned-bin"
                      />
                      <span className="input-hint">
                        Expected: {nextTask.suggestedLocationBarcode}
                      </span>
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
                      className="btn btn-success btn-action-lg flex-2"
                      disabled={isConfirming || !scannedLpn.trim() || !scannedLocationBarcode.trim()}
                      data-testid="confirm-putaway-btn"
                    >
                      <AppIcon name="CheckCircle2" size="md" />
                      <span>{isConfirming ? 'Relocating Stock...' : '✓ Confirm Dual-Scan & Putaway'}</span>
                    </button>
                    <button
                      type="button"
                      className="btn btn-danger btn-action-lg flex-1"
                      onClick={() => setShowExceptionModal(true)}
                      data-testid="report-putaway-exception-btn"
                    >
                      <AppIcon name="AlertTriangle" size="md" />
                      <span>Report Exception</span>
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
        <div
          ref={exceptionModalRef}
          className="modal-overlay"
          role="dialog"
          aria-modal="true"
          aria-labelledby="exception-modal-title"
          data-testid="putaway-exception-modal"
        >
          <div className="modal-card">
            <div className="modal-header">
              <div className="modal-title-group">
                <AppIcon name="AlertTriangle" size="md" color="warning" />
                <h2 id="exception-modal-title" className="modal-title">Report Putaway Obstruction</h2>
              </div>
              <button
                type="button"
                className="modal-close-btn"
                onClick={() => setShowExceptionModal(false)}
                aria-label="Close modal dialog"
              >
                <AppIcon name="X" size="sm" />
              </button>
            </div>
            <form onSubmit={handleReportException}>
              <div className="modal-body">
                <div className="form-group">
                  <label htmlFor="exception-reason-select" className="form-label">
                    Exception Reason *
                  </label>
                  <select
                    id="exception-reason-select"
                    className="form-select scan-select-input"
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
              </div>

              <div className="modal-actions">
                <button
                  type="button"
                  className="btn btn-secondary btn-action-md"
                  onClick={() => setShowExceptionModal(false)}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="btn btn-danger btn-action-md"
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
        .card {
          background: #ffffff;
          border: 1px solid var(--border-subtle);
          border-radius: var(--radius-md);
          padding: 20px;
          box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
        }
        .tab-bar {
          display: flex;
          gap: 10px;
          border-bottom: 2px solid var(--border-subtle);
          padding-bottom: 8px;
          flex-wrap: wrap;
        }
        .tab-btn {
          display: inline-flex;
          align-items: center;
          gap: 8px;
          background: none;
          border: none;
          padding: 8px 16px;
          font-size: 14px;
          font-weight: 600;
          color: var(--text-muted);
          cursor: pointer;
          border-radius: var(--radius-sm);
          min-height: 40px;
          transition: all 0.15s ease;
        }
        .tab-btn:hover {
          background: #f1f5f9;
          color: var(--text-main);
        }
        .tab-btn.active {
          background: #e8f0fe;
          color: #1a73e8;
          font-weight: 700;
        }
        /* MANAGE-ONLY WORKSTATION */
        .putaway-management-workstation {
          border: 2px solid #3b82f6;
          background: #f8fafc;
          padding: 24px;
        }
        .manage-workstation-header {
          display: flex;
          align-items: flex-start;
          gap: 16px;
          margin-bottom: 20px;
        }
        .manage-icon-badge {
          display: flex;
          align-items: center;
          justify-content: center;
          width: 48px;
          height: 48px;
          background: #eff6ff;
          border-radius: 10px;
          border: 1px solid #bfdbfe;
        }
        .manage-workstation-title {
          font-size: 18px;
          font-weight: 700;
          color: var(--text-main);
          margin: 0 0 4px 0;
        }
        .manage-workstation-desc {
          font-size: 14px;
          color: var(--text-muted);
          margin: 0;
          line-height: 1.5;
        }
        .manage-workstation-body {
          display: flex;
          flex-direction: column;
          gap: 16px;
        }
        .manage-action-card {
          display: flex;
          justify-content: space-between;
          align-items: center;
          background: #ffffff;
          padding: 20px;
          border-radius: var(--radius-md);
          border: 1px solid var(--border-subtle);
          gap: 16px;
          flex-wrap: wrap;
        }
        .manage-action-info {
          flex: 1;
          min-width: 240px;
        }
        .manage-box-title {
          font-size: 15px;
          font-weight: 700;
          color: var(--text-main);
          margin: 0 0 6px 0;
        }
        .manage-box-desc {
          font-size: 13px;
          color: var(--text-muted);
          margin: 0;
          line-height: 1.4;
        }
        .manage-perm-note {
          display: flex;
          align-items: center;
          gap: 8px;
          font-size: 13px;
          color: var(--text-muted);
          padding: 10px 14px;
          background: #f1f5f9;
          border-radius: var(--radius-sm);
        }
        /* AUTHORIZATION REQUIRED CARD */
        .authorization-required-card {
          text-align: center;
          padding: 40px 20px;
          margin: 20px 0;
          display: flex;
          flex-direction: column;
          align-items: center;
          gap: 12px;
        }
        .auth-card-title {
          font-size: 18px;
          font-weight: 700;
          color: var(--text-main);
          margin: 0;
        }
        .auth-card-desc {
          font-size: 14px;
          color: var(--text-muted);
          max-width: 480px;
          line-height: 1.5;
          margin: 0;
        }
        /* QUEUE VIEW & FILTER */
        .filter-bar {
          display: flex;
          gap: 12px;
          padding: 14px;
          margin-bottom: 16px;
          flex-wrap: wrap;
        }
        .filter-group {
          display: flex;
          flex-direction: column;
        }
        .search-input, .status-select {
          min-height: 44px;
        }
        .flex-1 { flex: 1; min-width: 140px; }
        .flex-2 { flex: 2; min-width: 220px; }
        .table-loading {
          display: flex;
          align-items: center;
          justify-content: center;
          gap: 10px;
          padding: 32px;
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
        .code-font { font-family: var(--font-mono, monospace); }
        .fw-600 { font-weight: 600; }
        .fw-700 { font-weight: 700; }
        .text-muted { color: var(--text-muted); }
        .highlight-bin {
          color: #047857;
          font-weight: 700;
        }
        .empty-row {
          text-align: center;
          padding: 32px;
          color: var(--text-muted);
        }
        /* RF DIRECTED WORKSTATION CARD */
        .rf-task-container {
          max-width: 760px;
          margin: 0 auto;
          width: 100%;
          box-sizing: border-box;
        }
        .rf-task-card {
          border: 2px solid #2563eb;
          box-shadow: 0 4px 12px rgba(37, 99, 235, 0.08);
          padding: 24px;
        }
        .rf-task-header {
          display: flex;
          justify-content: space-between;
          align-items: flex-start;
          border-bottom: 1px solid var(--border-subtle);
          padding-bottom: 16px;
          margin-bottom: 16px;
          gap: 12px;
          flex-wrap: wrap;
        }
        .rf-task-label {
          font-size: 11px;
          font-weight: 700;
          color: #2563eb;
          text-transform: uppercase;
          letter-spacing: 0.5px;
        }
        .rf-task-sku {
          font-size: 24px;
          font-weight: 700;
          color: var(--text-main);
          margin: 4px 0 2px 0;
        }
        .rf-task-desc {
          font-size: 14px;
          color: var(--text-muted);
        }
        .rf-badges {
          display: flex;
          gap: 8px;
          align-items: center;
        }
        /* HIGH CONTRAST ROUTING DISPLAY */
        .rf-routing-display {
          display: flex;
          align-items: center;
          justify-content: space-between;
          background: #f8fafc;
          border: 1px solid var(--border-subtle);
          padding: 16px;
          border-radius: var(--radius-md);
          margin-bottom: 18px;
          gap: 12px;
          flex-wrap: wrap;
        }
        .routing-step {
          display: flex;
          flex-direction: column;
          gap: 4px;
          flex: 1;
          min-width: 160px;
        }
        .source-step {
          padding: 8px 12px;
          background: #f1f5f9;
          border-radius: var(--radius-sm);
        }
        .destination-step {
          padding: 8px 12px;
          background: #ecfdf5;
          border: 2px solid #10b981;
          border-radius: var(--radius-sm);
        }
        .step-label {
          display: flex;
          align-items: center;
          gap: 5px;
          font-size: 11px;
          font-weight: 700;
          color: var(--text-muted);
          text-transform: uppercase;
          letter-spacing: 0.5px;
        }
        .destination-step .step-label {
          color: #047857;
        }
        .step-val {
          font-size: 20px;
          font-weight: 700;
          color: var(--text-main);
        }
        .destination-step .step-val {
          color: #065f46;
        }
        .step-sub {
          font-size: 12px;
          color: var(--text-muted);
        }
        .barcode-hint {
          color: #047857;
          font-size: 12px;
        }
        .routing-arrow {
          font-size: 28px;
          font-weight: 700;
          color: #64748b;
          padding: 0 4px;
        }
        .rf-metrics-row {
          display: grid;
          grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
          gap: 12px;
          margin-bottom: 20px;
          padding: 14px;
          background: #f8fafc;
          border-radius: var(--radius-sm);
          border: 1px solid var(--border-subtle);
        }
        .metric-cell {
          display: flex;
          flex-direction: column;
          gap: 2px;
        }
        .meta-label {
          font-size: 11px;
          text-transform: uppercase;
          color: var(--text-muted);
          letter-spacing: 0.5px;
        }
        .meta-val {
          font-size: 15px;
          color: var(--text-main);
        }
        /* DUAL-SCAN FORM */
        .rf-scan-form {
          display: flex;
          flex-direction: column;
          gap: 16px;
        }
        .scan-section-header {
          display: flex;
          flex-direction: column;
          gap: 2px;
        }
        .section-title {
          font-size: 16px;
          font-weight: 700;
          color: var(--text-main);
          margin: 0;
        }
        .section-subtitle {
          font-size: 13px;
          color: var(--text-muted);
          margin: 0;
        }
        .scan-inputs-grid {
          display: grid;
          grid-template-columns: 1fr 1fr;
          gap: 16px;
        }
        .scan-step-box {
          background: #ffffff;
          border: 1px solid var(--border-subtle);
          border-radius: var(--radius-sm);
          padding: 14px;
          display: flex;
          flex-direction: column;
          gap: 8px;
        }
        .target-step-box {
          border: 2px solid #10b981;
          background: #f0fdf4;
        }
        .step-badge-row {
          display: flex;
          align-items: center;
          gap: 8px;
        }
        .scan-step-number {
          display: inline-flex;
          align-items: center;
          justify-content: center;
          padding: 2px 8px;
          border-radius: 12px;
          background: #2563eb;
          color: #ffffff;
          font-size: 11px;
          font-weight: 700;
          text-transform: uppercase;
        }
        .target-step-badge {
          background: #059669;
        }
        .scan-step-title {
          font-size: 13px;
          font-weight: 600;
          color: var(--text-main);
        }
        /* BARCODE INPUTS: >= 52px height, 18px monospace font */
        .scan-input {
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
        .scan-input:focus {
          border-color: #2563eb;
          box-shadow: 0 0 0 3px rgba(37, 99, 235, 0.2);
          outline: none;
        }
        .target-bin-input:focus {
          border-color: #059669;
          box-shadow: 0 0 0 3px rgba(5, 150, 105, 0.2);
        }
        .input-hint {
          font-size: 12px;
          color: var(--text-muted);
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
        .rf-form-actions {
          display: flex;
          gap: 12px;
          margin-top: 8px;
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
        .empty-rf-card {
          text-align: center;
          padding: 48px 24px;
          display: flex;
          flex-direction: column;
          align-items: center;
          gap: 12px;
        }
        .empty-icon-wrapper {
          display: flex;
          align-items: center;
          justify-content: center;
          width: 60px;
          height: 60px;
          border-radius: 50%;
          background: #e6f4ea;
        }
        .empty-title {
          font-size: 20px;
          font-weight: 700;
          color: var(--text-main);
          margin: 0;
        }
        .empty-desc {
          font-size: 14px;
          color: var(--text-muted);
          margin: 0 0 12px 0;
        }
        .loading-card {
          text-align: center;
          padding: 48px;
          display: flex;
          flex-direction: column;
          align-items: center;
          gap: 16px;
          font-size: 15px;
          color: var(--text-muted);
        }
        .spin-icon {
          animation: spin 1s linear infinite;
        }
        @keyframes spin {
          100% { transform: rotate(360deg); }
        }
        /* PRIORITY PILLS */
        .priority-pill {
          display: inline-block;
          padding: 3px 8px;
          border-radius: 12px;
          font-size: 11px;
          font-weight: 700;
          text-transform: uppercase;
        }
        .priority-pill.expedite { background: #fce8e6; color: #c5221f; }
        .priority-pill.high { background: #fef7e0; color: #b06000; }
        .priority-pill.normal { background: #e8f0fe; color: #1a73e8; }
        /* STATUS BADGES */
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
          max-width: 500px;
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
        .scan-select-input {
          min-height: 44px;
          padding: 8px 12px;
          border: 1px solid var(--border-subtle);
          border-radius: var(--radius-sm);
          font-size: 14px;
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
          .scan-inputs-grid {
            grid-template-columns: 1fr;
          }
          .rf-routing-display {
            flex-direction: column;
            align-items: stretch;
          }
          .routing-arrow {
            text-align: center;
            transform: rotate(90deg);
          }
          .rf-form-actions {
            flex-direction: column;
          }
          .manage-action-card {
            flex-direction: column;
            align-items: stretch;
          }
        }
      `}</style>
    </div>
  );
};
