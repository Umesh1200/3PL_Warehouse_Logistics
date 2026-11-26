/**
 * Authoritative Bikram Sambat (BS) month calendar lookup table (2075 BS - 2090 BS).
 * Source: amitgaru/nepali-datetime calendar_bs.csv (SHA256 verified on backend BS_Calendar_2075_2090.csv),
 * epoch 2075-01-01 BS == 2018-04-14 AD.
 */
export interface BsDateParts {
  bsYear: number;
  bsMonth: number;
  bsDay: number;
  monthNameEn: string;
  monthNameNe: string;
  fiscalYearLabel: string;
  formatted: string;
}

const BS_MONTH_NAMES_EN = [
  'Baishakh', 'Jestha', 'Ashadh', 'Shrawan', 'Bhadra', 'Ashwin',
  'Kartik', 'Mangsir', 'Poush', 'Magh', 'Falgun', 'Chaitra',
] as const;

const BS_MONTH_NAMES_NE = [
  'बैशाख', 'जेठ', 'असार', 'साउन', 'भदौ', 'असोज',
  'कार्तिक', 'मंसिर', 'पुष', 'माघ', 'फागुन', 'चैत',
] as const;

const BS_YEAR_MONTH_DAYS: Record<number, readonly number[]> = {
  2075: [31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30],
  2076: [31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 30],
  2077: [31, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31],
  2078: [31, 31, 31, 32, 31, 31, 30, 29, 30, 29, 30, 30],
  2079: [31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30],
  2080: [31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 30],
  2081: [31, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31],
  2082: [31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30],
  2083: [31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30],
  2084: [31, 31, 32, 31, 31, 30, 30, 30, 29, 30, 30, 30],
  2085: [31, 32, 31, 32, 30, 31, 30, 30, 29, 30, 30, 30],
  2086: [30, 32, 31, 32, 31, 30, 30, 30, 29, 30, 30, 30],
  2087: [31, 31, 32, 31, 31, 31, 30, 29, 30, 30, 30, 30],
  2088: [30, 31, 32, 32, 30, 31, 30, 30, 29, 30, 30, 30],
  2089: [30, 32, 31, 32, 31, 30, 30, 30, 29, 30, 30, 30],
  2090: [30, 32, 31, 32, 31, 30, 30, 30, 29, 30, 30, 30],
};

// 2075-01-01 BS == 2018-04-14 AD
const EPOCH_UTC_DAYS = Math.floor(Date.UTC(2018, 3, 14) / 86_400_000);

export function computeNepaliFiscalYearLabel(bsYear: number, bsMonth: number): string {
  const startYear = bsMonth >= 4 ? bsYear : bsYear - 1;
  const endTwoDigits = (startYear + 1) % 100;
  return `${startYear}/${String(endTwoDigits).padStart(2, '0')}`;
}

export function convertGregorianYmdToBs(year: number, month: number, day: number): BsDateParts | null {
  const targetUtcDays = Math.floor(Date.UTC(year, month - 1, day) / 86_400_000);
  let remaining = targetUtcDays - EPOCH_UTC_DAYS;
  if (remaining < 0) return null;

  for (let bsYear = 2075; bsYear <= 2090; bsYear++) {
    const months = BS_YEAR_MONTH_DAYS[bsYear];
    for (let mIdx = 0; mIdx < 12; mIdx++) {
      const daysInMonth = months[mIdx];
      if (remaining < daysInMonth) {
        const bsMonth = mIdx + 1;
        const bsDay = remaining + 1;
        return {
          bsYear,
          bsMonth,
          bsDay,
          monthNameEn: BS_MONTH_NAMES_EN[mIdx],
          monthNameNe: BS_MONTH_NAMES_NE[mIdx],
          fiscalYearLabel: computeNepaliFiscalYearLabel(bsYear, bsMonth),
          formatted: `${bsYear}-${String(bsMonth).padStart(2, '0')}-${String(bsDay).padStart(2, '0')} BS`,
        };
      }
      remaining -= daysInMonth;
    }
  }

  return null;
}

export function convertInstantToBsInTimeZone(
  value: Date | string,
  timeZone: string = 'Asia/Kathmandu',
): BsDateParts | null {
  const date = typeof value === 'string' ? new Date(value) : value;
  if (Number.isNaN(date.getTime())) return null;

  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(date);

  const year = Number(parts.find((p) => p.type === 'year')?.value);
  const month = Number(parts.find((p) => p.type === 'month')?.value);
  const day = Number(parts.find((p) => p.type === 'day')?.value);
  if (!year || !month || !day) return null;

  return convertGregorianYmdToBs(year, month, day);
}
