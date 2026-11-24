import React, { useCallback, useEffect, useState } from 'react';
import { dispatchApi } from '../../services/dispatchApi';
import {
  EvaluateOrderDeliveryRollupResultDto,
  PackageTrackingTimelineDto,
  TrackingMilestoneStatus,
} from '../../types/outbound';
import { Modal } from '../common/Modal';
import { OrderDeliveryStatusBadge, TrackingMilestoneBadge } from './OutboundStatusBadge';

export interface TrackingTimelineModalProps {
  isOpen: boolean;
  packageId: string | null;
  orderId: string | null;
  shipmentId: string | null;
  packageNumber?: string | null;
  canManageRuns: boolean;
  onClose: () => void;
  onSynced?: () => Promise<void> | void;
  onFeedback: (fb: { type: 'success' | 'warning' | 'error'; message: string }) => void;
}

const ORDERED_MILESTONES: TrackingMilestoneStatus[] = [
  TrackingMilestoneStatus.PendingHandover,
  TrackingMilestoneStatus.Accepted,
  TrackingMilestoneStatus.InTransit,
  TrackingMilestoneStatus.OutForDelivery,
  TrackingMilestoneStatus.Delivered,
];

function getStepState(
  step: TrackingMilestoneStatus,
  current: TrackingMilestoneStatus
): 'done' | 'active' | 'exception' | 'pending' {
  if (
    current === TrackingMilestoneStatus.DeliveryException ||
    current === TrackingMilestoneStatus.ReturnedToSender
  ) {
    return step === TrackingMilestoneStatus.PendingHandover ? 'done' : 'exception';
  }
  const currentIdx = ORDERED_MILESTONES.indexOf(current);
  const stepIdx = ORDERED_MILESTONES.indexOf(step);
  if (stepIdx < currentIdx || current === TrackingMilestoneStatus.Delivered) return 'done';
  if (stepIdx === currentIdx) return 'active';
  return 'pending';
}

export const TrackingTimelineModal: React.FC<TrackingTimelineModalProps> = ({
  isOpen,
  packageId,
  orderId,
  shipmentId,
  packageNumber,
  canManageRuns,
  onClose,
  onSynced,
  onFeedback,
}) => {
  const [timeline, setTimeline] = useState<PackageTrackingTimelineDto | null>(null);
  const [orderRollup, setOrderRollup] = useState<EvaluateOrderDeliveryRollupResultDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [syncing, setSyncing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadData = useCallback(
    async (signal?: AbortSignal) => {
      if (!isOpen || !packageId) return;
      setLoading(true);
      setError(null);
      try {
        const [tl, rollup] = await Promise.all([
          dispatchApi.getPackageTimeline(packageId, signal),
          orderId ? dispatchApi.getOrderDeliveryStatus(orderId, signal) : Promise.resolve(null),
        ]);
        setTimeline(tl);
        setOrderRollup(rollup);
      } catch (err: any) {
        if (err.name !== 'AbortError') {
          setError(err.message || 'Failed to load package tracking timeline.');
        }
      } finally {
        setLoading(false);
      }
    },
    [isOpen, packageId, orderId]
  );

  useEffect(() => {
    if (!isOpen || !packageId) return;
    const ctrl = new AbortController();
    loadData(ctrl.signal);
    return () => ctrl.abort();
  }, [isOpen, packageId, loadData]);

  const handleSyncTracking = async () => {
    if (!canManageRuns || !shipmentId || syncing) return;
    setSyncing(true);
    setError(null);
    try {
      const res = await dispatchApi.syncShipmentTracking(shipmentId);
      onFeedback({
        type: 'success',
        message: `Carrier sync complete: ${res.newEventsIngested} new event(s), status ${res.shipmentDeliveryStatus}.`,
      });
      await loadData();
      if (onSynced) await onSynced();
    } catch (err: any) {
      setError(err.message || 'Failed to synchronize carrier tracking.');
    } finally {
      setSyncing(false);
    }
  };

  if (!isOpen || !packageId) return null;

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title={`Carrier Tracking & Delivery Rollup — ${timeline?.packageNumber || packageNumber || packageId}`}
      subtitle={
        timeline
          ? `Carrier: ${timeline.carrierCode} • Tracking #: ${timeline.trackingNumber || 'Unassigned'}`
          : 'Loading package telemetry...'
      }
      icon="Navigation"
      maxWidth="760px"
      footer={
        <div className="modal-actions-row">
          {canManageRuns && shipmentId && (
            <button
              type="button"
              className="btn btn-secondary"
              disabled={syncing}
              onClick={handleSyncTracking}
              data-testid="timeline-sync-btn"
            >
              {syncing ? 'Syncing Carrier...' : 'Sync Carrier Tracking'}
            </button>
          )}
          <button type="button" className="btn btn-primary" onClick={onClose}>
            Close
          </button>
        </div>
      }
    >
      <div className="outbound-form-stack" data-testid="tracking-timeline-modal">
        {error && (
          <div className="outbound-alert error" role="alert">
            <span>{error}</span>
          </div>
        )}

        {loading && <div className="empty-inline-state">Loading tracking events and order rollup...</div>}

        {!loading && timeline && (
          <>
            <div className="dispatch-shipment-header">
              <strong>Current Milestone</strong>
              <TrackingMilestoneBadge status={timeline.currentTrackingStatus} />
            </div>

            <div className="milestone-stepper-track" aria-label="Carrier tracking progression">
              {ORDERED_MILESTONES.map((step) => {
                const state = getStepState(step, timeline.currentTrackingStatus);
                return (
                  <div
                    key={step}
                    className={`milestone-step-pill ${state}`}
                    data-testid={`milestone-step-${step}`}
                  >
                    {step}
                  </div>
                );
              })}
            </div>

            {orderRollup && (
              <div className="rollup-summary-card" data-testid="order-delivery-rollup-card">
                <div className="dispatch-shipment-header">
                  <strong>Order Delivery Rollup</strong>
                  <OrderDeliveryStatusBadge status={orderRollup.deliveryStatus} />
                </div>
                <div className="dispatch-shipment-meta">
                  <span>
                    Delivered: <strong>{orderRollup.deliveredPackages} / {orderRollup.totalPackages}</strong> packages
                  </span>
                  <span>In Transit: {orderRollup.inTransitPackages}</span>
                  <span>Exceptions: {orderRollup.exceptionPackages}</span>
                  {orderRollup.deliveredAtUtc && (
                    <span>Delivered At: {new Date(orderRollup.deliveredAtUtc).toLocaleString()}</span>
                  )}
                </div>
              </div>
            )}

            <div>
              <strong>Carrier Event History ({timeline.events.length})</strong>
              {timeline.events.length === 0 ? (
                <div className="empty-inline-state" style={{ marginTop: '8px' }}>
                  No carrier milestone scans recorded yet.
                </div>
              ) : (
                <ul className="timeline-event-list" style={{ marginTop: '8px' }}>
                  {timeline.events.map((ev) => (
                    <li key={ev.id} className="timeline-event-item">
                      <div className="dispatch-shipment-header">
                        <span>
                          <strong>{ev.carrierEventCode}</strong> — {ev.carrierDescription}
                        </span>
                        <TrackingMilestoneBadge status={ev.milestoneStatus} />
                      </div>
                      <div className="dispatch-shipment-meta" style={{ marginTop: '4px' }}>
                        <span>Occurred: {new Date(ev.occurredAtUtc).toLocaleString()}</span>
                        {ev.location && <span>Location: {ev.location}</span>}
                      </div>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </>
        )}
      </div>
    </Modal>
  );
};
