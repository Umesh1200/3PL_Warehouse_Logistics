import React, { useEffect, useState } from 'react';
import { outboundApi } from '../../services/outboundApi';
import { BillOfLadingDto, ShipmentDto } from '../../types/outbound';
import { formatServiceLevelLabel } from './OutboundStatusBadge';
import { Modal } from '../common/Modal';
import { AppIcon } from '../common/AppIcon';

export interface BillOfLadingModalProps {
  isOpen: boolean;
  shipment: ShipmentDto | null;
  canShip: boolean;
  onClose: () => void;
  onBolIssued: () => Promise<void> | void;
  onFeedback: (fb: { type: 'success' | 'warning' | 'error'; message: string }) => void;
}

export const BillOfLadingModal: React.FC<BillOfLadingModalProps> = ({
  isOpen,
  shipment,
  canShip,
  onClose,
  onBolIssued,
  onFeedback,
}) => {
  const [bol, setBol] = useState<BillOfLadingDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [issuing, setIssuing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [trailerNumber, setTrailerNumber] = useState('');
  const [sealNumber, setSealNumber] = useState('');
  const [specialInstructions, setSpecialInstructions] = useState('');

  useEffect(() => {
    if (!isOpen || !shipment) return;
    setError(null);
    setTrailerNumber(shipment.trailerNumber || '');
    setSealNumber(shipment.sealNumber || '');
    setSpecialInstructions('');

    if (!shipment.billOfLadingId) {
      setBol(null);
      return;
    }

    const controller = new AbortController();
    setLoading(true);
    outboundApi
      .getBillOfLadingById(shipment.billOfLadingId, controller.signal)
      .then((res) => setBol(res))
      .catch((err: any) => {
        if (err.name !== 'AbortError') {
          setError(err.message || 'Failed to load issued Bill of Lading snapshot.');
        }
      })
      .finally(() => setLoading(false));

    return () => controller.abort();
  }, [isOpen, shipment?.id, shipment?.billOfLadingId]);

  if (!isOpen || !shipment) return null;

  const handleIssueBol = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canShip || issuing) return;
    setIssuing(true);
    setError(null);
    try {
      const issued = await outboundApi.generateBillOfLading(shipment.id, {
        trailerNumber: trailerNumber.trim() || null,
        sealNumber: sealNumber.trim() || null,
        specialInstructions: specialInstructions.trim() || null,
      });
      const fetched = await outboundApi.getBillOfLadingById(issued.bolId);
      setBol(fetched);
      onFeedback({
        type: 'success',
        message: `Immutable Bill of Lading ${issued.bolNumber} issued (${issued.totalPackageCount} cartons, ${issued.totalGrossWeightKg.toFixed(2)} kg).`,
      });
      await onBolIssued();
    } catch (err: any) {
      setError(err.message || 'Failed to generate Bill of Lading.');
    } finally {
      setIssuing(false);
    }
  };

  const handleDownloadJson = () => {
    if (!bol) return;
    const blob = new Blob([bol.documentSnapshotJson || JSON.stringify(bol, null, 2)], {
      type: 'application/json',
    });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${bol.bolNumber}.json`;
    a.click();
    URL.revokeObjectURL(url);
  };

  let parsedSnapshot: any = null;
  if (bol?.documentSnapshotJson) {
    try {
      parsedSnapshot = JSON.parse(bol.documentSnapshotJson);
    } catch {
      parsedSnapshot = null;
    }
  }

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title={bol ? `Bill of Lading — ${bol.bolNumber}` : `Issue Bill of Lading — ${shipment.shipmentNumber}`}
      subtitle="Immutable freight manifest snapshot recorded at dispatch preparation."
      icon="Receipt"
      maxWidth="760px"
      footer={
        <div className="modal-actions-row">
          <button type="button" className="btn btn-secondary" onClick={onClose}>
            Close
          </button>
          {bol && (
            <>
              <button type="button" className="btn btn-secondary" onClick={handleDownloadJson}>
                <AppIcon name="Download" size="xs" /> Export JSON Snapshot
              </button>
              <button
                type="button"
                className="btn btn-primary"
                onClick={() => window.print()}
                data-testid="print-bol-btn"
              >
                <AppIcon name="ExternalLink" size="xs" /> Print BOL Snapshot
              </button>
            </>
          )}
        </div>
      }
    >
      <div data-testid="bol-modal-content" className="bol-modal-body">
        {error && (
          <div className="outbound-alert error" role="alert">
            <span>{error}</span>
          </div>
        )}

        {loading && <p className="outbound-subtitle">Loading immutable Bill of Lading snapshot...</p>}

        {!loading && !bol && (
          <form onSubmit={handleIssueBol} className="bol-issue-form" data-testid="bol-issue-form">
            <div className="outbound-alert warning">
              <span>
                Issuing a BOL creates an immutable freight document snapshot for{' '}
                <strong>{shipment.shipmentNumber}</strong> ({shipment.totalPackagesCount} cartons,{' '}
                {shipment.totalWeightKg.toFixed(2)} kg). Subsequent requests preserve this exact snapshot.
              </span>
            </div>

            <div className="form-grid-2col">
              <div className="form-field">
                <label htmlFor="bol-trailer-input">Trailer Number</label>
                <input
                  id="bol-trailer-input"
                  type="text"
                  value={trailerNumber}
                  onChange={(e) => setTrailerNumber(e.target.value)}
                  placeholder="e.g. TRL-53-8841"
                  maxLength={100}
                />
              </div>
              <div className="form-field">
                <label htmlFor="bol-seal-input">Seal Number</label>
                <input
                  id="bol-seal-input"
                  type="text"
                  value={sealNumber}
                  onChange={(e) => setSealNumber(e.target.value)}
                  placeholder="e.g. SEAL-90421"
                  maxLength={100}
                />
              </div>
            </div>

            <div className="form-field">
              <label htmlFor="bol-instructions-input">Special Freight Instructions</label>
              <textarea
                id="bol-instructions-input"
                rows={2}
                value={specialInstructions}
                onChange={(e) => setSpecialInstructions(e.target.value)}
                placeholder="Handling notes, dock check-in code, liftgate instructions..."
                maxLength={500}
              />
            </div>

            <div className="modal-inline-submit">
              <button
                type="submit"
                className="btn btn-primary"
                disabled={issuing || !canShip || shipment.totalPackagesCount === 0}
                data-testid="issue-bol-submit-btn"
              >
                {issuing ? 'Issuing Immutable Snapshot...' : 'Issue Immutable BOL'}
              </button>
            </div>
          </form>
        )}

        {!loading && bol && (
          <div className="bol-snapshot-card" data-testid="bol-snapshot-view">
            <div className="bol-snapshot-header">
              <div>
                <div className="bol-number-title">{bol.bolNumber}</div>
                <div className="outbound-subtitle">
                  Issued {new Date(bol.issuedAtUtc).toLocaleString()} &bull; {bol.carrierCode} (
                  {formatServiceLevelLabel(bol.serviceLevel)})
                </div>
              </div>
              <span className={`badge ${bol.isVoided ? 'badge-danger' : 'badge-success'}`}>
                {bol.isVoided ? 'Voided' : 'Immutable Snapshot Issued'}
              </span>
            </div>

            <div className="bol-parties-grid">
              <div className="bol-party-box">
                <span className="bol-party-label">Shipper (Origin Hub)</span>
                <strong>{bol.shipperName}</strong>
                <span>{bol.shipperAddressSnapshot}</span>
              </div>
              <div className="bol-party-box">
                <span className="bol-party-label">Consignee (Destination)</span>
                <strong>{bol.consigneeName}</strong>
                <span>{bol.consigneeAddressSnapshot}</span>
              </div>
            </div>

            <div className="bol-metrics-strip">
              <div>
                <span className="bol-metric-label">Cartons</span>
                <strong>{bol.totalPackageCount}</strong>
              </div>
              <div>
                <span className="bol-metric-label">Gross Weight</span>
                <strong>{bol.totalGrossWeightKg.toFixed(2)} kg</strong>
              </div>
              <div>
                <span className="bol-metric-label">Total Volume</span>
                <strong>{bol.totalVolumeCm3.toFixed(0)} cm&sup3;</strong>
              </div>
              <div>
                <span className="bol-metric-label">Trailer / Seal</span>
                <strong>
                  {bol.trailerNumber || 'N/A'} / {bol.sealNumber || 'N/A'}
                </strong>
              </div>
            </div>

            {bol.specialInstructions && (
              <div className="bol-instructions-box">
                <strong>Special Instructions:</strong> {bol.specialInstructions}
              </div>
            )}

            {bol.packageTrackingNumbers && (
              <div className="bol-tracking-box">
                <span className="bol-party-label">Tracking Numbers Manifest</span>
                <code>{bol.packageTrackingNumbers}</code>
              </div>
            )}

            {Array.isArray(parsedSnapshot?.Packages) && parsedSnapshot.Packages.length > 0 && (
              <div className="table-responsive">
                <table className="data-table data-table-compact">
                  <thead>
                    <tr>
                      <th>Package #</th>
                      <th>Tracking #</th>
                      <th>Weight</th>
                      <th>Dimensions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {parsedSnapshot.Packages.map((pkg: any, idx: number) => (
                      <tr key={pkg.PackageNumber || idx}>
                        <td className="mono-cell">{pkg.PackageNumber}</td>
                        <td className="mono-cell">{pkg.TrackingNumber || '—'}</td>
                        <td>{Number(pkg.WeightKg ?? 0).toFixed(2)} kg</td>
                        <td>{pkg.Dimensions || '—'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        )}
      </div>
    </Modal>
  );
};
