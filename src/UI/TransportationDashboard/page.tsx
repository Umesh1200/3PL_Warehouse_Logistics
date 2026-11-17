import { useCallback, useState } from 'react';
import { request, reportUrl, type Report } from './services/transportationApi';
import { useReport } from './hooks/useReport';
import { permissions, permitted, type Filters, type Page, type User } from './types/transportation';
import { label, ReportView } from './components/ReportView';

const reports: { id: Report; title: string; permission: string }[] = [
  { id: 'scorecard', title: 'Carrier scorecard', permission: permissions.performance },
  { id: 'transit-time-distribution', title: 'Transit distribution', permission: permissions.performance },
  { id: 'exceptions', title: 'Delivery exceptions', permission: permissions.performance },
  ...(['variance', 'profitability', 'accessorials', 'discrepancies', 'rate-card-compliance'] as Report[]).map(id => ({ id, title: label(id.replaceAll('-', ' ')), permission: permissions.financial })),
  { id: 'facility-claims', title: 'Facility claims readiness', permission: permissions.claims },
  { id: 'shipment-claims', title: 'Shipment claims readiness', permission: permissions.claims },
  { id: 'financial-reconciliation', title: 'Shipment reconciliation', permission: permissions.financial }
];
const today = new Date().toISOString().slice(0, 10);
const initial: Filters = { carrierCode: '', startDateUtc: today, endDateUtc: today, clientId: '', facilityId: '', serviceLevel: '', destinationCountry: '', destinationState: '', asOfUtc: '', statusFilter: '', minimumDiscrepancyUsd: '', shipmentId: '', claimType: 'Lost' };
export default function TransportationDashboard() {
  const [session, setSession] = useState<{ token: string; user: User } | null>(null);
  const [message, setMessage] = useState('');
  const [signingIn, setSigningIn] = useState(false);
  const [draft, setDraft] = useState(initial);
  const [selection, setSelection] = useState<{ report: Report; filters: Filters; page: number; revision: number } | null>(null);
  const [report, setReport] = useState<Report>('scorecard');
  const resetSession = useCallback(() => { setSession(null); setSelection(null); setDraft({ ...initial }); setReport('scorecard'); }, []);
  const expire = useCallback(() => { resetSession(); setMessage('Your session has expired. Sign in again.'); }, [resetSession]);
  const current = reports.find(r => r.id === report)!;
  const allowed = !!session && permitted(session.user, current.permission);
  const url = selection && allowed && selection.report === report ? reportUrl(report, selection.filters, selection.page) : null;
  const appliedDetails = url ? [...new URLSearchParams(url.split('?')[1])].filter(([key]) => ['serviceLevel', 'destinationCountry', 'destinationState', 'asOfUtc', 'statusFilter', 'minimumDiscrepancyUsd', 'claimType'].includes(key)) : [];
  const { data, error, loading } = useReport(url, session?.token ?? '', expire, selection?.revision);
  async function signIn(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault(); setMessage(''); setSigningIn(true);
    const form = new FormData(event.currentTarget);
    try {
      const auth = await request<{ accessToken: string }>('/api/v1/auth/login', '', undefined, { email: form.get('email'), password: form.get('password') });
      const user = await request<User>('/api/v1/auth/me', auth.accessToken);
      setSession({ token: auth.accessToken, user });
      const first = reports.find(r => permitted(user, r.permission)); if (first) setReport(first.id);
    } catch (e) { setMessage(e instanceof Error ? e.message : 'Unable to sign in.'); }
    finally { setSigningIn(false); }
  }
  function input(key: keyof Filters, title: string, type = 'text', required = false) {
    return <label>{title}<input name={key} value={draft[key]} type={type} required={required} onChange={e => setDraft({ ...draft, [key]: e.target.value })} {...(key === 'carrierCode' ? { pattern: '[A-Za-z0-9][A-Za-z0-9._-]{0,49}', maxLength: 50 } : {})} {...(key === 'minimumDiscrepancyUsd' ? { min: 0, step: '0.01' } : {})} /></label>;
  }
  function select(key: keyof Filters, title: string, values: string[], optional = true) {
    return <label>{title}<select value={draft[key]} onChange={e => setDraft({ ...draft, [key]: e.target.value })}>{optional && <option value="">All</option>}{values.map(value => <option key={value}>{value}</option>)}</select></label>;
  }
  const shipment = report === 'shipment-claims' || report === 'financial-reconciliation';
  const facility = report === 'facility-claims';
  const financial = current.permission === permissions.financial;
  const page = data && 'items' in data ? data as Page : null;
  return <div className="shell"><aside><div className="brand">W3<span>Warehouse intelligence</span></div><p className="eyebrow">TRANSPORTATION</p><nav aria-label="Reports">{reports.filter(r => session && permitted(session.user, r.permission)).map(r => <button key={r.id} className={r.id === report ? 'active' : ''} onClick={() => { setReport(r.id); setSelection(null); }}>{r.title}</button>)}</nav><p className="scope">Access follows your assigned tenant, facilities and permissions.</p></aside>
    <main><header><div><p className="eyebrow">OPERATIONS / ANALYTICS</p><h1>Transportation intelligence</h1><p>Carrier performance, freight economics and claims evidence.</p></div>{session && <div><span>{session.user.email}</span><button onClick={() => { resetSession(); setMessage(''); }}>Sign out</button></div>}</header>
    {!session ? <section className="panel login"><h2>Sign in to your workspace</h2><p>Use your warehouse account. Your credentials and access token stay in this session’s memory.</p><form onSubmit={signIn}><label>Email<input name="email" type="email" autoComplete="username" required /></label><label>Password<input name="password" type="password" autoComplete="current-password" required /></label><button className="primary" disabled={signingIn}>{signingIn ? 'Signing in…' : 'Sign in'}</button></form>{message && <p role="alert">{message}</p>}</section> : <>
      <section className="panel"><h2>{current.title}</h2>{!allowed ? <p>You do not have access to transportation reports. Ask your administrator for the required permission.</p> : <form className="filters" onSubmit={e => {
        e.preventDefault();
        if (financial && draft.asOfUtc && !Number.isFinite(new Date(draft.asOfUtc).getTime())) { setMessage('Choose a valid valuation date and time.'); return; }
        if (!shipment && !facility) { const days = (Date.parse(draft.endDateUtc) - Date.parse(draft.startDateUtc)) / 86400000; if (days < 0 || days > 365 || Number(draft.endDateUtc.slice(0, 4)) >= 9999) { setMessage('Choose an ordered UTC date range of at most 366 inclusive days, before year 9999.'); return; } }
        setMessage(''); setSelection({ report, filters: { ...draft }, page: 1, revision: Date.now() });
      }}>
        {shipment ? input('shipmentId', 'Shipment ID', 'text', true) : <>{!facility && <>{input('carrierCode', 'Carrier code', 'text', true)}{input('startDateUtc', 'Start date (UTC)', 'date', true)}{input('endDateUtc', 'End date (UTC)', 'date', true)}</>}{input('facilityId', facility ? 'Facility ID (required)' : 'Facility ID (optional)', 'text', facility)}{input('clientId', 'Client ID (optional)')}</>}
        {['scorecard', 'transit-time-distribution', 'profitability'].includes(report) && select('serviceLevel', 'Service level', ['StandardGround', 'NextDayAir', 'SecondDayAir', 'PriorityOvernight', 'FreightLTL', 'FreightFTL', 'CustomerPickup'])}
        {report === 'transit-time-distribution' && <>{input('destinationCountry', 'Destination country')}{input('destinationState', 'Destination state')}</>}
        {financial && input('asOfUtc', 'Valuation time (your local time)', 'datetime-local')}
        {report === 'discrepancies' && <>{select('statusFilter', 'Reconciliation status', ['Estimated', 'Invoiced', 'Reconciled', 'Disputed', 'WrittenOff'])}{input('minimumDiscrepancyUsd', 'Minimum discrepancy (USD)', 'number')}</>}
        {report === 'shipment-claims' && select('claimType', 'Claim type', ['Lost', 'Damaged', 'Delayed', 'Other'], false)}
        <button className="primary" type="submit">{loading ? 'Loading…' : 'Apply filters / refresh'}</button>
      </form>}{message && <p role="alert">{message}</p>}</section>
      {allowed && <section className="panel results" aria-live="polite" aria-busy={loading}><div className="result-heading"><h2>{current.title}</h2><span className="badge">Read only</span></div>{financial && <p className="notice">USD-labeled amounts use US dollars; conversion evidence preserves source currencies. Estimated, invoiced and realized margins are separate; realized margin is accrual based, not cash profit.</p>}{selection && <p>Applied scope: {shipment ? `Shipment ${selection.filters.shipmentId}` : facility ? `Facility ${selection.filters.facilityId} · Client: ${selection.filters.clientId || 'authorized clients'} · Active alerts across all dates and carriers` : `${selection.filters.carrierCode} · ${selection.filters.startDateUtc} – ${selection.filters.endDateUtc} UTC · Facility: ${selection.filters.facilityId || 'authorized facilities'} · Client: ${selection.filters.clientId || 'authorized clients'}`}</p>}
        {appliedDetails.length > 0 && <p>Applied report filters: {appliedDetails.map(([key, value]) => `${label(key)}: ${value}`).join(' · ')}</p>}
        {loading ? <p role="status">Loading authorized analytics…</p> : error ? <p role="alert">{error}</p> : data ? <ReportView data={data} /> : <p className="empty">Choose filters and apply to load current analytics.</p>}
        {page && <div className="pagination"><button disabled={selection!.page <= 1} onClick={() => setSelection({ ...selection!, page: selection!.page - 1 })}>Previous page</button><span>Page {page.pageNumber} of {Math.max(1, page.totalPages)}</span><button disabled={selection!.page >= page.totalPages || selection!.page >= 1000000} onClick={() => setSelection({ ...selection!, page: selection!.page + 1 })}>Next page</button></div>}
      </section>}
    </>}
    </main></div>;
}
