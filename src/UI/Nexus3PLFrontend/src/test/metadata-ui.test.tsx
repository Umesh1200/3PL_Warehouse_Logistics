import React from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { AppIcon, hasAppIcon } from '../components/common/AppIcon';
import { DynamicList } from '../components/dynamic/DynamicList';
import { DynamicForm } from '../components/dynamic/DynamicForm';
import { DynamicEntityPage } from '../components/dynamic/DynamicEntityPage';
import { EntitySchema } from '../types/schema';
import { AuthProvider, AuthContext } from '../context/AuthContext';
import { RouterProvider } from '../context/RouterContext';
import { tokenManager } from '../services/apiClient';
import { schemaService, TEST_CLIENT_SCHEMA_FIXTURE } from '../services/schemaService';
import { adminApi } from '../services/adminApi';

const testSchema: EntitySchema = {
  name: 'TestItem',
  version: '1.0.0',
  title: 'Test Entity',
  pluralTitle: 'Test Entities',
  description: 'Schema for automated metadata renderer verification.',
  icon: 'Boxes',
  primaryKey: 'id',
  endpoints: {
    list: '/api/v1/test-items',
  },
  permissions: {
    view: 'items:view',
    create: 'items:create',
    edit: 'items:edit',
    delete: 'items:delete',
  },
  fields: [
    { name: 'id', label: 'ID', type: 'string', readOnly: true, hidden: true },
    {
      name: 'code',
      label: 'Unique Code',
      type: 'code',
      required: true,
      icon: 'Barcode',
      validation: { min: 3, max: 20 },
    },
    {
      name: 'title',
      label: 'Display Name',
      type: 'string',
      required: true,
      icon: 'Boxes',
    },
    {
      name: 'status',
      label: 'Current Status',
      type: 'select',
      required: true,
      defaultValue: 1,
      options: [
        { value: 1, label: 'Active', variant: 'success' },
        { value: 2, label: 'Suspended', variant: 'danger' },
      ],
    },
    {
      name: 'tier',
      label: 'Service Tier',
      type: 'badge',
      options: [
        { value: 'gold', label: 'Gold Level', variant: 'warning' },
        { value: 'silver', label: 'Silver Level', variant: 'info' },
      ],
    },
    {
      name: 'currency',
      label: 'Settlement Currency',
      type: 'currency',
      defaultValue: 'USD',
    },
    {
      name: 'secretKey',
      label: 'Secret Key',
      type: 'string',
      readPermission: 'items:secret',
    },
  ],
  views: {
    list: {
      columns: [
        { field: 'code', label: 'Code Header', format: 'code', sortable: true },
        { field: 'title', label: 'Item Name Header', format: 'text', sortable: true },
        { field: 'tier', label: 'Tier Header', format: 'badge' },
        { field: 'currency', label: 'Currency Header', format: 'currency' },
        { field: 'status', label: 'Status Header', format: 'badge' },
        { field: 'secretKey', label: 'Secret Header', format: 'text' },
      ],
      emptyState: {
        icon: 'Boxes',
        title: 'Zero Items Recorded',
        description: 'Test empty state description.',
        actionLabel: '+ Add Test Item',
      },
    },
    form: {
      sections: [
        {
          id: 'general',
          title: 'General Information',
          fields: ['code', 'title', 'status', 'tier', 'currency', 'secretKey'],
        },
      ],
    },
  },
  actions: [
    {
      id: 'create',
      label: 'New Test Item',
      icon: 'Plus',
      variant: 'primary',
      target: 'header',
      permission: 'items:create',
    },
    {
      id: 'edit',
      label: 'Edit',
      icon: 'Edit2',
      variant: 'secondary',
      target: 'row',
      permission: 'items:edit',
    },
    {
      id: 'deactivate',
      label: 'Deactivate',
      icon: 'Trash2',
      variant: 'danger',
      target: 'row',
      permission: 'items:delete',
      confirmPrompt: 'Confirm deactivation?',
    },
  ],
};

const mockAuthContext = (permissions: string[] = ['items:view', 'items:create', 'items:edit', 'items:delete']) => ({
  user: {
    userId: 'test-user',
    roles: ['Operator'],
    permissions,
  } as any,
  isAuthenticated: true,
  isLoading: false,
  login: vi.fn(),
  logout: vi.fn(),
  hasPermission: (p: string) => permissions.includes(p),
  hasAnyPermission: (perms: string[]) => perms.some((p) => permissions.includes(p)),
  hasRole: vi.fn().mockReturnValue(false),
});

describe('Metadata-Driven UI Architecture & Component Verification', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    tokenManager.clearSession();
    schemaService.clearCache();
  });

  describe('Section A: AppIcon and Allowlisted Typed Registry', () => {
    it('verifies icon existence in allowlist and prevents arbitrary dynamic execution', () => {
      expect(hasAppIcon('LayoutDashboard')).toBe(true);
      expect(hasAppIcon('Building')).toBe(true);
      expect(hasAppIcon('Boxes')).toBe(true);
      expect(hasAppIcon('PackageCheck')).toBe(true);
      expect(hasAppIcon('ArrowDownToLine')).toBe(true);
      expect(hasAppIcon('Truck')).toBe(true);
      expect(hasAppIcon('ShieldCheck')).toBe(true);

      // Prohibited / non-allowlisted icons return false
      expect(hasAppIcon('NonExistentCustomIcon')).toBe(false);
      expect(hasAppIcon('<svg><script>alert(1)</script></svg>')).toBe(false);
    });

    it('renders accessible labels when provided and aria-hidden when decorative', () => {
      const { rerender } = render(<AppIcon name="Boxes" label="Warehouse Inventory" />);
      const accessibleIcon = screen.getByRole('img', { name: 'Warehouse Inventory' });
      expect(accessibleIcon).toBeDefined();

      rerender(<AppIcon name="Boxes" />);
      const hiddenIcons = document.querySelectorAll('svg[aria-hidden="true"]');
      expect(hiddenIcons.length).toBeGreaterThan(0);
    });

    it('falls back gracefully to HelpCircle for unmapped icon names', () => {
      render(<AppIcon name="UnknownRandomIcon" label="Fallback Icon" />);
      const icon = screen.getByRole('img', { name: 'Fallback Icon' });
      expect(icon).toBeDefined();
    });
  });

  describe('Section B: P-META-01 Authoritative Schema Resolution & Fail Closed', () => {
    it('throws and fails closed on HTTP 401, 403, 404, or network error without falling back to local schema', async () => {
      tokenManager.setTokens('tok', 'ref');

      // 404 Not Found
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 404, statusText: 'Not Found' }));
      await expect(schemaService.getSchema('NonExistentEntity')).rejects.toThrow();

      // 403 Forbidden
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 403, statusText: 'Forbidden' }));
      await expect(schemaService.getSchema('Client')).rejects.toThrow();

      // 401 Unauthorized
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 401, statusText: 'Unauthorized' }));
      await expect(schemaService.getSchema('Client')).rejects.toThrow();

      // Network error
      vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('Network offline')));
      await expect(schemaService.getSchema('Client')).rejects.toThrow();
    });

    it('rejects malformed schema responses missing essential structure', async () => {
      tokenManager.setTokens('tok', 'ref');
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ corrupted: true }), // Missing name, version, fields, etc.
      }));
      await expect(schemaService.getSchema('Client')).rejects.toThrow(/malformed/i);
    });

    it('scopes cached schemas by identity and effective permissions and invalidates on clearCache', async () => {
      tokenManager.setTokens('tok', 'ref');
      const fetchSpy = vi.fn().mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => TEST_CLIENT_SCHEMA_FIXTURE,
      });
      vi.stubGlobal('fetch', fetchSpy);

      const scopeUserA = { userId: 'user-a', permissions: ['clients:view'] };
      const scopeUserB = { userId: 'user-b', permissions: ['clients:view', 'clients:create'] };

      const schema1 = await schemaService.getSchema('Client', scopeUserA);
      expect(schema1.name).toBe('Client');
      expect(fetchSpy).toHaveBeenCalledTimes(1);

      // Hit cache for same user A
      const schemaCached = await schemaService.getSchema('Client', scopeUserA);
      expect(schemaCached).toBe(schema1);
      expect(fetchSpy).toHaveBeenCalledTimes(1);

      // Different user / permission scope misses cache and fetches
      await schemaService.getSchema('Client', scopeUserB);
      expect(fetchSpy).toHaveBeenCalledTimes(2);

      // Clearing cache forces fresh fetch
      schemaService.clearCache();
      await schemaService.getSchema('Client', scopeUserA);
      expect(fetchSpy).toHaveBeenCalledTimes(3);
    });

    it('DynamicEntityPage renders error alert with retry button when schema resolution fails', async () => {
      tokenManager.setTokens('tok', 'ref');
      vi.stubGlobal('fetch', vi.fn().mockImplementation(async (input: any) => {
        const url = String(input);
        if (url.includes('/api/v1/auth/me')) {
          return {
            ok: true,
            status: 200,
            json: async () => ({ userId: 'u1', roles: ['Admin'], permissions: ['clients:view'] }),
          };
        }
        return { ok: false, status: 500, statusText: 'Internal Error' };
      }));

      render(
        <RouterProvider initialPath="/app/admin/clients">
          <AuthProvider>
            <DynamicEntityPage entityName="Client" />
          </AuthProvider>
        </RouterProvider>
      );

      await waitFor(() => {
        expect(screen.getByRole('alert')).toBeDefined();
        expect(screen.getByText('Schema Resolution Error')).toBeDefined();
      });
    });
  });

  describe('Section C: DynamicForm Renderer & Safety', () => {
    it('enforces required field validation before invoking onSubmit', async () => {
      const handleSubmit = vi.fn();
      render(<DynamicForm schema={testSchema} onSubmit={handleSubmit} />);

      const submitBtn = screen.getByTestId('submit-form-btn');
      fireEvent.click(submitBtn);

      expect(handleSubmit).not.toHaveBeenCalled();
      expect(screen.getByText('Unique Code is required.')).toBeDefined();
      expect(screen.getByText('Display Name is required.')).toBeDefined();
    });

    it('auto-transforms code fields to uppercase and accepts valid submissions', async () => {
      const handleSubmit = vi.fn();
      render(<DynamicForm schema={testSchema} onSubmit={handleSubmit} />);

      const codeInput = screen.getByLabelText(/Unique Code/i);
      const titleInput = screen.getByLabelText(/Display Name/i);

      fireEvent.change(codeInput, { target: { value: 'item_code_01' } });
      fireEvent.change(titleInput, { target: { value: 'Test Title' } });

      expect((codeInput as HTMLInputElement).value).toBe('ITEM_CODE_01');

      const submitBtn = screen.getByTestId('submit-form-btn');
      fireEvent.click(submitBtn);

      expect(handleSubmit).toHaveBeenCalledWith(
        expect.objectContaining({
          code: 'ITEM_CODE_01',
          title: 'Test Title',
        })
      );
    });

    it('hides fields where user lacks permission and omits them from payload', async () => {
      const authWithoutSecret = mockAuthContext(['items:view', 'items:create']);
      const handleSubmit = vi.fn();

      render(
        <AuthContext.Provider value={authWithoutSecret as any}>
          <DynamicForm schema={testSchema} onSubmit={handleSubmit} />
        </AuthContext.Provider>
      );

      // Secret Key field requires 'items:secret' - must not be rendered
      expect(screen.queryByLabelText(/Secret Key/i)).toBeNull();

      fireEvent.change(screen.getByLabelText(/Unique Code/i), { target: { value: 'PUBLIC_CODE' } });
      fireEvent.change(screen.getByLabelText(/Display Name/i), { target: { value: 'Public Name' } });
      fireEvent.click(screen.getByTestId('submit-form-btn'));

      expect(handleSubmit).toHaveBeenCalled();
      const payload = handleSubmit.mock.calls[0][0];
      expect(payload.secretKey).toBeUndefined();
    });
  });

  describe('Section D: DynamicList & Metadata Adaptation', () => {
    it('demonstrates metadata-driven column header and data mapping', () => {
      const mockItems = [
        {
          id: 'test-1',
          code: 'CODE-99',
          title: 'Mechanical Actuator',
          tier: 'gold',
          currency: 'USD',
          status: 1,
        },
      ];

      render(
        <DynamicList
          schema={testSchema}
          items={mockItems}
          totalCount={1}
          page={1}
          pageSize={10}
          loading={false}
          onSearchChange={vi.fn()}
          onSortChange={vi.fn()}
          onPageChange={vi.fn()}
          onRefresh={vi.fn()}
          onAction={vi.fn()}
        />
      );

      expect(screen.getByText('Code Header')).toBeDefined();
      expect(screen.getByText('Item Name Header')).toBeDefined();
      expect(screen.getByText('Tier Header')).toBeDefined();
      expect(screen.getByText('Currency Header')).toBeDefined();
      expect(screen.getByText('Status Header')).toBeDefined();
      expect(screen.getByText('CODE-99')).toBeDefined();
      expect(screen.getByText('Mechanical Actuator')).toBeDefined();
      expect(screen.getByText('Gold Level')).toBeDefined();
      expect(screen.getByText('Active')).toBeDefined();
    });

    it('excludes hidden fields and restricted fields from effective list columns', () => {
      const authNoSecret = mockAuthContext(['items:view']);
      render(
        <AuthContext.Provider value={authNoSecret as any}>
          <DynamicList
            schema={testSchema}
            items={[{ id: '1', code: 'C1', title: 'T1' }]}
            totalCount={1}
            page={1}
            pageSize={10}
            loading={false}
            onSearchChange={vi.fn()}
            onSortChange={vi.fn()}
            onPageChange={vi.fn()}
            onRefresh={vi.fn()}
            onAction={vi.fn()}
          />
        </AuthContext.Provider>
      );

      // 'id' is hidden: true -> must not have column header
      expect(screen.queryByText('ID')).toBeNull();
      // 'secretKey' requires 'items:secret' -> must not have column header
      expect(screen.queryByText('Secret Header')).toBeNull();
    });

    it('supports keyboard accessibility (Enter/Space) and aria-sort on sortable headers', () => {
      const handleSort = vi.fn();
      render(
        <DynamicList
          schema={testSchema}
          items={[{ id: '1', code: 'C1', title: 'T1' }]}
          totalCount={1}
          page={1}
          pageSize={10}
          sortBy="code"
          sortDirection="asc"
          loading={false}
          onSearchChange={vi.fn()}
          onSortChange={handleSort}
          onPageChange={vi.fn()}
          onRefresh={vi.fn()}
          onAction={vi.fn()}
        />
      );

      const sortableHeader = screen.getByRole('button', { name: /Sort by Code Header/i });
      expect(sortableHeader.getAttribute('tabindex')).toBe('0');
      expect(sortableHeader.getAttribute('aria-sort')).toBe('ascending');

      // Test Enter key
      fireEvent.keyDown(sortableHeader, { key: 'Enter' });
      expect(handleSort).toHaveBeenCalledWith('code');

      // Test Space key
      fireEvent.keyDown(sortableHeader, { key: ' ' });
      expect(handleSort).toHaveBeenCalledTimes(2);
    });

    it('renders empty state defined in metadata schema and guards action button with permissions', () => {
      const authWithCreate = mockAuthContext(['items:view', 'items:create']);
      const authWithoutCreate = mockAuthContext(['items:view']);

      const { rerender } = render(
        <AuthContext.Provider value={authWithoutCreate as any}>
          <DynamicList
            schema={testSchema}
            items={[]}
            totalCount={0}
            page={1}
            pageSize={10}
            loading={false}
            onSearchChange={vi.fn()}
            onSortChange={vi.fn()}
            onPageChange={vi.fn()}
            onRefresh={vi.fn()}
            onAction={vi.fn()}
          />
        </AuthContext.Provider>
      );

      expect(screen.getByText('Zero Items Recorded')).toBeDefined();
      expect(screen.getByText('Test empty state description.')).toBeDefined();
      // Without items:create, + Add Test Item button is absent
      expect(screen.queryByText('+ Add Test Item')).toBeNull();

      // With items:create, action button is visible
      rerender(
        <AuthContext.Provider value={authWithCreate as any}>
          <DynamicList
            schema={testSchema}
            items={[]}
            totalCount={0}
            page={1}
            pageSize={10}
            loading={false}
            onSearchChange={vi.fn()}
            onSortChange={vi.fn()}
            onPageChange={vi.fn()}
            onRefresh={vi.fn()}
            onAction={vi.fn()}
          />
        </AuthContext.Provider>
      );
      expect(screen.getByText('+ Add Test Item')).toBeDefined();
    });
  });

  describe('Section E: Pilot DynamicEntityPage Search, Sort & CRUD Projection Integration', () => {
    it('manages create modal lifecycle and projects valid create payload', async () => {
      tokenManager.setTokens('valid-tok', 'valid-ref');
      vi.stubGlobal(
        'fetch',
        vi.fn().mockImplementation(async (input: any) => {
          const url = String(input);
          if (url.includes('/api/v1/auth/me')) {
            return {
              ok: true,
              status: 200,
              json: async () => ({
                userId: 'u1',
                roles: ['SuperAdmin'],
                permissions: ['items:view', 'items:create', 'items:edit', 'items:delete'],
              }),
            };
          }
          return { ok: false, status: 404 };
        })
      );

      const handleCreate = vi.fn().mockResolvedValue({ id: 'created-id' });
      const handleFetchList = vi.fn().mockResolvedValue({ items: [], totalCount: 0 });

      render(
        <RouterProvider initialPath="/app/admin/test">
          <AuthProvider>
            <DynamicEntityPage
              entityName="TestItem"
              initialSchema={testSchema}
              customFetchList={handleFetchList}
              customCreate={handleCreate}
            />
          </AuthProvider>
        </RouterProvider>
      );

      const newBtn = await screen.findByTestId('action-create');
      fireEvent.click(newBtn);

      expect(screen.getByText('New Test Entity')).toBeDefined();

      fireEvent.change(screen.getByLabelText(/Unique Code/i), { target: { value: 'NEW_CODE' } });
      fireEvent.change(screen.getByLabelText(/Display Name/i), { target: { value: 'New Test Name' } });

      const submitBtn = screen.getByTestId('submit-form-btn');
      fireEvent.click(submitBtn);

      await waitFor(() => {
        expect(handleCreate).toHaveBeenCalledWith(
          expect.objectContaining({
            code: 'NEW_CODE',
            title: 'New Test Name',
          })
        );
      });
    });

    it('emits searchTerm, sortBy and sortDescending and resets page to 1 on filter/sort change', async () => {
      tokenManager.setTokens('valid-tok', 'valid-ref');
      const mockUser = { userId: 'u1', roles: ['Admin'], permissions: ['clients:view'] };
      const getClientsSpy = vi.spyOn(adminApi, 'getClients').mockResolvedValue({
        items: [
          { id: 'c1', companyName: 'Acme Corp', accountCode: 'ACME', status: 1, slaTier: 1, currency: 'USD', createdAt: '2026-01-01' },
        ],
        totalCount: 25,
        pageNumber: 2,
        pageSize: 10,
        totalPages: 3,
      } as any);

      vi.stubGlobal('fetch', vi.fn().mockImplementation(async (input: any) => {
        const url = String(input);
        if (url.includes('/api/v1/auth/me')) {
          return { ok: true, status: 200, json: async () => mockUser };
        }
        if (url.includes('/api/v1/schemas/Client')) {
          return { ok: true, status: 200, json: async () => TEST_CLIENT_SCHEMA_FIXTURE };
        }
        return { ok: false, status: 404 };
      }));

      render(
        <RouterProvider initialPath="/app/admin/clients">
          <AuthProvider>
            <DynamicEntityPage entityName="Client" />
          </AuthProvider>
        </RouterProvider>
      );

      await waitFor(() => {
        expect(screen.getByText('Acme Corp')).toBeDefined();
      });

      // Search term entry
      const searchInput = screen.getByTestId('search-input');
      fireEvent.change(searchInput, { target: { value: 'Acme' } });

      await waitFor(() => {
        expect(getClientsSpy).toHaveBeenCalledWith(
          expect.objectContaining({
            searchTerm: 'Acme',
            pageNumber: 1, // Reset to page 1 on search
          }),
          expect.anything()
        );
      });

      // Sort column click
      const companyHeader = screen.getByRole('button', { name: /Sort by Company Name/i });
      fireEvent.click(companyHeader);

      await waitFor(() => {
        expect(getClientsSpy).toHaveBeenCalledWith(
          expect.objectContaining({
            sortBy: 'companyName',
            pageNumber: 1, // Reset to page 1 on sort
          }),
          expect.anything()
        );
      });
    });

    it('loads authoritative detail data before opening edit modal and projects writable update fields', async () => {
      tokenManager.setTokens('valid-tok', 'valid-ref');
      const mockDetail = {
        id: 'c1',
        companyName: 'Acme Corp Updated',
        accountCode: 'ACME',
        status: 1,
        slaTier: 2,
        currency: 'EUR',
      };
      const getClientByIdSpy = vi.spyOn(adminApi, 'getClientById').mockResolvedValue(mockDetail as any);
      const updateClientSpy = vi.spyOn(adminApi, 'updateClient').mockResolvedValue(mockDetail as any);
      vi.spyOn(adminApi, 'getClients').mockResolvedValue({
        items: [mockDetail],
        totalCount: 1,
        pageNumber: 1,
        pageSize: 10,
        totalPages: 1,
      } as any);

      vi.stubGlobal('fetch', vi.fn().mockImplementation(async (input: any) => {
        const url = String(input);
        if (url.includes('/api/v1/auth/me')) {
          return {
            ok: true,
            status: 200,
            json: async () => ({
              userId: 'u1',
              roles: ['Admin'],
              permissions: ['clients:view', 'clients:edit'],
            }),
          };
        }
        if (url.includes('/api/v1/schemas/Client')) {
          return { ok: true, status: 200, json: async () => TEST_CLIENT_SCHEMA_FIXTURE };
        }
        return { ok: false, status: 404 };
      }));

      render(
        <RouterProvider initialPath="/app/admin/clients">
          <AuthProvider>
            <DynamicEntityPage entityName="Client" />
          </AuthProvider>
        </RouterProvider>
      );

      await waitFor(() => {
        expect(screen.getByTestId('row-action-edit-c1')).toBeDefined();
      });

      // Trigger edit
      fireEvent.click(screen.getByTestId('row-action-edit-c1'));

      await waitFor(() => {
        expect(getClientByIdSpy).toHaveBeenCalledWith('c1');
        expect(screen.getByText('Edit Client Tenant')).toBeDefined();
      });

      // Change company name and submit
      const companyInput = document.querySelector('input[name="companyName"]') as HTMLInputElement;
      fireEvent.change(companyInput, { target: { value: 'Acme Worldwide' } });
      fireEvent.click(screen.getByTestId('submit-form-btn'));

      await waitFor(() => {
        expect(updateClientSpy).toHaveBeenCalledWith(
          'c1',
          expect.objectContaining({
            companyName: 'Acme Worldwide',
            // accountCode is omitted from update payload (immutable)
          })
        );
        const submittedPayload = updateClientSpy.mock.calls[0][1];
        expect((submittedPayload as any).accountCode).toBeUndefined();
      });
    });

    it('fails closed with explicit error message for unsupported entity operations', async () => {
      tokenManager.setTokens('valid-tok', 'valid-ref');
      vi.stubGlobal('fetch', vi.fn().mockImplementation(async (input: any) => {
        const url = String(input);
        if (url.includes('/api/v1/auth/me')) {
          return {
            ok: true,
            status: 200,
            json: async () => ({
              userId: 'u1',
              roles: ['SuperAdmin'],
              permissions: ['items:view', 'items:create'],
            }),
          };
        }
        return { ok: false, status: 404 };
      }));

      render(
        <RouterProvider initialPath="/app/admin/unknown">
          <AuthProvider>
            <DynamicEntityPage
              entityName="UnknownEntity"
              initialSchema={testSchema}
              customFetchList={vi.fn().mockResolvedValue({ items: [], totalCount: 0 })}
            />
          </AuthProvider>
        </RouterProvider>
      );

      const newBtn = await screen.findByTestId('action-create');
      fireEvent.click(newBtn);

      fireEvent.change(screen.getByLabelText(/Unique Code/i), { target: { value: 'UNK-01' } });
      fireEvent.change(screen.getByLabelText(/Display Name/i), { target: { value: 'Unknown Item' } });
      fireEvent.click(screen.getByTestId('submit-form-btn'));

      await waitFor(() => {
        expect(screen.getByText(/Create operation is not supported for entity 'UnknownEntity'/i)).toBeDefined();
      });
    });
  });
});
