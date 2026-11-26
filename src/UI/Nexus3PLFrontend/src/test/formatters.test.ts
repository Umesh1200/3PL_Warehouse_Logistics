import { describe, it, expect } from 'vitest';
import {
  formatCivilDate,
  formatCurrency,
  formatDate,
  formatDateTime,
  formatDimensions,
  formatNumber,
  formatTime,
  formatTimestampDate,
  formatWeight,
} from '../utils/formatters';
import {
  computeNepaliFiscalYearLabel,
  convertGregorianYmdToBs,
  convertInstantToBsInTimeZone,
} from '../utils/bikramSambat';

describe('MDM-1.4 Frontend Localization Formatters & Bikram Sambat Helpers', () => {
  it('formats currencies with ISO minor units (USD=2, NPR=2, JPY=0, KWD=3) and never defaults blank currency to USD', () => {
    expect(formatCurrency(1234.5, 'USD', { locale: 'en-US' })).toBe('$1,234.50');
    expect(formatCurrency(152.5, 'JPY', { locale: 'en-US' })).toBe('¥153');
    expect(formatCurrency(12.3456, 'KWD', { locale: 'en-US' })).toContain('12.346');
    const npr = formatCurrency(1600.75, 'NPR', { locale: 'en-NP' });
    expect(npr).toContain('1,600.75');
    expect(npr).toMatch(/NPR|रू|Rs/);

    const missing = formatCurrency(250, null, { locale: 'en-US' });
    expect(missing).toContain('250.00');
    expect(missing).toContain('currency unset');
    expect(missing).not.toContain('USD');
  });

  it('converts Bikram Sambat dates accurately across Ashadh 32 -> Shrawan 1 fiscal boundary and Asia/Kathmandu midnight', () => {
    // 2083-03-32 BS (Ashadh End) == 2026-07-16 AD -> FY 2082/83
    const ashadhEnd = convertGregorianYmdToBs(2026, 7, 16)!;
    expect(ashadhEnd.bsYear).toBe(2083);
    expect(ashadhEnd.bsMonth).toBe(3);
    expect(ashadhEnd.bsDay).toBe(32);
    expect(ashadhEnd.fiscalYearLabel).toBe('2082/83');

    // 2083-04-01 BS (Shrawan 1) == 2026-07-17 AD -> FY 2083/84
    const shrawanStart = convertGregorianYmdToBs(2026, 7, 17)!;
    expect(shrawanStart.bsYear).toBe(2083);
    expect(shrawanStart.bsMonth).toBe(4);
    expect(shrawanStart.bsDay).toBe(1);
    expect(shrawanStart.fiscalYearLabel).toBe('2083/84');

    // Timezone rollover: 2026-07-16T18:10:00Z is 2026-07-16 23:55 in Kathmandu (2083-03-32)
    // while 2026-07-16T18:20:00Z is 2026-07-17 00:05 in Kathmandu (2083-04-01)!
    const beforeNpMidnight = convertInstantToBsInTimeZone('2026-07-16T18:10:00Z', 'Asia/Kathmandu')!;
    const afterNpMidnight = convertInstantToBsInTimeZone('2026-07-16T18:20:00Z', 'Asia/Kathmandu')!;
    expect(beforeNpMidnight.formatted).toBe('2083-03-32 BS');
    expect(beforeNpMidnight.fiscalYearLabel).toBe('2082/83');
    expect(afterNpMidnight.formatted).toBe('2083-04-01 BS');
    expect(afterNpMidnight.fiscalYearLabel).toBe('2083/84');

    expect(computeNepaliFiscalYearLabel(2083, 12)).toBe('2083/84');
    expect(computeNepaliFiscalYearLabel(2084, 3)).toBe('2083/84');
    expect(computeNepaliFiscalYearLabel(2084, 4)).toBe('2084/85');
  });

  it('formats dates and times using facility IANA timezone and dual BS display without UTC hardcoding', () => {
    const iso = '2026-07-16T18:20:00Z';
    const npDate = formatDate(iso, { timeZone: 'Asia/Kathmandu', countryCode: 'NP', locale: 'en-NP' });
    expect(npDate).toContain('2083-04-01 BS');

    const npDateTime = formatDateTime(iso, { timeZone: 'Asia/Kathmandu', countryCode: 'NP', locale: 'en-US' });
    expect(npDateTime).toContain('00:05:00');
    expect(npDateTime).toContain('2083-04-01 BS');

    const chiTime = formatTime('2026-07-16T18:20:00Z', { timeZone: 'America/Chicago', locale: 'en-US' });
    expect(chiTime).toContain('13:20:00');
    expect(chiTime).not.toContain('UTC');

    expect(formatNumber(14500, { locale: 'en-US' })).toBe('14,500');
  });

  it('preserves civil calendar dates across timezones while converting timestamps across Kathmandu and US DST transitions', () => {
    // Civil date-only ISO string MUST NOT shift backward in negative-offset timezones like America/Chicago
    const civilDate = '2026-07-17';
    expect(formatDate(civilDate, { timeZone: 'UTC', locale: 'en-US' })).toContain('Jul 17, 2026');
    expect(formatDate(civilDate, { timeZone: 'America/Chicago', locale: 'en-US' })).toContain('Jul 17, 2026');
    expect(formatCivilDate(civilDate, { timeZone: 'America/Chicago', locale: 'en-US' })).toContain('Jul 17, 2026');
    const npCivil = formatDate(civilDate, { timeZone: 'Asia/Kathmandu', countryCode: 'NP', locale: 'en-NP' });
    expect(npCivil).toContain('2083-04-01 BS');
    expect(npCivil).toContain('17');

    // Full UTC timestamps DO convert across US/Nepal timezone boundaries
    expect(formatTimestampDate('2026-07-17T01:30:00Z', { timeZone: 'America/Chicago', locale: 'en-US' })).toContain('Jul 16, 2026');
    expect(formatDate('2026-07-17T01:30:00Z', { timeZone: 'America/Chicago', locale: 'en-US' })).toContain('Jul 16, 2026');

    // US Central DST transitions (2026-03-08 spring forward CDT; 2026-11-01 fall back CST)
    expect(formatDateTime('2026-03-08T07:30:00Z', { timeZone: 'America/Chicago', locale: 'en-US' })).toContain('01:30:00');
    expect(formatDateTime('2026-03-08T08:30:00Z', { timeZone: 'America/Chicago', locale: 'en-US' })).toContain('03:30:00');
    expect(formatDateTime('2026-11-01T05:30:00Z', { timeZone: 'America/Chicago', locale: 'en-US' })).toContain('00:30:00');
    expect(formatDateTime('2026-11-01T07:30:00Z', { timeZone: 'America/Chicago', locale: 'en-US' })).toContain('01:30:00');
  });

  it('formats weights and dimensions consistently from canonical metric storage', () => {
    expect(formatWeight(10, 'KG', { locale: 'en-US' })).toBe('10.00 kg');
    expect(formatWeight(10, 'LB', { locale: 'en-US' })).toBe('22.05 lb');
    expect(formatDimensions(100, 50, 25.4, 'CM', { locale: 'en-US' })).toBe('100.00 × 50.00 × 25.40 cm');
    expect(formatDimensions(100, 50, 25.4, 'IN', { locale: 'en-US' })).toBe('39.37 × 19.69 × 10.00 in');
  });
});
