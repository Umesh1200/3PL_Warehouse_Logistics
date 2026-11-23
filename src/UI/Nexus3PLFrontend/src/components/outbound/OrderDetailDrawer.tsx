import React, { useEffect, useRef, useState } from 'react';
import { Modal } from '../common/Modal';
import { ApiError } from '../../services/apiClient';
import { outboundApi } from '../../services/outboundApi';
import { FacilityDto } from '../../types/auth';
import {
  AllocationExecutionResultDto,
  AllocationItemDto,
  AllocationStrategyType,
  OutboundOrderDto,
  OutboundOrderStatus,
  StockAvailabilityAssessmentDto,
} from '../../types/outbound';
import {
  OrderStatusBadge,
  computeOrderAllocationProgress,
  formatOrderTypeLabel,
  generateActionIdempotencyKey,
} from './OutboundStatusBadge';

export interface OrderDetailDrawerProps {
  orderId: string | null;
  onClose: () => void;
  onOrderMutated: () => void;
  activeFacility: FacilityDto | null;
  isStaff: boolean;
  isClient: boolean;
  isSuperAdmin: boolean;
  canAllocate: boolean;
  canManage: boolean;
  canCancel: boolean;
}

type ConfirmActionKind = 'hold' | 'release' | 'cancel' | 'deallocate' | 'release-allocation';

interface ConfirmActionState {
  kind: ConfirmActionKind;
  allocation?: AllocationItemDto;
}

function formatApiMutationError(err: unknown, fallback: string): string {
  if (err instanceof ApiError) {
    if (err.status === 403) return `Access Denied (403): ${err.message}`;
    if (err.status === 409) {
      return `Concurrency Conflict (409): ${err.message}. Reload the order before retrying.`;
    }
    if (err.status === 422) return `Invalid Domain State (422): ${err.message}`;
    return `HTTP ${err.status}: ${err.message}`;
  }
  return err instanceof Error ? err.message : fallback;
}

export const OrderDetailDrawer: React.FC<OrderDetailDrawerProps> = ({
  orderId,
  onClose,
  onOrderMutated,
  activeFacility,
  isStaff,
  isClient,
  isSuperAdmin,
  canAllocate,
  canManage,
  canCancel,
}) => {
  const [order, setOrder] = useState<OutboundOrderDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [availability, setAvailability] = useState<StockAvailabilityAssessmentDto | null>(null);
  const [loadingAvailability, setLoadingAvailability] = useState(false);

  const [strategy, setStrategy] = useState<AllocationStrategyType>(AllocationStrategyType.FIFO);
  const [allocationResult, setAllocationResult] = useState<AllocationExecutionResultDto | null>(null);
  const [isAllocating, setIsAllocating] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [actionSuccess, setActionSuccess] = useState<string | null>(null);

  const [confirmState, setConfirmState] = useState<ConfirmActionState | null>(null);
  const [confirmReason, setConfirmReason] = useState('');
  const [confirmQty, setConfirmQty] = useState('');
  const [confirmError, setConfirmError] = useState<string | null>(null);
  const [isConfirming, setIsConfirming] = useState(false);

  // Synchronous single-flight + stable idempotency key per logical allocation attempt
  const inFlightAllocateRef = useRef(false);
  const inFlightConfirmRef = useRef(false);
  const pendingAllocKeyRef = useRef<string | null>(null);
  const drawerRef = useRef<HTMLDivElement>(null);

  const fetchOrderDetail = async (targetId: string, signal?: AbortSignal) => {
    setLoading(true);
    setLoadError(null);
    try {
      const dto = await outboundApi.getOrderById(targetId, signal);
      setOrder(dto);
    } catch (err: any) {
      if (err.name !== 'AbortError') {
        setLoadError(formatApiMutationError(err, 'Failed to load order details.'));
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (!orderId) {
      setOrder(null);
      setAvailability(null);
      setAllocationResult(null);
      setActionError(null);
      setActionSuccess(null);
      pendingAllocKeyRef.current = null;
      return;
    }

    const ctrl = new AbortController();
    setAvailability(null);
    setAllocationResult(null);
    setActionError(null);
    setActionSuccess(null);
    pendingAllocKeyRef.current = null;
    fetchOrderDetail(orderId, ctrl.signal);
    return () => ctrl.abort();
  }, [orderId]);

  useEffect(() => {
    if (!orderId) return;
    const prevFocused = document.activeElement as HTMLElement | null;
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !confirmState) {
        onClose();
      }
    };
    window.addEventListener('keydown', handleKey);
    const timer = setTimeout(() => {
      drawerRef.current?.focus();
    }, 30);
    return () => {
      window.removeEventListener('keydown', handleKey);
      clearTimeout(timer);
      prevFocused?.focus();
    };
  }, [orderId, confirmState, onClose]);

  if (!orderId) return null;

  const isForeignFacilityForStaff =
    Boolean(order) &&
    isStaff &&
    !isSuperAdmin &&
    Boolean(activeFacility) &&
    order!.facilityId !== activeFacility!.id;

  const canMutateInCurrentFacility = !isForeignFacilityForStaff;
  const progress = order ? computeOrderAllocationProgress(order) : { ordered: 0, allocated: 0, percent: 0 };

  const isTerminal =
    order?.status === OutboundOrderStatus.Cancelled || order?.status === OutboundOrderStatus.Completed;
  const isOnHold = order?.status === OutboundOrderStatus.OnHold;

  const handleCheckAvailability = async () => {
    if (!order) return;
    setLoadingAvailability(true);
    setActionError(null);
    try {
      const res = await outboundApi.getStockAvailability(order.id);
      setAvailability(res);
    } catch (err: any) {
      if (err.name !== 'AbortError') {
        setActionError(formatApiMutationError(err, 'Failed to assess stock availability.'));
      }
    } finally {
      setLoadingAvailability(false);
    }
  };

  const handleAllocate = async () => {
    if (!order || inFlightAllocateRef.current || isAllocating) return;
    if (!canMutateInCurrentFacility) {
      setActionError('Access Denied (403): Staff allocations must occur within the assigned active facility.');
      return;
    }

    // Generate idempotency key once per logical action; preserve same key across retries until succeeded
    if (!pendingAllocKeyRef.current) {
      pendingAllocKeyRef.current = generateActionIdempotencyKey('alloc');
    }
    const idempotencyKey = pendingAllocKeyRef.current;

    inFlightAllocateRef.current = true;
    setIsAllocating(true);
    setActionError(null);
    setActionSuccess(null);

    try {
      const result = await outboundApi.allocateOrder(order.id, {
        strategyOverride: strategy,
        idempotencyKey,
      });
      pendingAllocKeyRef.current = null;
      setAllocationResult(result);
      setActionSuccess(
        `Allocation ${result.isDuplicateRequest ? '(Idempotent Replay) ' : ''}completed: ${result.totalQuantityAllocated} allocated${
          result.totalShortageQuantity > 0 ? `, ${result.totalShortageQuantity} short` : ''
        }.`
      );
      await fetchOrderDetail(order.id);
      if (availability) {
        await handleCheckAvailability();
      }
      onOrderMutated();
    } catch (err: any) {
      // Preserve pendingAllocKeyRef so immediate retry reuses same key safely
      setActionError(formatApiMutationError(err, 'Allocation failed.'));
    } finally {
      inFlightAllocateRef.current = false;
      setIsAllocating(false);
    }
  };

  const openConfirmModal = (kind: ConfirmActionKind, allocation?: AllocationItemDto) => {
    setConfirmReason('');
    setConfirmQty(allocation ? String(allocation.quantityAllocated) : '');
    setConfirmError(null);
    setConfirmState({ kind, allocation });
  };

  const handleExecuteConfirm = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!order || !confirmState || inFlightConfirmRef.current || isConfirming) return;
    setConfirmError(null);

    const requiresReason = confirmState.kind !== 'release';
    if (requiresReason && !confirmReason.trim()) {
      setConfirmError('Please provide an explicit operational reason.');
      return;
    }

    if (confirmState.kind === 'release-allocation' && confirmState.allocation) {
      if (confirmQty.trim()) {
        const parsedQty = Number(confirmQty);
        if (
          !Number.isFinite(parsedQty) ||
          parsedQty <= 0 ||
          parsedQty > confirmState.allocation.quantityAllocated
        ) {
          setConfirmError(
            `Release quantity must be > 0 and <= ${confirmState.allocation.quantityAllocated}.`
          );
          return;
        }
      }
    }

    inFlightConfirmRef.current = true;
    setIsConfirming(true);
    setActionError(null);
    setActionSuccess(null);

    try {
      if (confirmState.kind === 'hold') {
        await outboundApi.holdOrder(order.id, confirmReason.trim());
        setActionSuccess(`Order ${order.orderNumber} placed on Hold.`);
      } else if (confirmState.kind === 'release') {
        await outboundApi.releaseOrder(order.id, confirmReason.trim() || undefined);
        setActionSuccess(`Order ${order.orderNumber} released from Hold.`);
      } else if (confirmState.kind === 'cancel') {
        await outboundApi.cancelOrder(order.id, confirmReason.trim());
        setActionSuccess(`Order ${order.orderNumber} cancelled.`);
      } else if (confirmState.kind === 'deallocate') {
        const res = await outboundApi.deallocateOrder(order.id, confirmReason.trim());
        setAllocationResult(null);
        setActionSuccess(
          `Deallocated ${res.totalQuantityDeallocated} units across ${res.allocationsReleasedCount} reservation(s).`
        );
      } else if (confirmState.kind === 'release-allocation' && confirmState.allocation) {
        const qty = confirmQty.trim() ? Number(confirmQty) : null;
        const relRes = await outboundApi.releaseAllocation(confirmState.allocation.allocationId, {
          quantityToRelease: qty,
          reason: confirmReason.trim(),
        });
        setActionSuccess(
          `Released ${relRes.quantityReleased} units from allocation ${relRes.allocationId.slice(0, 8)}...`
        );
        // Refresh allocation summary item status locally if present
        setAllocationResult((prev) =>
          prev
            ? {
                ...prev,
                status: relRes.orderStatus,
                lineSummaries: prev.lineSummaries.map((ls) => ({
                  ...ls,
                  allocations: ls.allocations.map((a) =>
                    a.allocationId === relRes.allocationId
                      ? {
                          ...a,
                          quantityAllocated: relRes.activeRemainingQuantity,
                          status: relRes.allocationStatus,
                        }
                      : a
                  ),
                })),
              }
            : null
        );
      }

      setConfirmState(null);
      await fetchOrderDetail(order.id);
      if (availability) {
        await handleCheckAvailability();
      }
      onOrderMutated();
    } catch (err: any) {
      setConfirmError(formatApiMutationError(err, 'Action failed.'));
    } finally {
      inFlightConfirmRef.current = false;
      setIsConfirming(false);
    }
  };

  return (
    <div className="drawer-overlay" onClick={onClose} role="presentation">
      <div
        className="detail-drawer"
        role="dialog"
        aria-modal="true"
        aria-label="Outbound Order Detail"
        tabIndex={-1}
        ref={drawerRef}
        onClick={(e) => e.stopPropagation()}
        data-testid="order-detail-drawer"
      >
        <div className="drawer-header">
          <div>
            <h2>{order ? `Order ${order.orderNumber}` : 'Outbound Order Detail'}</h2>
            {order && (
              <p className="drawer-subtitle">
                {order.customerName} &bull; {formatOrderTypeLabel(order.orderType)} &bull; Version {order.version}
              </p>
            )}
          </div>
          <button
            type="button"
            className="modal-close-btn"
            onClick={onClose}
            aria-label="Close order detail"
          >
            &times;
          </button>
        </div>

        <div className="drawer-body">
          {loading && !order && <div className="outbound-empty-state">Loading order details...</div>}
          {loadError && (
            <div className="outbound-alert error" role="alert">
              <span>{loadError}</span>
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={() => fetchOrderDetail(orderId)}
              >
                Retry
              </button>
            </div>
          )}

          {order && (
            <>
              {isForeignFacilityForStaff && (
                <div className="outbound-alert warning" role="status">
                  Viewing cross-facility order ({order.facilityName}). Switch active facility to mutate
                  allocation or lifecycle status.
                </div>
              )}

              {actionError && (
                <div className="outbound-alert error" role="alert" data-testid="order-action-error">
                  {actionError}
                </div>
              )}
              {actionSuccess && (
                <div className="outbound-alert success" role="status" data-testid="order-action-success">
                  {actionSuccess}
                </div>
              )}

              <section className="detail-card-grid">
                <div className="detail-meta-item">
                  <span className="meta-label">Status</span>
                  <div>
                    <OrderStatusBadge status={order.status} />
                  </div>
                </div>
                <div className="detail-meta-item">
                  <span className="meta-label">Priority / Service</span>
                  <span>
                    P{order.priority} &bull; {order.serviceLevel}
                  </span>
                </div>
                <div className="detail-meta-item">
                  <span className="meta-label">Source Facility</span>
                  <span>{order.facilityName || order.facilityId}</span>
                </div>
                {order.destinationFacilityName && (
                  <div className="detail-meta-item">
                    <span className="meta-label">Destination Facility</span>
                    <span>{order.destinationFacilityName}</span>
                  </div>
                )}
                <div className="detail-meta-item">
                  <span className="meta-label">External Ref</span>
                  <span>{order.externalOrderReference || '—'}</span>
                </div>
                <div className="detail-meta-item">
                  <span className="meta-label">Required Ship Date</span>
                  <span>
                    {order.requiredShipDateUtc
                      ? new Date(order.requiredShipDateUtc).toLocaleDateString()
                      : '—'}
                  </span>
                </div>
                <div className="detail-meta-item">
                  <span className="meta-label">Allocation Progress</span>
                  <span>
                    {progress.allocated} / {progress.ordered} units ({progress.percent}%)
                  </span>
                </div>
              </section>

              {order.shippingAddress && (
                <section className="detail-section">
                  <h3>Shipping Address</h3>
                  <p className="address-snapshot">
                    <strong>{order.shippingAddress.recipientName}</strong>
                    {order.shippingAddress.companyName ? ` (${order.shippingAddress.companyName})` : ''}
                    <br />
                    {order.shippingAddress.addressLine1}
                    {order.shippingAddress.addressLine2 ? `, ${order.shippingAddress.addressLine2}` : ''}
                    <br />
                    {order.shippingAddress.city}, {order.shippingAddress.stateOrProvince}{' '}
                    {order.shippingAddress.postalCode}, {order.shippingAddress.countryCode}
                  </p>
                </section>
              )}

              {(order.holdReason || order.cancellationReason) && (
                <section className="outbound-alert warning">
                  {order.holdReason && (
                    <div>
                      <strong>Hold Reason:</strong> {order.holdReason}
                    </div>
                  )}
                  {order.cancellationReason && (
                    <div>
                      <strong>Cancellation Reason:</strong> {order.cancellationReason}
                    </div>
                  )}
                </section>
              )}

              <section className="detail-section">
                <div className="section-header-inline">
                  <h3>Order Lines ({order.lines.length})</h3>
                  <button
                    type="button"
                    className="btn btn-outline btn-sm"
                    onClick={handleCheckAvailability}
                    disabled={loadingAvailability}
                    data-testid="check-stock-availability"
                  >
                    {loadingAvailability ? 'Checking...' : 'Check Stock Availability'}
                  </button>
                </div>

                <div className="outbound-table-wrap">
                  <table className="outbound-table">
                    <thead>
                      <tr>
                        <th>#</th>
                        <th>SKU</th>
                        <th>Ordered</th>
                        <th>Allocated</th>
                        <th>Remaining</th>
                        <th>Constraints</th>
                        <th>Status</th>
                      </tr>
                    </thead>
                    <tbody>
                      {order.lines.map((line) => (
                        <tr key={line.id}>
                          <td>{line.lineNumber}</td>
                          <td>
                            <div className="cell-strong">{line.sku}</div>
                            <div className="cell-sub">{line.itemDescription}</div>
                          </td>
                          <td>{line.quantityOrdered}</td>
                          <td>{line.quantityAllocated}</td>
                          <td>{line.remainingDemand}</td>
                          <td>
                            {line.requiredLotNumber ? `Lot: ${line.requiredLotNumber}` : 'Any lot'}
                            {line.minExpiryDateUtc
                              ? ` • Min Exp ${new Date(line.minExpiryDateUtc).toLocaleDateString()}`
                              : ''}
                          </td>
                          <td>
                            <span className="badge badge-info">{line.status}</span>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </section>

              {availability && (
                <section className="detail-section availability-panel" data-testid="stock-availability-panel">
                  <div className="section-header-inline">
                    <h3>Stock Availability Assessment</h3>
                    <span
                      className={`badge ${availability.isFullyAvailable ? 'badge-success' : 'badge-warning'}`}
                    >
                      {availability.overallFulfillmentPercent}% Fulfillable
                    </span>
                  </div>
                  <div className="outbound-table-wrap">
                    <table className="outbound-table">
                      <thead>
                        <tr>
                          <th>SKU</th>
                          <th>Requested</th>
                          <th>Available</th>
                          <th>Shortage</th>
                          <th>Status</th>
                        </tr>
                      </thead>
                      <tbody>
                        {availability.lines.map((al) => (
                          <tr key={al.lineId}>
                            <td>{al.sku}</td>
                            <td>{al.quantityRequested}</td>
                            <td>{al.quantityAvailable}</td>
                            <td>{al.shortageQuantity}</td>
                            <td>
                              <span
                                className={`badge ${
                                  al.isFullyAvailable ? 'badge-success' : 'badge-danger'
                                }`}
                              >
                                {al.isFullyAvailable ? 'Available' : 'Shortage'}
                              </span>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </section>
              )}

              {/* Server-Authorized Operational Controls */}
              <section className="detail-section">
                <h3>Operational Actions</h3>
                <div className="actions-toolbar">
                  {isStaff && canAllocate && !isTerminal && !isOnHold && (
                    <div className="alloc-control-group">
                      <label htmlFor="alloc-strategy-select" className="sr-only">
                        Allocation Strategy
                      </label>
                      <select
                        id="alloc-strategy-select"
                        value={strategy}
                        onChange={(e) => setStrategy(e.target.value as AllocationStrategyType)}
                        disabled={!canMutateInCurrentFacility || isAllocating}
                      >
                        <option value={AllocationStrategyType.FIFO}>FIFO</option>
                        <option value={AllocationStrategyType.FEFO}>FEFO</option>
                        <option value={AllocationStrategyType.Manual}>Manual</option>
                      </select>
                      <button
                        type="button"
                        className="btn btn-primary btn-sm"
                        onClick={handleAllocate}
                        disabled={!canMutateInCurrentFacility || isAllocating}
                        data-testid="btn-allocate-order"
                      >
                        {isAllocating
                          ? 'Allocating...'
                          : pendingAllocKeyRef.current
                          ? 'Retry Allocate Stock'
                          : 'Allocate Stock'}
                      </button>
                    </div>
                  )}

                  {isStaff && canAllocate && progress.allocated > 0 && !isTerminal && (
                    <button
                      type="button"
                      className="btn btn-secondary btn-sm"
                      disabled={!canMutateInCurrentFacility}
                      onClick={() => openConfirmModal('deallocate')}
                      data-testid="btn-deallocate-order"
                    >
                      Deallocate All
                    </button>
                  )}

                  {isStaff && canManage && !isTerminal && !isOnHold && (
                    <button
                      type="button"
                      className="btn btn-secondary btn-sm"
                      disabled={!canMutateInCurrentFacility}
                      onClick={() => openConfirmModal('hold')}
                      data-testid="btn-hold-order"
                    >
                      Hold Order
                    </button>
                  )}

                  {isStaff && canManage && isOnHold && (
                    <button
                      type="button"
                      className="btn btn-outline btn-sm"
                      disabled={!canMutateInCurrentFacility}
                      onClick={() => openConfirmModal('release')}
                      data-testid="btn-release-order"
                    >
                      Release Hold
                    </button>
                  )}

                  {canCancel && (isStaff || isClient) && !isTerminal && (
                    <button
                      type="button"
                      className="btn btn-secondary btn-sm"
                      disabled={isStaff && !canMutateInCurrentFacility}
                      onClick={() => openConfirmModal('cancel')}
                      data-testid="btn-cancel-order"
                    >
                      Cancel Order
                    </button>
                  )}
                </div>
              </section>

              {allocationResult && (
                <section className="detail-section" data-testid="allocation-result-panel">
                  <div className="section-header-inline">
                    <h3>Latest Allocation Result</h3>
                    {allocationResult.isDuplicateRequest && (
                      <span className="badge badge-info">Idempotent Replay</span>
                    )}
                  </div>
                  <div className="summary-metrics-inline">
                    <span>Ordered: {allocationResult.totalQuantityOrdered}</span>
                    <span>Allocated: {allocationResult.totalQuantityAllocated}</span>
                    <span>Shortage: {allocationResult.totalShortageQuantity}</span>
                  </div>

                  {allocationResult.lineSummaries.flatMap((ls) => ls.allocations).length > 0 && (
                    <div className="outbound-table-wrap">
                      <table className="outbound-table">
                        <thead>
                          <tr>
                            <th>Location</th>
                            <th>Lot / LPN</th>
                            <th>Qty</th>
                            <th>Status</th>
                            <th>Action</th>
                          </tr>
                        </thead>
                        <tbody>
                          {allocationResult.lineSummaries.flatMap((ls) =>
                            ls.allocations.map((alloc) => (
                              <tr key={alloc.allocationId}>
                                <td>{alloc.locationCode}</td>
                                <td>{alloc.lotNumber || alloc.lpnCode || '—'}</td>
                                <td>{alloc.quantityAllocated}</td>
                                <td>
                                  <span className="badge badge-success">{alloc.status}</span>
                                </td>
                                <td>
                                  {isStaff && canAllocate && alloc.status === 'Allocated' && (
                                    <button
                                      type="button"
                                      className="btn btn-secondary btn-sm"
                                      onClick={() => openConfirmModal('release-allocation', alloc)}
                                      data-testid={`release-alloc-${alloc.allocationId}`}
                                    >
                                      Release
                                    </button>
                                  )}
                                </td>
                              </tr>
                            ))
                          )}
                        </tbody>
                      </table>
                    </div>
                  )}

                  {allocationResult.shortages.length > 0 && (
                    <div className="outbound-alert warning">
                      <strong>Allocation Shortages:</strong>
                      <ul className="shortage-list">
                        {allocationResult.shortages.map((s) => (
                          <li key={s.shortageId}>
                            Short {s.shortageQuantity} &mdash; {s.reason}
                            {s.notes ? ` (${s.notes})` : ''}
                          </li>
                        ))}
                      </ul>
                    </div>
                  )}
                </section>
              )}

              {order.statusHistories && order.statusHistories.length > 0 && (
                <section className="detail-section">
                  <h3>Status Audit History</h3>
                  <ul className="history-list">
                    {order.statusHistories.map((h, i) => (
                      <li key={`${h.changedAtUtc}-${i}`}>
                        <span>
                          <strong>{h.fromStatus}</strong> &rarr; <strong>{h.toStatus}</strong>
                        </span>
                        <span className="cell-sub">
                          {h.reason || h.notes || 'Status transition'} &bull;{' '}
                          {new Date(h.changedAtUtc).toLocaleString()}
                        </span>
                      </li>
                    ))}
                  </ul>
                </section>
              )}
            </>
          )}
        </div>
      </div>

      {confirmState && (
        <Modal
          isOpen={Boolean(confirmState)}
          onClose={() => !isConfirming && setConfirmState(null)}
          title={
            confirmState.kind === 'hold'
              ? 'Place Order On Hold'
              : confirmState.kind === 'release'
              ? 'Release Held Order'
              : confirmState.kind === 'cancel'
              ? 'Cancel Outbound Order'
              : confirmState.kind === 'deallocate'
              ? 'Deallocate Inventory Reservations'
              : 'Release Specific Allocation'
          }
          maxWidth="480px"
        >
          <form onSubmit={handleExecuteConfirm} className="outbound-form-stack">
            {confirmError && (
              <div className="outbound-alert error" role="alert">
                {confirmError}
              </div>
            )}

            {confirmState.kind === 'release-allocation' && confirmState.allocation && (
              <div className="form-field">
                <label htmlFor="release-alloc-qty">
                  Quantity to Release (max {confirmState.allocation.quantityAllocated}, blank for full)
                </label>
                <input
                  id="release-alloc-qty"
                  type="number"
                  step="any"
                  min="0.01"
                  max={confirmState.allocation.quantityAllocated}
                  value={confirmQty}
                  onChange={(e) => setConfirmQty(e.target.value)}
                />
              </div>
            )}

            <div className="form-field">
              <label htmlFor="confirm-action-reason">
                {confirmState.kind === 'release' ? 'Release Notes (Optional)' : 'Operational Reason *'}
              </label>
              <input
                id="confirm-action-reason"
                type="text"
                value={confirmReason}
                onChange={(e) => setConfirmReason(e.target.value)}
                placeholder={
                  confirmState.kind === 'release'
                    ? 'Customer credit cleared...'
                    : 'Provide audit reason...'
                }
                required={confirmState.kind !== 'release'}
              />
            </div>

            <div className="modal-inline-actions">
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => setConfirmState(null)}
                disabled={isConfirming}
              >
                Back
              </button>
              <button
                type="submit"
                className="btn btn-primary"
                disabled={isConfirming}
                data-testid="confirm-order-action"
              >
                {isConfirming ? 'Processing...' : 'Confirm'}
              </button>
            </div>
          </form>
        </Modal>
      )}
    </div>
  );
};
