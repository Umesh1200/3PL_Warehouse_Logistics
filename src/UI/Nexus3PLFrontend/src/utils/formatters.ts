import { convertGregorianYmdToBs, convertInstantToBsInTimeZone } from './bikramSambat';

export type FormatterLocale = 'en-US' | 'en-NP' | 'ne-NP' | string;
export type WeightUnit = 'KG' | 'LB' | 'G' | 'OZ';
export type LengthUnit = 'CM' | 'M' | 'IN' | 'FT';

export interface FormatterContext {
  locale?: FormatterLocale;
  timeZone?: string | null;
  currencyCode?: string | null;
  minorUnits?: number;
  countryCode?: string | null;
  showDualBsDate?: boolean;
}

const CURRENCY_MINOR_UNITS: Record<string, number> = {
  USD: 2,
  NPR: 2,
  INR: 2,
  EUR: 2,
  GBP: 2,
  CAD: 2,
  AUD: 2,
  SGD: 2,
  CNY: 2,
  AED: 2,
  JPY: 0,
  KWD: 3,
};

// UN/ECE Rec 20 aligned factors (matching seeded backend UomConversion ratios)
const WEIGHT_FACTORS_FROM_KG: Record<WeightUnit, number> = {
  KG: 1,
  G: 1000,
  LB: 2.2046226218,
  OZ: 35.2739619496,
};

const LENGTH_FACTORS_FROM_CM: Record<LengthUnit, number> = {
  CM: 1,
  M: 0.01,
  IN: 0.3937007874,
  FT: 0.032808399,
};

function roundAwayFromZero(value: number, decimals: number): number {
  const factor = Math.pow(10, decimals);
  const scaled = Math.abs(value) * factor;
  const rounded = Math.round(scaled + Number.EPSILON);
  return Math.sign(value) * (rounded / factor);
}

export function resolveEffectiveLocale(ctx?: FormatterContext): string {
  if (ctx?.locale && ctx.locale.trim().length > 0) return ctx.locale.trim();
  const cc = (ctx?.countryCode ?? '').trim().toUpperCase();
  if (cc === 'NP' || cc === 'NPL') return 'en-NP';
  return 'en-US';
}

export function resolveEffectiveTimeZone(ctx?: FormatterContext): string {
  if (ctx?.timeZone && ctx.timeZone.trim().length > 0) {
    try {
      Intl.DateTimeFormat('en-US', { timeZone: ctx.timeZone.trim() });
      return ctx.timeZone.trim();
    } catch {
      // Invalid timezone string falls back safely to UTC rather than throwing in UI
    }
  }
  const cc = (ctx?.countryCode ?? '').trim().toUpperCase();
  if (cc === 'NP' || cc === 'NPL') return 'Asia/Kathmandu';
  return 'UTC';
}

const ISO_DATE_ONLY_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;

export function parseCivilDateParts(value: string): { year: number; month: number; day: number } | null {
  const match = ISO_DATE_ONLY_PATTERN.exec(value.trim());
  if (!match) return null;

  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const probe = new Date(Date.UTC(year, month - 1, day));
  if (
    probe.getUTCFullYear() !== year ||
    probe.getUTCMonth() !== month - 1 ||
    probe.getUTCDate() !== day
  ) {
    return null;
  }

  return { year, month, day };
}

/**
 * Formats a calendar date-only ISO string (`YYYY-MM-DD`) as a civil date without
 * applying UTC-to-local timezone offsets that could shift the calendar day backward or forward.
 */
export function formatCivilDate(
  value: string,
  ctx?: FormatterContext,
): string {
  const parts = parseCivilDateParts(value);
  if (!parts) return String(value);

  const locale = resolveEffectiveLocale(ctx);
  const timeZone = resolveEffectiveTimeZone(ctx);
  const utcMidday = new Date(Date.UTC(parts.year, parts.month - 1, parts.day, 12, 0, 0));
  const formattedAd = new Intl.DateTimeFormat(locale, {
    timeZone: 'UTC',
    year: 'numeric',
    month: 'short',
    day: '2-digit',
  }).format(utcMidday);

  const enableBs = ctx?.showDualBsDate ?? (ctx?.countryCode?.toUpperCase() === 'NP' || timeZone === 'Asia/Kathmandu');
  if (enableBs) {
    const bs = convertGregorianYmdToBs(parts.year, parts.month, parts.day);
    if (bs) {
      return `${formattedAd} (${bs.formatted})`;
    }
  }

  return formattedAd;
}

/**
 * Formats an instant timestamp (`Date` or ISO timestamp string) into a calendar date
 * inside the caller's configured IANA timezone.
 */
export function formatTimestampDate(
  value: Date | string,
  ctx?: FormatterContext,
): string {
  const date = typeof value === 'string' ? new Date(value) : value;
  if (Number.isNaN(date.getTime())) return String(value);

  const locale = resolveEffectiveLocale(ctx);
  const timeZone = resolveEffectiveTimeZone(ctx);
  const formattedAd = new Intl.DateTimeFormat(locale, {
    timeZone,
    year: 'numeric',
    month: 'short',
    day: '2-digit',
  }).format(date);

  const enableBs = ctx?.showDualBsDate ?? (ctx?.countryCode?.toUpperCase() === 'NP' || timeZone === 'Asia/Kathmandu');
  if (enableBs) {
    const bs = convertInstantToBsInTimeZone(date, timeZone);
    if (bs) {
      return `${formattedAd} (${bs.formatted})`;
    }
  }

  return formattedAd;
}

export function formatDate(
  value: Date | string | null | undefined,
  ctx?: FormatterContext,
): string {
  if (value === null || value === undefined || value === '') return '—';
  if (typeof value === 'string' && ISO_DATE_ONLY_PATTERN.test(value.trim())) {
    return formatCivilDate(value, ctx);
  }
  return formatTimestampDate(value, ctx);
}

export function formatDateTime(
  value: Date | string | null | undefined,
  ctx?: FormatterContext,
): string {
  if (value === null || value === undefined || value === '') return '—';
  const date = typeof value === 'string' ? new Date(value) : value;
  if (Number.isNaN(date.getTime())) return String(value);

  const locale = resolveEffectiveLocale(ctx);
  const timeZone = resolveEffectiveTimeZone(ctx);
  const formatted = new Intl.DateTimeFormat(locale, {
    timeZone,
    year: 'numeric',
    month: 'short',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
    timeZoneName: 'short',
  }).format(date);

  const enableBs = ctx?.showDualBsDate ?? (ctx?.countryCode?.toUpperCase() === 'NP' || timeZone === 'Asia/Kathmandu');
  if (enableBs) {
    const bs = convertInstantToBsInTimeZone(date, timeZone);
    if (bs) {
      return `${formatted} [${bs.formatted}]`;
    }
  }

  return formatted;
}

export function formatTime(
  value: Date | string | null | undefined,
  ctx?: FormatterContext,
): string {
  if (value === null || value === undefined || value === '') return '--:--:--';
  const date = typeof value === 'string' ? new Date(value) : value;
  if (Number.isNaN(date.getTime())) return String(value);

  const locale = resolveEffectiveLocale(ctx);
  const timeZone = resolveEffectiveTimeZone(ctx);
  return new Intl.DateTimeFormat(locale, {
    timeZone,
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
    timeZoneName: 'short',
  }).format(date);
}

export function formatNumber(
  value: number | null | undefined,
  ctx?: FormatterContext,
  fractionDigits?: number,
): string {
  if (value === null || value === undefined || Number.isNaN(value)) return '—';
  const locale = resolveEffectiveLocale(ctx);
  return new Intl.NumberFormat(locale, fractionDigits !== undefined
    ? { minimumFractionDigits: fractionDigits, maximumFractionDigits: fractionDigits }
    : undefined).format(value);
}

export function formatCurrency(
  amount: number | null | undefined,
  currencyCode?: string | null,
  ctx?: FormatterContext,
): string {
  if (amount === null || amount === undefined || Number.isNaN(amount)) return '—';
  const resolvedCurrency = (currencyCode ?? ctx?.currencyCode ?? '').trim().toUpperCase();
  if (!/^[A-Z]{3}$/.test(resolvedCurrency)) {
    // Fail-safe explicit representation: never silently stamp "USD" when currency is missing/unconfigured.
    return `${formatNumber(amount, ctx, ctx?.minorUnits ?? 2)} (currency unset)`;
  }

  const locale = resolveEffectiveLocale(ctx);
  const minorUnits = ctx?.minorUnits ?? CURRENCY_MINOR_UNITS[resolvedCurrency] ?? 2;
  const rounded = roundAwayFromZero(amount, minorUnits);

  return new Intl.NumberFormat(locale, {
    style: 'currency',
    currency: resolvedCurrency,
    minimumFractionDigits: minorUnits,
    maximumFractionDigits: minorUnits,
  }).format(rounded);
}

export function formatWeight(
  valueKg: number | null | undefined,
  targetUnit: WeightUnit = 'KG',
  ctx?: FormatterContext,
  decimals: number = 2,
): string {
  if (valueKg === null || valueKg === undefined || Number.isNaN(valueKg)) return '—';
  const factor = WEIGHT_FACTORS_FROM_KG[targetUnit] ?? 1;
  const converted = roundAwayFromZero(valueKg * factor, decimals);
  return `${formatNumber(converted, ctx, decimals)} ${targetUnit.toLowerCase()}`;
}

export function formatDimensions(
  lengthCm: number | null | undefined,
  widthCm: number | null | undefined,
  heightCm: number | null | undefined,
  targetUnit: LengthUnit = 'CM',
  ctx?: FormatterContext,
  decimals: number = 2,
): string {
  if (
    lengthCm === null || lengthCm === undefined ||
    widthCm === null || widthCm === undefined ||
    heightCm === null || heightCm === undefined
  ) {
    return '—';
  }
  const factor = LENGTH_FACTORS_FROM_CM[targetUnit] ?? 1;
  const l = formatNumber(roundAwayFromZero(lengthCm * factor, decimals), ctx, decimals);
  const w = formatNumber(roundAwayFromZero(widthCm * factor, decimals), ctx, decimals);
  const h = formatNumber(roundAwayFromZero(heightCm * factor, decimals), ctx, decimals);
  return `${l} × ${w} × ${h} ${targetUnit.toLowerCase()}`;
}
