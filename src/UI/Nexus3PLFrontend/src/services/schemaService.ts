import { EntitySchema } from '../types/schema';
import { apiClient } from './apiClient';

/**
 * Isolated test fixture schema for Client entity.
 * Intended ONLY for isolated component unit testing and mock fixtures.
 * In production runtime, schema resolution must always resolve authoritatively from the backend.
 */
export const TEST_CLIENT_SCHEMA_FIXTURE: EntitySchema = {
  name: 'Client',
  version: '1.0.0',
  title: 'Client Tenant',
  pluralTitle: 'Client Tenants',
  description: 'Multi-tenant accounts, billing currencies, and service level agreements.',
  icon: 'Building',
  primaryKey: 'id',
  endpoints: {
    list: '/api/v1/clients',
    get: '/api/v1/clients/{id}',
    create: '/api/v1/clients',
    update: '/api/v1/clients/{id}',
    delete: '/api/v1/clients/{id}',
  },
  permissions: {
    view: 'clients:view',
    create: 'clients:create',
    edit: 'clients:edit',
    delete: 'clients:delete',
  },
  fields: [
    {
      name: 'id',
      label: 'Client ID',
      type: 'string',
      readOnly: true,
      hidden: true,
    },
    {
      name: 'accountCode',
      label: 'Client Code',
      type: 'code',
      required: true,
      placeholder: 'e.g. APEX_LOGISTICS',
      icon: 'Barcode',
      helperText: 'Uppercase alphanumeric partition code (min 3 chars).',
      validation: {
        min: 3,
        max: 50,
        pattern: '^[A-Z0-9_\\-]+$',
        message: 'Account code must contain uppercase letters, numbers, hyphens or underscores.',
      },
    },
    {
      name: 'companyName',
      label: 'Company Name',
      type: 'string',
      required: true,
      placeholder: 'Legal corporate name',
      icon: 'Building',
      validation: {
        min: 2,
        max: 200,
        message: 'Company name is required (max 200 characters).',
      },
    },
    {
      name: 'slaTier',
      label: 'SLA Tier',
      type: 'select',
      required: true,
      defaultValue: 1,
      icon: 'Sparkles',
      options: [
        { value: 1, label: 'Standard', variant: 'info' },
        { value: 2, label: 'Silver', variant: 'info' },
        { value: 3, label: 'Gold', variant: 'warning' },
        { value: 4, label: 'Platinum', variant: 'success' },
      ],
    },
    {
      name: 'currency',
      label: 'Billing Currency',
      type: 'currency',
      required: true,
      defaultValue: 'USD',
      placeholder: 'USD',
      icon: 'DollarSign',
      validation: {
        min: 3,
        max: 3,
        message: 'Currency must be a 3-character ISO code.',
      },
    },
    {
      name: 'status',
      label: 'Status',
      type: 'select',
      required: true,
      defaultValue: 2,
      icon: 'CheckCircle2',
      options: [
        { value: 1, label: 'Onboarding', variant: 'info' },
        { value: 2, label: 'Active', variant: 'success' },
        { value: 3, label: 'Suspended', variant: 'warning' },
        { value: 4, label: 'Terminated', variant: 'danger' },
        { value: 'Onboarding', label: 'Onboarding', variant: 'info' },
        { value: 'Active', label: 'Active', variant: 'success' },
        { value: 'Suspended', label: 'Suspended', variant: 'warning' },
        { value: 'Terminated', label: 'Terminated', variant: 'danger' },
      ],
    },
    {
      name: 'createdAt',
      label: 'Created At',
      type: 'date',
      readOnly: true,
      icon: 'Calendar',
    },
    {
      name: 'activeUsersCount',
      label: 'Active Users',
      type: 'integer',
      readOnly: true,
      icon: 'Users',
    },
  ],
  views: {
    list: {
      columns: [
        { field: 'accountCode', label: 'Client Code', width: '150px', sortable: true, format: 'code' },
        { field: 'companyName', label: 'Company Name', sortable: true, format: 'text' },
        { field: 'slaTier', label: 'SLA Tier', width: '130px', sortable: true, format: 'badge' },
        { field: 'currency', label: 'Currency', width: '110px', align: 'center', sortable: true, format: 'currency' },
        { field: 'status', label: 'Status', width: '120px', align: 'center', sortable: true, format: 'badge' },
        { field: 'createdAt', label: 'Created', width: '130px', align: 'right', sortable: true, format: 'date' },
      ],
      defaultSort: {
        field: 'createdAt',
        direction: 'desc',
      },
      searchFields: ['companyName', 'accountCode'],
      emptyState: {
        icon: 'Building',
        title: 'No Client Tenants Found',
        description: 'No client records match the query criteria. Create a new client to get started.',
        actionLabel: '+ New Client',
      },
    },
    form: {
      sections: [
        {
          id: 'core',
          title: 'Tenant Identity & SLA Configuration',
          description: 'Primary organization details, account partition identifier, and billing settings.',
          icon: 'Building',
          columns: 2,
          fields: ['companyName', 'accountCode', 'slaTier', 'currency', 'status'],
        },
      ],
    },
  },
  actions: [
    {
      id: 'create',
      label: 'New Client',
      icon: 'Plus',
      variant: 'primary',
      target: 'header',
      permission: 'clients:create',
    },
    {
      id: 'edit',
      label: 'Edit Details',
      icon: 'Edit2',
      variant: 'secondary',
      target: 'row',
      permission: 'clients:edit',
    },
    {
      id: 'deactivate',
      label: 'Deactivate',
      icon: 'Trash2',
      variant: 'danger',
      target: 'row',
      permission: 'clients:delete',
      confirmPrompt: 'Are you sure you want to deactivate this client tenant? Operational intake will be paused.',
    },
  ],
};

/** @deprecated Alias preserved for isolated test backwards compatibility */
export const DEFAULT_CLIENT_SCHEMA = TEST_CLIENT_SCHEMA_FIXTURE;

export interface AuthSchemaScope {
  userId?: string | null;
  roles?: string[];
  permissions?: string[];
}

const schemaCache = new Map<string, EntitySchema>();

export const schemaService = {
  /**
   * Retrieve authoritative, permission-resolved schema for an entity from backend.
   * Fails closed on HTTP 401, 403, 404, network failure, or malformed schema response.
   * Production requests strictly require server resolution without unrestricted local fallback.
   */
  getSchema: async (
    entityName: string,
    authScope?: AuthSchemaScope,
    signal?: AbortSignal
  ): Promise<EntitySchema> => {
    const scopeKey = authScope?.userId
      ? `${authScope.userId}:${(authScope.permissions || []).slice().sort().join(',')}`
      : 'anonymous';
    const cacheKey = `${scopeKey}::${entityName.toLowerCase()}`;

    if (schemaCache.has(cacheKey)) {
      return schemaCache.get(cacheKey)!;
    }

    const remote = await apiClient<EntitySchema>(`/api/v1/schemas/${entityName}`, {
      method: 'GET',
      signal,
    });

    if (!remote || !remote.name || !Array.isArray(remote.fields)) {
      throw new Error(`Malformed schema payload received for entity '${entityName}'.`);
    }

    schemaCache.set(cacheKey, remote);
    return remote;
  },

  /**
   * Clear schema cache (e.g. upon user role change, login, logout, or session expiry).
   */
  clearCache: () => {
    schemaCache.clear();
  },
};
