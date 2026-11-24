import React, { useEffect, useState } from 'react';
import { dispatchApi } from '../../services/dispatchApi';
import {
  DispatchBoardShipmentDto,
  EligibleShipmentDriverDto,
  ShipmentStatus,
} from '../../types/outbound';
import { Modal } from '../common/Modal';

export interface DriverAssignModalProps {
  isOpen: boolean;
  shipment: DispatchBoardShipmentDto | null;
  eligibleDrivers: EligibleShipmentDriverDto[];
  onClose: () => void;
  onAssigned: () => Promise<void> | void;
  onFeedback: (fb: { type: 'success' | 'warning' | 'error'; message: string }) => void;
}

export const DriverAssignModal: React.FC<DriverAssignModalProps> = ({
  isOpen,
  shipment,
  eligibleDrivers,
  onClose,
  onAssigned,
  onFeedback,
}) => {
  const [driverUserId, setDriverUserId] = useState<string>('');
  const [reason, setReason] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!isOpen || !shipment) return;
    setDriverUserId(shipment.assignedDriverUserId || '');
    setReason('');
    setError(null);
  }, [isOpen, shipment?.shipmentId]);

  if (!isOpen || !shipment) return null;

  const isDispatchedChange =
    shipment.status === ShipmentStatus.Dispatched &&
    (shipment.assignedDriverUserId || '') !== driverUserId;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (submitting) return;
    if (isDispatchedChange && !reason.trim()) {
      setError('An explicit audit reason is required to reassign or revoke a dispatched shipment driver.');
      return;
    }

    setSubmitting(true);
    setError(null);
    try {
      const res = await dispatchApi.assignShipmentDriver(shipment.shipmentId, {
        driverUserId: driverUserId || null,
        expectedVersion: shipment.version,
        reason: reason.trim() || null,
      });
      onFeedback({
        type: 'success',
        message: `${res.message} (Version v${res.version}, Custody: ${res.custodyStatus})`,
      });
      await onAssigned();
      onClose();
    } catch (err: any) {
      setError(err.message || 'Failed to update driver assignment.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title={`Assign Courier Driver — ${shipment.shipmentNumber}`}
      subtitle={`Current Version: v${shipment.version} • Custody: ${shipment.custodyStatus}`}
      icon="Truck"
      maxWidth="540px"
    >
      <form onSubmit={handleSubmit} data-testid="assign-driver-form" className="outbound-form-stack">
        {error && (
          <div className="outbound-alert error" role="alert">
            <span>{error}</span>
          </div>
        )}

        <div className="form-field">
          <label htmlFor="assign-driver-select">Assigned Courier Driver</label>
          <select
            id="assign-driver-select"
            value={driverUserId}
            onChange={(e) => setDriverUserId(e.target.value)}
            data-testid="assign-driver-select"
          >
            <option value="">Unassign / Revoke Courier Custody</option>
            {eligibleDrivers.map((d) => (
              <option key={d.id} value={d.id}>
                {d.firstName} {d.lastName} ({d.email})
              </option>
            ))}
          </select>
        </div>

        <div className="form-field">
          <label htmlFor="assign-driver-reason">
            Custody Assignment / Reassignment Reason {isDispatchedChange ? '*' : '(Optional)'}
          </label>
          <input
            id="assign-driver-reason"
            type="text"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            placeholder={
              isDispatchedChange
                ? 'Mandatory reason for dispatched custody transfer/revocation'
                : 'Route assignment note'
            }
            data-testid="assign-driver-reason"
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
            data-testid="assign-driver-submit-btn"
          >
            {submitting ? 'Saving...' : 'Save Driver Assignment'}
          </button>
        </div>
      </form>
    </Modal>
  );
};
