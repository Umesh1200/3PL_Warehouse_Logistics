import React, { useCallback, useEffect, useRef, useState } from 'react';
import { AppIcon } from '../../components/common/AppIcon';
import { BulkOrderImportModal } from '../../components/outbound/BulkOrderImportModal';
import { OrderCreateModal } from '../../components/outbound/OrderCreateModal';
import { OrderDetailDrawer } from '../../components/outbound/OrderDetailDrawer';
import {
  OrderStatusBadge,
  computeOrderAllocationProgress,
  formatOrderTypeLabel,
} from '../../components/outbound/OutboundStatusBadge';
import { useAuth } from '../../context/AuthContext';
import { useFacility } from '../../context/FacilityContext';
import { useRouter } from '../../context/RouterContext';
import { outboundApi } from '../../services/outboundApi';
import {
  GetOutboundOrdersParams,
  OutboundOrderDto,
  OutboundOrderStatus,
  OutboundOrderType,
} from '../../types/outbound';
import '../../styles/outbound.css';

export const OutboundOrdersPage: React.FC = () => {
  const { activeFacility, facilities } = useFacility();
  const { hasPermission, hasAnyPermission, isStaff, isClient, isSuperAdmin } = useAuth();
  const { navigate } = useRouter();

  const isAllowedIdentity = isStaff || isClient;
  const canAccessModule =
    isAllowedIdentity &&
    (isSuperAdmin || hasAnyPermission(['outbound:orders:view', 'outbound:orders:create']));

  const canView = isAllowedIdentity && hasPermission('outbound:orders:view');
  const canCreate = isAllowedIdentity && hasPermission('outbound:orders:create');
  const canImport = isAllowedIdentity && hasPermission('outbound:orders:import');
  const canAllocate = isStaff && hasPermission('outbound:orders:allocate');
  const canManage = isStaff && hasPermission('outbound:orders:manage');
  const canCancel = isAllowedIdentity && hasPermission('outbound:orders:cancel');
  const canLookupClients = isStaff && hasPermission('clients:view');
  const canLookupItems = hasPermission('items:view');

  const [orders, setOrders] = useState<OutboundOrderDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const pageSize = 15;
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<string | null>(null);

  // Server-side filters
  const [searchTerm, setSearchTerm] = useState('');
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [typeFilter, setTypeFilter] = useState<string>('all');
  const [priorityFilter, setPriorityFilter] = useState<string>('');
  const [fromDate, setFromDate] = useState<string>('');
  const [toDate, setToDate] = useState<string>('');

  const [selectedOrderId, setSelectedOrderId] = useState<string | null>(null);
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [showImportModal, setShowImportModal] = useState(false);

  // Reset pagination and close detail drawer only when switching between distinct facilities
  const prevFacilityIdRef = useRef<string | undefined>(undefined);
  useEffect(() => {
    const nextId = activeFacility?.id;
    if (prevFacilityIdRef.current && nextId && prevFacilityIdRef.current !== nextId) {
      setPage(1);
      setSelectedOrderId(null);
    }
    prevFacilityIdRef.current = nextId;
  }, [activeFacility?.id]);

  const loadOrders = useCallback(
    async (signal?: AbortSignal) => {
      if (!canView) return;
      setLoading(true);
      setError(null);
      try {
        const params: GetOutboundOrdersParams = {
          facilityId: activeFacility?.id,
          status: statusFilter !== 'all' ? (statusFilter as OutboundOrderStatus) : undefined,
          orderType: typeFilter !== 'all' ? (typeFilter as OutboundOrderType) : undefined,
          priority: priorityFilter.trim() ? Number(priorityFilter) : undefined,
          searchTerm: searchTerm.trim() || undefined,
          fromDateUtc: fromDate ? new Date(`${fromDate}T00:00:00Z`).toISOString() : undefined,
          toDateUtc: toDate ? new Date(`${toDate}T23:59:59Z`).toISOString() : undefined,
          pageNumber: page,
          pageSize,
        };
        const res = await outboundApi.getOrders(params, signal);
        setOrders(res.items);
        setTotalCount(res.totalCount);
      } catch (err: any) {
        if (err.name !== 'AbortError') {
          setError(err.message || 'Failed to load outbound orders.');
        }
      } finally {
        setLoading(false);
      }
    },
    [canView, activeFacility?.id, statusFilter, typeFilter, priorityFilter, searchTerm, fromDate, toDate, page]
  );

  useEffect(() => {
    if (!canView) return;
    const ctrl = new AbortController();
    loadOrders(ctrl.signal);
    return () => ctrl.abort();
  }, [canView, loadOrders]);

  if (!canAccessModule) {
    return (
      <div className="forbidden-card" role="alert" data-testid="outbound-orders-forbidden">
        <div className="badge badge-danger" style={{ marginBottom: 12 }}>
          403 FORBIDDEN
        </div>
        <h2>Outbound Orders Access Denied</h2>
        <p style={{ color: 'var(--text-muted)', margin: '10px 0 20px' }}>
          Your account identity or permission grants do not authorize outbound order operations (
          <code>outbound:orders:view</code> / <code>outbound:orders:create</code>).
        </p>
        <button type="button" className="btn btn-primary" onClick={() => navigate('/app/dashboard')}>
          Return to Dashboard
        </button>
      </div>
    );
  }

  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));

  return (
    <div className="outbound-page" role="region" aria-label="Outbound Orders Management">
      <div className="outbound-header">
        <div>
          <p className="outbound-subtitle">
            {activeFacility ? `${activeFacility.name} (${activeFacility.facilityCode})` : 'All Authorized Facilities'}
            {' '}&bull; Total Matching Orders: <strong data-testid="orders-total-count">{totalCount}</strong>
          </p>
        </div>
        <div className="outbound-header-actions">
          {canView && (
            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => loadOrders()}
              disabled={loading}
            >
              <AppIcon name="RefreshCw" size="sm" />
              <span>Refresh</span>
            </button>
          )}
          {canImport && (
            <button
              type="button"
              className="btn btn-outline"
              onClick={() => setShowImportModal(true)}
              data-testid="open-bulk-import-btn"
            >
              <AppIcon name="Upload" size="sm" />
              <span>Bulk Import</span>
            </button>
          )}
          {canCreate && (
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => setShowCreateModal(true)}
              data-testid="open-create-order-btn"
            >
              <AppIcon name="Plus" size="sm" />
              <span>Create Order</span>
            </button>
          )}
        </div>
      </div>

      {feedback && (
        <div className="outbound-alert success" role="status">
          <span>{feedback}</span>
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

      {canView ? (
        <>
          <div className="card outbound-filters-card">
            <div className="form-field">
              <label htmlFor="orders-search">Search Order / Ref / Customer</label>
              <input
                id="orders-search"
                type="search"
                value={searchTerm}
                onChange={(e) => {
                  setSearchTerm(e.target.value);
                  setPage(1);
                }}
                placeholder="SO-..., EXT-..., customer"
              />
            </div>

            <div className="form-field">
              <label htmlFor="orders-status-filter">Status</label>
              <select
                id="orders-status-filter"
                value={statusFilter}
                onChange={(e) => {
                  setStatusFilter(e.target.value);
                  setPage(1);
                }}
              >
                <option value="all">All Statuses</option>
                {Object.values(OutboundOrderStatus).map((st) => (
                  <option key={st} value={st}>
                    {st}
                  </option>
                ))}
              </select>
            </div>

            <div className="form-field">
              <label htmlFor="orders-type-filter">Order Type</label>
              <select
                id="orders-type-filter"
                value={typeFilter}
                onChange={(e) => {
                  setTypeFilter(e.target.value);
                  setPage(1);
                }}
              >
                <option value="all">All Types</option>
                {Object.values(OutboundOrderType).map((ot) => (
                  <option key={ot} value={ot}>
                    {formatOrderTypeLabel(ot)}
                  </option>
                ))}
              </select>
            </div>

            <div className="form-field">
              <label htmlFor="orders-priority-filter">Priority</label>
              <input
                id="orders-priority-filter"
                type="number"
                min={1}
                max={10}
                value={priorityFilter}
                onChange={(e) => {
                  setPriorityFilter(e.target.value);
                  setPage(1);
                }}
                placeholder="Any"
              />
            </div>

            <div className="form-field">
              <label htmlFor="orders-from-date">From Date</label>
              <input
                id="orders-from-date"
                type="date"
                value={fromDate}
                onChange={(e) => {
                  setFromDate(e.target.value);
                  setPage(1);
                }}
              />
            </div>

            <div className="form-field">
              <label htmlFor="orders-to-date">To Date</label>
              <input
                id="orders-to-date"
                type="date"
                value={toDate}
                onChange={(e) => {
                  setToDate(e.target.value);
                  setPage(1);
                }}
              />
            </div>

            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => {
                setSearchTerm('');
                setStatusFilter('all');
                setTypeFilter('all');
                setPriorityFilter('');
                setFromDate('');
                setToDate('');
                setPage(1);
              }}
            >
              Reset Filters
            </button>
          </div>

          {error && (
            <div className="outbound-alert error" role="alert" data-testid="orders-error-banner">
              <span>{error}</span>
              <button type="button" className="btn btn-secondary btn-sm" onClick={() => loadOrders()}>
                Retry
              </button>
            </div>
          )}

          <div className="card">
            {loading && orders.length === 0 ? (
              <div className="outbound-empty-state" data-testid="orders-loading">
                Loading outbound orders...
              </div>
            ) : orders.length === 0 ? (
              <div className="outbound-empty-state" data-testid="orders-empty">
                No outbound orders match the selected filters in{' '}
                {activeFacility?.name || 'your authorized facilities'}.
              </div>
            ) : (
              <div className="outbound-table-wrap">
                <table className="outbound-table" data-testid="outbound-orders-table">
                  <thead>
                    <tr>
                      <th>Order / Ext Ref</th>
                      <th>Customer</th>
                      <th>Type</th>
                      <th>Status</th>
                      <th>Priority</th>
                      <th>Service Level</th>
                      <th>Required Ship</th>
                      <th>Facility</th>
                      <th>Allocation Progress</th>
                      <th>Action</th>
                    </tr>
                  </thead>
                  <tbody>
                    {orders.map((order) => {
                      const prog = computeOrderAllocationProgress(order);
                      return (
                        <tr key={order.id} data-testid={`order-row-${order.id}`}>
                          <td>
                            <div className="cell-strong">{order.orderNumber}</div>
                            <div className="cell-sub">{order.externalOrderReference || 'No external ref'}</div>
                          </td>
                          <td>{order.customerName}</td>
                          <td>{formatOrderTypeLabel(order.orderType)}</td>
                          <td>
                            <OrderStatusBadge status={order.status} />
                          </td>
                          <td>P{order.priority}</td>
                          <td>{order.serviceLevel}</td>
                          <td>
                            {order.requiredShipDateUtc
                              ? new Date(order.requiredShipDateUtc).toLocaleDateString()
                              : '—'}
                          </td>
                          <td>
                            <div>{order.facilityName || order.facilityId}</div>
                            {order.destinationFacilityName && (
                              <div className="cell-sub">&rarr; {order.destinationFacilityName}</div>
                            )}
                          </td>
                          <td>
                            <div className="progress-inline">
                              <div className="progress-track" aria-hidden="true">
                                <div className="progress-fill" style={{ width: `${prog.percent}%` }} />
                              </div>
                              <span>
                                {prog.allocated}/{prog.ordered} ({prog.percent}%)
                              </span>
                            </div>
                          </td>
                          <td>
                            <button
                              type="button"
                              className="btn btn-outline btn-sm"
                              onClick={() => setSelectedOrderId(order.id)}
                              data-testid={`inspect-order-${order.id}`}
                            >
                              Inspect
                            </button>
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
                Page {page} of {totalPages} ({totalCount} total order{totalCount === 1 ? '' : 's'})
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
          Order listing requires <code>outbound:orders:view</code>. Use Create Order above to submit new
          orders.
        </div>
      )}

      <OrderCreateModal
        isOpen={showCreateModal}
        onClose={() => setShowCreateModal(false)}
        onCreated={(created) => {
          setFeedback(`Created outbound order ${created.orderNumber}.`);
          setSelectedOrderId(created.id);
          loadOrders();
        }}
        activeFacility={activeFacility}
        facilities={facilities}
        isStaff={isStaff}
        canLookupClients={canLookupClients}
        canLookupItems={canLookupItems}
      />

      <BulkOrderImportModal
        isOpen={showImportModal}
        onClose={() => setShowImportModal(false)}
        onImported={(batch) => {
          setFeedback(
            `Bulk batch ${batch.batchNumber}: ${batch.successfulRows} imported, ${batch.duplicateRows} duplicates, ${batch.failedRows} failed.`
          );
          loadOrders();
        }}
        activeFacility={activeFacility}
        isStaff={isStaff}
        canLookupClients={canLookupClients}
      />

      <OrderDetailDrawer
        orderId={selectedOrderId}
        onClose={() => setSelectedOrderId(null)}
        onOrderMutated={() => loadOrders()}
        activeFacility={activeFacility}
        isStaff={isStaff}
        isClient={isClient}
        isSuperAdmin={isSuperAdmin}
        canAllocate={canAllocate}
        canManage={canManage}
        canCancel={canCancel}
      />
    </div>
  );
};
