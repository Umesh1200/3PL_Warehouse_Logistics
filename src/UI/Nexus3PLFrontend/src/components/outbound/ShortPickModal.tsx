import React, { useEffect, useRef, useState } from 'react';
import { Modal } from '../common/Modal';
import { outboundApi } from '../../services/outboundApi';
import {
  PickTaskDto,
  ShortPickExecutionResultDto,
  ShortPickReason,
} from '../../types/outbound';
import { formatShortPickReasonLabel, generateActionIdempotencyKey } from './OutboundStatusBadge';

export interface ShortPickModalProps {
  isOpen: boolean;
  onClose: () => void;
  task: PickTaskDto | null;
  initialLocationBarcode?: string;
  initialItemBarcode?: string;
  initialToteLpn?: string;
  onReported: (result: ShortPickExecutionResultDto) => void;
}

export const ShortPickModal: React.FC<ShortPickModalProps> = ({
  isOpen,
  onClose,
  task,
  initialLocationBarcode = '',
  initialItemBarcode = '',
  initialToteLpn = '',
  onReported,
}) => {
  const [quantityPicked, setQuantityPicked] = useState('0');
  const [reason, setReason] = useState<ShortPickReason>(ShortPickReason.BinEmpty);
  const [notes, setNotes] = useState('');
  const [scannedLocationBarcode, setScannedLocationBarcode] = useState('');
  const [scannedItemBarcode, setScannedItemBarcode] = useState('');
  const [scannedToteLpn, setScannedToteLpn] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const inFlightRef = useRef(false);
  const idempotencyKeyRef = useRef<string | null>(null);

  useEffect(() => {
    if (!isOpen || !task) return;
    setQuantityPicked('0');
    setReason(ShortPickReason.BinEmpty);
    setNotes('');
    setScannedLocationBarcode(initialLocationBarcode);
    setScannedItemBarcode(initialItemBarcode);
    setScannedToteLpn(initialToteLpn);
    setError(null);
    idempotencyKeyRef.current = null;
  }, [isOpen, task?.id, initialLocationBarcode, initialItemBarcode, initialToteLpn]);

  if (!task) return null;

  const remainingToPick = Math.max(0, task.quantityToPick - task.quantityPicked);
  const parsedQty = quantityPicked.trim() === '' ? Number.NaN : Number(quantityPicked);
  const isPositiveShortPick = Number.isFinite(parsedQty) && parsedQty > 0;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (inFlightRef.current || isSubmitting) return;

    const qty = quantityPicked.trim() === '' ? Number.NaN : Number(quantityPicked);
    if (!Number.isFinite(qty) || qty < 0) {
      setError('Picked quantity must be zero or a positive number.');
      return;
    }
    if (qty >= remainingToPick) {
      setError(
        `Short-pick quantity must be strictly less than remaining task quantity (${remainingToPick}). Use Confirm Pick for full fulfillment.`
      );
      return;
    }
    if (qty > 0) {
      if (!scannedLocationBarcode.trim()) {
        setError('Source bin barcode scan is required when reporting partial picked units.');
        return;
      }
      if (!scannedItemBarcode.trim()) {
        setError('Item / SKU barcode scan is required when reporting partial picked units.');
        return;
      }
      if (!scannedToteLpn.trim()) {
        setError('Target tote / cart LPN barcode scan is required when reporting partial picked units.');
        return;
      }
    }

    if (!idempotencyKeyRef.current) {
      idempotencyKeyRef.current = generateActionIdempotencyKey('short-pick');
    }

    inFlightRef.current = true;
    setIsSubmitting(true);
    setError(null);
    try {
      const result = await outboundApi.reportShortPick(task.id, {
        quantityPicked: qty,
        reason,
        notes: notes.trim() || undefined,
        scannedLocationBarcode: scannedLocationBarcode.trim() || undefined,
        scannedItemBarcode: scannedItemBarcode.trim() || undefined,
        scannedToteLpn: scannedToteLpn.trim() || undefined,
        idempotencyKey: idempotencyKeyRef.current,
      });
      idempotencyKeyRef.current = null;
      onReported(result);
    } catch (err: any) {
      setError(err.message || 'Failed to submit short-pick exception.');
    } finally {
      inFlightRef.current = false;
      setIsSubmitting(false);
    }
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title="Report Short-Pick Exception"
      subtitle={`${task.sku} • Order ${task.orderNumber} • Bin ${task.sourceLocationCode}`}
      icon="AlertTriangle"
      maxWidth="620px"
    >
      <form onSubmit={handleSubmit} className="outbound-form-stack" data-testid="short-pick-form">
        <div className="outbound-alert warning" role="note">
          <span>
            Remaining Expected: <strong>{remainingToPick}</strong> unit(s). Reporting a short-pick adjusts allocation and
            queues automated cycle-count verification.
          </span>
        </div>

        {error && (
          <div className="outbound-alert error" role="alert" data-testid="short-pick-error">
            {error}
          </div>
        )}

        <div className="outbound-form-grid">
          <div className="form-field">
            <label htmlFor="short-pick-qty">Quantity Physically Picked *</label>
            <input
              id="short-pick-qty"
              type="number"
              step="any"
              min={0}
              max={remainingToPick}
              value={quantityPicked}
              onChange={(e) => {
                setQuantityPicked(e.target.value);
                if (error) setError(null);
              }}
              data-testid="short-pick-qty-input"
              required
            />
          </div>

          <div className="form-field">
            <label htmlFor="short-pick-reason">Discrepancy Reason *</label>
            <select
              id="short-pick-reason"
              value={reason}
              onChange={(e) => setReason(e.target.value as ShortPickReason)}
              data-testid="short-pick-reason-select"
            >
              {Object.values(ShortPickReason).map((r) => (
                <option key={r} value={r}>
                  {formatShortPickReasonLabel(r)}
                </option>
              ))}
            </select>
          </div>
        </div>

        <div className="outbound-form-grid">
          <div className="form-field">
            <label htmlFor="short-pick-loc">Bin Barcode {isPositiveShortPick ? '*' : '(Optional for 0 qty)'}</label>
            <input
              id="short-pick-loc"
              type="text"
              placeholder={`Expected: ${task.sourceLocationCode}`}
              value={scannedLocationBarcode}
              onChange={(e) => {
                setScannedLocationBarcode(e.target.value);
                if (error) setError(null);
              }}
              autoComplete="off"
              data-testid="short-pick-location-input"
            />
          </div>
          <div className="form-field">
            <label htmlFor="short-pick-item">Item / SKU Barcode {isPositiveShortPick ? '*' : '(Optional for 0 qty)'}</label>
            <input
              id="short-pick-item"
              type="text"
              placeholder={`Expected: ${task.sku}`}
              value={scannedItemBarcode}
              onChange={(e) => {
                setScannedItemBarcode(e.target.value);
                if (error) setError(null);
              }}
              autoComplete="off"
              data-testid="short-pick-item-input"
            />
          </div>
          <div className="form-field">
            <label htmlFor="short-pick-tote">Target Tote LPN {isPositiveShortPick ? '*' : '(Optional for 0 qty)'}</label>
            <input
              id="short-pick-tote"
              type="text"
              placeholder={task.targetToteBarcode ? `Expected: ${task.targetToteBarcode}` : 'Scan tote / cart LPN'}
              value={scannedToteLpn}
              onChange={(e) => {
                setScannedToteLpn(e.target.value);
                if (error) setError(null);
              }}
              autoComplete="off"
              data-testid="short-pick-tote-input"
            />
          </div>
        </div>

        <div className="form-field">
          <label htmlFor="short-pick-notes">Operator Investigation Notes</label>
          <textarea
            id="short-pick-notes"
            rows={3}
            placeholder="Describe damaged units, bin obstruction, or physical count discrepancy..."
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            data-testid="short-pick-notes-input"
          />
        </div>

        <div className="modal-inline-actions">
          <button type="button" className="btn btn-secondary" onClick={onClose} disabled={isSubmitting}>
            Cancel
          </button>
          <button
            type="submit"
            className="btn btn-danger"
            disabled={isSubmitting}
            data-testid="submit-short-pick-btn"
          >
            {isSubmitting ? 'Submitting Exception...' : 'Submit Short-Pick'}
          </button>
        </div>
      </form>
    </Modal>
  );
};
