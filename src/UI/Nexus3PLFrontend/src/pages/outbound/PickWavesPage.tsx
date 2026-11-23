import React, { useCallback, useEffect, useRef, useState } from 'react';
import { AppIcon } from '../../components/common/AppIcon';
import { Modal } from '../../components/common/Modal';
import {
  OrderStatusBadge,
  TaskStatusBadge,
  WaveStatusBadge,
  formatWaveTypeLabel,
} from '../../components/outbound/OutboundStatusBadge';
import { PickWaveGenerateModal } from '../../components/outbound/PickWaveGenerateModal';
import { useAuth } from '../../context/AuthContext';
import { useFacility } from '../../context/FacilityContext';
import { useRouter } from '../../context/RouterContext';
import { ApiError } from '../../services/apiClient';
import { outboundApi } from '../../services/outboundApi';
import { PickWaveDto, PickWaveStatus, PickWaveType } from '../../types/outbound';
import '../../styles/outbound.css';

export const PickWavesPage: React.FC = () => {
  const { activeFacility } = useFacility();
  const { hasPermission, hasAnyPermission, isStaff, isSuperAdmin } = useAuth();
  const { navigate } = useRouter();

  // Wave planning & management is strictly restricted to trusted Staff (or verified Staff SuperAdmin)
  const canAccessWaves =
    isStaff && (isSuperAdmin || hasAnyPermission(['outbound:waves:view', 'outbound:waves:manage']));
  const canView = isStaff && hasPermission('outbound:waves:view');
  const canManage = isStaff && hasPermission('outbound:waves:manage');
  const canLookupClients = isStaff && hasPermission('clients:view');
  const canLookupOrders = isStaff && hasPermission('outbound:orders:view');
  const canLookupLocations = isStaff && hasPermission('facilities:view');

  const [waves, setWaves] = useState<PickWaveDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const pageSize = 15;
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [typeFilter, setTypeFilter] = useState<string>('all');

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<{ type: 'success' | 'error'; message: string } | null>(null);

  const [selectedWaveId, setSelectedWaveId] = useState<string | null>(null);
  const [selectedWave, setSelectedWave] = useState<PickWaveDto | null>(null);
  const [loadingDetail, setLoadingDetail] = useState(false);
  const [detailError, setDetailError] = useState<string | null>(null);

  const [showGenerateModal, setShowGenerateModal] = useState(false);
  const [wavePendingRelease, setWavePendingRelease] = useState<PickWaveDto | null>(null);
  const [isReleasing, setIsReleasing] = useState(false);
  const inFlightReleaseRef = useRef(false);

  const prevFacilityIdRef = useRef<string | undefined>(undefined);
  useEffect(() => {
    const nextId = activeFacility?.id;
    if (prevFacilityIdRef.current && nextId && prevFacilityIdRef.current !== nextId) {
      setPage(1);
      setSelectedWaveId(null);
      setSelectedWave(null);
    }
    prevFacilityIdRef.current = nextId;
  }, [activeFacility?.id]);

  const loadWaves = useCallback(
    async (signal?: AbortSignal) => {
      if (!canView || !activeFacility) return;
      setLoading(true);
      setError(null);
      try {
        const res = await outboundApi.getPickWaves(
          {
            facilityId: activeFacility.id,
            status: statusFilter !== 'all' ? (statusFilter as PickWaveStatus) : undefined,
            waveType: typeFilter !== 'all' ? (typeFilter as PickWaveType) : undefined,
            pageNumber: page,
            pageSize,
          },
          signal
        );
        setWaves(res.items);
        setTotalCount(res.totalCount);
      } catch (err: any) {
        if (err.name !== 'AbortError') {
          setError(err.message || 'Failed to load pick waves.');
        }
      } finally {
        setLoading(false);
      }
    },
    [canView, activeFacility, statusFilter, typeFilter, page]
  );

  useEffect(() => {
    if (!canView || !activeFacility) return;
    const ctrl = new AbortController();
    loadWaves(ctrl.signal);
    return () => ctrl.abort();
  }, [canView, activeFacility, loadWaves]);

  const loadWaveDetail = useCallback(
    async (waveId: string, signal?: AbortSignal) => {
      if (!canView) return;
      setLoadingDetail(true);
      setDetailError(null);
      try {
        const detail = await outboundApi.getPickWaveById(waveId, signal);
        setSelectedWave(detail);
      } catch (err: any) {
        if (err.name !== 'AbortError') {
          setDetailError(err.message || 'Failed to load pick wave details.');
        }
      } finally {
        setLoadingDetail(false);
      }
    },
    [canView]
  );

  useEffect(() => {
    if (!selectedWaveId || !canView) return;
    const ctrl = new AbortController();
    loadWaveDetail(selectedWaveId, ctrl.signal);
    return () => ctrl.abort();
  }, [selectedWaveId, canView, loadWaveDetail]);

  if (!canAccessWaves) {
    return (
      <div className="forbidden-card" role="alert" data-testid="pick-waves-forbidden">
        <div className="badge badge-danger" style={{ marginBottom: 12 }}>
          403 FORBIDDEN
        </div>
        <h2>Pick Wave Access Denied</h2>
        <p style={{ color: 'var(--text-muted)', margin: '10px 0 20px' }}>
          Pick wave planning and release require trusted warehouse staff identity with{' '}
          <code>outbound:waves:view</code> or <code>outbound:waves:manage</code>.
        </p>
        <button type="button" className="btn btn-primary" onClick={() => navigate('/app/dashboard')}>
          Return to Dashboard
        </button>
      </div>
    );
  }

  const handleConfirmReleaseWave = async () => {
    if (!wavePendingRelease || inFlightReleaseRef.current || isReleasing) return;
    inFlightReleaseRef.current = true;
    setIsReleasing(true);
    setFeedback(null);
    try {
      const released = await outboundApi.releasePickWave(wavePendingRelease.id);
      setWavePendingRelease(null);
      setSelectedWaveId(released.id);
      setSelectedWave(released);
      setFeedback({
        type: 'success',
        message: `Pick wave ${released.waveNumber} released to floor (${released.tasksCount} ready task(s)).`,
      });
      await loadWaves();
    } catch (err: any) {
      const prefix = err instanceof ApiError ? `HTTP ${err.status}: ` : '';
      setFeedback({ type: 'error', message: `${prefix}${err.message || 'Failed to release pick wave.'}` });
      setWavePendingRelease(null);
    } finally {
      inFlightReleaseRef.current = false;
      setIsReleasing(false);
    }
  };

  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));

  return (
    <div className="outbound-page" role="region" aria-label="Pick Wave Planning">
      <div className="outbound-header">
        <div>
          <p className="outbound-subtitle">
            {activeFacility ? `${activeFacility.name} (${activeFacility.facilityCode})` : 'Select Facility'}
            {' '}&bull; Waves: <strong data-testid="waves-total-count">{totalCount}</strong>
          </p>
        </div>
        <div className="outbound-header-actions">
          {canView && (
            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => loadWaves()}
              disabled={loading || !activeFacility}
            >
              <AppIcon name="RefreshCw" size="sm" />
              <span>Refresh</span>
            </button>
          )}
          {canManage && (
            <button
              type="button"
              className="btn btn-primary"
              disabled={!activeFacility}
              onClick={() => setShowGenerateModal(true)}
              data-testid="open-generate-wave-btn"
            >
              <AppIcon name="Plus" size="sm" />
              <span>Generate Pick Wave</span>
            </button>
          )}
        </div>
      </div>

      {feedback && (
        <div className={`outbound-alert ${feedback.type}`} role={feedback.type === 'error' ? 'alert' : 'status'}>
          <span>{feedback.message}</span>
          <button type="button" className="btn btn-secondary btn-sm" onClick={() => setFeedback(null)}>
            Dismiss
          </button>
        </div>
      )}

      {canView ? (
        <>
          <div className="card outbound-filters-card">
            <div className="form-field">
              <label htmlFor="waves-facility-display">Facility Context</label>
              <input
                id="waves-facility-display"
                type="text"
                readOnly
                value={activeFacility ? `${activeFacility.name} (${activeFacility.facilityCode})` : 'None'}
              />
            </div>

            <div className="form-field">
              <label htmlFor="waves-status-filter">Wave Status</label>
              <select
                id="waves-status-filter"
                value={statusFilter}
                onChange={(e) => {
                  setStatusFilter(e.target.value);
                  setPage(1);
                }}
              >
                <option value="all">All Statuses</option>
                {Object.values(PickWaveStatus).map((st) => (
                  <option key={st} value={st}>
                    {st}
                  </option>
                ))}
              </select>
            </div>

            <div className="form-field">
              <label htmlFor="waves-type-filter">Wave Type</label>
              <select
                id="waves-type-filter"
                value={typeFilter}
                onChange={(e) => {
                  setTypeFilter(e.target.value);
                  setPage(1);
                }}
              >
                <option value="all">All Types</option>
                {Object.values(PickWaveType).map((wt) => (
                  <option key={wt} value={wt}>
                    {formatWaveTypeLabel(wt)}
                  </option>
                ))}
              </select>
            </div>
          </div>

          {error && (
            <div className="outbound-alert error" role="alert">
              <span>{error}</span>
              <button type="button" className="btn btn-secondary btn-sm" onClick={() => loadWaves()}>
                Retry
              </button>
            </div>
          )}

          <div className="card">
            {loading && waves.length === 0 ? (
              <div className="outbound-empty-state" data-testid="waves-loading">
                Loading pick waves...
              </div>
            ) : waves.length === 0 ? (
              <div className="outbound-empty-state" data-testid="waves-empty">
                No pick waves found for {activeFacility?.name || 'this facility'}.
              </div>
            ) : (
              <div className="outbound-table-wrap">
                <table className="outbound-table" data-testid="pick-waves-table">
                  <thead>
                    <tr>
                      <th>Wave #</th>
                      <th>Type</th>
                      <th>Status</th>
                      <th>Priority</th>
                      <th>Orders</th>
                      <th>Tasks</th>
                      <th>Qty Progress</th>
                      <th>Created</th>
                      <th>Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {waves.map((wave) => {
                      const pct =
                        wave.totalQuantityToPick > 0
                          ? Math.min(100, Math.round((wave.totalQuantityPicked / wave.totalQuantityToPick) * 100))
                          : 0;
                      const isWaveInActiveFacility = !activeFacility || wave.facilityId === activeFacility.id;
                      return (
                        <tr key={wave.id} data-testid={`wave-row-${wave.id}`}>
                          <td className="cell-strong">{wave.waveNumber}</td>
                          <td>{formatWaveTypeLabel(wave.waveType)}</td>
                          <td>
                            <WaveStatusBadge status={wave.status} />
                          </td>
                          <td>P{wave.priority}</td>
                          <td>{wave.ordersCount}</td>
                          <td>{wave.tasksCount}</td>
                          <td>
                            <div className="progress-inline">
                              <div className="progress-track" aria-hidden="true">
                                <div className="progress-fill" style={{ width: `${pct}%` }} />
                              </div>
                              <span>
                                {wave.totalQuantityPicked}/{wave.totalQuantityToPick} ({pct}%)
                              </span>
                            </div>
                          </td>
                          <td>{new Date(wave.createdAt).toLocaleString()}</td>
                          <td>
                            <div className="actions-toolbar">
                              <button
                                type="button"
                                className="btn btn-outline btn-sm"
                                onClick={() => setSelectedWaveId(wave.id)}
                                data-testid={`inspect-wave-${wave.id}`}
                              >
                                Details
                              </button>
                              {canManage && wave.status === PickWaveStatus.Planning && (
                                <button
                                  type="button"
                                  className="btn btn-primary btn-sm"
                                  disabled={!isWaveInActiveFacility || isReleasing}
                                  onClick={() => setWavePendingRelease(wave)}
                                  data-testid={`release-wave-${wave.id}`}
                                >
                                  Release
                                </button>
                              )}
                            </div>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}

            <div className="outbound-pagination">
              <span className="cell-sub">
                Page {page} of {totalPages} ({totalCount} total wave{totalCount === 1 ? '' : 's'})
              </span>
              <div className="pagination-controls">
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  disabled={page <= 1 || loading}
                  onClick={() => setPage((p) => Math.max(1, p - 1))}
                >
                  Previous
                </button>
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  disabled={page >= totalPages || loading}
                  onClick={() => setPage((p) => p + 1)}
                >
                  Next
                </button>
              </div>
            </div>
          </div>
        </>
      ) : (
        <div className="card outbound-empty-state">
          Wave listing requires <code>outbound:waves:view</code>. Use Generate Pick Wave above to plan new
          waves.
        </div>
      )}

      {selectedWaveId && (
        <div className="card" role="region" aria-label="Pick Wave Detail" data-testid="pick-wave-detail-panel">
          <div className="section-header-inline">
            <div>
              <h2>{selectedWave ? `Wave ${selectedWave.waveNumber}` : 'Wave Details'}</h2>
              {selectedWave && (
                <p className="cell-sub">
                  {formatWaveTypeLabel(selectedWave.waveType)} &bull; Priority P{selectedWave.priority}
                  {selectedWave.tempClass ? ` • ${selectedWave.tempClass}` : ''}
                </p>
              )}
            </div>
            <div className="actions-toolbar">
              {selectedWave &&
                canManage &&
                selectedWave.status === PickWaveStatus.Planning &&
                (!activeFacility || selectedWave.facilityId === activeFacility.id) && (
                  <button
                    type="button"
                    className="btn btn-primary btn-sm"
                    disabled={isReleasing}
                    onClick={() => setWavePendingRelease(selectedWave)}
                    data-testid="detail-release-wave-btn"
                  >
                    Release Wave
                  </button>
                )}
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={() => {
                  setSelectedWaveId(null);
                  setSelectedWave(null);
                }}
              >
                Close Detail
              </button>
            </div>
          </div>

          {loadingDetail && <div className="outbound-empty-state">Loading wave details...</div>}
          {detailError && (
            <div className="outbound-alert error" role="alert">
              <span>{detailError}</span>
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={() => loadWaveDetail(selectedWaveId)}
              >
                Retry
              </button>
            </div>
          )}

          {selectedWave && (
            <div className="outbound-form-stack" style={{ marginTop: 14 }}>
              <div className="detail-card-grid">
                <div className="detail-meta-item">
                  <span className="meta-label">Status</span>
                  <div>
                    <WaveStatusBadge status={selectedWave.status} />
                  </div>
                </div>
                <div className="detail-meta-item">
                  <span className="meta-label">Orders / Tasks</span>
                  <span>
                    {selectedWave.ordersCount} orders &bull; {selectedWave.tasksCount} tasks
                  </span>
                </div>
                <div className="detail-meta-item">
                  <span className="meta-label">Quantity Progress</span>
                  <span>
                    {selectedWave.totalQuantityPicked} / {selectedWave.totalQuantityToPick} units
                  </span>
                </div>
                {typeof selectedWave.estimatedTravelMeters === 'number' &&
                  selectedWave.estimatedTravelMeters > 0 && (
                    <div className="detail-meta-item" data-testid="wave-travel-estimate">
                      <span className="meta-label">Estimated Travel</span>
                      <span>{selectedWave.estimatedTravelMeters.toFixed(1)} m</span>
                    </div>
                  )}
                {selectedWave.releasedAtUtc && (
                  <div className="detail-meta-item">
                    <span className="meta-label">Released At</span>
                    <span>{new Date(selectedWave.releasedAtUtc).toLocaleString()}</span>
                  </div>
                )}
              </div>

              <div>
                <h3 style={{ marginBottom: 8 }}>Associated Orders ({selectedWave.waveOrders.length})</h3>
                <div className="outbound-table-wrap">
                  <table className="outbound-table">
                    <thead>
                      <tr>
                        <th>Seq</th>
                        <th>Order #</th>
                        <th>Customer</th>
                        <th>Priority</th>
                        <th>Order Status</th>
                      </tr>
                    </thead>
                    <tbody>
                      {selectedWave.waveOrders.map((wo) => (
                        <tr key={wo.id}>
                          <td>{wo.sequenceNumber}</td>
                          <td className="cell-strong">{wo.orderNumber}</td>
                          <td>{wo.customerName}</td>
                          <td>P{wo.priority}</td>
                          <td>
                            <OrderStatusBadge status={wo.orderStatus} />
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>

              <div>
                <h3 style={{ marginBottom: 8 }}>Sequenced Pick Tasks ({selectedWave.tasks.length})</h3>
                <div className="outbound-table-wrap">
                  <table className="outbound-table">
                    <thead>
                      <tr>
                        <th>Seq</th>
                        <th>Order #</th>
                        <th>SKU</th>
                        <th>Source Bin</th>
                        <th>To Pick</th>
                        <th>Picked</th>
                        <th>Status</th>
                      </tr>
                    </thead>
                    <tbody>
                      {selectedWave.tasks.map((task) => (
                        <tr key={task.id}>
                          <td>{task.pickSequence}</td>
                          <td>{task.orderNumber}</td>
                          <td>
                            <div className="cell-strong">{task.sku}</div>
                            {task.itemDescription && <div className="cell-sub">{task.itemDescription}</div>}
                          </td>
                          <td>{task.sourceLocationCode}</td>
                          <td>{task.quantityToPick}</td>
                          <td>{task.quantityPicked}</td>
                          <td>
                            <TaskStatusBadge status={task.status} />
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            </div>
          )}
        </div>
      )}

      <PickWaveGenerateModal
        isOpen={showGenerateModal}
        onClose={() => setShowGenerateModal(false)}
        onGenerated={(wave) => {
          setSelectedWaveId(wave.id);
          setSelectedWave(wave);
          setFeedback({
            type: 'success',
            message: `Generated planning wave ${wave.waveNumber} (${wave.tasksCount} tasks).`,
          });
          loadWaves();
        }}
        activeFacility={activeFacility}
        canLookupClients={canLookupClients}
        canLookupOrders={canLookupOrders}
        canLookupLocations={canLookupLocations}
      />

      {wavePendingRelease && (
        <Modal
          isOpen={Boolean(wavePendingRelease)}
          onClose={() => !isReleasing && setWavePendingRelease(null)}
          title="Release Pick Wave"
          subtitle={`Wave ${wavePendingRelease.waveNumber} (${wavePendingRelease.tasksCount} task(s))`}
          maxWidth="460px"
        >
          <div className="outbound-form-stack">
            <p style={{ fontSize: 13.5, color: 'var(--text-main)' }}>
              Releasing wave <strong>{wavePendingRelease.waveNumber}</strong> transitions it from{' '}
              <strong>Planning</strong> to <strong>Released</strong> so RF operators can claim and execute
              its sequenced tasks. Wave release does not claim tasks or move inventory.
            </p>
            <div className="modal-inline-actions">
              <button
                type="button"
                className="btn btn-secondary"
                disabled={isReleasing}
                onClick={() => setWavePendingRelease(null)}
              >
                Cancel
              </button>
              <button
                type="button"
                className="btn btn-primary"
                disabled={isReleasing}
                onClick={handleConfirmReleaseWave}
                data-testid="confirm-release-wave-btn"
              >
                {isReleasing ? 'Releasing...' : 'Confirm Wave Release'}
              </button>
            </div>
          </div>
        </Modal>
      )}
    </div>
  );
};
