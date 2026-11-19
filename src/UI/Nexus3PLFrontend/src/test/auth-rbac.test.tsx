import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { AuthProvider, useAuth } from '../context/AuthContext';
import { FacilityProvider, useFacility } from '../context/FacilityContext';
import { RouterProvider, useRouter } from '../context/RouterContext';
import { PermissionGuard } from '../components/auth/PermissionGuard';
import { Sidebar } from '../components/layout/Sidebar';
import { Header } from '../components/layout/Header';
import { tokenManager, requestManager, apiClient } from '../services/apiClient';
import { adminApi } from '../services/adminApi';
import { AccessLevel, UserType, UserStatus } from '../types/auth';

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
});
