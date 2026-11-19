import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { AuthProvider, useAuth } from '../context/AuthContext';
import { FacilityProvider, useFacility } from '../context/FacilityContext';
import { RouterProvider, useRouter } from '../context/RouterContext';
import { PermissionGuard } from '../components/auth/PermissionGuard';
import { Sidebar } from '../components/layout/Sidebar';
import { Header } from '../components/layout/Header';
import { RolesPage } from '../pages/admin/RolesPage';
import { UsersPage } from '../pages/admin/UsersPage';
import { ClientsPage } from '../pages/admin/ClientsPage';
import { tokenManager, requestManager, apiClient, executeSingleFlightRefresh, AuthExpiredError } from '../services/apiClient';
import { adminApi } from '../services/adminApi';
import { AccessLevel, UserType, UserStatus, SlaTier, ClientStatus } from '../types/auth';

describe('Phase 7.2: Authentication, Session Management & RBAC Shell Tests', () => {
  beforeEach(() => {
    tokenManager.clearSession();
    vi.restoreAllMocks();
  });

  afterEach(() => {
    tokenManager.clearSession();
    vi.restoreAllMocks();
  });

  it('performs typed login and initializes user profile context', async () => {
    const mockAuthResponse = {
      accessToken: 'access-jwt-123',
      refreshToken: 'refresh-jwt-456',
      tokenType: 'Bearer',
      expiresIn: 3600,
      user: {
        id: 'usr-1',
        email: 'ops@3pl.local',
        firstName: 'Ops',
        lastName: 'Manager',
        userType: UserType.Staff,
        clientId: null,
        status: UserStatus.Active,
        createdAt: '2026-11-01T00:00:00Z',
        roles: ['OperationsStaff'],
      },
    };

    const mockProfile = {
      userId: 'usr-1',
      email: 'ops@3pl.local',
      firstName: 'Ops',
      lastName: 'Manager',
      userType: UserType.Staff,
      tenantId: null,
      companyName: null,
      roles: ['OperationsStaff'],
      permissions: ['inventory:view', 'facilities:view'],
    };

    const fetchSpy = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      if (url.endsWith('/api/v1/auth/login')) {
        return {
          ok: true,
          status: 200,
          json: async () => mockAuthResponse,
        } as Response;
      }
      if (url.endsWith('/api/v1/auth/me')) {
        return {
          ok: true,
          status: 200,
          json: async () => mockProfile,
        } as Response;
      }
      if (url.endsWith('/api/v1/facilities')) {
        return {
          ok: true,
          status: 200,
          json: async () => [
            {
              id: 'fac-1',
              facilityCode: 'FAC-CHI',
              name: 'Chicago Central Hub',
              timezone: 'America/Chicago',
              isActive: true,
            },
          ],
        } as Response;
      }
      return { ok: false, status: 404 } as Response;
    });

    vi.stubGlobal('fetch', fetchSpy);

    const TestAuthConsumer = () => {
      const { user, isAuthenticated, login } = useAuth();
      return (
        <div>
          <span data-testid="auth-status">{isAuthenticated ? 'AUTHENTICATED' : 'ANONYMOUS'}</span>
          {user && <span data-testid="user-email">{user.email}</span>}
          <button
            onClick={() => login({ email: 'ops@3pl.local', password: 'password123' })}
          >
            Trigger Login
          </button>
        </div>
      );
    };

    render(
      <AuthProvider>
        <TestAuthConsumer />
      </AuthProvider>
    );

    expect(screen.getByTestId('auth-status').textContent).toBe('ANONYMOUS');

    fireEvent.click(screen.getByText('Trigger Login'));

    await waitFor(() => {
      expect(screen.getByTestId('auth-status').textContent).toBe('AUTHENTICATED');
      expect(screen.getByTestId('user-email').textContent).toBe('ops@3pl.local');
    });

    expect(tokenManager.getAccessToken()).toBe('access-jwt-123');
    expect(tokenManager.getRefreshToken()).toBe('refresh-jwt-456');
  });

  it('coordinates concurrent 401 failures through a single-flight refresh mutex', async () => {
    tokenManager.setTokens('expired-access-token', 'valid-refresh-token');

    let refreshCallCount = 0;

    const fetchSpy = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();

      if (url.endsWith('/api/v1/auth/refresh-token')) {
        refreshCallCount++;
        // Simulate refresh endpoint network delay
        await new Promise((r) => setTimeout(r, 40));
        return {
          ok: true,
          status: 200,
          json: async () => ({
            accessToken: 'refreshed-new-token',
            refreshToken: 'refreshed-new-refresh-token',
            tokenType: 'Bearer',
            expiresIn: 3600,
            user: { id: 'usr-1', email: 'test@example.com' },
          }),
        } as Response;
      }

      // First call with expired token returns 401
      const authHeader = (init?.headers as Record<string, string>)?.[
        'Authorization'
      ] || '';
      if (authHeader === 'Bearer expired-access-token') {
        return {
          ok: false,
          status: 401,
          json: async () => ({ detail: 'Token expired' }),
        } as Response;
      }

      // Second call with refreshed token returns 200
      if (authHeader === 'Bearer refreshed-new-token') {
        return {
          ok: true,
          status: 200,
          json: async () => ({ data: `Success on ${url}` }),
        } as Response;
      }

      return { ok: false, status: 500 } as Response;
    });

    vi.stubGlobal('fetch', fetchSpy);

    // Dispatch 3 concurrent API calls simultaneously while token is expired
    const [res1, res2, res3] = await Promise.all([
      apiClient<{ data: string }>('/api/v1/users'),
      apiClient<{ data: string }>('/api/v1/roles'),
      apiClient<{ data: string }>('/api/v1/facilities'),
    ]);

    // All 3 requests should succeed upon mutex resolution
    expect(res1.data).toBe('Success on /api/v1/users');
    expect(res2.data).toBe('Success on /api/v1/roles');
    expect(res3.data).toBe('Success on /api/v1/facilities');

    // Crucial mutex assertion: Exactly ONE refresh-token network call was initiated
    expect(refreshCallCount).toBe(1);
    expect(tokenManager.getAccessToken()).toBe('refreshed-new-token');
  });

  it('aborts active queries and cleans up session tokens upon logout', () => {
    tokenManager.setTokens('valid-token', 'valid-refresh');
    tokenManager.setActiveFacilityId('fac-1');

    const abortSpy = vi.spyOn(requestManager, 'abortAll');

    const TestLogoutConsumer = () => {
      const { logout } = useAuth();
      return <button onClick={logout}>Execute Logout</button>;
    };

    render(
      <AuthProvider>
        <TestLogoutConsumer />
      </AuthProvider>
    );

    fireEvent.click(screen.getByText('Execute Logout'));

    expect(abortSpy).toHaveBeenCalled();
    expect(tokenManager.getAccessToken()).toBeNull();
    expect(tokenManager.getRefreshToken()).toBeNull();
    expect(tokenManager.getActiveFacilityId()).toBeNull();
  });

  it('dynamically filters sidebar navigation according to caller permissions', async () => {
    const staffProfile = {
      userId: 'usr-staff',
      email: 'staff@3pl.local',
      firstName: 'Staff',
      lastName: 'Member',
      userType: UserType.Staff,
      tenantId: null,
      companyName: null,
      roles: ['StaffRole'],
      // Only has inventory:view and facilities:view; lacks users:view and roles:view
      permissions: ['inventory:view', 'facilities:view'],
    };

    const fetchSpy = vi.fn(async (input: RequestInfo | URL) => {
      const url = input.toString();
      if (url.endsWith('/api/v1/auth/me')) {
        return {
          ok: true,
          status: 200,
          json: async () => staffProfile,
        } as Response;
      }
      return { ok: false, status: 404 } as Response;
    });
    vi.stubGlobal('fetch', fetchSpy);

    tokenManager.setTokens('staff-token', 'staff-refresh');

    render(
      <RouterProvider initialPath="/app/dashboard">
        <AuthProvider>
          <Sidebar />
        </AuthProvider>
      </RouterProvider>
    );

    // Permitted links are present
    await waitFor(() => {
      expect(screen.getByText('Inventory Balances')).toBeDefined();
      expect(screen.getByText('Facilities Overview')).toBeDefined();
    });

    // Unauthorized links are excluded from DOM
    expect(screen.queryByText('Users Directory')).toBeNull();
    expect(screen.queryByText('Roles & Permissions')).toBeNull();
    expect(screen.queryByText('Client Tenants')).toBeNull();
  });

  it('renders accessible 403 Forbidden card when lacking permission', () => {
    const TestConsumer = () => {
      return (
        <RouterProvider initialPath="/app/admin/users">
          <AuthProvider>
            <PermissionGuard requiredPermission="users:view">
              <div>Protected Admin Content</div>
            </PermissionGuard>
          </AuthProvider>
        </RouterProvider>
      );
    };

    render(<TestConsumer />);

    expect(screen.getByRole('alert')).toBeDefined();
    expect(screen.getByText('403 FORBIDDEN')).toBeDefined();
    expect(screen.getByText('Access Denied')).toBeDefined();
    expect(screen.queryByText('Protected Admin Content')).toBeNull();
  });

  it('submits permission assignments formatted as PermissionAssignmentDto with AccessLevel', async () => {
    let capturedBody: any = null;

    const fetchSpy = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      if (url.includes('/api/v1/roles/5/permissions')) {
        capturedBody = JSON.parse(init?.body as string);
        return { ok: true, status: 200, json: async () => ({}) } as Response;
      }
      return { ok: false, status: 404 } as Response;
    });
    vi.stubGlobal('fetch', fetchSpy);

    tokenManager.setTokens('token', 'refresh');

    await adminApi.assignRolePermissions(5, [
      { permissionKey: 'inventory:view', accessLevel: AccessLevel.Read },
      { permissionKey: 'inbound:receive', accessLevel: AccessLevel.Write },
    ]);

    expect(capturedBody).toEqual([
      { permissionKey: 'inventory:view', accessLevel: 1 },
      { permissionKey: 'inbound:receive', accessLevel: 2 },
    ]);
  });

  it('prevents refresh completion from restoring a session after logout occurs', async () => {
    tokenManager.setTokens('old-access', 'old-refresh');
    let resolveRefreshFetch: (res: any) => void;
    const refreshPromise = new Promise((resolve) => {
      resolveRefreshFetch = resolve;
    });

    const fetchSpy = vi.fn(async (input: RequestInfo | URL) => {
      const url = input.toString();
      if (url.includes('/api/v1/auth/refresh-token')) {
        await refreshPromise;
        return {
          ok: true,
          status: 200,
          json: async () => ({
            accessToken: 're-minted-access-token',
            refreshToken: 're-minted-refresh-token',
            tokenType: 'Bearer',
            expiresIn: 3600,
            user: { id: 'usr-1', email: 'test@example.com' },
          }),
        } as Response;
      }
      return { ok: false, status: 404 } as Response;
    });
    vi.stubGlobal('fetch', fetchSpy);

    // Start refresh in flight
    const refreshAction = executeSingleFlightRefresh();

    // User clicks logout while refresh is in flight!
    tokenManager.clearSession();

    // Refresh network call finishes after logout
    resolveRefreshFetch!({});

    // The refresh promise must reject with AuthExpiredError and NOT re-populate tokens
    await expect(refreshAction).rejects.toThrow();
    expect(tokenManager.getAccessToken()).toBeNull();
    expect(tokenManager.getRefreshToken()).toBeNull();
  });

  it('handles late-401 race without dispatching duplicate refreshes', async () => {
    tokenManager.setTokens('token-1', 'refresh-1');
    let refreshCount = 0;

    const fetchSpy = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      if (url.includes('/api/v1/auth/refresh-token')) {
        refreshCount++;
        return {
          ok: true,
          status: 200,
          json: async () => ({
            accessToken: 'token-2',
            refreshToken: 'refresh-2',
            tokenType: 'Bearer',
            expiresIn: 3600,
            user: { id: 'usr-1', email: 'test@example.com' },
          }),
        } as Response;
      }

      const authHeader = (init?.headers as Record<string, string>)?.[
        'Authorization'
      ];
      if (authHeader === 'Bearer token-1') {
        return { ok: false, status: 401, json: async () => ({}) } as Response;
      }
      if (authHeader === 'Bearer token-2') {
        return { ok: true, status: 200, json: async () => ({ result: 'ok' }) } as Response;
      }
      return { ok: false, status: 500 } as Response;
    });
    vi.stubGlobal('fetch', fetchSpy);

    // First request triggers refresh
    const res1 = await apiClient<{ result: string }>('/api/v1/resource-a');
    expect(res1.result).toBe('ok');
    expect(refreshCount).toBe(1);

    // Late-arriving 401 using the already-refreshed 'token-1'
    const lateResult = await executeSingleFlightRefresh('token-1');
    expect(lateResult).toBe('token-2');
    // No second refresh dispatched!
    expect(refreshCount).toBe(1);
  });

  it('cancels route-scoped requests when route scope is aborted', async () => {
    let aborted = false;
    const fetchSpy = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      return new Promise<Response>((_, reject) => {
        const sig = init?.signal;
        if (sig) {
          sig.addEventListener('abort', () => {
            aborted = true;
            reject(new DOMException('Request aborted', 'AbortError'));
          });
        }
      });
    });
    vi.stubGlobal('fetch', fetchSpy);

    tokenManager.setTokens('tok', 'ref');

    const promise = apiClient('/api/v1/users', { scope: 'route' });

    // Abort route scope
    requestManager.abortScope('route');

    await expect(promise).rejects.toThrow();
    expect(aborted).toBe(true);
  });

  it('renders RolesPage safely with backend RolePermissionDto objects', async () => {
    const mockRoles = [
      {
        id: 1,
        roleName: 'WarehouseSupervisor',
        description: 'Supervises physical warehouse zones',
        isSystemRole: false,
        permissions: [
          { id: 101, permissionKey: 'inventory:view', accessLevel: 1 },
          { id: 102, permissionKey: 'receiving:manage', accessLevel: 2 },
        ],
      },
    ];

    const fetchSpy = vi.fn(async (input: RequestInfo | URL) => {
      const url = input.toString();
      if (url.endsWith('/api/v1/roles')) {
        return { ok: true, status: 200, json: async () => mockRoles } as Response;
      }
      if (url.endsWith('/api/v1/roles/permissions')) {
        return { ok: true, status: 200, json: async () => ['inventory:view', 'receiving:manage'] } as Response;
      }
      if (url.endsWith('/api/v1/auth/me')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            userId: 'u1',
            roles: ['SuperAdmin'],
            permissions: ['roles:view'],
          }),
        } as Response;
      }
      return { ok: false, status: 404 } as Response;
    });
    vi.stubGlobal('fetch', fetchSpy);

    tokenManager.setTokens('tok', 'ref');

    render(
      <RouterProvider initialPath="/app/admin/roles">
        <AuthProvider>
          <RolesPage />
        </AuthProvider>
      </RouterProvider>
    );

    await waitFor(() => {
      expect(screen.getByText('WarehouseSupervisor')).toBeDefined();
      expect(screen.getByText(/inventory:view \(Read\)/i)).toBeDefined();
      expect(screen.getByText(/receiving:manage \(Write\)/i)).toBeDefined();
    });
  });

  it('loads users directory successfully even when roles endpoint returns 403', async () => {
    const mockUsers = {
      items: [
        {
          id: 'u-1',
          email: 'clerk@3pl.local',
          firstName: 'John',
          lastName: 'Clerk',
          userType: UserType.Staff,
          clientId: null,
          status: UserStatus.Active,
          createdAt: '2026-11-01T00:00:00Z',
          roles: ['OperationsClerk'],
        },
      ],
      totalCount: 1,
      pageNumber: 1,
      pageSize: 50,
      totalPages: 1,
    };

    const fetchSpy = vi.fn(async (input: RequestInfo | URL) => {
      const url = input.toString();
      if (url.includes('/api/v1/users')) {
        return { ok: true, status: 200, json: async () => mockUsers } as Response;
      }
      if (url.endsWith('/api/v1/roles')) {
        return { ok: false, status: 403, json: async () => ({ detail: 'Forbidden' }) } as Response;
      }
      if (url.endsWith('/api/v1/auth/me')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            userId: 'u-viewer',
            roles: ['UserViewer'],
            permissions: ['users:view'], // Has users:view, lacks roles:view
          }),
        } as Response;
      }
      return { ok: false, status: 404 } as Response;
    });
    vi.stubGlobal('fetch', fetchSpy);

    tokenManager.setTokens('tok', 'ref');

    render(
      <RouterProvider initialPath="/app/admin/users">
        <AuthProvider>
          <UsersPage />
        </AuthProvider>
      </RouterProvider>
    );

    await waitFor(() => {
      expect(screen.getByText('clerk@3pl.local')).toBeDefined();
      expect(screen.getByText('John Clerk')).toBeDefined();
    });
  });

  it('renders ClientsPage with real backend DTO values without fabricated fallbacks', async () => {
    const mockClients = {
      items: [
        {
          id: 'c-1',
          companyName: 'Acme International',
          accountCode: 'ACC-ACME-01',
          status: ClientStatus.Active,
          slaTier: SlaTier.Gold, // 3
          currency: 'EUR',
          createdAt: '2026-10-15T00:00:00Z',
        },
      ],
      totalCount: 1,
      pageNumber: 1,
      pageSize: 50,
      totalPages: 1,
    };

    const fetchSpy = vi.fn(async (input: RequestInfo | URL) => {
      const url = input.toString();
      if (url.includes('/api/v1/clients')) {
        return { ok: true, status: 200, json: async () => mockClients } as Response;
      }
      if (url.endsWith('/api/v1/auth/me')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            userId: 'u1',
            roles: ['SuperAdmin'],
            permissions: ['clients:view'],
          }),
        } as Response;
      }
      return { ok: false, status: 404 } as Response;
    });
    vi.stubGlobal('fetch', fetchSpy);

    tokenManager.setTokens('tok', 'ref');

    render(
      <RouterProvider initialPath="/app/admin/clients">
        <AuthProvider>
          <ClientsPage />
        </AuthProvider>
      </RouterProvider>
    );

    await waitFor(() => {
      expect(screen.getAllByText('ACC-ACME-01').length).toBeGreaterThan(0);
      expect(screen.getByText('Acme International')).toBeDefined();
      expect(screen.getByText('Gold')).toBeDefined();
      expect(screen.getByText('EUR')).toBeDefined();
      expect(screen.getByText('Active')).toBeDefined();
    });
  });
});
