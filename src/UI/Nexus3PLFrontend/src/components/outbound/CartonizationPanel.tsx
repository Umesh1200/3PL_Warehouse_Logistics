import React, { useState } from 'react';
import { outboundApi } from '../../services/outboundApi';
import {
  CartonizationRecommendationDto,
  PackagingMaterialDto,
} from '../../types/outbound';
import { formatPackagingMaterialTypeLabel } from './OutboundStatusBadge';
import { AppIcon } from '../common/AppIcon';

export interface CartonizationPanelProps {
  clientId: string;
  facilityId: string;
  outboundOrderId: string;
  materials: PackagingMaterialDto[];
  isSessionActive: boolean;
  isCreatingCarton: boolean;
  onApplyRecommendedBox: (materialId: string) => void;
}

export const CartonizationPanel: React.FC<CartonizationPanelProps> = ({
  clientId,
  facilityId,
  outboundOrderId,
  materials,
  isSessionActive,
  isCreatingCarton,
  onApplyRecommendedBox,
}) => {
  const [preferredMaterialId, setPreferredMaterialId] = useState('');
  const [utilizationFactor, setUtilizationFactor] = useState('0.85');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [recommendation, setRecommendation] = useState<CartonizationRecommendationDto | null>(null);

  const handleRunRecommendation = async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await outboundApi.recommendCartonization({
        clientId,
        facilityId,
        outboundOrderId,
        preferredPackagingMaterialId: preferredMaterialId || undefined,
        volumeUtilizationFactor: Number(utilizationFactor) || 0.85,
      });
      setRecommendation(res);
    } catch (err: any) {
      setError(err.message || 'Failed to compute 3D cartonization recommendation.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <section className="card packing-section-card" data-testid="cartonization-panel">
      <div className="packing-section-header">
        <div>
          <h3>3D Cartonization Engine</h3>
          <p className="outbound-subtitle">
            Evaluates orthogonal bounding-box fit, volume fill target, and carton burst weight limits.
          </p>
        </div>
        <button
          type="button"
          className="btn btn-secondary"
          disabled={loading}
          onClick={handleRunRecommendation}
          data-testid="recommend-cartonization-btn"
        >
          <AppIcon name="Sparkles" size="sm" />
          <span>{loading ? 'Calculating Fit...' : 'Recommend Cartonization'}</span>
        </button>
      </div>

      <div className="outbound-form-grid">
        <div className="form-field">
          <label htmlFor="cartonize-pref-box">Preferred Packaging Material</label>
          <select
            id="cartonize-pref-box"
            value={preferredMaterialId}
            onChange={(e) => setPreferredMaterialId(e.target.value)}
            data-testid="cartonize-preferred-material"
          >
            <option value="">Auto-Select Optimal Box</option>
            {materials.map((m) => (
              <option key={m.id} value={m.id}>
                {m.code} &mdash; {m.name} ({m.lengthCm}&times;{m.widthCm}&times;{m.heightCm} cm, max {m.maxWeightKg} kg)
              </option>
            ))}
          </select>
        </div>

        <div className="form-field">
          <label htmlFor="cartonize-util">Max Volume Fill Factor</label>
          <select
            id="cartonize-util"
            value={utilizationFactor}
            onChange={(e) => setUtilizationFactor(e.target.value)}
            data-testid="cartonize-utilization-select"
          >
            <option value="0.75">75% (Fragile / Void Fill)</option>
            <option value="0.85">85% (Standard 3PL Target)</option>
            <option value="0.95">95% (Tight Industrial Fit)</option>
          </select>
        </div>
      </div>

      {error && (
        <div className="outbound-alert error" role="alert" data-testid="cartonization-error">
          {error}
        </div>
      )}

      {recommendation && !recommendation.isSuccess && (
        <div className="outbound-alert warning" role="alert" data-testid="cartonization-warning">
          {recommendation.message}
        </div>
      )}

      {recommendation && recommendation.isSuccess && (
        <div className="cartonization-results" data-testid="cartonization-results">
          <div className="outbound-alert success">
            <span>
              <strong>{recommendation.recommendedPackageCount} carton(s) recommended</strong> &bull; Est. Total Gross Weight:{' '}
              <strong>{recommendation.totalEstimatedGrossWeightKg.toFixed(2)} kg</strong> &bull; {recommendation.message}
            </span>
          </div>

          {recommendation.warnings.length > 0 && (
            <div className="outbound-alert warning">
              {recommendation.warnings.join(' | ')}
            </div>
          )}

          <div className="carton-rec-grid">
            {recommendation.recommendedPackages.map((pkg) => (
              <div key={pkg.packageIndex} className="carton-rec-card" data-testid={`carton-rec-card-${pkg.packageIndex}`}>
                <div className="carton-rec-top">
                  <div>
                    <span className="rf-eyebrow">
                      BOX #{pkg.packageIndex} &bull; {formatPackagingMaterialTypeLabel(pkg.materialType)}
                    </span>
                    <h4 className="code-font">
                      {pkg.packagingMaterialCode} &mdash; {pkg.packagingMaterialName}
                    </h4>
                  </div>
                  <button
                    type="button"
                    className="btn btn-primary btn-sm"
                    disabled={!isSessionActive || isCreatingCarton}
                    onClick={() => onApplyRecommendedBox(pkg.packagingMaterialId)}
                    data-testid={`apply-rec-box-btn-${pkg.packageIndex}`}
                  >
                    Create Carton
                  </button>
                </div>

                <div className="carton-rec-metrics">
                  <span>
                    Dims: <strong className="code-font">{pkg.boxLengthCm}&times;{pkg.boxWidthCm}&times;{pkg.boxHeightCm} cm</strong>
                  </span>
                  <span>
                    Est. Gross: <strong>{pkg.estimatedGrossWeightKg.toFixed(2)} kg</strong> (Tare {pkg.tareWeightKg.toFixed(2)} kg / Max {pkg.boxMaxWeightKg} kg)
                  </span>
                  <span>
                    Dim Weight: <strong>{pkg.volumetricWeightKg.toFixed(2)} kg</strong>
                  </span>
                </div>

                <div className="allocation-bar-wrap" style={{ marginTop: '8px' }}>
                  <div className="allocation-bar-track">
                    <div
                      className="allocation-bar-fill"
                      style={{ width: `${Math.min(100, Math.max(0, pkg.volumeUtilizationPercentage))}%` }}
                    />
                  </div>
                  <span className="allocation-bar-label">{pkg.volumeUtilizationPercentage.toFixed(1)}% vol</span>
                </div>

                {pkg.items.length > 0 && (
                  <ul className="carton-rec-items">
                    {pkg.items.map((item, idx) => (
                      <li key={`${item.itemId}-${idx}`}>
                        <span className="code-font">{item.sku}</span> &times; <strong>{item.quantity}</strong> ({item.totalWeightKg.toFixed(2)} kg)
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            ))}
          </div>
        </div>
      )}
    </section>
  );
};
