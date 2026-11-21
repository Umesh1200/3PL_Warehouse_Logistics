import React, { useContext } from 'react';
import { EntitySchema, ListColumnDefinition, ActionDefinition } from '../../types/schema';
import { AppIcon } from '../common/AppIcon';
import { StatusBadge } from '../common/StatusBadge';
import { AuthContext } from '../../context/AuthContext';

export interface DynamicListProps {
  schema: EntitySchema;
  items: any[];
  totalCount: number;
  page: number;
  pageSize: number;
  loading: boolean;
  error?: string | null;
  searchTerm?: string;
  sortBy?: string;
  sortDirection?: 'asc' | 'desc';
  onSearchChange: (term: string) => void;
  onSortChange: (field: string) => void;
  onPageChange: (newPage: number) => void;
  onRefresh: () => void;
  onAction: (actionId: string, item?: any) => void;
}

export const DynamicList: React.FC<DynamicListProps> = ({
  schema,
  items,
  totalCount,
  page,
  pageSize,
  loading,
  error,
  searchTerm = '',
  sortBy,
  sortDirection = 'asc',
  onSearchChange,
  onSortChange,
  onPageChange,
  onRefresh,
  onAction,
}) => {
  const auth = useContext(AuthContext);
  const isSuperAdmin = auth?.user?.roles?.some((r) => r.toLowerCase() === 'superadmin') ?? true;

  const checkPermission = (perm?: string): boolean => {
    if (!perm) return true;
    if (!auth) return true; // Standalone renderer allows all permitted schema actions
    if (isSuperAdmin) return true;
    return auth.hasPermission(perm);
  };

  const columns = schema.views.list.columns;
  const fieldMap = new Map(schema.fields.map((f) => [f.name, f]));

  const headerActions = schema.actions.filter(
    (a) => a.target === 'header' && checkPermission(a.permission)
  );

  const rowActions = schema.actions.filter(
    (a) => a.target === 'row' && checkPermission(a.permission)
  );

  const totalPages = Math.ceil(totalCount / pageSize) || 1;

  const renderCellContent = (item: any, col: ListColumnDefinition) => {
    const rawVal = item[col.field];
    const fieldDef = fieldMap.get(col.field);

    if (rawVal === undefined || rawVal === null || rawVal === '') {
      return <span className="cell-muted">—</span>;
    }

    const format = col.format || fieldDef?.type || 'text';

    switch (format) {
      case 'code':
        return <span className="code-badge">{String(rawVal)}</span>;

      case 'badge': {
        // Look up option in fieldDef
        const option = fieldDef?.options?.find(
          (opt) => String(opt.value) === String(rawVal)
        );
        const label = option?.label || String(rawVal);
        const variant = option?.variant || 'secondary';
        const icon = option?.icon;
        return <StatusBadge label={label} variant={variant} icon={icon} />;
      }

      case 'currency':
        return <span className="currency-pill">{String(rawVal)}</span>;

      case 'date':
        try {
          return <span>{new Date(rawVal).toLocaleDateString()}</span>;
        } catch {
          return <span>{String(rawVal)}</span>;
        }

      case 'datetime':
        try {
          return <span>{new Date(rawVal).toLocaleString()}</span>;
        } catch {
          return <span>{String(rawVal)}</span>;
        }

      default:
        return <span>{String(rawVal)}</span>;
    }
  };

  return (
    <div className="dynamic-list-container" role="region" aria-label={`${schema.pluralTitle} Directory`}>
      {/* TOOLBAR */}
      <div className="list-toolbar card">
        <div className="toolbar-left">
          <div className="search-box">
            <AppIcon name="Search" size="sm" color="muted" className="search-icon" />
            <input
              type="text"
              className="search-input"
              placeholder={`Search ${schema.pluralTitle.toLowerCase()}...`}
              value={searchTerm}
              onChange={(e) => onSearchChange(e.target.value)}
              aria-label={`Search ${schema.pluralTitle}`}
              data-testid="search-input"
            />
            {searchTerm && (
              <button
                type="button"
                className="clear-search-btn"
                onClick={() => onSearchChange('')}
                aria-label="Clear search"
              >
                <AppIcon name="X" size="xs" />
              </button>
            )}
          </div>
        </div>

        <div className="toolbar-right">
          <button
            type="button"
            className="btn btn-secondary btn-icon"
            onClick={onRefresh}
            title="Refresh list"
            aria-label="Refresh list"
            data-testid="refresh-btn"
          >
            <AppIcon name="RefreshCw" size="sm" />
            <span className="btn-text">Refresh</span>
          </button>

          {headerActions.map((action) => (
            <button
              key={action.id}
              type="button"
              className={`btn btn-${action.variant || 'primary'}`}
              onClick={() => onAction(action.id)}
              data-testid={`action-${action.id}`}
            >
              {action.icon && <AppIcon name={action.icon} size="sm" />}
              <span>{action.label}</span>
            </button>
          ))}
        </div>
      </div>

      {error && (
        <div className="alert-notice alert-danger" role="alert">
          <AppIcon name="AlertCircle" size="sm" color="danger" />
          <span>{error}</span>
        </div>
      )}

      {/* TABLE DATA GRID */}
      <div className="card table-card">
        {loading ? (
          <div className="table-loading-skeleton" role="status" aria-live="polite">
            <AppIcon name="RefreshCw" size="md" color="primary" className="spin-icon" />
            <span>Loading {schema.pluralTitle.toLowerCase()}...</span>
          </div>
        ) : items.length === 0 ? (
          <div className="empty-state-card" data-testid="empty-state">
            <div className="empty-icon-wrap">
              <AppIcon
                name={schema.views.list.emptyState?.icon || schema.icon || 'Boxes'}
                size="xl"
                color="muted"
              />
            </div>
            <h3 className="empty-title">
              {schema.views.list.emptyState?.title || `No ${schema.pluralTitle} Found`}
            </h3>
            <p className="empty-desc">
              {schema.views.list.emptyState?.description ||
                `There are no ${schema.pluralTitle.toLowerCase()} recorded in this partition.`}
            </p>
            {headerActions.length > 0 && schema.views.list.emptyState?.actionLabel && (
              <button
                type="button"
                className="btn btn-primary"
                onClick={() => onAction(headerActions[0].id)}
              >
                <AppIcon name={headerActions[0].icon || 'Plus'} size="sm" />
                <span>{schema.views.list.emptyState.actionLabel}</span>
              </button>
            )}
          </div>
        ) : (
          <div className="table-responsive">
            <table className="enterprise-data-table" data-testid="entity-table">
              <thead>
                <tr>
                  {columns.map((col) => {
                    const fieldDef = fieldMap.get(col.field);
                    const label = col.label || fieldDef?.label || col.field;
                    const isSorted = sortBy === col.field;
                    const alignClass = col.align ? `text-${col.align}` : '';

                    return (
                      <th
                        key={col.field}
                        style={{ width: col.width }}
                        className={`${col.sortable ? 'sortable-header' : ''} ${alignClass}`}
                        onClick={() => col.sortable && onSortChange(col.field)}
                        aria-sort={
                          isSorted ? (sortDirection === 'asc' ? 'ascending' : 'descending') : undefined
                        }
                      >
                        <div className={`th-content ${alignClass}`}>
                          <span>{label}</span>
                          {col.sortable && (
                            <span className="sort-icon-wrap">
                              {isSorted ? (
                                <AppIcon
                                  name={sortDirection === 'asc' ? 'ChevronUp' : 'ChevronDown'}
                                  size="xs"
                                  color="primary"
                                />
                              ) : (
                                <AppIcon name="ArrowUpDown" size="xs" color="muted" />
                              )}
                            </span>
                          )}
                        </div>
                      </th>
                    );
                  })}
                  {rowActions.length > 0 && <th className="text-right">Actions</th>}
                </tr>
              </thead>
              <tbody>
                {items.map((item) => {
                  const keyVal = item[schema.primaryKey] || Math.random();

                  return (
                    <tr key={keyVal} data-testid={`row-${keyVal}`}>
                      {columns.map((col) => {
                        const alignClass = col.align ? `text-${col.align}` : '';
                        return (
                          <td key={col.field} className={alignClass}>
                            {renderCellContent(item, col)}
                          </td>
                        );
                      })}
                      {rowActions.length > 0 && (
                        <td className="text-right">
                          <div className="row-actions-group">
                            {rowActions.map((action) => (
                              <button
                                key={action.id}
                                type="button"
                                className={`btn-row-action btn-row-${action.variant || 'secondary'}`}
                                onClick={() => onAction(action.id, item)}
                                title={action.label}
                                aria-label={`${action.label} ${item[schema.primaryKey]}`}
                                data-testid={`row-action-${action.id}-${keyVal}`}
                              >
                                {action.icon && <AppIcon name={action.icon} size="xs" />}
                                <span className="row-action-text">{action.label}</span>
                              </button>
                            ))}
                          </div>
                        </td>
                      )}
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}

        {/* PAGINATION FOOTER */}
        {!loading && items.length > 0 && (
          <div className="table-pagination">
            <span className="pagination-info">
              Showing <strong>{(page - 1) * pageSize + 1}</strong> &ndash;{' '}
              <strong>{Math.min(page * pageSize, totalCount)}</strong> of{' '}
              <strong>{totalCount}</strong> records
            </span>

            <div className="pagination-controls">
              <button
                type="button"
                className="btn-page-nav"
                disabled={page <= 1}
                onClick={() => onPageChange(page - 1)}
                aria-label="Previous page"
              >
                <AppIcon name="ChevronLeft" size="sm" />
                <span>Prev</span>
              </button>
              <span className="page-indicator">
                Page {page} of {totalPages}
              </span>
              <button
                type="button"
                className="btn-page-nav"
                disabled={page >= totalPages}
                onClick={() => onPageChange(page + 1)}
                aria-label="Next page"
              >
                <span>Next</span>
                <AppIcon name="ChevronRight" size="sm" />
              </button>
            </div>
          </div>
        )}
      </div>

      <style>{`
        .dynamic-list-container {
          display: flex;
          flex-direction: column;
          gap: 16px;
        }
        .list-toolbar {
          display: flex;
          align-items: center;
          justify-content: space-between;
          padding: 16px 20px;
          gap: 16px;
          flex-wrap: wrap;
        }
        .toolbar-left {
          flex: 1;
          min-width: 240px;
        }
        .toolbar-right {
          display: flex;
          align-items: center;
          gap: 10px;
        }
        .search-box {
          position: relative;
          display: flex;
          align-items: center;
          width: 100%;
          max-width: 360px;
        }
        .search-icon {
          position: absolute;
          left: 12px;
          pointer-events: none;
        }
        .search-input {
          width: 100%;
          padding: 9px 34px;
          border-radius: var(--radius-sm);
          border: 1px solid var(--border-actionable);
          font-family: inherit;
          font-size: 13.5px;
          color: var(--text-main);
          background: #ffffff;
          transition: border-color var(--transition-fast), box-shadow var(--transition-fast);
        }
        .search-input:focus {
          outline: none;
          border-color: var(--brand-primary);
          box-shadow: 0 0 0 3px rgba(82, 84, 230, 0.15);
        }
        .clear-search-btn {
          position: absolute;
          right: 10px;
          color: var(--text-muted);
          padding: 2px;
        }
        .table-card {
          padding: 0;
          overflow: hidden;
        }
        .table-responsive {
          width: 100%;
          overflow-x: auto;
        }
        .enterprise-data-table {
          width: 100%;
          border-collapse: collapse;
          text-align: left;
          font-size: 13.5px;
        }
        .enterprise-data-table th {
          background: #f8fafc;
          padding: 12px 18px;
          font-size: 11.5px;
          font-weight: 700;
          color: var(--text-muted);
          text-transform: uppercase;
          letter-spacing: 0.05em;
          border-bottom: 1px solid var(--border-subtle);
          user-select: none;
        }
        .sortable-header {
          cursor: pointer;
          transition: background var(--transition-fast);
        }
        .sortable-header:hover {
          background: #f1f5f9;
        }
        .th-content {
          display: inline-flex;
          align-items: center;
          gap: 6px;
        }
        .sort-icon-wrap {
          display: inline-flex;
        }
        .enterprise-data-table td {
          padding: 14px 18px;
          border-bottom: 1px solid var(--border-subtle);
          color: var(--text-main);
          vertical-align: middle;
        }
        .enterprise-data-table tbody tr:hover {
          background: #fafbfc;
        }
        .text-center { text-align: center; }
        .text-right { text-align: right; }
        .code-badge {
          display: inline-block;
          font-family: 'JetBrains Mono', 'SF Mono', Consolas, monospace;
          font-weight: 700;
          font-size: 12.5px;
          padding: 2px 7px;
          background: #f1f5f9;
          border-radius: var(--radius-xs);
          color: var(--brand-navy-900);
          border: 1px solid #e2e8f0;
        }
        .currency-pill {
          display: inline-block;
          font-family: monospace;
          font-weight: 700;
          font-size: 12px;
          padding: 2px 8px;
          background: #eef2ff;
          color: #4338ca;
          border-radius: var(--radius-xs);
        }
        .cell-muted {
          color: var(--text-muted);
        }
        .row-actions-group {
          display: flex;
          align-items: center;
          justify-content: flex-end;
          gap: 6px;
        }
        .btn-row-action {
          display: inline-flex;
          align-items: center;
          gap: 4px;
          padding: 5px 10px;
          border-radius: var(--radius-xs);
          font-size: 12px;
          font-weight: 600;
          border: 1px solid transparent;
        }
        .btn-row-secondary {
          background: #ffffff;
          border-color: #cbd5e1;
          color: var(--text-main);
        }
        .btn-row-secondary:hover {
          background: #f1f5f9;
          border-color: var(--brand-primary);
        }
        .btn-row-danger {
          background: #fffafa;
          border-color: #fca5a5;
          color: var(--status-danger-text);
        }
        .btn-row-danger:hover {
          background: var(--status-danger-bg);
          border-color: var(--status-danger-border);
        }
        .table-loading-skeleton {
          display: flex;
          align-items: center;
          justify-content: center;
          gap: 12px;
          padding: 48px 24px;
          color: var(--text-muted);
          font-size: 14px;
        }
        .spin-icon {
          animation: spin 1s linear infinite;
        }
        @keyframes spin {
          from { transform: rotate(0deg); }
          to { transform: rotate(360deg); }
        }
        .empty-state-card {
          display: flex;
          flex-direction: column;
          align-items: center;
          justify-content: center;
          padding: 56px 24px;
          text-align: center;
        }
        .empty-icon-wrap {
          width: 64px;
          height: 64px;
          border-radius: var(--radius-full);
          background: #f1f5f9;
          display: flex;
          align-items: center;
          justify-content: center;
          margin-bottom: 16px;
        }
        .empty-title {
          font-size: 17px;
          font-weight: 750;
          color: var(--text-main);
          margin-bottom: 6px;
        }
        .empty-desc {
          font-size: 13.5px;
          color: var(--text-muted);
          max-width: 440px;
          margin-bottom: 20px;
          line-height: 1.5;
        }
        .table-pagination {
          display: flex;
          align-items: center;
          justify-content: space-between;
          padding: 14px 20px;
          background: #f8fafc;
          border-top: 1px solid var(--border-subtle);
          font-size: 13px;
          color: var(--text-muted);
        }
        .pagination-controls {
          display: flex;
          align-items: center;
          gap: 12px;
        }
        .btn-page-nav {
          display: inline-flex;
          align-items: center;
          gap: 4px;
          padding: 5px 10px;
          background: #ffffff;
          border: 1px solid #cbd5e1;
          border-radius: var(--radius-xs);
          color: var(--text-main);
          font-size: 12.5px;
          font-weight: 600;
        }
        .btn-page-nav:disabled {
          opacity: 0.5;
          cursor: not-allowed;
        }
        .btn-page-nav:not(:disabled):hover {
          background: #f1f5f9;
          border-color: var(--brand-primary);
        }
        .page-indicator {
          font-weight: 600;
        }
        @media (max-width: 640px) {
          .row-action-text {
            display: none;
          }
          .table-pagination {
            flex-direction: column;
            gap: 12px;
          }
        }
      `}</style>
    </div>
  );
};
