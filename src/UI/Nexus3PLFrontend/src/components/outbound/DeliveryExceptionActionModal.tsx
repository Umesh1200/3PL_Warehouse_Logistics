import React, { useEffect, useState } from 'react';
import { dispatchApi } from '../../services/dispatchApi';
import {
  CustomerContactMethod,
  CustomerContactOutcome,
  DeliveryExceptionQueueItemDto,
  DeliveryExceptionResolution,
  DeliveryExceptionType,
} from '../../types/outbound';
import { Modal } from '../common/Modal';

export type ExceptionModalMode =
  | 'driverReport'
  | 'contact'
  | 'reattempt'
  | 'reschedule'
  | 'updateAddress'
  | 'redirectDepot'
  | 'resolve'
  | 'reopen'
  | 'void';

export interface DeliveryExceptionActionModalProps {
  isOpen: boolean;
  mode: ExceptionModalMode;
  packageId?: string | null;
  packageNumber?: string | null;
  exceptionItem?: DeliveryExceptionQueueItemDto | null;
  onClose: () => void;
  onCompleted: () => Promise<void> | void;
  onFeedback: (fb: { type: 'success' | 'warning' | 'error'; message: string }) => void;
}

export const DeliveryExceptionActionModal: React.FC<DeliveryExceptionActionModalProps> = ({
  isOpen,
  mode,
  packageId,
  packageNumber,
  exceptionItem,
  onClose,
  onCompleted,
  onFeedback,
}) => {
  const [driverExceptionType, setDriverExceptionType] = useState<DeliveryExceptionType>(
    DeliveryExceptionType.RecipientUnavailable
  );
  const [contactMethod, setContactMethod] = useState<CustomerContactMethod>(CustomerContactMethod.PhoneCall);
  const [contactOutcome, setContactOutcome] = useState<CustomerContactOutcome>(
    CustomerContactOutcome.ReachedAgreedToReattempt
  );
  const [resolution, setResolution] = useState<DeliveryExceptionResolution>(DeliveryExceptionResolution.Resolved);
  const [scheduledDate, setScheduledDate] = useState('');
  const [primaryText, setPrimaryText] = useState('');
  const [notes, setNotes] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!isOpen) return;
    setError(null);
    setPrimaryText('');
    setNotes('');
    const tomorrow = new Date(Date.now() + 86400000).toISOString().slice(0, 16);
    setScheduledDate(tomorrow);
  }, [isOpen, mode, exceptionItem?.exceptionId, packageId]);

  if (!isOpen) return null;

  const titles: Record<ExceptionModalMode, string> = {
    driverReport: `Report Delivery Exception — ${packageNumber || packageId}`,
    contact: `Log Consignee Contact — ${exceptionItem?.trackingNumber || ''}`,
    reattempt: `Schedule Delivery Reattempt — ${exceptionItem?.trackingNumber || ''}`,
    reschedule: `Reschedule Delivery Reattempt — ${exceptionItem?.trackingNumber || ''}`,
    updateAddress: `Correct Delivery Address — ${exceptionItem?.trackingNumber || ''}`,
    redirectDepot: `Redirect to Carrier Depot — ${exceptionItem?.trackingNumber || ''}`,
    resolve: `Resolve Delivery Exception — ${exceptionItem?.trackingNumber || ''}`,
    reopen: `Reopen Delivery Exception — ${exceptionItem?.trackingNumber || ''}`,
    void: `Void Delivery Exception — ${exceptionItem?.trackingNumber || ''}`,
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (submitting) return;
    setSubmitting(true);
    setError(null);
    try {
      let msg = 'Exception workflow updated.';
      if (mode === 'driverReport') {
        if (!packageId) throw new Error('Package ID is required.');
        const r = await dispatchApi.reportDriverException({
          packageId,
          exceptionType: driverExceptionType,
          driverNotes: notes,
        });
        msg = `Driver exception recorded (${r.exceptionType}, Order status: ${r.orderDeliveryStatus}).`;
      } else if (exceptionItem) {
        const id = exceptionItem.exceptionId;
        if (mode === 'contact') {
          await dispatchApi.recordCustomerContact(id, {
            contactMethod,
            outcome: contactOutcome,
            notes,
          });
          msg = 'Customer contact log recorded.';
        } else if (mode === 'reattempt') {
          await dispatchApi.scheduleReattempt(id, {
            scheduledDateUtc: new Date(scheduledDate).toISOString(),
            notes,
          });
          msg = 'Delivery reattempt scheduled.';
        } else if (mode === 'reschedule') {
          await dispatchApi.rescheduleReattempt(id, {
            newScheduledDateUtc: new Date(scheduledDate).toISOString(),
            reason: notes,
          });
          msg = 'Delivery reattempt rescheduled.';
        } else if (mode === 'updateAddress') {
          await dispatchApi.updateDeliveryAddress(id, {
            correctedAddress: primaryText,
            notes,
          });
          msg = 'Consignee address updated for carrier redelivery.';
        } else if (mode === 'redirectDepot') {
          await dispatchApi.redirectToDepot(id, {
            depotLocation: primaryText,
            notes,
          });
          msg = 'Package redirected to carrier depot.';
        } else if (mode === 'resolve') {
          const r = await dispatchApi.resolveException(id, {
            resolution,
            resolutionNotes: notes,
          });
          msg = `Exception resolved (Order status: ${r.orderDeliveryStatus}).`;
        } else if (mode === 'reopen') {
          await dispatchApi.reopenException(id, notes);
          msg = 'Delivery exception reopened.';
        } else if (mode === 'void') {
          await dispatchApi.voidException(id, notes);
          msg = 'Delivery exception voided.';
        }
      }
      onFeedback({ type: 'success', message: msg });
      await onCompleted();
      onClose();
    } catch (err: any) {
      setError(err.message || 'Failed to update delivery exception workflow.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title={titles[mode]}
      icon="AlertTriangle"
      maxWidth="540px"
    >
      <form onSubmit={handleSubmit} className="outbound-form-stack" data-testid="exception-action-form">
        {error && (
          <div className="outbound-alert error" role="alert">
            <span>{error}</span>
          </div>
        )}

        {mode === 'driverReport' && (
          <div className="form-field">
            <label htmlFor="exc-driver-type">Exception Type</label>
            <select
              id="exc-driver-type"
              value={driverExceptionType}
              onChange={(e) => setDriverExceptionType(e.target.value as DeliveryExceptionType)}
              data-testid="exc-driver-type-select"
            >
              {Object.values(DeliveryExceptionType).map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </select>
          </div>
        )}

        {mode === 'contact' && (
          <div className="form-grid-2col">
            <div className="form-field">
              <label htmlFor="exc-contact-method">Contact Method</label>
              <select
                id="exc-contact-method"
                value={contactMethod}
                onChange={(e) => setContactMethod(e.target.value as CustomerContactMethod)}
              >
                {Object.values(CustomerContactMethod).map((m) => (
                  <option key={m} value={m}>
                    {m}
                  </option>
                ))}
              </select>
            </div>
            <div className="form-field">
              <label htmlFor="exc-contact-outcome">Outcome</label>
              <select
                id="exc-contact-outcome"
                value={contactOutcome}
                onChange={(e) => setContactOutcome(e.target.value as CustomerContactOutcome)}
              >
                {Object.values(CustomerContactOutcome).map((o) => (
                  <option key={o} value={o}>
                    {o}
                  </option>
                ))}
              </select>
            </div>
          </div>
        )}

        {(mode === 'reattempt' || mode === 'reschedule') && (
          <div className="form-field">
            <label htmlFor="exc-reattempt-date">Scheduled Date/Time (UTC) *</label>
            <input
              id="exc-reattempt-date"
              type="datetime-local"
              value={scheduledDate}
              onChange={(e) => setScheduledDate(e.target.value)}
              required
              data-testid="exc-reattempt-date"
            />
          </div>
        )}

        {(mode === 'updateAddress' || mode === 'redirectDepot') && (
          <div className="form-field">
            <label htmlFor="exc-primary-text">
              {mode === 'updateAddress' ? 'Corrected Street Address *' : 'Carrier Depot Location *'}
            </label>
            <input
              id="exc-primary-text"
              type="text"
              value={primaryText}
              onChange={(e) => setPrimaryText(e.target.value)}
              required
              data-testid="exc-primary-input"
            />
          </div>
        )}

        {mode === 'resolve' && (
          <div className="form-field">
            <label htmlFor="exc-resolution-select">Resolution Outcome</label>
            <select
              id="exc-resolution-select"
              value={resolution}
              onChange={(e) => setResolution(e.target.value as DeliveryExceptionResolution)}
              data-testid="exc-resolution-select"
            >
              {Object.values(DeliveryExceptionResolution).map((r) => (
                <option key={r} value={r}>
                  {r}
                </option>
              ))}
            </select>
          </div>
        )}

        <div className="form-field">
          <label htmlFor="exc-notes">Notes / Reason *</label>
          <textarea
            id="exc-notes"
            rows={3}
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            required
            placeholder="Operational details or consignee instructions..."
            data-testid="exc-notes-input"
          />
        </div>

        <div className="modal-actions-row">
          <button type="button" className="btn btn-secondary" onClick={onClose}>
            Cancel
          </button>
          <button
            type="submit"
            className="btn btn-primary"
            disabled={submitting}
            data-testid="exc-action-submit-btn"
          >
            {submitting ? 'Submitting...' : 'Confirm Action'}
          </button>
        </div>
      </form>
    </Modal>
  );
};
