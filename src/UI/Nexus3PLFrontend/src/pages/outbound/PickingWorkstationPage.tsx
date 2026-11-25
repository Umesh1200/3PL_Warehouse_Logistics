import React, { useCallback, useEffect, useRef, useState } from 'react';
import { AppIcon } from '../../components/common/AppIcon';
import { AssignPickTaskModal } from '../../components/outbound/AssignPickTaskModal';
import {
  TaskStatusBadge,
  generateActionIdempotencyKey,
} from '../../components/outbound/OutboundStatusBadge';
import { ShortPickModal } from '../../components/outbound/ShortPickModal';
import { OutboundTableRegion } from '../../components/outbound/OutboundTableRegion';
import { useAuth } from '../../context/AuthContext';
import { useFacility } from '../../context/FacilityContext';
import { useRouter } from '../../context/RouterContext';
import { ApiError } from '../../services/apiClient';
import { outboundApi } from '../../services/outboundApi';
import {
  PickTaskDto,
  PickTaskStatus,
  ShortPickExecutionResultDto,
} from '../../types/outbound';
import '../../styles/outbound.css';

function formatPickMutationError(err: unknown, fallback: string): string {
  if (err instanceof ApiError) {
    if (err.status === 403) return `Access Denied (403): ${err.message}`;
    if (err.status === 409) return `Concurrency Conflict (409): ${err.message}`;
    return `${err.message} (HTTP ${err.status})`;
  }
  return err instanceof Error ? err.message : fallback;
}

export const PickingWorkstationPage: React.FC = () => {
  const { activeFacility } = useFacility();
  const { hasPermission, hasAnyPermission, isStaff, isSuperAdmin, user, isLoading: isAuthLoading } = useAuth();
  const { navigate } = useRouter();

  // Strictly separate Queue View vs Supervisor Management vs RF Execution
  const canViewQueue = isStaff && hasPermission('outbound:tasks:view');
  const canManage = isStaff && hasPermission('outbound:tasks:manage');
  const canPick = isStaff && hasPermission('outbound:pick');
  const canAccessModule =
    isStaff && (isSuperAdmin || hasAnyPermission(['outbound:tasks:view', 'outbound:tasks:manage', 'outbound:pick']));

  const [activeTab, setActiveTab] = useState<'queue' | 'rf'>('queue');
  const tabInitializedRef = useRef(false);

  useEffect(() => {
    if (!isAuthLoading && !tabInitializedRef.current) {
      tabInitializedRef.current = true;
      if (!canViewQueue && canPick) {
        setActiveTab('rf');
      }
    }
  }, [isAuthLoading, canViewQueue, canPick]);

  // Queue state
  const [tasks, setTasks] = useState<PickTaskDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const pageSize = 15;
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [searchTerm, setSearchTerm] = useState('');
  const [loadingQueue, setLoadingQueue] = useState(false);
  const [queueError, setQueueError] = useState<string | null>(null);
  const [assigningTask, setAssigningTask] = useState<PickTaskDto | null>(null);

  // RF Execution state
  const [nextTask, setNextTask] = useState<PickTaskDto | null>(null);
  const [loadingNext, setLoadingNext] = useState(false);
  const [rfLoadError, setRfLoadError] = useState<string | null>(null);
  const [rfActionError, setRfActionError] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<{ type: 'success' | 'warning' | 'error'; message: string } | null>(null);

  // Scan inputs
  const [scannedLocationBarcode, setScannedLocationBarcode] = useState('');
  const [scannedItemBarcode, setScannedItemBarcode] = useState('');
  const [scannedLotNumber, setScannedLotNumber] = useState('');
  const [scannedToteLpn, setScannedToteLpn] = useState('');
  const [pickQuantity, setPickQuantity] = useState('');

  const [isClaiming, setIsClaiming] = useState(false);
  const [isConfirming, setIsConfirming] = useState(false);
  const [showShortPickModal, setShowShortPickModal] = useState(false);

  const inFlightClaimRef = useRef(false);
  const inFlightConfirmRef = useRef(false);
  const confirmIdempotencyKeyRef = useRef<string | null>(null);
  const scanLocationInputRef = useRef<HTMLInputElement>(null);

  // Reset pagination & stale RF task on facility switch
  const prevFacilityIdRef = useRef<string | undefined>(undefined);
  useEffect(() => {
    const nextId = activeFacility?.id;
    if (prevFacilityIdRef.current && nextId && prevFacilityIdRef.current !== nextId) {
      setPage(1);
      setNextTask(null);
      setAssigningTask(null);
      setShowShortPickModal(false);
      confirmIdempotencyKeyRef.current = null;
    }
    prevFacilityIdRef.current = nextId;
  }, [activeFacility?.id]);

  const syncScanDefaultsForTask = useCallback((task: PickTaskDto | null) => {
    setScannedLocationBarcode('');
    setScannedItemBarcode('');
    setScannedLotNumber('');
    setScannedToteLpn('');
    if (task) {
      const remaining = Math.max(0, task.quantityToPick - task.quantityPicked);
      setPickQuantity(String(remaining > 0 ? remaining : task.quantityToPick));
    } else {
      setPickQuantity('');
    }
    confirmIdempotencyKeyRef.current = null;
  }, []);

  const loadTasks = useCallback(
    async (signal?: AbortSignal) => {
      if (!activeFacility || !canViewQueue) return;
      setLoadingQueue(true);
      setQueueError(null);
      try {
        const res = await outboundApi.getPickTasks(
          {
            facilityId: activeFacility.id,
            status: statusFilter !== 'all' ? (statusFilter as PickTaskStatus) : undefined,
            searchTerm: searchTerm.trim() || undefined,
            pageNumber: page,
            pageSize,
          },
          signal
        );
        setTasks(res.items);
        setTotalCount(res.totalCount);
      } catch (err: any) {
        if (err.name !== 'AbortError') {
          setQueueError(formatPickMutationError(err, 'Failed to load pick task queue.'));
        }
      } finally {
        setLoadingQueue(false);
      }
    },
    [activeFacility?.id, canViewQueue, statusFilter, searchTerm, page]
  );

  const loadNextTask = useCallback(
    async (signal?: AbortSignal) => {
      if (!activeFacility || !canPick) return;
      setLoadingNext(true);
      setRfLoadError(null);
      setRfActionError(null);
      try {
        const task = await outboundApi.getNextPickTask(activeFacility.id, signal);
        setNextTask(task);
        syncScanDefaultsForTask(task);
      } catch (err: any) {
        if (err.name !== 'AbortError') {
          setRfLoadError(formatPickMutationError(err, 'Unable to load next directed pick task.'));
          setNextTask(null);
        }
      } finally {
        setLoadingNext(false);
      }
    },
    [activeFacility?.id, canPick, syncScanDefaultsForTask]
  );

  useEffect(() => {
    if (isAuthLoading || !isStaff || !activeFacility) return;
    const ctrl = new AbortController();
    if (activeTab === 'queue' && canViewQueue) {
      loadTasks(ctrl.signal);
    } else if (activeTab === 'rf' && canPick) {
      loadNextTask(ctrl.signal);
    }
    return () => ctrl.abort();
  }, [isAuthLoading, isStaff, activeFacility?.id, activeTab, canViewQueue, canPick, loadTasks, loadNextTask]);

  const isOwnedByCaller = Boolean(
    user?.userId && nextTask?.assignedUserId && nextTask.assignedUserId === user.userId
  );
  const isUnassignedPending = Boolean(
    nextTask && nextTask.status === PickTaskStatus.Pending && !nextTask.assignedUserId
  );
  const isSupervisorOverrideEligible = Boolean(canManage && nextTask && !isUnassignedPending && !isOwnedByCaller);
  const canExecuteOnTask = Boolean(nextTask && (isOwnedByCaller || isSupervisorOverrideEligible));

  // Focus source bin barcode input cleanly once claimed/executable without stealing active input focus
  useEffect(() => {
    if (activeTab === 'rf' && nextTask && canExecuteOnTask) {
      const rafId = window.requestAnimationFrame(() => {
        const active = document.activeElement;
        if (!active || active === document.body || active.tagName === 'BUTTON') {
          scanLocationInputRef.current?.focus();
        }
      });
      return () => window.cancelAnimationFrame(rafId);
    }
  }, [activeTab, nextTask?.id, canExecuteOnTask]);

  const handleClaimTask = async () => {
    if (!nextTask || !canPick || inFlightClaimRef.current || isClaiming) return;
    inFlightClaimRef.current = true;
    setIsClaiming(true);
    setRfActionError(null);
    setFeedback(null);
    try {
      const claimed = await outboundApi.claimPickTask(nextTask.id);
      setNextTask(claimed);
      syncScanDefaultsForTask(claimed);
      setFeedback({
        type: 'success',
        message: `Task ${claimed.sku} (${claimed.sourceLocationCode}) claimed and locked to your RF session.`,
      });
    } catch (err: any) {
      const msg = formatPickMutationError(err, 'Failed to claim pick task.');
      setRfActionError(msg);
      if (err instanceof ApiError && err.status === 409) {
        await loadNextTask();
      }
    } finally {
      inFlightClaimRef.current = false;
      setIsClaiming(false);
    }
  };

  const handleConfirmPick = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!nextTask || !canPick || !canExecuteOnTask || inFlightConfirmRef.current || isConfirming) return;

    const loc = scannedLocationBarcode.trim();
    const itemBc = scannedItemBarcode.trim();
    const tote = scannedToteLpn.trim();
    const lot = scannedLotNumber.trim();
    const qty = Number(pickQuantity);

    if (!loc) {
      setRfActionError('Scan source storage bin barcode before confirming.');
      scanLocationInputRef.current?.focus();
      return;
    }
    if (!itemBc) {
      setRfActionError('Scan product SKU or barcode before confirming.');
      return;
    }
    if (nextTask.lotNumber && !lot) {
      setRfActionError(`Lot number '${nextTask.lotNumber}' scan is required for this task.`);
      return;
    }
    if (!tote) {
      setRfActionError('Scan destination cart or tote LPN barcode.');
      return;
    }
    if (!Number.isFinite(qty) || qty <= 0) {
      setRfActionError('Picked quantity must be greater than zero.');
      return;
    }

    if (!confirmIdempotencyKeyRef.current) {
      confirmIdempotencyKeyRef.current = generateActionIdempotencyKey('rf-pick');
    }

    inFlightConfirmRef.current = true;
    setIsConfirming(true);
    setRfActionError(null);
    setFeedback(null);
    try {
      const res = await outboundApi.confirmPickTask(nextTask.id, {
        scannedLocationBarcode: loc,
        scannedItemBarcode: itemBc,
        scannedLotNumber: lot || null,
        scannedToteLpn: tote,
        quantity: qty,
        idempotencyKey: confirmIdempotencyKeyRef.current,
      });
      confirmIdempotencyKeyRef.current = null;
      setFeedback({
        type: 'success',
        message:
          res.message ||
          `Confirmed ${res.quantityPicked} unit(s) into tote ${res.destinationToteBarcode || tote}.${res.waveCompleted ? ' Pick wave completed!' : ''}`,
      });
      await loadNextTask();
    } catch (err: any) {
      setRfActionError(formatPickMutationError(err, 'Pick confirmation failed.'));
    } finally {
      inFlightConfirmRef.current = false;
      setIsConfirming(false);
    }
  };

  const handleShortPickReported = async (res: ShortPickExecutionResultDto) => {
    setShowShortPickModal(false);
    setFeedback({
      type: res.locationLockApplied ? 'warning' : 'success',
      message: `Short-pick recorded (${res.discrepancyQuantity} unit(s) short).${
        res.generatedCycleCountId ? ' Cycle count scheduled.' : ''
      }${res.locationLockApplied ? ' Source bin locked for audit.' : ''}`,
    });
    await loadNextTask();
  };

  if (isAuthLoading) {
    return (
      <div className="card outbound-empty-state" role="status">
        Loading picking workstation permissions...
      </div>
    );
  }

  if (!canAccessModule) {
    return (
      <div className="card outbound-empty-state" role="region" data-testid="picking-forbidden-card">
        <h3>403 Restricted — Picking Access Required</h3>
        <p>
          {!isStaff
            ? 'Warehouse RF picking and task queues are restricted exclusively to authorized warehouse staff.'
            : 'Your role lacks outbound:tasks:view, outbound:tasks:manage, or outbound:pick permissions.'}
        </p>
        <div className="modal-inline-actions" style={{ justifyContent: 'center', marginTop: 12 }}>
          <button type="button" className="btn btn-primary" onClick={() => navigate('/app/dashboard')}>
            Return to Dashboard
          </button>
        </div>
      </div>
    );
  }

  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  const remainingTaskQty = nextTask ? Math.max(0, nextTask.quantityToPick - nextTask.quantityPicked) : 0;

  return (
    <div className="outbound-page" role="region" aria-label="Directed Picking Workstation">
      {/* Header & Strict Role-Scoped Tabs */}
      <div className="outbound-header">
        <div>
          <p className="outbound-subtitle">
            {activeFacility
              ? `${activeFacility.name} (${activeFacility.facilityCode})`
              : 'Select an active facility'}
            {activeTab === 'queue' && canViewQueue && (
              <>
                {' '}
                &bull; Total Tasks: <strong data-testid="pick-tasks-total">{totalCount}</strong>
              </>
            )}
          </p>
        </div>

        <div className="outbound-header-actions">
          {(canViewQueue || canPick) && (
            <div className="picking-mode-tabs" role="tablist" aria-label="Picking Views">
              {canViewQueue && (
                <button
                  type="button"
                  role="tab"
                  aria-selected={activeTab === 'queue'}
                  className={`btn ${activeTab === 'queue' ? 'btn-primary' : 'btn-secondary'}`}
                  onClick={() => setActiveTab('queue')}
                  data-testid="tab-pick-queue"
                >
                  <AppIcon name="Layers" size="sm" />
                  <span>Task Queue</span>
                </button>
              )}
              {canPick && (
                <button
                  type="button"
                  role="tab"
                  aria-selected={activeTab === 'rf'}
                  className={`btn ${activeTab === 'rf' ? 'btn-primary' : 'btn-secondary'}`}
                  onClick={() => setActiveTab('rf')}
                  data-testid="tab-pick-rf"
                >
                  <AppIcon name="Barcode" size="sm" />
                  <span>RF Pick Mode</span>
                </button>
              )}
            </div>
          )}

          <button
            type="button"
            className="btn btn-secondary"
            disabled={!activeFacility || loadingQueue || loadingNext}
            onClick={() => (activeTab === 'rf' && canPick ? loadNextTask() : loadTasks())}
            data-testid="picking-refresh-btn"
          >
            <AppIcon name="RefreshCw" size="sm" />
            <span>Refresh</span>
          </button>
        </div>
      </div>

      {feedback && (
        <div
          className={`outbound-alert ${feedback.type}`}
          role={feedback.type === 'error' ? 'alert' : 'status'}
          data-testid="picking-feedback-banner"
        >
          <span>{feedback.message}</span>
          <button
            type="button"
            className="btn btn-secondary btn-sm"
            onClick={() => setFeedback(null)}
            aria-label="Dismiss notification"
          >
            Dismiss
          </button>
        </div>
      )}

      {/* Manage-Only Workstation View (lacks outbound:tasks:view and outbound:pick) */}
      {!canViewQueue && !canPick && canManage && (
        <div className="card outbound-empty-state" data-testid="pick-manage-workstation">
          <h3>Supervisor Pick Management Mode</h3>
          <p>
            You hold supervisor assignment clearance (<code>outbound:tasks:manage</code>), while queue browsing requires{' '}
            <code>outbound:tasks:view</code> and handheld RF scanning requires <code>outbound:pick</code>.
          </p>
        </div>
      )}

      {/* Supervisor / Queue Tab (requires outbound:tasks:view) */}
      {activeTab === 'queue' && canViewQueue && (
        <div data-testid="pick-queue-view" className="outbound-form-stack">
          <div className="card outbound-filters-card">
            <div className="form-field">
              <label htmlFor="pick-search">Search Order / SKU / Bin / Tote</label>
              <input
                id="pick-search"
                type="search"
                value={searchTerm}
                onChange={(e) => {
                  setSearchTerm(e.target.value);
                  setPage(1);
                }}
                placeholder="SO-..., SKU-..., A-01-BIN"
              />
            </div>

            <div className="form-field">
              <label htmlFor="pick-status-filter">Task Status</label>
              <select
                id="pick-status-filter"
                value={statusFilter}
                onChange={(e) => {
                  setStatusFilter(e.target.value);
                  setPage(1);
                }}
              >
                <option value="all">All Statuses</option>
                {Object.values(PickTaskStatus).map((st) => (
                  <option key={st} value={st}>
                    {st}
                  </option>
                ))}
              </select>
            </div>
          </div>

          {queueError && (
            <div className="outbound-alert error" role="alert" data-testid="pick-queue-error">
              <span>{queueError}</span>
              <button type="button" className="btn btn-secondary btn-sm" onClick={() => loadTasks()}>
                Retry
              </button>
            </div>
          )}

          <div className="card">
            {loadingQueue ? (
              <div className="outbound-empty-state">Loading facility pick tasks...</div>
            ) : tasks.length === 0 ? (
              <div className="outbound-empty-state">
                <p>
                  <strong>No pick tasks match your current filters.</strong>
                </p>
              </div>
            ) : (
              <>
                <OutboundTableRegion
                  ariaLabel="Pick queue tasks table"
                  hintText="Swipe or scroll horizontally to view quantity and operator assignment columns →"
                  hintTestId="pick-queue-scroll-hint"
                  regionTestId="pick-queue-scroll-region"
                >
                  <table className="outbound-table">
                    <thead>
                      <tr>
                        <th>Seq</th>
                        <th>Order #</th>
                        <th>SKU</th>
                        <th>Source Bin</th>
                        <th>Lot / Expected LPN</th>
                        <th>Qty Picked / Target</th>
                        <th>Status</th>
                        <th>Assignee</th>
                        {canManage && <th>Action</th>}
                      </tr>
                    </thead>
                    <tbody>
                      {tasks.map((t) => {
                        const assignable =
                          canManage &&
                          (t.status === PickTaskStatus.Pending || t.status === PickTaskStatus.Assigned);
                        return (
                          <tr key={t.id} data-testid={`pick-task-row-${t.id}`}>
                            <td>#{t.pickSequence}</td>
                            <td className="cell-strong">{t.orderNumber}</td>
                            <td>
                              <div className="cell-strong">{t.sku}</div>
                              <div className="cell-sub">{t.itemDescription}</div>
                            </td>
                            <td className="code-font">{t.sourceLocationCode}</td>
                            <td className="cell-sub">
                              {t.lotNumber || t.expectedLpnCode || 'Any'}
                            </td>
                            <td>
                              {t.quantityPicked} / {t.quantityToPick}
                              {t.shortPickQuantity > 0 && (
                                <span className="cell-sub"> ({t.shortPickQuantity} short)</span>
                              )}
                            </td>
                            <td>
                              <TaskStatusBadge status={t.status} />
                            </td>
                            <td className="cell-sub">
                              {!t.assignedUserId
                                ? 'Unassigned'
                                : user?.userId && t.assignedUserId === user.userId
                                ? 'You'
                                : `Staff (${t.assignedUserId.slice(0, 8)}...)`}
                            </td>
                            {canManage && (
                              <td>
                                {assignable ? (
                                  <button
                                    type="button"
                                    className="btn btn-outline btn-sm"
                                    onClick={() => setAssigningTask(t)}
                                    data-testid={`assign-task-btn-${t.id}`}
                                  >
                                    Assign
                                  </button>
                                ) : (
                                  <span className="cell-sub">—</span>
                                )}
                              </td>
                            )}
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </OutboundTableRegion>

                <div className="outbound-pagination">
                  <span className="cell-sub">
                    Page {page} of {totalPages} ({totalCount} total task{totalCount === 1 ? '' : 's'})
                  </span>
                  <div className="pagination-controls">
                    <button
                      type="button"
                      className="btn btn-secondary btn-sm"
                      disabled={page <= 1 || loadingQueue}
                      onClick={() => setPage((p) => Math.max(1, p - 1))}
                    >
                      Previous
                    </button>
                    <button
                      type="button"
                      className="btn btn-secondary btn-sm"
                      disabled={page >= totalPages || loadingQueue}
                      onClick={() => setPage((p) => p + 1)}
                    >
                      Next
                    </button>
                  </div>
                </div>
              </>
            )}
          </div>
        </div>
      )}

      {/* RF Execution Tab (requires outbound:pick) */}
      {activeTab === 'rf' && canPick && (
        <div data-testid="pick-rf-view" className="outbound-form-stack">
          {rfLoadError ? (
            <div className="card rf-state-card" role="alert" data-testid="rf-error-banner">
              <AppIcon name="AlertTriangle" size="lg" />
              <h3>Failed to Load Next Pick Task</h3>
              <p>{rfLoadError}</p>
              <button
                type="button"
                className="btn btn-primary"
                onClick={() => loadNextTask()}
                data-testid="rf-retry-next-btn"
              >
                <AppIcon name="RefreshCw" size="sm" />
                <span>Retry Load Next Task</span>
              </button>
            </div>
          ) : loadingNext ? (
            <div className="card outbound-empty-state" role="status">
              Querying next directed pick task for your RF session...
            </div>
          ) : !nextTask ? (
            <div className="card rf-state-card" data-testid="rf-empty-state">
              <AppIcon name="CheckCircle2" size="lg" />
              <h3>No Released Pick Tasks Available</h3>
              <p>
                All currently released pick tasks in {activeFacility?.name || 'this facility'} are completed or claimed.
              </p>
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => loadNextTask()}
                data-testid="rf-refresh-empty-btn"
              >
                <AppIcon name="RefreshCw" size="sm" />
                <span>Check for New Tasks</span>
              </button>
            </div>
          ) : (
            <div className="card rf-pick-card" data-testid="rf-pick-task-card">
              <div className="rf-pick-header">
                <div>
                  <span className="rf-eyebrow">
                    DIRECTED PICK TASK • SEQ #{nextTask.pickSequence} • ORDER {nextTask.orderNumber}
                  </span>
                  <h2 className="rf-sku-heading">{nextTask.sku}</h2>
                  <p className="outbound-subtitle">{nextTask.itemDescription}</p>
                </div>
                <div className="rf-header-badges">
                  <TaskStatusBadge status={nextTask.status} />
                  {isOwnedByCaller && <span className="badge badge-success">Claimed by You</span>}
                  {isSupervisorOverrideEligible && <span className="badge badge-info">Supervisor Override</span>}
                </div>
              </div>

              <div className="rf-routing-banner">
                <div className="rf-route-node">
                  <span className="rf-route-label">PICK FROM STORAGE BIN</span>
                  <strong className="rf-route-code code-font">{nextTask.sourceLocationCode}</strong>
                </div>
                <div className="rf-route-arrow" aria-hidden="true">
                  &rarr;
                </div>
                <div className="rf-route-node">
                  <span className="rf-route-label">REMAINING QTY TO PICK</span>
                  <strong className="rf-route-code">{remainingTaskQty} unit(s)</strong>
                </div>
                <div className="rf-route-node">
                  <span className="rf-route-label">EXPECTED LOT / LPN / TOTE</span>
                  <strong className="rf-route-sub code-font">
                    {nextTask.lotNumber || nextTask.expectedLpnCode || nextTask.targetToteBarcode || 'Open Tote'}
                  </strong>
                </div>
              </div>

              {rfActionError && (
                <div className="outbound-alert error" role="alert" data-testid="rf-action-error">
                  {rfActionError}
                </div>
              )}

              {/* Explicit Atomic Ownership Gate: Unclaimed Pending tasks MUST be claimed before scanning */}
              {isUnassignedPending && (
                <div className="rf-claim-gate" data-testid="rf-claim-gate">
                  <div>
                    <strong>Ownership Required Before Execution</strong>
                    <p className="outbound-subtitle">
                      This task is unassigned (Pending). Claim ownership atomically to lock it to your scanner before
                      confirming stock movement.
                    </p>
                  </div>
                  <button
                    type="button"
                    className="btn btn-primary btn-rf-lg"
                    disabled={isClaiming}
                    onClick={handleClaimTask}
                    data-testid="claim-pick-task-btn"
                  >
                    <AppIcon name="CheckCircle2" size="sm" />
                    <span>{isClaiming ? 'Claiming Task...' : 'Claim Task to Start Picking'}</span>
                  </button>
                </div>
              )}

              {!isUnassignedPending && !canExecuteOnTask && (
                <div className="outbound-alert error" role="alert" data-testid="rf-ownership-blocked">
                  This pick task is currently assigned to another operator ({nextTask.assignedUserId}).
                </div>
              )}

              {/* Barcode Scan & Execution Form */}
              <form onSubmit={handleConfirmPick} className="outbound-form-stack" data-testid="rf-confirm-pick-form">
                <div className="rf-scan-grid">
                  <div className="form-field">
                    <label htmlFor="rf-scan-location">1. Scan Source Bin Barcode *</label>
                    <input
                      ref={scanLocationInputRef}
                      id="rf-scan-location"
                      type="text"
                      className="rf-scan-input code-font"
                      placeholder={`Expected: ${nextTask.sourceLocationCode}`}
                      value={scannedLocationBarcode}
                      onChange={(e) => setScannedLocationBarcode(e.target.value)}
                      disabled={!canExecuteOnTask || isConfirming}
                      autoComplete="off"
                      data-testid="rf-scan-location"
                    />
                  </div>

                  <div className="form-field">
                    <label htmlFor="rf-scan-item">2. Scan Item / SKU Barcode *</label>
                    <input
                      id="rf-scan-item"
                      type="text"
                      className="rf-scan-input code-font"
                      placeholder={`Expected: ${nextTask.sku}`}
                      value={scannedItemBarcode}
                      onChange={(e) => setScannedItemBarcode(e.target.value)}
                      disabled={!canExecuteOnTask || isConfirming}
                      autoComplete="off"
                      data-testid="rf-scan-item"
                    />
                  </div>

                  <div className="form-field">
                    <label htmlFor="rf-scan-tote">3. Scan Destination Tote / Cart LPN *</label>
                    <input
                      id="rf-scan-tote"
                      type="text"
                      className="rf-scan-input code-font"
                      placeholder={nextTask.targetToteBarcode ? `Expected: ${nextTask.targetToteBarcode}` : 'Scan tote / cart LPN'}
                      value={scannedToteLpn}
                      onChange={(e) => setScannedToteLpn(e.target.value)}
                      disabled={!canExecuteOnTask || isConfirming}
                      autoComplete="off"
                      data-testid="rf-scan-tote"
                    />
                  </div>

                  <div className="form-field">
                    <label htmlFor="rf-qty-input">4. Picked Quantity *</label>
                    <input
                      id="rf-qty-input"
                      type="number"
                      step="any"
                      min="0.01"
                      className="rf-scan-input"
                      value={pickQuantity}
                      onChange={(e) => setPickQuantity(e.target.value)}
                      disabled={!canExecuteOnTask || isConfirming}
                      data-testid="rf-qty-input"
                    />
                  </div>

                  {nextTask.lotNumber && (
                    <div className="form-field">
                      <label htmlFor="rf-scan-lot">5. Scan Lot Number *</label>
                      <input
                        id="rf-scan-lot"
                        type="text"
                        className="rf-scan-input code-font"
                        placeholder={`Expected: ${nextTask.lotNumber}`}
                        value={scannedLotNumber}
                        onChange={(e) => setScannedLotNumber(e.target.value)}
                        disabled={!canExecuteOnTask || isConfirming}
                        autoComplete="off"
                        data-testid="rf-scan-lot"
                      />
                    </div>
                  )}
                </div>

                <div className="rf-action-bar">
                  <button
                    type="submit"
                    className="btn btn-primary btn-rf-lg"
                    disabled={!canExecuteOnTask || isConfirming}
                    data-testid="confirm-pick-btn"
                  >
                    <AppIcon name="CheckCircle2" size="sm" />
                    <span>{isConfirming ? 'Confirming Pick...' : 'Confirm Pick to Tote'}</span>
                  </button>

                  <button
                    type="button"
                    className="btn btn-danger btn-rf-lg"
                    disabled={!canExecuteOnTask || isConfirming}
                    onClick={() => setShowShortPickModal(true)}
                    data-testid="open-short-pick-btn"
                  >
                    <AppIcon name="AlertTriangle" size="sm" />
                    <span>Report Short Pick</span>
                  </button>
                </div>
              </form>
            </div>
          )}
        </div>
      )}

      {/* Short-Pick Modal */}
      <ShortPickModal
        isOpen={showShortPickModal}
        onClose={() => setShowShortPickModal(false)}
        task={nextTask}
        initialLocationBarcode={scannedLocationBarcode}
        initialItemBarcode={scannedItemBarcode}
        initialToteLpn={scannedToteLpn}
        onReported={handleShortPickReported}
      />

      {/* Supervisor Task Assign Modal */}
      <AssignPickTaskModal
        isOpen={Boolean(assigningTask)}
        onClose={() => setAssigningTask(null)}
        task={assigningTask}
        facilityId={activeFacility?.id}
        onAssigned={(updated) => {
          setAssigningTask(null);
          setFeedback({
            type: 'success',
            message: `Assigned task ${updated.sku} (Order ${updated.orderNumber}) to operator.`,
          });
          loadTasks();
        }}
      />
    </div>
  );
};
