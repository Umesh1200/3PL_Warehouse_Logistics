import React, { useEffect, useRef, useState } from 'react';
import { outboundApi } from '../../services/outboundApi';
import {
  EligibleShipmentDriverDto,
  ShipmentDto,
  ShipmentStatus,
} from '../../types/outbound';
import { generateActionIdempotencyKey } from './OutboundStatusBadge';
import { Modal } from '../common/Modal';

export interface DriverCustodyHandoverModalProps {
  isOpen: boolean;
  shipment: ShipmentDto | null;
  eligibleDrivers: EligibleShipmentDriverDto[];
  canShip: boolean;
  onClose: () => void;
  onDispatched: () => Promise<void> | void;
  onFeedback: (fb: { type: 'success' | 'warning' | 'error'; message: string }) => void;
}

export const DriverCustodyHandoverModal: React.FC<DriverCustodyHandoverModalProps> = ({
  isOpen,
  shipment,
  eligibleDrivers,
  canShip,
  onClose,
  onDispatched,
  onFeedback,
}) => {
  const [assignedDriverUserId, setAssignedDriverUserId] = useState('');
  const [driverName, setDriverName] = useState('');
  const [driverLicenseNumber, setDriverLicenseNumber] = useState('');
  const [driverSignature, setDriverSignature] = useState('');
  const [trailerNumber, setTrailerNumber] = useState('');
  const [sealNumber, setSealNumber] = useState('');
  const [notes, setNotes] = useState('');

  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const inFlightRef = useRef(false);
  const idempKeyRef = useRef<string | null>(null);

  useEffect(() => {
    if (!isOpen || !shipment) return;
    setError(null);
    setAssignedDriverUserId(shipment.assignedDriverUserId || '');
    setTrailerNumber(shipment.trailerNumber || '');
    setSealNumber(shipment.sealNumber || '');
    setNotes('');

    const matchedDriver = shipment.assignedDriverUserId
      ? eligibleDrivers.find((d) => d.id === shipment.assignedDriverUserId)
      : undefined;
    setDriverName(matchedDriver ? `${matchedDriver.firstName} ${matchedDriver.lastName}`.trim() : '');
    setDriverLicenseNumber('');
    setDriverSignature('');
    idempKeyRef.current = null;
  }, [isOpen, shipment?.id]);

  if (!isOpen || !shipment) return null;

  const handleDriverSelect = (driverId: string) => {
    setAssignedDriverUserId(driverId);
    const found = eligibleDrivers.find((d) => d.id === driverId);
    if (found) {
      setDriverName(`${found.firstName} ${found.lastName}`.trim());
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canShip || inFlightRef.current || submitting) return;

    if (!driverName.trim()) {
      setError('Carrier driver full name is required.');
      return;
    }
    if (!driverSignature.trim()) {
      setError('Driver custody signature acknowledgment is required.');
      return;
    }

    if (!idempKeyRef.current) {
      idempKeyRef.current = generateActionIdempotencyKey('disp');
    }

    inFlightRef.current = true;
    setSubmitting(true);
    setError(null);
    try {
      const res = await outboundApi.confirmDispatchCustody(shipment.id, {
        driverName: driverName.trim(),
        driverLicenseNumber: driverLicenseNumber.trim() || null,
        driverSignature: driverSignature.trim(),
        trailerNumber: trailerNumber.trim() || null,
        sealNumber: sealNumber.trim() || null,
        notes: notes.trim() || null,
        idempotencyKey: idempKeyRef.current,
        assignedDriverUserId: assignedDriverUserId || null,
      });

      idempKeyRef.current = null;
      const completedSummary =
        res.completedOrderIds.length > 0
          ? ` Completed ${res.completedOrderIds.length} outbound order(s).`
          : '';
      onFeedback({
        type: 'success',
        message: `Shipment ${res.shipmentNumber} dispatched to ${res.driverName}.${completedSummary}`,
      });
      await onDispatched();
      onClose();
    } catch (err: any) {
      setError(err.message || 'Failed to confirm driver custody handover.');
    } finally {
      inFlightRef.current = false;
      setSubmitting(false);
    }
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title={`Driver Custody Handover — ${shipment.shipmentNumber}`}
      subtitle={`Finalize trailer dispatch (${shipment.totalPackagesCount} cartons, ${shipment.totalWeightKg.toFixed(2)} kg) with zero secondary stock mutation.`}
      icon="Truck"
      maxWidth="640px"
    >
      <form onSubmit={handleSubmit} data-testid="driver-handover-form" className="outbound-form-stack">
        {error && (
          <div className="outbound-alert error" role="alert">
            <span>{error}</span>
          </div>
        )}

        {shipment.status !== ShipmentStatus.ReadyForDispatch && (
          <div className="outbound-alert warning">
            <span>
              Shipment is currently <strong>{shipment.status}</strong>. All consolidated cartons must be staged and loaded first.
            </span>
          </div>
        )}

        <div className="form-field">
          <label htmlFor="handover-driver-select">System Courier Driver (Optional Binding)</label>
          <select
            id="handover-driver-select"
            value={assignedDriverUserId}
            onChange={(e) => handleDriverSelect(e.target.value)}
            data-testid="handover-driver-select"
          >
            <option value="">External / Walk-up Carrier Driver</option>
            {eligibleDrivers.map((d) => (
              <option key={d.id} value={d.id}>
                {d.firstName} {d.lastName} ({d.email})
              </option>
            ))}
          </select>
        </div>

        <div className="form-grid-2col">
          <div className="form-field">
            <label htmlFor="handover-driver-name">Driver Full Name *</label>
            <input
              id="handover-driver-name"
              type="text"
              value={driverName}
              onChange={(e) => setDriverName(e.target.value)}
              placeholder="e.g. Dan Driver"
              required
              data-testid="handover-driver-name"
            />
          </div>
          <div className="form-field">
            <label htmlFor="handover-driver-license">Driver License / Badge #</label>
            <input
              id="handover-driver-license"
              type="text"
              value={driverLicenseNumber}
              onChange={(e) => setDriverLicenseNumber(e.target.value)}
              placeholder="e.g. CDL-IL-90210"
              data-testid="handover-driver-license"
            />
          </div>
        </div>

        <div className="form-field">
          <label htmlFor="handover-driver-signature">Driver Signature / Custody Acknowledgment *</label>
          <input
            id="handover-driver-signature"
            type="text"
            value={driverSignature}
            onChange={(e) => setDriverSignature(e.target.value)}
            placeholder="Signed full name or tablet signature token"
            required
            data-testid="handover-driver-signature"
          />
        </div>

        <div className="form-grid-2col">
          <div className="form-field">
            <label htmlFor="handover-trailer">Trailer Number</label>
            <input
              id="handover-trailer"
              type="text"
              value={trailerNumber}
              onChange={(e) => setTrailerNumber(e.target.value)}
              placeholder="e.g. TRL-53-8841"
              data-testid="handover-trailer"
            />
          </div>
          <div className="form-field">
            <label htmlFor="handover-seal">Seal Number</label>
            <input
              id="handover-seal"
              type="text"
              value={sealNumber}
              onChange={(e) => setSealNumber(e.target.value)}
              placeholder="e.g. SEAL-90421"
              data-testid="handover-seal"
            />
          </div>
        </div>

        <div className="form-field">
          <label htmlFor="handover-notes">Dispatch &amp; Dock Gate Notes</label>
          <textarea
            id="handover-notes"
            rows={2}
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            placeholder="Gate departure notes..."
          />
        </div>

        <div className="modal-actions-row">
          <button type="button" className="btn btn-secondary" onClick={onClose}>
            Cancel
          </button>
          <button
            type="submit"
            className="btn btn-primary"
            disabled={submitting || !canShip || shipment.status !== ShipmentStatus.ReadyForDispatch}
            data-testid="confirm-dispatch-submit-btn"
          >
            {submitting ? 'Confirming Dispatch...' : 'Confirm Custody Transfer & Dispatch'}
          </button>
        </div>
      </form>
    </Modal>
  );
};
