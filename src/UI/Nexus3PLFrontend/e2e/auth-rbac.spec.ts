import { test, expect } from '@playwright/test';

test.describe('Phase 7.2: Real Browser E2E Authentication & RBAC Shell Verification', () => {
  test('verifies login form validation, error handling, session setup, and logout flow', async ({ page }) => {
    // Intercept backend API requests with realistic enterprise contracts
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

    // 3. Test Demo Quick-Fill button and successful authentication
    const superAdminQuickBtn = page.getByRole('button', { name: /SuperAdmin \(All Perms\)/i });
    await superAdminQuickBtn.click();
    await expect(emailInput).toHaveValue('superadmin@3pl.local');

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

    // 8. Execute Sign Out and Verify Session Teardown
    const signOutBtn = page.getByRole('button', { name: /Sign Out/i });
    await signOutBtn.click();

    await page.waitForURL('**/login');
    await expect(page.getByRole('heading', { name: /Operations Portal Sign In/i })).toBeVisible();

    // Attempting to access protected route after logout should redirect back to /login
    await page.goto('/app/dashboard');
    await page.waitForURL('**/login?redirect=*');
  });
});
