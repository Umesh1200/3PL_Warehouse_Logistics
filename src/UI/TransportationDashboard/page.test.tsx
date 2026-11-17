import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import TransportationDashboard from './page';
import { permissions } from './types/transportation';
afterEach(cleanup);
function mockApi(grants: string[]) {
  const fetch = vi.fn(async (path: string) => ({ ok: true, json: async () => path.endsWith('/login') ? { accessToken: 'secret' } : path.endsWith('/me') ? { email: 'user@example.com', firstName: 'User', roles: [], permissions: grants } : { totalDispatchedCount: 2, totalDeliveredCount: 1 } }));
  vi.stubGlobal('fetch', fetch); return fetch;
}
async function login() {
  fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'user@example.com' } });
  fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'password' } });
  fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
  await screen.findByRole('button', { name: 'Sign out' });
}
it('loads profile before showing independent permission-gated reports, clears data on logout', async () => {
  const fetch = mockApi([permissions.performance]); render(<TransportationDashboard />); await login();
  expect(screen.queryByRole('button', { name: 'Profitability' })).toBeNull();
  expect(screen.queryByRole('button', { name: 'Facility claims readiness' })).toBeNull();
  expect(fetch.mock.calls.map(call => call[0])).toEqual(['/api/v1/auth/login', '/api/v1/auth/me']);
  fireEvent.change(screen.getByLabelText('Carrier code'), { target: { value: 'FEDEX' } });
  fireEvent.click(screen.getByRole('button', { name: 'Apply filters / refresh' }));
  await screen.findAllByText('Total Dispatched Count');
  expect(fetch.mock.calls[2][0]).toContain('/carriers/FEDEX/scorecard?');
  fireEvent.click(screen.getByRole('button', { name: 'Sign out' }));
  expect(screen.queryByText('Total Dispatched Count')).toBeNull();
});
it('claims-only user can page facility claims without performance access', async () => {
  const fetch = mockApi([permissions.claims]);
  fetch.mockImplementation(async (path: string) => ({ ok: true, json: async () => path.endsWith('/login') ? { accessToken: 'secret' } : path.endsWith('/me') ? { email: 'user@example.com', firstName: '', roles: [], permissions: [permissions.claims] } : { items: [{ trackingNumber: 'TRACK' }], totalCount: 26, pageNumber: path.includes('pageNumber=2') ? 2 : 1, totalPages: 2 } } as never));
  render(<TransportationDashboard />); await login();
  expect(screen.queryByLabelText('Carrier code')).toBeNull();
  fireEvent.change(screen.getByLabelText('Facility ID (required)'), { target: { value: 'facility' } });
  fireEvent.click(screen.getByRole('button', { name: 'Apply filters / refresh' }));
  await screen.findByText('TRACK');
  fireEvent.click(screen.getByRole('button', { name: 'Next page' }));
  await screen.findByText('Page 2 of 2');
  expect(fetch.mock.calls[3][0]).toContain('pageNumber=2');
  expect(fetch.mock.calls[3][0]).not.toContain('startDateUtc');
});

it.each(['signout', 'expiry'])('clears the previous account filters after %s', async reason => {
  const fetch = mockApi([permissions.performance]);
  render(<TransportationDashboard />); await login();
  fireEvent.change(screen.getByLabelText('Carrier code'), { target: { value: 'PRIVATE-CARRIER' } });
  fireEvent.change(screen.getByLabelText('Client ID (optional)'), { target: { value: 'previous-client' } });
  fireEvent.change(screen.getByLabelText('Facility ID (optional)'), { target: { value: 'previous-facility' } });
  if (reason === 'signout') fireEvent.click(screen.getByRole('button', { name: 'Sign out' }));
  else {
    fetch.mockResolvedValueOnce({ ok: false, status: 401, json: async () => ({}) } as never);
    fireEvent.click(screen.getByRole('button', { name: 'Apply filters / refresh' }));
    await screen.findByRole('button', { name: 'Sign in' });
  }
  await login();
  expect((screen.getByLabelText('Carrier code') as HTMLInputElement).value).toBe('');
  expect((screen.getByLabelText('Client ID (optional)') as HTMLInputElement).value).toBe('');
  expect((screen.getByLabelText('Facility ID (optional)') as HTMLInputElement).value).toBe('');
  expect(screen.queryByText(/PRIVATE-CARRIER/)).toBeNull();
});

it('identifies applied service and destination filters separately from unsubmitted edits', async () => {
  mockApi([permissions.performance]); render(<TransportationDashboard />); await login();
  fireEvent.click(screen.getByRole('button', { name: 'Transit distribution' }));
  fireEvent.change(screen.getByLabelText('Carrier code'), { target: { value: 'FEDEX' } });
  fireEvent.change(screen.getByLabelText('Service level'), { target: { value: 'FreightLTL' } });
  fireEvent.change(screen.getByLabelText('Destination country'), { target: { value: 'US' } });
  fireEvent.click(screen.getByRole('button', { name: 'Apply filters / refresh' }));
  expect(screen.getByText('Applied report filters: Service Level: FreightLTL · Destination Country: US')).toBeTruthy();
  fireEvent.change(screen.getByLabelText('Destination country'), { target: { value: 'CA' } });
  expect(screen.getByText('Applied report filters: Service Level: FreightLTL · Destination Country: US')).toBeTruthy();
});
