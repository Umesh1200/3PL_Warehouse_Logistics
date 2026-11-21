import React from 'react';
import { PutawayTaskDto, ReceivingSessionDto } from '../../types/inbound';
import { AppIcon } from '../common/AppIcon';
import { Link } from '../../context/RouterContext';

export interface WorkQueuesSectionProps {
  putawayTasks: PutawayTaskDto[];
  loadingPutaway: boolean;
  putawayRestricted: boolean;
  putawayError: string | null;
  receivingSessions: ReceivingSessionDto[];
  loadingReceiving: boolean;
  receivingRestricted: boolean;
  receivingError: string | null;
}

export const WorkQueuesSection: React.FC<WorkQueuesSectionProps> = ({
  putawayTasks,
  loadingPutaway,
  putawayRestricted,
  putawayError,
  receivingSessions,
  loadingReceiving,
  receivingRestricted,
  receivingError,
}) => {
  const getPutawayStatusBadge = (status: string) => {
    switch (status) {
      case 'Pending':
        return <span className="badge badge-warning">Pending</span>;
      case 'Assigned':
      case 'InTransit':
        return <span className="badge badge-info">{status}</span>;
      case 'Completed':
        return <span className="badge badge-success">Completed</span>;
      case 'ExceptionHold':
      case 'Cancelled':
        return <span className="badge badge-danger">{status}</span>;
      default:
        return <span className="badge">{status}</span>;
    }
  };

  const getReceivingStatusBadge = (status: string) => {
    switch (status) {
      case 'Started':
      case 'InProgress':
        return <span className="badge badge-info">{status}</span>;
      case 'Completed':
        return <span className="badge badge-success">Completed</span>;
      case 'Flagged':
        return <span className="badge badge-warning">Flagged</span>;
      case 'Cancelled':
        return <span className="badge badge-danger">Cancelled</span>;
      default:
        return <span className="badge">{status}</span>;
    }
  };

  return (
    <div className="work-queues-grid" role="region" aria-label="Warehouse Execution Queues">
      {/* 1. Directed Putaway Staging Queue */}
      <div className="queue-card" data-testid="queue-putaway">
        <div className="queue-header">
          <div className="queue-title-group">
            <AppIcon name="PackageCheck" size="sm" color="primary" />
            <h3 className="queue-title">Directed Putaway Queue</h3>
            {!loadingPutaway && !putawayRestricted && (
              <span className="badge badge-info" style={{ fontSize: '11px' }}>
                {putawayTasks.length} active
              </span>
            )}
          </div>
          <Link to="/app/inbound/putaway" className="queue-action-link" aria-label="Launch RF Scanner">
            <span>Launch RF Scanner</span>
            <AppIcon name="ChevronRight" size="xs" color="primary" />
          </Link>
        </div>

        {putawayRestricted ? (
          <div className="queue-empty">
            <AppIcon name="Lock" size="md" color="muted" />
            <p>Requires inbound putaway execution authorization.</p>
          </div>
        ) : putawayError ? (
          <div className="queue-empty" style={{ color: 'var(--status-danger-text)' }}>
            <AppIcon name="AlertTriangle" size="md" color="danger" />
            <p>{putawayError}</p>
          </div>
        ) : loadingPutaway ? (
          <div style={{ padding: '20px' }}>
            <div className="skeleton-line" style={{ marginBottom: '10px' }} />
            <div className="skeleton-line" style={{ marginBottom: '10px' }} />
            <div className="skeleton-line" />
          </div>
        ) : putawayTasks.length === 0 ? (
          <div className="queue-empty">
            <AppIcon name="CheckCircle2" size="lg" color="success" />
            <p><strong>Staging buffer is clear</strong></p>
            <p style={{ fontSize: '12px' }}>All received pallets have been directed or assigned to racks.</p>
          </div>
        ) : (
          <div className="queue-table-wrap">
            <table className="queue-table" aria-label="Pending Directed Putaway Tasks">
              <thead>
                <tr>
                  <th scope="col">LPN</th>
                  <th scope="col">SKU / Item</th>
                  <th scope="col">Staging &rarr; Rack</th>
                  <th scope="col" style={{ textAlign: 'center' }}>Qty</th>
                  <th scope="col" style={{ textAlign: 'right' }}>Status</th>
                </tr>
              </thead>
              <tbody>
                {putawayTasks.slice(0, 5).map((t) => (
                  <tr key={t.id}>
                    <td>
                      <code style={{ fontWeight: 700, color: 'var(--brand-primary)' }}>
                        {t.lpnCode || t.id.substring(0, 8)}
                      </code>
                    </td>
                    <td>
                      <div style={{ fontWeight: 600 }}>{t.sku}</div>
                      <div style={{ fontSize: '11.5px', color: 'var(--text-muted)' }}>{t.itemDescription}</div>
                    </td>
                    <td>
                      <span style={{ fontSize: '12px' }}>
                        {t.sourceLocationCode} &rarr; <strong>{t.suggestedLocationCode}</strong>
                      </span>
                    </td>
                    <td style={{ textAlign: 'center', fontWeight: 650 }}>{t.quantity}</td>
                    <td style={{ textAlign: 'right' }}>{getPutawayStatusBadge(t.status)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* 2. Inbound Goods Receiving Sessions */}
      <div className="queue-card" data-testid="queue-receiving">
        <div className="queue-header">
          <div className="queue-title-group">
            <AppIcon name="ArrowDownToLine" size="sm" color="primary" />
            <h3 className="queue-title">Active Receiving Sessions</h3>
            {!loadingReceiving && !receivingRestricted && (
              <span className="badge badge-info" style={{ fontSize: '11px' }}>
                {receivingSessions.length} active
              </span>
            )}
          </div>
          <Link to="/app/inbound" className="queue-action-link" aria-label="Go to Inbound Receiving">
            <span>Receiving Hub</span>
            <AppIcon name="ChevronRight" size="xs" color="primary" />
          </Link>
        </div>

        {receivingRestricted ? (
          <div className="queue-empty">
            <AppIcon name="Lock" size="md" color="muted" />
            <p>Requires inbound goods receiving authorization.</p>
          </div>
        ) : receivingError ? (
          <div className="queue-empty" style={{ color: 'var(--status-danger-text)' }}>
            <AppIcon name="AlertTriangle" size="md" color="danger" />
            <p>{receivingError}</p>
          </div>
        ) : loadingReceiving ? (
          <div style={{ padding: '20px' }}>
            <div className="skeleton-line" style={{ marginBottom: '10px' }} />
            <div className="skeleton-line" style={{ marginBottom: '10px' }} />
            <div className="skeleton-line" />
          </div>
        ) : receivingSessions.length === 0 ? (
          <div className="queue-empty">
            <AppIcon name="Inbox" size="lg" color="muted" />
            <p><strong>No active sessions at dock doors</strong></p>
            <p style={{ fontSize: '12px' }}>All inbound trailers are cleared or awaiting dock check-in.</p>
          </div>
        ) : (
          <div className="queue-table-wrap">
            <table className="queue-table" aria-label="Active Receiving Sessions">
              <thead>
                <tr>
                  <th scope="col">Session #</th>
                  <th scope="col">Dock Door</th>
                  <th scope="col">Trailer / ASN</th>
                  <th scope="col" style={{ textAlign: 'center' }}>Lines</th>
                  <th scope="col" style={{ textAlign: 'right' }}>Status</th>
                </tr>
              </thead>
              <tbody>
                {receivingSessions.slice(0, 5).map((s) => (
                  <tr key={s.id}>
                    <td>
                      <code style={{ fontWeight: 700, color: 'var(--brand-primary)' }}>
                        {s.sessionNumber}
                      </code>
                    </td>
                    <td>{s.doorNumber || 'Door --'}</td>
                    <td>
                      <div style={{ fontWeight: 600 }}>{s.trailerNumber || 'N/A'}</div>
                      {s.asnNumber && (
                        <div style={{ fontSize: '11.5px', color: 'var(--text-muted)' }}>ASN: {s.asnNumber}</div>
                      )}
                    </td>
                    <td style={{ textAlign: 'center', fontWeight: 650 }}>
                      {s.lines ? s.lines.length : 0}
                    </td>
                    <td style={{ textAlign: 'right' }}>{getReceivingStatusBadge(s.status)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
};
