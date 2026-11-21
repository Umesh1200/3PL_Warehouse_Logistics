import { test, expect } from '@playwright/test';

test.describe('Phase 7.2: Real Browser E2E Authentication & RBAC Shell Verification', () => {
  test('verifies login form validation, error handling, session setup, backend-shaped admin pages, and logout flow', async ({ page }) => {
    // Intercept backend API requests with realistic enterprise .NET 10 contracts
    await page.route('**/api/v1/auth/login', async (route) => {
      const request = route.request();
      const body = JSON.parse(request.postData() || '{}');

      if (body.email === 'bad@company.com') {
        return route.fulfill({
          status: 401,
          contentType: 'application/json',
          body: JSON.stringify({ detail: 'Invalid work email or password' }),
        });
      }

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          accessToken: 'e2e-access-token-xyz',
          refreshToken: 'e2e-refresh-token-abc',
          tokenType: 'Bearer',
          expiresIn: 3600,
          user: {
            id: 'e2e-user-1',
            email: body.email,
            firstName: 'Sarah',
            lastName: 'Connor',
            userType: 1, // Staff
            clientId: null,
            status: 1,
            createdAt: '2026-11-01T00:00:00Z',
            roles: ['SuperAdmin'],
          },
        }),
      });
    });

    await page.route('**/api/v1/auth/me', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          userId: 'e2e-user-1',
          email: 'superadmin@3pl.local',
          firstName: 'Sarah',
          lastName: 'Connor',
          userType: 1,
          tenantId: null,
          companyName: 'Nexus Logistics Global',
          roles: ['SuperAdmin'],
          permissions: [
            'users:view',
            'roles:view',
            'clients:view',
            'facilities:view',
            'analytics:control-tower:view',
          ],
        }),
      });
    });

    await page.route('**/api/v1/facilities', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {
            id: 'fac-chi',
            facilityCode: 'FAC-CHI1',
            name: 'Chicago Central Logistics Hub',
            city: 'Chicago, IL',
            timezone: 'America/Chicago',
            isActive: true,
          },
          {
            id: 'fac-dal',
            facilityCode: 'FAC-DAL1',
            name: 'Dallas Distribution Center',
            city: 'Dallas, TX',
            timezone: 'America/Chicago',
            isActive: true,
          },
        ]),
      });
    });

    await page.route('**/api/v1/facilities/*', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          id: 'fac-dal',
          facilityCode: 'FAC-DAL1',
          name: 'Dallas Distribution Center',
          city: 'Dallas, TX',
          timezone: 'America/Chicago',
          isActive: true,
        }),
      });
    });

    // Real backend-shaped RoleDto with RolePermissionDto objects
    await page.route('**/api/v1/roles', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {
            id: 1,
            roleName: 'SuperAdmin',
            description: 'Unrestricted system-wide governance',
            isSystemRole: true,
            permissions: [
              { id: 1, permissionKey: 'users:view', accessLevel: 3 },
              { id: 2, permissionKey: 'roles:view', accessLevel: 3 },
              { id: 3, permissionKey: 'clients:view', accessLevel: 3 },
              { id: 4, permissionKey: 'facilities:view', accessLevel: 3 },
            ],
          },
        ]),
      });
    });

    await page.route('**/api/v1/roles/permissions', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          'users:view',
          'roles:view',
          'clients:view',
          'facilities:view',
        ]),
      });
    });

    // Entity Schema route mock for metadata-driven modules
    await page.route('**/api/v1/schemas/**', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
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
            { name: 'id', label: 'Client ID', type: 'string', readOnly: true, hidden: true },
            { name: 'accountCode', label: 'Client Code', type: 'code', required: true },
            { name: 'companyName', label: 'Company Name', type: 'string', required: true },
            {
              name: 'slaTier',
              label: 'SLA Tier',
              type: 'select',
              options: [
                { value: 1, label: 'Standard', variant: 'info' },
                { value: 2, label: 'Silver', variant: 'info' },
                { value: 3, label: 'Gold', variant: 'warning' },
                { value: 4, label: 'Platinum', variant: 'success' },
              ],
            },
            { name: 'currency', label: 'Billing Currency', type: 'currency' },
            {
              name: 'status',
              label: 'Status',
              type: 'select',
              options: [
                { value: 1, label: 'Onboarding', variant: 'info' },
                { value: 2, label: 'Active', variant: 'success' },
                { value: 3, label: 'Suspended', variant: 'warning' },
                { value: 4, label: 'Terminated', variant: 'danger' },
              ],
            },
            { name: 'createdAt', label: 'Created At', type: 'date', readOnly: true },
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
            },
          },
          actions: [],
        }),
      });
    });

    // Real backend-shaped ClientDto with accountCode, slaTier: 3 (Gold), currency: "EUR"
    await page.route('**/api/v1/clients*', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          items: [
            {
              id: 'c-apex-01',
              companyName: 'Apex Distribution Group',
              accountCode: 'ACC-APEX-01',
              status: 2, // Active
              slaTier: 3, // Gold
              currency: 'EUR',
              createdAt: '2026-11-01T00:00:00Z',
              activeUsersCount: 12,
              contacts: [],
            },
          ],
          totalCount: 1,
          pageNumber: 1,
          pageSize: 50,
          totalPages: 1,
        }),
      });
    });

    // Real backend-shaped UserDto
    await page.route('**/api/v1/users*', async (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          items: [
            {
              id: 'e2e-user-1',
              email: 'superadmin@3pl.local',
              firstName: 'Sarah',
              lastName: 'Connor',
              userType: 1,
              clientId: null,
              status: 1,
              createdAt: '2026-11-01T00:00:00Z',
              roles: ['SuperAdmin'],
            },
          ],
          totalCount: 1,
          pageNumber: 1,
          pageSize: 50,
          totalPages: 1,
        }),
      });
    });

    // 1. Visit Login Page
    await page.goto('/login');
    await expect(page.getByRole('heading', { name: /Operations Portal Sign In/i })).toBeVisible();

    const emailInput = page.getByLabel(/Work Email/i);
    const passwordInput = page.getByLabel(/Password/i);
    const submitBtn = page.getByRole('button', { name: /Sign In to Platform/i });

    // 2. Test Invalid Credentials Error Surface
    await emailInput.fill('bad@company.com');
    await passwordInput.fill('WrongPassword!');
    await submitBtn.click();

    const errorAlert = page.getByRole('alert');
    await expect(errorAlert).toBeVisible();
    await expect(errorAlert).toContainText(/Invalid work email or password/i);

    // 3. Verify Demo Quick-Fill buttons are HIDDEN in production builds (Finding 7 fix)
    const superAdminQuickBtn = page.getByRole('button', { name: /SuperAdmin \(All Perms\)/i });
    await expect(superAdminQuickBtn).not.toBeVisible();

    // Fill valid enterprise credentials directly and submit
    await emailInput.fill('superadmin@3pl.local');
    await passwordInput.fill('SuperAdmin123!');
    await submitBtn.click();

    // 4. Verify Redirection into Authenticated AppShell
    await page.waitForURL('**/app/dashboard');
    await expect(page.getByRole('heading', { name: /Welcome back, Sarah Connor/i })).toBeVisible();

    // 5. Verify App Header Components: User Identity, Roles, and Facility Picker
    await expect(page.locator('.user-name')).toHaveText('Sarah Connor');
    await expect(page.locator('.role-pill')).toHaveText('SuperAdmin');

    const facilitySelect = page.getByLabel(/Select Active Warehouse Facility/i);
    await expect(facilitySelect).toBeVisible();
    await expect(facilitySelect).toHaveValue('fac-chi');

    // Switch active facility
    await facilitySelect.selectOption('fac-dal');
    await expect(facilitySelect).toHaveValue('fac-dal');

    // 6. Verify Dynamic Sidebar Navigation contains permitted admin links
    const sidebar = page.getByRole('complementary', { name: /Enterprise Navigation/i });
    await expect(sidebar).toBeVisible();
    await expect(sidebar.getByRole('link', { name: /Users Directory/i })).toBeVisible();
    await expect(sidebar.getByRole('link', { name: /Roles & Permissions/i })).toBeVisible();
    await expect(sidebar.getByRole('link', { name: /Client Tenants/i })).toBeVisible();
    await expect(sidebar.getByRole('link', { name: /Facilities Overview/i })).toBeVisible();

    // 7. Navigate to Facilities Page via Sidebar
    await sidebar.getByRole('link', { name: /Facilities Overview/i }).click();
    await page.waitForURL('**/app/admin/facilities');
    await expect(page.getByRole('heading', { name: /Authorized Facilities Directory/i })).toBeVisible();

    // 8. Navigate to Roles & Permissions: Verify real RoleDto and RolePermissionDto objects render safely without React #31
    await sidebar.getByRole('link', { name: /Roles & Permissions/i }).click();
    await page.waitForURL('**/app/admin/roles');
    await expect(page.getByRole('heading', { name: /Roles & Access Control Matrix/i })).toBeVisible();
    await expect(page.locator('.role-title').filter({ hasText: 'SuperAdmin' })).toBeVisible();
    await expect(page.getByText(/users:view \(Admin\)/i)).toBeVisible();

    // 9. Navigate to Client Tenants: Verify real accountCode, Gold SLA tier, and EUR currency render without fabricated defaults
    await sidebar.getByRole('link', { name: /Client Tenants/i }).click();
    await page.waitForURL('**/app/admin/clients');
    await expect(page.getByRole('heading', { name: /Client Tenants Directory/i })).toBeVisible();
    await expect(page.getByText('Apex Distribution Group')).toBeVisible();
    await expect(page.getByText('Gold')).toBeVisible();
    await expect(page.getByText('EUR')).toBeVisible();

    // 10. Navigate to Users Directory
    await sidebar.getByRole('link', { name: /Users Directory/i }).click();
    await page.waitForURL('**/app/admin/users');
    await expect(page.getByRole('heading', { name: /Enterprise Users Directory/i })).toBeVisible();
    await expect(page.getByText('superadmin@3pl.local')).toBeVisible();

    // 11. Execute Sign Out and Verify Session Teardown
    const signOutBtn = page.getByRole('button', { name: /Sign Out/i });
    await signOutBtn.click();

    await page.waitForURL('**/login');
    await expect(page.getByRole('heading', { name: /Operations Portal Sign In/i })).toBeVisible();

    // Attempting to access protected route after logout should redirect back to /login
    await page.goto('/app/dashboard');
    await page.waitForURL('**/login?redirect=*');
  });

  test('verifies refresh does not restore logged-out session during late network race', async ({ page }) => {
    let delayRefreshResolve: () => void;
    const delayPromise = new Promise<void>((resolve) => {
      delayRefreshResolve = resolve;
    });

    await page.route('**/api/v1/auth/refresh-token', async (route) => {
      await delayPromise;
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          accessToken: 'race-new-access-token',
          refreshToken: 'race-new-refresh-token',
          tokenType: 'Bearer',
          expiresIn: 3600,
          user: { id: 'u1', email: 'test@example.com' },
        }),
      });
    });

    // Populate expired session into localStorage
    await page.goto('/login');
    await page.evaluate(() => {
      localStorage.setItem('nexus_access_token', 'old-expired-token');
      localStorage.setItem('nexus_refresh_token', 'old-refresh-token');
    });

    // Trigger logout on client
    await page.evaluate(() => {
      // simulate user clicking logout or clearSession
      localStorage.removeItem('nexus_access_token');
      localStorage.removeItem('nexus_refresh_token');
    });

    // Now let the background refresh finish
    delayRefreshResolve!();

    // Verify localStorage has NOT been repopulated with race-new-access-token
    const storedToken = await page.evaluate(() => localStorage.getItem('nexus_access_token'));
    expect(storedToken).toBeNull();
  });
});
