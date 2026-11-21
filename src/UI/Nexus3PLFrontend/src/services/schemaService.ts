import { EntitySchema } from '../types/schema';
import { apiClient } from './apiClient';

/**
 * Built-in fallback schema for Client entity.
 * Used for instant cold-start, testing fixtures, or when server schema is preloaded.
 */
export const DEFAULT_CLIENT_SCHEMA: EntitySchema = {
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
    {
      name: 'contactFirstName',
      label: 'Contact First Name',
      type: 'string',
      placeholder: 'John',
      icon: 'Users',
    },
    {
      name: 'contactLastName',
      label: 'Contact Last Name',
      type: 'string',
      placeholder: 'Doe',
      icon: 'Users',
    },
    {
      name: 'contactEmail',
      label: 'Contact Email',
      type: 'email',
      placeholder: 'jdoe@company.com',
      icon: 'Mail',
    },
    {
      name: 'contactPhone',
      label: 'Contact Phone',
      type: 'phone',
      placeholder: '+1 555-0199',
      icon: 'Phone',
    },
  ],
  views: {
    list: {
      columns: [
        { field: 'accountCode', label: 'Client Code', width: '150px', sortable: true, format: 'code' },
        { field: 'companyName', label: 'Company Name', sortable: true, format: 'text' },
        { field: 'slaTier', label: 'SLA Tier', width: '130px', format: 'badge' },
        { field: 'currency', label: 'Currency', width: '110px', align: 'center', format: 'currency' },
        { field: 'status', label: 'Status', width: '120px', align: 'center', format: 'badge' },
        { field: 'createdAt', label: 'Created', width: '130px', align: 'right', format: 'date' },
      ],
      defaultSort: {
        field: 'companyName',
        direction: 'asc',
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
        {
          id: 'contact',
          title: 'Primary Contact Information',
          description: 'Direct communications contact for logistics operations and exceptions.',
          icon: 'Mail',
          columns: 2,
          fields: ['contactFirstName', 'contactLastName', 'contactEmail', 'contactPhone'],
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

const schemaCache = new Map<string, EntitySchema>();

export const schemaService = {
  /**
   * Retrieve authoritative, permission-resolved schema for an entity from backend.
   */
  getSchema: async (entityName: string, signal?: AbortSignal): Promise<EntitySchema> => {
    const key = entityName.toLowerCase();
    if (schemaCache.has(key)) {
      return schemaCache.get(key)!;
    }

    try {
      const remote = await apiClient<EntitySchema>(`/api/v1/schemas/${entityName}`, {
        method: 'GET',
        signal,
      });
      if (remote && remote.name) {
        schemaCache.set(key, remote);
        return remote;
      }
    } catch (err) {
      // If remote schema is unavailable, fallback to default schema definition
      if (key === 'client') {
        schemaCache.set(key, DEFAULT_CLIENT_SCHEMA);
        return DEFAULT_CLIENT_SCHEMA;
      }
      throw err;
    }

    if (key === 'client') {
      schemaCache.set(key, DEFAULT_CLIENT_SCHEMA);
      return DEFAULT_CLIENT_SCHEMA;
    }

    throw new Error(`Schema for entity '${entityName}' not found.`);
  },

  /**
   * Clear schema cache (e.g. upon user role change or logout).
   */
  clearCache: () => {
    schemaCache.clear();
  },
};
