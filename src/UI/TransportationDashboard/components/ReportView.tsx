import type { AnalyticsRecord, Page } from '../types/transportation';
export const label = (value: string) => value.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/^./, c => c.toUpperCase()).replace(/Usd/g, '(USD)').replace(/Utc/g, '(UTC)');
export function format(value: unknown): string {
  if (value === null || value === undefined) return 'Not available';
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  if (typeof value === 'number') return value.toLocaleString(undefined, { maximumSignificantDigits: 15 });
  return String(value);
}
function Value({ value }: { value: unknown }) {
  if (Array.isArray(value)) return value.length ? <ol>{value.map((item, i) => <li key={i}>{typeof item === 'object' && item !== null ? <RecordView data={item as AnalyticsRecord} /> : format(item)}</li>)}</ol> : <span>No records</span>;
  if (typeof value === 'object' && value !== null) return <RecordView data={value as AnalyticsRecord} />;
  return <>{format(value)}</>;
}
function RecordView({ data }: { data: AnalyticsRecord }) {
  return <dl className="metrics">{Object.entries(data).map(([key, value]) => <div key={key}><dt>{label(key)}</dt><dd><Value value={value} /></dd></div>)}</dl>;
}
export function ReportView({ data }: { data: AnalyticsRecord | Page }) {
  if ('items' in data && Array.isArray(data.items)) return <>
    <p>{format(data.totalCount)} matching records</p>
    {data.items.length === 0 ? <p className="empty">No records match this authorized scope.</p> : data.items.map((item, i) => <article className="row" key={i}><RecordView data={item} /></article>)}
  </>;
  const record = data as AnalyticsRecord;
  const buckets = [['Less than 1 day', 'lessThan1DayCount'], ['1–2 days', 'oneToTwoDaysCount'], ['2–3 days', 'twoToThreeDaysCount'], ['3–5 days', 'threeToFiveDaysCount'], ['More than 5 days', 'moreThanFiveDaysCount']];
  const max = Math.max(1, ...buckets.map(([, key]) => Number(record[key] ?? 0)));
  return <>
    {'totalDispatchedCount' in record && <dl className="metrics">{['totalDispatchedCount', 'totalDeliveredCount', 'onTimeDeliveryRatePercent', 'firstAttemptDeliveryRatePercent', 'exceptionRatePercent', 'averageTransitDays'].map(key => <div key={key}><dt>{label(key)}</dt><dd>{format(record[key])}</dd></div>)}</dl>}
    {'totalShipmentsCount' in record && 'estimatedGrossMarginUsd' in record && <dl className="metrics">{['estimatedGrossMarginUsd', 'estimatedMarginPercent', 'invoicedGrossMarginUsd', 'invoicedMarginPercent', 'realizedGrossMarginUsd', 'realizedMarginPercent'].map(key => <div key={key}><dt>{label(key)}</dt><dd>{format(record[key])}</dd></div>)}</dl>}
    {'sampleSize' in record && <p className="notice">Sample: {format(record.sampleSize)} · {record.isStatisticallyReliable ? 'Statistically reliable' : 'Insufficient sample for statistical reliability'}</p>}
    {'lessThan1DayCount' in record && <figure aria-label="Transit time distribution"><figcaption>Transit time distribution</figcaption>{buckets.map(([name, key]) => <div className="bar-row" key={key}><span>{name}</span><div className="bar" style={{ width: `${Number(record[key] ?? 0) / max * 60}%` }} /><strong>{format(record[key])}</strong></div>)}</figure>}
    <RecordView data={record} />
  </>;
}
