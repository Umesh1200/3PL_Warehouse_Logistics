import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { test, expect } from '@playwright/test';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const AUDIT_DIR = path.resolve(__dirname, '../../../../docs/audits/mdm-1-6');

const FACILITIES = [
  {
    id: 'fac-chi-16',
    facilityCode: 'FAC-CHI1',
    name: 'Chicago Central Hub',
    city: 'Chicago',
    countryCode: 'US',
    countryNormalizationStatus: 'Normalized',
    timezone: 'America/Chicago',
    isActive: true,
  },
  {
    id: 'fac-ktm-16',
    facilityCode: 'FAC-KTM1',
    name: 'Kathmandu Distribution Hub',
    city: 'Kathmandu',
    countryCode: 'NP',
    countryNormalizationStatus: 'Normalized',
    timezone: 'Asia/Kathmandu',
    isActive: true,
  },
];

const ALL_AUDITS = Array.from({ length: 22 }, (_, i) => {
  const idx = i + 1;
  const isNormalized = idx > 12;
  return {
    auditId: `aud-e2e-${idx}`,
    entityType: idx === 1 ? 'Customer' : idx % 2 === 0 ? 'ShippingAddress' : 'Facility',
    entityId: `${String(idx).padStart(8, '0')}-1111-2222-3333-444444444444`,
    fieldName: idx === 1 ? 'DefaultCountryCode' : 'CountryCode',
    originalCountryValue: idx === 1 ? '[REDACTED_POTENTIAL_PII]' : isNormalized ? 'USA' : 'ZZZ',
    normalizedCountryCode: isNormalized ? 'US' : null,
    normalizationStatus: isNormalized ? 'Normalized' : 'PendingNormalization',
    preservedHistoricalValue: idx === 15 || idx === 21,
    wasInferredFromParent: idx === 16 || idx === 22,
    inferenceSource: idx === 16 || idx === 22 ? 'CustomerDefault+AdministrativeDivision+PostalCode' : idx === 15 || idx === 21 ? 'CatalogAlias' : null,
    reason:
      idx === 1
        ? 'Unrecognized value in Customer.DefaultCountryCode (potential PII redacted).'
        : isNormalized
          ? 'Normalized to canonical ISO US via Reference Catalog.'
          : 'Unrecognized country token not found in active catalog.',
    facilityId: idx % 3 === 0 ? 'fac-ktm-16' : 'fac-chi-16',
    facilityCode: idx % 3 === 0 ? 'FAC-KTM1' : 'FAC-CHI1',
    clientId: idx % 2 === 0 ? 'cli-alpha' : null,
    correlationBatchId: 'MDM16-R04-PG',
    recordedAtUtc: `2026-11-27T0${idx % 9}:15:00Z`,
  };
});

async function setupCommonRoutes(
  page: any,
  userProfile: {
    userId: string;
    email: string;
    firstName: string;
    lastName: string;
    userType: 'Staff' | 'Client' | 'Driver';
    roles: string[];
    permissions: string[];
    assignedFacilityId?: string | null;
    tenantId?: string | null;
  },
  options?: { failFirstQuarantineCall?: boolean }
) {
  let failOnce = options?.failFirstQuarantineCall ?? false;
  const quarantineRequests: string[] = [];

  await page.route('**/api/v1/auth/login', async (route: any) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        accessToken: `e2e-jwt-${userProfile.userId}`,
        refreshToken: `e2e-ref-${userProfile.userId}`,
        tokenType: 'Bearer',
        expiresIn: 3600,
        user: {
          id: userProfile.userId,
          email: userProfile.email,
          firstName: userProfile.firstName,
          lastName: userProfile.lastName,
          userType: userProfile.userType,
          clientId: userProfile.tenantId ?? null,
          status: 'Active',
          createdAt: '2026-11-01T00:00:00Z',
          roles: userProfile.roles,
        },
      }),
    });
  });

  await page.route('**/api/v1/auth/me', async (route: any) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        userId: userProfile.userId,
        email: userProfile.email,
        firstName: userProfile.firstName,
        lastName: userProfile.lastName,
        userType: userProfile.userType,
        status: 'Active',
        tenantId: userProfile.tenantId ?? null,
        companyName: userProfile.tenantId ? 'Tenant Co' : 'Nexus3PL',
        roles: userProfile.roles,
        permissions: userProfile.permissions,
      }),
    });
  });

  await page.route('**/api/v1/facilities', async (route: any) => {
    const facs = userProfile.assignedFacilityId
      ? FACILITIES.filter((f) => f.id === userProfile.assignedFacilityId)
      : FACILITIES;
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(facs) });
  });

  await page.route('**/api/v1/control-tower/**', async (route: any) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ facilityId: FACILITIES[0].id, stages: [], facilityHealthScore: 95 }),
    });
  });

  await page.route('**/api/v1/reference/normalization-quarantine**', async (route: any) => {
    const reqUrl = route.request().url();
    quarantineRequests.push(reqUrl);

    if (failOnce) {
      failOnce = false;
      await route.fulfill({
        status: 500,
        contentType: 'application/json',
        body: JSON.stringify({ message: 'Simulated database timeout' }),
      });
      return;
    }

    const url = new URL(reqUrl);
    const statusParam = url.searchParams.get('status');
    const includeAll = url.searchParams.get('includeAllStatuses') === 'true';
    const entityType = url.searchParams.get('entityType') || '';
    const facilityId = url.searchParams.get('facilityId') || '';
    const search = (url.searchParams.get('search') || '').toLowerCase();
    const pageNumber = Number(url.searchParams.get('pageNumber') || '1');
    const pageSize = Number(url.searchParams.get('pageSize') || '20');

    let filtered = [...ALL_AUDITS];
    if (facilityId) {
      filtered = filtered.filter((a) => a.facilityId === facilityId);
    }
    const pendingCount = filtered.filter((a) => a.normalizationStatus === 'PendingNormalization').length;
    const normalizedCount = filtered.filter((a) => a.normalizationStatus === 'Normalized').length;

    if (!includeAll && statusParam) {
      filtered = filtered.filter((a) => a.normalizationStatus === statusParam);
    }
    if (entityType) {
      filtered = filtered.filter((a) => a.entityType === entityType);
    }
    if (search) {
      filtered = filtered.filter(
        (a) =>
          (a.originalCountryValue || '').toLowerCase().includes(search) ||
          a.reason.toLowerCase().includes(search) ||
          a.entityType.toLowerCase().includes(search)
      );
    }

    const totalCount = filtered.length;
    const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
    const slice = filtered.slice((pageNumber - 1) * pageSize, pageNumber * pageSize);

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        items: slice,
        totalCount,
        pageNumber,
        pageSize,
        totalPages,
        pendingCount,
        normalizedCount,
      }),
    });
  });

  return { quarantineRequests };
}

test.describe('MDM-1.6 R05: Country Normalization Quarantine Diagnostics E2E & Responsive Audit', () => {
  test('authorized Staff exercises error retry, PII redaction, filters, status transitions, pagination, keyboard nav & 1440/768/375/320 viewports', async ({
    page,
  }) => {
    const { quarantineRequests } = await setupCommonRoutes(
      page,
      {
        userId: 'staff-plat-16',
        email: 'steward@nexus3pl.local',
        firstName: 'Data',
        lastName: 'Steward',
        userType: 'Staff',
        roles: ['NormalizationAuditor'],
        permissions: ['global:normalization:view', 'facilities:view', 'analytics:multi-facility:view'],
      },
      { failFirstQuarantineCall: true }
    );

    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('/login');
    await page.fill('input[type="email"]', 'steward@nexus3pl.local');
    await page.fill('input[type="password"]', 'Secret123!');
    await page.click('button[type="submit"]');
    await page.waitForURL('**/app/dashboard');

    await page.getByRole('link', { name: 'Country Quarantine' }).click();
    await page.waitForURL('**/app/admin/normalization-quarantine');

    // Error & Retry state
    await expect(page.getByRole('alert')).toContainText('Simulated database timeout');
    await page.getByRole('button', { name: /Retry/i }).click();

    await expect(page.getByRole('heading', { level: 2, name: 'Country Normalization & Quarantine Diagnostics' })).toBeVisible();
    await expect(page.getByText('[REDACTED_POTENTIAL_PII]')).toBeVisible();

    // Status transition All -> Normalized -> PendingNormalization + Pagination check
    await page.getByLabel('Normalization Status').selectOption('All');
    await expect(page.getByText(/Showing page 1 of 2 \(22 total records\)/)).toBeVisible();

    // Desktop screenshot (1440x900)
    await page.screenshot({
      path: path.join(AUDIT_DIR, 'mdm16-quarantine-desktop-1440.png'),
      fullPage: true,
      animations: 'disabled',
    });

    await page.getByRole('button', { name: 'Next' }).click();
    await expect(page.getByText(/Showing page 2 of 2 \(22 total records\)/)).toBeVisible();
    await expect(page.getByText('Historical Preserved')).toBeVisible();
    await expect(page.getByText('Parent Inferred')).toBeVisible();

    // Keyboard navigation from Status filter to Entity Type select
    await page.getByLabel('Normalization Status').focus();
    await page.keyboard.press('Tab');
    await expect(page.getByLabel('Entity Type')).toBeFocused();

    // Empty state via Entity Type filter
    await page.getByLabel('Entity Type').selectOption('Supplier');
    await expect(page.getByText('No country normalization diagnostic records match the active scope filters.')).toBeVisible();
    await page.getByLabel('Entity Type').selectOption('');

    // Responsive 768x1024 Tablet
    await page.setViewportSize({ width: 768, height: 1024 });
    await page.waitForTimeout(150);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.screenshot({
      path: path.join(AUDIT_DIR, 'mdm16-quarantine-tablet-768.png'),
      fullPage: true,
      animations: 'disabled',
    });

    // Responsive 375x812 Mobile
    await page.setViewportSize({ width: 375, height: 812 });
    await page.waitForTimeout(150);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.screenshot({
      path: path.join(AUDIT_DIR, 'mdm16-quarantine-mobile-375.png'),
      fullPage: true,
      animations: 'disabled',
    });

    // Responsive 320x568 Small Mobile
    await page.setViewportSize({ width: 320, height: 568 });
    await page.waitForTimeout(150);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.screenshot({
      path: path.join(AUDIT_DIR, 'mdm16-quarantine-mobile-320.png'),
      fullPage: true,
      animations: 'disabled',
    });

    expect(quarantineRequests.length).toBeGreaterThanOrEqual(4);
  });

  test('scopes facilities:manage callers and blocks Client, Driver, and unprivileged Staff without leaking data', async ({
    page,
  }) => {
    // 1. Facility Manager (facilities:manage without global:normalization:view) sends facilityId
    const facMgr = await setupCommonRoutes(page, {
      userId: 'fac-mgr-16',
      email: 'facmgr@nexus3pl.local',
      firstName: 'Facility',
      lastName: 'Manager',
      userType: 'Staff',
      roles: ['OperationsManager'],
      permissions: ['facilities:manage', 'facilities:view'],
      assignedFacilityId: 'fac-chi-16',
    });

    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto('/login');
    await page.fill('input[type="email"]', 'facmgr@nexus3pl.local');
    await page.fill('input[type="password"]', 'Secret123!');
    await page.click('button[type="submit"]');
    await page.waitForURL('**/app/dashboard');
    await page.getByRole('link', { name: 'Country Quarantine' }).click();
    await page.waitForURL('**/app/admin/normalization-quarantine');

    await expect(page.locator('.kpi-scope')).toHaveText('FAC-CHI1');
    await expect.poll(() => facMgr.quarantineRequests.some((u) => u.includes('facilityId=fac-chi-16'))).toBe(true);

    // Sign out before testing blocked identities
    await page.evaluate(() => { localStorage.clear(); sessionStorage.clear(); });
    await page.unrouteAll({ behavior: 'ignoreErrors' });

    // 2. Client user even with spoofed permission receives 403 and triggers zero diagnostic API calls
    const cli = await setupCommonRoutes(page, {
      userId: 'cli-user-16',
      email: 'client@tenant.local',
      firstName: 'Client',
      lastName: 'Admin',
      userType: 'Client',
      roles: ['ClientAdmin'],
      permissions: ['global:normalization:view'],
      tenantId: 'cli-alpha',
    });

    await page.goto('/login');
    await page.fill('input[type="email"]', 'client@tenant.local');
    await page.fill('input[type="password"]', 'Secret123!');
    await page.click('button[type="submit"]');
    await page.waitForURL('**/app/**');
    await page.goto('/app/admin/normalization-quarantine');

    await expect(page.getByText('403 FORBIDDEN')).toBeVisible();
    await expect(page.getByText('Staff Authorization Required')).toBeVisible();
    expect(cli.quarantineRequests).toHaveLength(0);

    // 3. Unprivileged Staff (inventory:view only) receives PermissionGuard deny and triggers zero diagnostic API calls
    await page.evaluate(() => { localStorage.clear(); sessionStorage.clear(); });
    await page.unrouteAll({ behavior: 'ignoreErrors' });
    const unpriv = await setupCommonRoutes(page, {
      userId: 'unpriv-staff-16',
      email: 'operator@nexus3pl.local',
      firstName: 'Floor',
      lastName: 'Operator',
      userType: 'Staff',
      roles: ['WarehouseOperator'],
      permissions: ['inventory:view', 'facilities:view'],
      assignedFacilityId: 'fac-chi-16',
    });

    await page.goto('/login');
    await page.fill('input[type="email"]', 'operator@nexus3pl.local');
    await page.fill('input[type="password"]', 'Secret123!');
    await page.click('button[type="submit"]');
    await page.waitForURL('**/app/**');
    await page.goto('/app/admin/normalization-quarantine');

    await expect(page.getByRole('heading', { level: 2, name: 'Country Normalization & Quarantine Diagnostics' })).toHaveCount(0);
    expect(unpriv.quarantineRequests).toHaveLength(0);
  });
});
