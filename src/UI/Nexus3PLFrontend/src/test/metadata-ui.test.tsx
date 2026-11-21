import React from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { AppIcon, hasAppIcon } from '../components/common/AppIcon';
import { DynamicList } from '../components/dynamic/DynamicList';
import { DynamicForm } from '../components/dynamic/DynamicForm';
import { DynamicEntityPage } from '../components/dynamic/DynamicEntityPage';
import { EntitySchema } from '../types/schema';
import { AuthProvider } from '../context/AuthContext';
import { RouterProvider } from '../context/RouterContext';
import { tokenManager } from '../services/apiClient';

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
  ],
  views: {
    list: {
      columns: [
        { field: 'code', label: 'Code Header', format: 'code' },
        { field: 'title', label: 'Item Name Header', format: 'text' },
        { field: 'tier', label: 'Tier Header', format: 'badge' },
        { field: 'currency', label: 'Currency Header', format: 'currency' },
        { field: 'status', label: 'Status Header', format: 'badge' },
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
          fields: ['code', 'title', 'status', 'tier', 'currency'],
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

describe('Metadata-Driven UI Architecture & Component Verification', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    tokenManager.clearSession();
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
      // Without label, aria-hidden="true" is set
      const hiddenIcons = document.querySelectorAll('svg[aria-hidden="true"]');
      expect(hiddenIcons.length).toBeGreaterThan(0);
    });

    it('falls back gracefully to HelpCircle for unmapped icon names', () => {
      render(<AppIcon name="UnknownRandomIcon" label="Fallback Icon" />);
      const icon = screen.getByRole('img', { name: 'Fallback Icon' });
      expect(icon).toBeDefined();
    });
  });

  describe('Section C: DynamicForm Renderer', () => {
    it('enforces required field validation before invoking onSubmit', async () => {
      const handleSubmit = vi.fn();
      render(
        <DynamicForm
          schema={testSchema}
          onSubmit={handleSubmit}
        />
      );

      // Attempt to submit empty form
      const submitBtn = screen.getByTestId('submit-form-btn');
      fireEvent.click(submitBtn);

      expect(handleSubmit).not.toHaveBeenCalled();
      expect(screen.getByText('Unique Code is required.')).toBeDefined();
      expect(screen.getByText('Display Name is required.')).toBeDefined();
    });

    it('auto-transforms code fields to uppercase and accepts valid submissions', async () => {
      const handleSubmit = vi.fn();
      render(
        <DynamicForm
          schema={testSchema}
          onSubmit={handleSubmit}
        />
      );

      const codeInput = screen.getByLabelText(/Unique Code/i);
      const titleInput = screen.getByLabelText(/Display Name/i);

      fireEvent.change(codeInput, { target: { value: 'item_code_01' } });
      fireEvent.change(titleInput, { target: { value: 'Test Title' } });

      // Check uppercase normalization
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
  });

  describe('Section C & D: DynamicList & Metadata Adaptation', () => {
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

      // Verify custom headers rendered from schema.views.list.columns
      expect(screen.getByText('Code Header')).toBeDefined();
      expect(screen.getByText('Item Name Header')).toBeDefined();
      expect(screen.getByText('Tier Header')).toBeDefined();
      expect(screen.getByText('Currency Header')).toBeDefined();
      expect(screen.getByText('Status Header')).toBeDefined();

      // Verify data rows
      expect(screen.getByText('CODE-99')).toBeDefined();
      expect(screen.getByText('Mechanical Actuator')).toBeDefined();
      expect(screen.getByText('Gold Level')).toBeDefined();
      expect(screen.getByText('Active')).toBeDefined();
    });

    it('demonstrates changing metadata columns reflects immediately without modifying JSX components', () => {
      // Modify column order and label strictly via metadata definition
      const modifiedSchema: EntitySchema = {
        ...testSchema,
        views: {
          ...testSchema.views,
          list: {
            ...testSchema.views.list,
            columns: [
              { field: 'title', label: 'OVERRIDDEN_PRODUCT_NAME_HEADER', format: 'text' },
              { field: 'currency', label: 'OVERRIDDEN_CURRENCY_HEADER', format: 'currency' },
            ],
          },
        },
      };

      const mockItems = [{ id: 'test-2', title: 'Smart Sensor Box', currency: 'EUR' }];

      render(
        <DynamicList
          schema={modifiedSchema}
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

      expect(screen.getByText('OVERRIDDEN_PRODUCT_NAME_HEADER')).toBeDefined();
      expect(screen.getByText('OVERRIDDEN_CURRENCY_HEADER')).toBeDefined();
      expect(screen.queryByText('Code Header')).toBeNull();
      expect(screen.getByText('Smart Sensor Box')).toBeDefined();
    });

    it('renders empty state defined in metadata schema when item count is zero', () => {
      render(
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
      );

      expect(screen.getByText('Zero Items Recorded')).toBeDefined();
      expect(screen.getByText('Test empty state description.')).toBeDefined();
      expect(screen.getByText('+ Add Test Item')).toBeDefined();
    });
  });

  describe('Section D: Pilot DynamicEntityPage Full Integration', () => {
    it('manages create modal lifecycle and invokes custom create handler', async () => {
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

      // Wait for auth to settle and header button to be visible
      const newBtn = await screen.findByTestId('action-create');
      fireEvent.click(newBtn);

      // Verify modal is open
      expect(screen.getByText('New Test Entity')).toBeDefined();

      // Fill in required fields
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
  });
});
