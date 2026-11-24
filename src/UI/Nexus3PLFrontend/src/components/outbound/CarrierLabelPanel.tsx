import React, { useEffect, useRef, useState } from 'react';
import { outboundApi } from '../../services/outboundApi';
import {
  CarrierRateQuoteDto,
  CarrierServiceLevel,
  PackageStatus,
  PackageSummaryDto,
  ShippingLabelDto,
} from '../../types/outbound';
import {
  PackageStatusBadge,
  ShippingLabelStatusBadge,
  formatServiceLevelLabel,
  generateActionIdempotencyKey,
} from './OutboundStatusBadge';
import { AppIcon } from '../common/AppIcon';

export interface CarrierLabelPanelProps {
  pkg: PackageSummaryDto | null;
  canShip: boolean;
  onLabelUpdated: () => Promise<void> | void;
  onFeedback: (fb: { type: 'success' | 'warning' | 'error'; message: string }) => void;
}

const CARRIERS = ['SIM-FEDEX', 'SIM-UPS', 'SIM-USPS', 'SIM-FREIGHT'];

export const CarrierLabelPanel: React.FC<CarrierLabelPanelProps> = ({
  pkg,
  canShip,
  onLabelUpdated,
  onFeedback,
}) => {
  const [rates, setRates] = useState<CarrierRateQuoteDto[]>([]);
  const [loadingRates, setLoadingRates] = useState(false);
  const [rateError, setRateError] = useState<string | null>(null);

  const [carrierCode, setCarrierCode] = useState('SIM-FEDEX');
  const [serviceLevel, setServiceLevel] = useState<CarrierServiceLevel>(CarrierServiceLevel.StandardGround);
  const [labelFormat, setLabelFormat] = useState<'ZPL' | 'PDF'>('ZPL');
  const [simulateLabelFail, setSimulateLabelFail] = useState(false);

  const [isGenerating, setIsGenerating] = useState(false);
  const [labelPreview, setLabelPreview] = useState<ShippingLabelDto | null>(null);
  const [loadingPreview, setLoadingPreview] = useState(false);

  const [showVoidForm, setShowVoidForm] = useState(false);
  const [voidReason, setVoidReason] = useState('');
  const [simulateVoidFail, setSimulateVoidFail] = useState(false);
  const [isVoiding, setIsVoiding] = useState(false);
  const [panelError, setPanelError] = useState<string | null>(null);

  const inFlightGenerateRef = useRef(false);
  const generateIdempKeyRef = useRef<string | null>(null);
  const inFlightVoidRef = useRef(false);
  const voidIdempKeyRef = useRef<string | null>(null);

  useEffect(() => {
    setRates([]);
    setRateError(null);
    setPanelError(null);
    setLabelPreview(null);
    setShowVoidForm(false);
    setVoidReason('');
    generateIdempKeyRef.current = null;
    voidIdempKeyRef.current = null;
  }, [pkg?.id]);

  if (!canShip) {
    return (
      <section className="card packing-section-card" data-testid="carrier-ship-permission-notice">
        <div className="packing-section-header">
          <div>
            <h3>Carrier Rate Quote &amp; Shipping Labels</h3>
            <p className="outbound-subtitle">
              Requires <code>outbound:ship</code> permission to quote carrier rates, generate labels, or void labels.
            </p>
          </div>
          <span className="badge badge-info">Ship Permission Required</span>
        </div>
      </section>
    );
  }

  if (!pkg) {
    return (
      <section className="card packing-section-card" data-testid="carrier-no-package">
        <h3>Carrier Rate Quote &amp; Shipping Labels</h3>
        <p className="outbound-subtitle">Select a sealed/packed carton to quote carrier rates and generate shipping labels.</p>
      </section>
    );
  }

  const canRateOrLabel =
    pkg.status === PackageStatus.Packed ||
    pkg.status === PackageStatus.LabelPrinted ||
    pkg.status === PackageStatus.Staged;

  const handleQuoteRates = async () => {
    setLoadingRates(true);
    setRateError(null);
    setPanelError(null);
    try {
      const res = await outboundApi.getPackageShippingRates({ packageId: pkg.id });
      setRates(res);
    } catch (err: any) {
      setRateError(err.message || 'Failed to retrieve carrier rate quotes.');
    } finally {
      setLoadingRates(false);
    }
  };

  const handleGenerateLabel = async (e: React.FormEvent) => {
    e.preventDefault();
    if (inFlightGenerateRef.current || isGenerating) return;
    if (!generateIdempKeyRef.current) {
      generateIdempKeyRef.current = generateActionIdempotencyKey('lbl');
    }

    inFlightGenerateRef.current = true;
    setIsGenerating(true);
    setPanelError(null);
    try {
      const res = await outboundApi.generateShippingLabel({
        packageId: pkg.id,
        carrierCode,
        serviceLevel,
        labelFormat,
        idempotencyKey: generateIdempKeyRef.current,
        simulateFailure: simulateLabelFail,
      });

      if (!res.isSuccess) {
        setPanelError(
          res.errorMessage ||
            `Carrier rejected label request. Package ${res.packageNumber} safely remains in ${res.packageStatus} status.`
        );
        onFeedback({
          type: 'warning',
          message: `${res.message} Package remains ${res.packageStatus}.`,
        });
        return;
      }

      generateIdempKeyRef.current = null;
      setLabelPreview({
        id: res.labelId,
        packageId: res.packageId,
        packageNumber: res.packageNumber,
        trackingNumber: res.trackingNumber,
        carrierCode: res.carrierCode,
        serviceLevel: res.serviceLevel,
        labelFormat: res.labelFormat,
        labelData: res.labelData,
        routingCode: res.routingCode,
        status: res.labelStatus,
        generatedAtUtc: new Date().toISOString(),
      });
      onFeedback({
        type: 'success',
        message: `Generated ${res.carrierCode} label (${res.trackingNumber}) for ${res.packageNumber} ($${res.rateCharged.toFixed(2)}).`,
      });
      await onLabelUpdated();
    } catch (err: any) {
      setPanelError(err.message || 'Failed to generate shipping label.');
    } finally {
      inFlightGenerateRef.current = false;
      setIsGenerating(false);
    }
  };

  const handleFetchPreview = async () => {
    if (!pkg.latestLabelId) return;
    setLoadingPreview(true);
    setPanelError(null);
    try {
      const lbl = await outboundApi.getShippingLabelById(pkg.latestLabelId);
      setLabelPreview(lbl);
    } catch (err: any) {
      setPanelError(err.message || 'Failed to retrieve label payload.');
    } finally {
      setLoadingPreview(false);
    }
  };

  const handleVoidLabel = async (e: React.FormEvent) => {
    e.preventDefault();
    const targetLabelId = labelPreview?.id || pkg.latestLabelId;
    if (!targetLabelId || inFlightVoidRef.current || isVoiding) return;

    const reason = voidReason.trim();
    if (!reason) {
      setPanelError('Void reason is required to void an active carrier shipping label.');
      return;
    }

    if (!voidIdempKeyRef.current) {
      voidIdempKeyRef.current = generateActionIdempotencyKey('void');
    }

    inFlightVoidRef.current = true;
    setIsVoiding(true);
    setPanelError(null);
    try {
      const res = await outboundApi.voidShippingLabel(targetLabelId, {
        reason,
        idempotencyKey: voidIdempKeyRef.current,
        simulateFailure: simulateVoidFail,
      });

      if (!res.isSuccess) {
        setPanelError(
          res.errorMessage ||
            `Carrier rejected void request. Package remains in ${res.packageStatus} status.`
        );
        return;
      }

      voidIdempKeyRef.current = null;
      setShowVoidForm(false);
      setVoidReason('');
      setLabelPreview(null);
      onFeedback({
        type: 'success',
        message: `Voided label ${res.trackingNumber} (${res.voidConfirmationNumber}). Carton reverted to ${res.packageStatus}.`,
      });
      await onLabelUpdated();
    } catch (err: any) {
      setPanelError(err.message || 'Failed to void shipping label.');
    } finally {
      inFlightVoidRef.current = false;
      setIsVoiding(false);
    }
  };

  return (
    <section className="card packing-section-card" data-testid="carrier-label-panel">
      <div className="packing-section-header">
        <div>
          <span className="rf-eyebrow">SELECTED CARTON &bull; {pkg.packageNumber}</span>
          <h3>Carrier Rate Quote &amp; Shipping Labels</h3>
          <p className="outbound-subtitle">
            Decoupled carrier execution outside DB transactions. Gross Weight: <strong>{pkg.grossWeightKg.toFixed(2)} kg</strong>{' '}
            &bull; Status: <PackageStatusBadge status={pkg.status} />
          </p>
        </div>

        <button
          type="button"
          className="btn btn-secondary"
          disabled={!canRateOrLabel || loadingRates}
          onClick={handleQuoteRates}
          data-testid="quote-rates-btn"
        >
          <AppIcon name="DollarSign" size="sm" />
          <span>{loadingRates ? 'Quoting...' : 'Quote Carrier Rates'}</span>
        </button>
      </div>

      {!canRateOrLabel && (
        <div className="outbound-alert warning" data-testid="label-close-required-notice">
          Seal carton <strong>{pkg.packageNumber}</strong> with scale gross weight before quoting rates or printing carrier labels.
        </div>
      )}

      {rateError && (
        <div className="outbound-alert error" role="alert">
          {rateError}
        </div>
      )}

      {panelError && (
        <div className="outbound-alert error" role="alert" data-testid="carrier-panel-error">
          {panelError}
        </div>
      )}

      {rates.length > 0 && (
        <div className="rate-quotes-wrap" data-testid="rate-quotes-list">
          <p className="table-scroll-hint">Swipe horizontally to compare carrier rates and service days &rarr;</p>
          <div className="outbound-table-scroll">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Carrier</th>
                  <th>Service Level</th>
                  <th>Transit</th>
                  <th>Guaranteed</th>
                  <th>Total Rate</th>
                  <th>Select</th>
                </tr>
              </thead>
              <tbody>
                {rates.map((q) => {
                  const isSelected = q.carrierCode === carrierCode && q.serviceLevel === serviceLevel;
                  return (
                    <tr key={`${q.carrierCode}-${q.serviceLevel}`}>
                      <td className="code-font">
                        <strong>{q.carrierCode}</strong>
                      </td>
                      <td>{q.serviceDescription || formatServiceLevelLabel(q.serviceLevel)}</td>
                      <td>{q.estimatedDeliveryDays} day(s)</td>
                      <td>{q.isGuaranteed ? 'Yes' : 'Standard'}</td>
                      <td>
                        <strong>
                          {q.currency} {q.totalAmount.toFixed(2)}
                        </strong>
                      </td>
                      <td>
                        <button
                          type="button"
                          className={`btn btn-sm ${isSelected ? 'btn-primary' : 'btn-secondary'}`}
                          onClick={() => {
                            setCarrierCode(q.carrierCode);
                            setServiceLevel(q.serviceLevel);
                            generateIdempKeyRef.current = null;
                          }}
                          data-testid={`select-rate-${q.carrierCode}-${q.serviceLevel}`}
                        >
                          {isSelected ? 'Selected' : 'Use Rate'}
                        </button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Label Generation Form (when package is Packed without active label) */}
      {canRateOrLabel && !pkg.hasActiveLabel && (
        <form onSubmit={handleGenerateLabel} className="outbound-form-stack" data-testid="generate-label-form">
          <div className="outbound-form-grid">
            <div className="form-field">
              <label htmlFor="lbl-carrier">Carrier Code</label>
              <select
                id="lbl-carrier"
                value={carrierCode}
                onChange={(e) => {
                  setCarrierCode(e.target.value);
                  generateIdempKeyRef.current = null;
                }}
                data-testid="label-carrier-select"
              >
                {CARRIERS.map((c) => (
                  <option key={c} value={c}>
                    {c}
                  </option>
                ))}
              </select>
            </div>

            <div className="form-field">
              <label htmlFor="lbl-service">Service Level</label>
              <select
                id="lbl-service"
                value={serviceLevel}
                onChange={(e) => {
                  setServiceLevel(e.target.value as CarrierServiceLevel);
                  generateIdempKeyRef.current = null;
                }}
                data-testid="label-service-select"
              >
                {Object.values(CarrierServiceLevel).map((sl) => (
                  <option key={sl} value={sl}>
                    {formatServiceLevelLabel(sl)}
                  </option>
                ))}
              </select>
            </div>

            <div className="form-field">
              <label htmlFor="lbl-format">Label Format</label>
              <select
                id="lbl-format"
                value={labelFormat}
                onChange={(e) => setLabelFormat(e.target.value as 'ZPL' | 'PDF')}
                data-testid="label-format-select"
              >
                <option value="ZPL">Thermal ZPL</option>
                <option value="PDF">Laser PDF Payload</option>
              </select>
            </div>
          </div>

          <div className="packing-inline-controls">
            <label className="checkbox-inline">
              <input
                type="checkbox"
                checked={simulateLabelFail}
                onChange={(e) => setSimulateLabelFail(e.target.checked)}
                data-testid="simulate-label-fail-checkbox"
              />
              <span>Simulate Carrier Outage (Verify Safe Failure Recovery)</span>
            </label>

            <button
              type="submit"
              className="btn btn-primary"
              disabled={isGenerating}
              data-testid="generate-label-btn"
            >
              <AppIcon name="Truck" size="sm" />
              <span>{isGenerating ? 'Requesting Carrier Label...' : 'Generate Shipping Label'}</span>
            </button>
          </div>
        </form>
      )}

      {/* Active Label Summary + Preview + Void Flow */}
      {pkg.hasActiveLabel && (
        <div className="active-label-card" data-testid="active-label-summary">
          <div className="packing-section-header">
            <div>
              <span className="rf-eyebrow">ACTIVE SHIPPING LABEL</span>
              <p className="code-font">
                Carrier: <strong>{pkg.carrierCode || labelPreview?.carrierCode}</strong> &bull; Tracking:{' '}
                <strong data-testid="active-tracking-number">{pkg.trackingNumber || labelPreview?.trackingNumber}</strong>
              </p>
            </div>
            <div className="row-actions-group">
              {pkg.latestLabelId && (
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  disabled={loadingPreview}
                  onClick={handleFetchPreview}
                  data-testid="preview-label-btn"
                >
                  <AppIcon name="Eye" size="sm" />
                  <span>{loadingPreview ? 'Loading...' : 'View Label Payload'}</span>
                </button>
              )}
              {(pkg.latestLabelId || labelPreview?.id) && pkg.status !== PackageStatus.Loaded && (
                <button
                  type="button"
                  className="btn btn-danger btn-sm"
                  onClick={() => setShowVoidForm((v) => !v)}
                  data-testid="open-void-label-btn"
                >
                  Void Label
                </button>
              )}
            </div>
          </div>

          {showVoidForm && (
            <form onSubmit={handleVoidLabel} className="void-label-box" data-testid="void-label-form">
              <div className="form-field">
                <label htmlFor="void-label-reason">Void Reason *</label>
                <input
                  id="void-label-reason"
                  type="text"
                  placeholder="e.g., Service upgrade or damaged label reprint"
                  value={voidReason}
                  onChange={(e) => setVoidReason(e.target.value)}
                  data-testid="void-label-reason-input"
                />
              </div>
              <div className="packing-inline-controls">
                <label className="checkbox-inline">
                  <input
                    type="checkbox"
                    checked={simulateVoidFail}
                    onChange={(e) => setSimulateVoidFail(e.target.checked)}
                    data-testid="simulate-void-fail-checkbox"
                  />
                  <span>Simulate Void Failure</span>
                </label>
                <div className="row-actions-group">
                  <button
                    type="button"
                    className="btn btn-secondary btn-sm"
                    onClick={() => setShowVoidForm(false)}
                    disabled={isVoiding}
                  >
                    Cancel
                  </button>
                  <button
                    type="submit"
                    className="btn btn-danger btn-sm"
                    disabled={isVoiding}
                    data-testid="confirm-void-label-btn"
                  >
                    {isVoiding ? 'Voiding Label...' : 'Confirm Void Label'}
                  </button>
                </div>
              </div>
            </form>
          )}
        </div>
      )}

      {labelPreview && (
        <div className="label-payload-preview" data-testid="label-payload-preview">
          <div className="packing-section-header">
            <span className="code-font">
              <strong>{labelPreview.labelFormat}</strong> Label Preview &bull; Routing:{' '}
              {labelPreview.routingCode || 'STANDARD'} &bull;{' '}
              <ShippingLabelStatusBadge status={labelPreview.status} />
            </span>
          </div>
          <pre className="label-zpl-code">{labelPreview.labelData}</pre>
        </div>
      )}
    </section>
  );
};
