import React, { useState, useEffect, useCallback, useContext } from 'react';
import { EntitySchema } from '../../types/schema';
import { schemaService } from '../../services/schemaService';
import { DynamicList } from './DynamicList';
import { DynamicForm } from './DynamicForm';
import { Modal } from '../common/Modal';
import { AppIcon } from '../common/AppIcon';
import { adminApi } from '../../services/adminApi';
import { AuthContext } from '../../context/AuthContext';
import { normalizeClientStatus, normalizeSlaTier } from '../../types/auth';

export interface DynamicEntityPageProps {
  entityName: string;
  initialSchema?: EntitySchema;
  customFetchList?: (params: { pageNumber: number; pageSize: number; searchTerm?: string; sortBy?: string; sortDescending?: boolean }, signal?: AbortSignal) => Promise<{ items: any[]; totalCount: number }>;
  customCreate?: (data: any) => Promise<any>;
  customUpdate?: (id: string, data: any) => Promise<any>;
  customDelete?: (id: string) => Promise<any>;
}

export const DynamicEntityPage: React.FC<DynamicEntityPageProps> = ({
  entityName,
  initialSchema,
  customFetchList,
  customCreate,
  customUpdate,
  customDelete,
}) => {
  const auth = useContext(AuthContext);
  const user = auth?.user;

  // In production runtime, fail closed: never pre-seed schema with unrestricted local fallback
  const [schema, setSchema] = useState<EntitySchema | null>(initialSchema || null);
  const [loadingSchema, setLoadingSchema] = useState(!initialSchema);
  const [schemaError, setSchemaError] = useState<string | null>(null);

  // List data state
  const [items, setItems] = useState<any[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const pageSize = 15;
  const [loadingData, setLoadingData] = useState(true);
  const [dataError, setDataError] = useState<string | null>(null);

  // Filters & sorting
  const [searchTerm, setSearchTerm] = useState('');
  const [sortBy, setSortBy] = useState<string | undefined>();
  const [sortDirection, setSortDirection] = useState<'asc' | 'desc'>('asc');

  // Modal states
  const [isFormModalOpen, setIsFormModalOpen] = useState(false);
  const [editingItem, setEditingItem] = useState<any | null>(null);
  const [loadingDetail, setLoadingDetail] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  // Deactivate confirmation modal
  const [deactivatingItem, setDeactivatingItem] = useState<any | null>(null);
  const [isDeactivating, setIsDeactivating] = useState(false);

  // Feedback banner
  const [feedback, setFeedback] = useState<{ type: 'success' | 'error'; message: string } | null>(null);

  // 1. Fetch Authoritative Schema from Server
  useEffect(() => {
    if (initialSchema) {
      setSchema(initialSchema);
      setLoadingSchema(false);
      if (initialSchema.views?.list?.defaultSort) {
        setSortBy((prev) => prev || initialSchema.views.list.defaultSort!.field);
        setSortDirection((prev) => prev || initialSchema.views.list.defaultSort!.direction);
      }
      return;
    }

    let active = true;
    const ctrl = new AbortController();

    async function loadSchema() {
      try {
        setLoadingSchema(true);
        setSchemaError(null);
        const res = await schemaService.getSchema(
          entityName,
          { userId: user?.userId, roles: user?.roles, permissions: user?.permissions },
          ctrl.signal
        );
        if (active) {
          // Unconditionally replace with server's permission-resolved response
          setSchema(res);
          if (res.views?.list?.defaultSort) {
            setSortBy((prev) => prev || res.views.list.defaultSort!.field);
            setSortDirection((prev) => prev || res.views.list.defaultSort!.direction);
          }
        }
      } catch (err: any) {
        if (active && err.name !== 'AbortError') {
          // Fail closed on error: remove schema to prevent displaying unauthorized views
          setSchema(null);
          setSchemaError(err.message || `Failed to load authoritative schema for ${entityName}`);
        }
      } finally {
        if (active) setLoadingSchema(false);
      }
    }

    loadSchema();
    return () => {
      active = false;
      ctrl.abort();
    };
  }, [entityName, initialSchema, user?.userId, user?.roles, user?.permissions]);

  // 2. Fetch Entity Data via Server-side Search, Sort, and Pagination
  const loadData = useCallback(
    async (signal?: AbortSignal) => {
      if (!schema) return;
      try {
        setLoadingData(true);
        setDataError(null);

        let result: { items: any[]; totalCount: number };

        if (customFetchList) {
          result = await customFetchList(
            {
              pageNumber: page,
              pageSize,
              searchTerm: searchTerm.trim() || undefined,
              sortBy,
              sortDescending: sortDirection === 'desc',
            },
            signal
          );
        } else if (entityName.toLowerCase() === 'client') {
          const res = await adminApi.getClients(
            {
              pageNumber: page,
              pageSize,
              searchTerm: searchTerm.trim() || undefined,
              sortBy,
              sortDescending: sortDirection === 'desc',
            },
            signal
          );
          result = { items: res.items || [], totalCount: res.totalCount ?? (res.items?.length || 0) };
        } else {
          throw new Error(`List operation is not supported for entity '${entityName}'.`);
        }

        setItems(result.items);
        setTotalCount(result.totalCount);
      } catch (err: any) {
        if (err.name !== 'AbortError') {
          setDataError(err.message || `Failed to load ${entityName} records.`);
        }
      } finally {
        setLoadingData(false);
      }
    },
    [schema, entityName, page, searchTerm, sortBy, sortDirection, customFetchList]
  );

  useEffect(() => {
    if (!schema) return;
    const ctrl = new AbortController();
    loadData(ctrl.signal);
    return () => ctrl.abort();
  }, [schema, loadData]);

  // Filter & Sort Handlers: Reset to page 1 on filter/sort change
  const handleSearchChange = (term: string) => {
    setSearchTerm(term);
    setPage(1);
  };

  const handleSortChange = (field: string) => {
    if (sortBy === field) {
      setSortDirection((prev) => (prev === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortBy(field);
      setSortDirection('asc');
    }
    setPage(1);
  };

  // Action Dispatcher: Loads authoritative detail before opening edit modal
  const handleAction = async (actionId: string, item?: any) => {
    if (actionId === 'create') {
      setEditingItem(null);
      setFormError(null);
      setIsFormModalOpen(true);
    } else if (actionId === 'edit' && item) {
      if (!schema) return;
      setFormError(null);
      const primaryKeyVal = item[schema.primaryKey];
      try {
        setLoadingDetail(true);
        let detailData = item;
        if (entityName.toLowerCase() === 'client') {
          detailData = await adminApi.getClientById(primaryKeyVal);
          // Canonical enum normalization so select elements match schema options
          detailData = {
            ...detailData,
            slaTier: normalizeSlaTier(detailData.slaTier),
            status: normalizeClientStatus(detailData.status),
          };
        }
        setEditingItem(detailData);
        setIsFormModalOpen(true);
      } catch (err: any) {
        setFeedback({
          type: 'error',
          message: err.message || `Failed to load authoritative details for ${schema.title} '${item.companyName || primaryKeyVal}'.`,
        });
      } finally {
        setLoadingDetail(false);
      }
    } else if (actionId === 'deactivate' && item) {
      setDeactivatingItem(item);
    }
  };

  // Form Submit Handler: Builds explicit projection of writable supported fields
  const handleFormSubmit = async (formData: Record<string, any>) => {
    if (!schema) return;
    try {
      setIsSubmitting(true);
      setFormError(null);

      if (editingItem) {
        // Update projection: permitted writable fields only (no accountCode or contacts)
        const primaryKeyVal = editingItem[schema.primaryKey];
        if (customUpdate) {
          await customUpdate(primaryKeyVal, formData);
        } else if (entityName.toLowerCase() === 'client') {
          const updatePayload = {
            id: primaryKeyVal,
            companyName: formData.companyName?.trim(),
            slaTier: normalizeSlaTier(formData.slaTier),
            currency: (formData.currency?.trim() || 'USD').toUpperCase(),
            status: normalizeClientStatus(formData.status),
          };
          await adminApi.updateClient(primaryKeyVal, updatePayload);
        } else {
          throw new Error(`Update operation is not supported for entity '${entityName}'.`);
        }
        setFeedback({
          type: 'success',
          message: `${schema.title} '${formData.companyName || primaryKeyVal}' updated successfully.`,
        });
      } else {
        // Create projection: permitted writable fields
        if (customCreate) {
          await customCreate(formData);
        } else if (entityName.toLowerCase() === 'client') {
          const createPayload = {
            companyName: formData.companyName?.trim(),
            accountCode: formData.accountCode?.trim().toUpperCase(),
            slaTier: normalizeSlaTier(formData.slaTier),
            currency: (formData.currency?.trim() || 'USD').toUpperCase(),
            status: normalizeClientStatus(formData.status),
          };
          await adminApi.createClient(createPayload);
        } else {
          throw new Error(`Create operation is not supported for entity '${entityName}'.`);
        }
        setFeedback({
          type: 'success',
          message: `New ${schema.title} '${formData.companyName || formData.accountCode}' created successfully.`,
        });
      }

      setIsFormModalOpen(false);
      loadData();
    } catch (err: any) {
      setFormError(err.message || `Failed to save ${schema.title}.`);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleConfirmDeactivate = async () => {
    if (!schema || !deactivatingItem) return;
    try {
      setIsDeactivating(true);
      const primaryKeyVal = deactivatingItem[schema.primaryKey];

      if (customDelete) {
        await customDelete(primaryKeyVal);
      } else if (entityName.toLowerCase() === 'client') {
        await adminApi.deactivateClient(primaryKeyVal);
      } else {
        throw new Error(`Deactivate operation is not supported for entity '${entityName}'.`);
      }

      setFeedback({
        type: 'success',
        message: `${schema.title} '${deactivatingItem.companyName || primaryKeyVal}' deactivated successfully.`,
      });
      setDeactivatingItem(null);
      loadData();
    } catch (err: any) {
      setFeedback({
        type: 'error',
        message: err.message || `Failed to deactivate ${schema.title}.`,
      });
    } finally {
      setIsDeactivating(false);
    }
  };

  if (loadingSchema) {
    return (
      <div className="card loading-schema-card" role="status">
        <AppIcon name="RefreshCw" size="md" color="primary" className="spin-icon" />
        <span>Initializing metadata schema for {entityName}...</span>
        <style>{`
          .loading-schema-card {
            display: flex;
            align-items: center;
            justify-content: center;
            gap: 12px;
            padding: 48px;
            color: var(--text-muted);
          }
          .spin-icon { animation: spin 1s linear infinite; }
          @keyframes spin { from { transform: rotate(0deg); } to { transform: rotate(360deg); } }
        `}</style>
      </div>
    );
  }

  if (schemaError || !schema) {
    return (
      <div className="card error-schema-card" role="alert">
        <AppIcon name="AlertTriangle" size="lg" color="danger" />
        <h3>Schema Resolution Error</h3>
        <p>{schemaError || `Unable to load schema definition for '${entityName}'.`}</p>
        <button type="button" className="btn btn-secondary" onClick={() => window.location.reload()}>
          Retry
        </button>
      </div>
    );
  }

  return (
    <div className="dynamic-entity-page">
      {/* PAGE HEADER */}
      <div className="card entity-header-card">
        <div className="card-header">
          <div className="header-title-wrap">
            {schema.icon && (
              <div className="entity-icon-pill">
                <AppIcon name={schema.icon} size="md" color="primary" />
              </div>
            )}
            <div>
              <h2 className="card-title">{schema.pluralTitle} Directory</h2>
              {schema.description && (
                <p className="card-subtitle">{schema.description}</p>
              )}
            </div>
          </div>
        </div>
      </div>

      {feedback && (
        <div
          className={`alert-notice ${feedback.type === 'success' ? 'alert-success' : 'alert-danger'}`}
          role="alert"
          data-testid="feedback-banner"
        >
          <AppIcon
            name={feedback.type === 'success' ? 'CheckCircle2' : 'AlertCircle'}
            size="sm"
            color={feedback.type === 'success' ? 'success' : 'danger'}
          />
          <span style={{ flex: 1 }}>{feedback.message}</span>
          <button
            type="button"
            className="alert-dismiss-btn"
            onClick={() => setFeedback(null)}
            aria-label="Dismiss feedback alert"
          >
            <AppIcon name="X" size="xs" />
          </button>
        </div>
      )}

      {/* DYNAMIC LIST */}
      <DynamicList
        schema={schema}
        items={items}
        totalCount={totalCount}
        page={page}
        pageSize={pageSize}
        loading={loadingData}
        error={dataError}
        searchTerm={searchTerm}
        sortBy={sortBy}
        sortDirection={sortDirection}
        onSearchChange={handleSearchChange}
        onSortChange={handleSortChange}
        onPageChange={setPage}
        onRefresh={() => loadData()}
        onAction={handleAction}
      />

      {/* CREATE / EDIT MODAL */}
      <Modal
        isOpen={isFormModalOpen}
        onClose={() => setIsFormModalOpen(false)}
        title={editingItem ? `Edit ${schema.title}` : `New ${schema.title}`}
        subtitle={
          editingItem
            ? `Modify configuration and SLA attributes for ${editingItem.companyName || editingItem.accountCode}.`
            : `Register a new tenant partition into the enterprise logistics ledger.`
        }
        icon={schema.icon}
        maxWidth="680px"
      >
        <DynamicForm
          schema={schema}
          initialData={editingItem || undefined}
          onSubmit={handleFormSubmit}
          onCancel={() => setIsFormModalOpen(false)}
          isSubmitting={isSubmitting}
          errorMessage={formError}
          mode={editingItem ? 'edit' : 'create'}
        />
      </Modal>

      {/* DEACTIVATE CONFIRMATION MODAL */}
      <Modal
        isOpen={!!deactivatingItem}
        onClose={() => setDeactivatingItem(null)}
        title={`Deactivate ${schema.title}`}
        icon="AlertTriangle"
        maxWidth="500px"
        footer={
          <>
            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => setDeactivatingItem(null)}
              disabled={isDeactivating}
            >
              Cancel
            </button>
            <button
              type="button"
              className="btn btn-danger"
              onClick={handleConfirmDeactivate}
              disabled={isDeactivating}
              data-testid="confirm-deactivate-btn"
            >
              <AppIcon name={isDeactivating ? 'RefreshCw' : 'Trash2'} size="sm" />
              <span>{isDeactivating ? 'Deactivating...' : 'Confirm Deactivation'}</span>
            </button>
          </>
        }
      >
        <div className="deactivate-confirm-body">
          <p>
            Are you sure you want to deactivate{' '}
            <strong>{deactivatingItem?.companyName || deactivatingItem?.accountCode}</strong>?
          </p>
          <p className="deactivate-warning-text">
            Deactivating a client tenant immediately halts inbound receiving sessions, suspends directed putaway, and prevents active fulfillment orders for this tenant partition.
          </p>
        </div>
      </Modal>

      <style>{`
        .dynamic-entity-page {
          display: flex;
          flex-direction: column;
          gap: 16px;
        }
        .entity-header-card {
          margin-bottom: 16px;
        }
        .header-title-wrap {
          display: flex;
          align-items: center;
          gap: 12px;
        }
        .entity-icon-pill {
          display: flex;
          align-items: center;
          justify-content: center;
          width: 38px;
          height: 38px;
          border-radius: var(--radius-sm);
          background: #eff6ff;
          border: 1px solid #dbeafe;
        }
        .alert-notice {
          display: flex;
          align-items: center;
          gap: 12px;
          padding: 12px 18px;
          border-radius: var(--radius-sm);
          font-size: 13.5px;
          font-weight: 500;
          animation: slideDown 0.15s ease-out;
        }
        .alert-success {
          background: var(--status-success-bg);
          border: 1px solid var(--status-success-border);
          color: var(--status-success-text);
        }
        .alert-danger {
          background: var(--status-danger-bg);
          border: 1px solid var(--status-danger-border);
          color: var(--status-danger-text);
        }
        .alert-dismiss-btn {
          color: inherit;
          opacity: 0.7;
          transition: opacity var(--transition-fast);
        }
        .alert-dismiss-btn:hover {
          opacity: 1;
        }
        .deactivate-confirm-body {
          font-size: 14px;
          line-height: 1.5;
          color: var(--text-main);
        }
        .deactivate-warning-text {
          margin-top: 10px;
          font-size: 13px;
          color: var(--status-danger-text);
          background: var(--status-danger-bg);
          padding: 10px 14px;
          border-radius: var(--radius-xs);
          border: 1px solid var(--status-danger-border);
        }
        @keyframes slideDown {
          from { transform: translateY(-8px); opacity: 0; }
          to { transform: translateY(0); opacity: 1; }
        }
      `}</style>
    </div>
  );
};
