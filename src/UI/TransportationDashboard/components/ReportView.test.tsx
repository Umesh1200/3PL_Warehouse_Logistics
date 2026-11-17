import { render, screen, cleanup } from '@testing-library/react';
import { afterEach, expect, it } from 'vitest';
import { format, ReportView } from './ReportView';
afterEach(cleanup);
it('preserves unavailable denominators instead of converting them to zero', () => {
  expect(format(null)).toBe('Not available'); expect(format(undefined)).toBe('Not available'); expect(format(0)).toBe('0');
  expect(format(0.00007)).not.toBe('0');
  render(<ReportView data={{ totalDispatchedCount: 0, totalDeliveredCount: 0 }} />);
  expect(screen.getAllByText('Not available').length).toBeGreaterThan(0);
});
it('renders empty pages and literal untrusted evidence safely', () => {
  const { rerender } = render(<ReportView data={{ items: [], totalCount: 0, pageNumber: 1, totalPages: 0 }} />);
  expect(screen.getByText('No records match this authorized scope.')).toBeTruthy();
  rerender(<ReportView data={{ reason: '<script>bad()</script>' }} />);
  expect(screen.getByText('<script>bad()</script>')).toBeTruthy(); expect(document.querySelector('script')).toBeNull();
});
