import { act, renderHook, waitFor, cleanup } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { useReport } from './useReport';
afterEach(cleanup);
it('cancels obsolete requests and hides previous scope data immediately', async () => {
  let resolveOld!: (value: unknown) => void;
  const fetch = vi.fn().mockImplementationOnce(() => new Promise(resolve => { resolveOld = resolve; })).mockResolvedValue({ ok: true, json: async () => ({ carrierCode: 'NEW' }) });
  vi.stubGlobal('fetch', fetch); const expired = vi.fn();
  const { result, rerender } = renderHook(({ url, token }) => useReport(url, token, expired), { initialProps: { url: '/old', token: 'a' } });
  rerender({ url: '/new', token: 'a' });
  expect(fetch.mock.calls[0][1].signal.aborted).toBe(true);
  await waitFor(() => expect(result.current.data).toEqual({ carrierCode: 'NEW' }));
  await act(async () => resolveOld({ ok: true, json: async () => ({ carrierCode: 'OLD' }) }));
  expect(result.current.data).toEqual({ carrierCode: 'NEW' });
  rerender({ url: '/new', token: '' }); expect(result.current.data).toBeUndefined();
});
it('expires authentication on 401 and displays permission errors on 403', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce({ ok: false, status: 401, json: async () => ({}) }).mockResolvedValueOnce({ ok: false, status: 403, json: async () => ({}) }));
  const expired = vi.fn();
  const { result, rerender } = renderHook(({ url }) => useReport(url, 'token', expired), { initialProps: { url: '/first' } });
  await waitFor(() => expect(expired).toHaveBeenCalledOnce());
  rerender({ url: '/second' });
  await waitFor(() => expect(result.current.error).toContain('permission'));
});
