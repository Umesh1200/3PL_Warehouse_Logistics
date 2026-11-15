import React from 'react';
import { SlottingRecommendationDetailDto } from '../types/slotting.types';

interface RecommendationDetailModalProps {
  detail: SlottingRecommendationDetailDto | null;
  isOpen: boolean;
  onClose: () => void;
}

export const RecommendationDetailModal: React.FC<RecommendationDetailModalProps> = ({
  detail,
  isOpen,
  onClose
}) => {
  if (!isOpen || !detail) return null;

  const {
    currentLocation: curr,
    proposedLocation: prop
  } = detail;

  const hardGatesList = [
    { name: 'Gate 1: Weight Capacity', ok: !detail.violatedConstraints?.some(c => c.toLowerCase().includes('weight')) },
    { name: 'Gate 2: Volumetric Capacity', ok: !detail.violatedConstraints?.some(c => c.toLowerCase().includes('volumetric')) },
    { name: 'Gate 3: Hazardous Goods Isolation', ok: !detail.violatedConstraints?.some(c => c.toLowerCase().includes('hazard')) },
    { name: 'Gate 4: Planar Footprint & Crush', ok: !detail.violatedConstraints?.some(c => c.toLowerCase().includes('crush') || c.toLowerCase().includes('footprint')) },
    { name: 'Gate 5: Temperature Zone Integrity', ok: !detail.violatedConstraints?.some(c => c.toLowerCase().includes('temperature')) },
    { name: 'Gate 6: Fragile / Heavy Stacking', ok: !detail.violatedConstraints?.some(c => c.toLowerCase().includes('fragile') || c.toLowerCase().includes('stack')) },
    { name: 'Gate 7: Golden Zone Heavy Cap', ok: !detail.violatedConstraints?.some(c => c.toLowerCase().includes('golden zone heavy')) },
    { name: 'Gate 8: Bottom Tier Light Velocity', ok: !detail.violatedConstraints?.some(c => c.toLowerCase().includes('bottom tier')) },
    { name: 'Gate 9: Reserved Safety Margin', ok: !detail.violatedConstraints?.some(c => c.toLowerCase().includes('margin') || c.toLowerCase().includes('safety')) },
    { name: '2D Non-Overlap Packing', ok: !detail.violatedConstraints?.some(c => c.toLowerCase().includes('pack') || c.toLowerCase().includes('geometric')) }
  ];

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/70 backdrop-blur-sm animate-fadeIn">
      <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-4xl max-h-[90vh] flex flex-col shadow-2xl overflow-hidden">
        {/* Modal Header */}
        <div className="px-6 py-4 border-b border-slate-800 bg-slate-900/90 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <span className="p-2 rounded-lg bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
              <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z" />
              </svg>
            </span>
            <div>
              <div className="flex items-center gap-2">
                <h2 className="text-base font-bold text-white font-mono">{detail.itemSku}</h2>
                <span className="px-2 py-0.5 rounded text-[10px] font-semibold bg-emerald-500/20 text-emerald-300 border border-emerald-500/30">
                  Priority: {detail.priorityScore.toFixed(1)} / 100.0
                </span>
                <span className="px-2 py-0.5 rounded text-[10px] font-semibold bg-slate-800 text-slate-300 border border-slate-700">
                  Status: {detail.status}
                </span>
              </div>
              <p className="text-xs text-slate-400 mt-0.5">
                {detail.itemDescription || 'SKU relocation recommendation explainability audit trail'}
              </p>
            </div>
          </div>

          <button
            onClick={onClose}
            className="p-1.5 rounded-lg text-slate-400 hover:text-white hover:bg-slate-800 transition-colors"
          >
            <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        {/* Modal Scrollable Body */}
        <div className="p-6 overflow-y-auto space-y-6 text-xs text-slate-300">
          {/* Section 1: Side-by-Side Location Comparison */}
          <div>
            <h3 className="text-sm font-semibold text-white mb-3 flex items-center gap-2">
              <span className="w-2 h-2 rounded-full bg-cyan-400" />
              Source vs Destination Location Comparison
            </h3>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              {/* Current Location Card */}
              <div className="bg-slate-800/40 border border-slate-800 rounded-xl p-4">
                <div className="flex items-center justify-between pb-2 border-b border-slate-700/60 mb-3">
                  <span className="text-xs font-semibold text-slate-400 uppercase tracking-wider">
                    Current Location (Source)
                  </span>
                  <span className="font-mono text-sm font-bold text-slate-200">
                    {curr?.locationCode}
                  </span>
                </div>
                <div className="space-y-2 text-xs">
                  <div className="flex justify-between">
                    <span className="text-slate-400">Zone Code:</span>
                    <span className="font-mono">{curr?.zoneCode}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-slate-400">Distance to Dock:</span>
                    <span className="font-mono">{curr?.distanceToDockMeters.toFixed(1)} m</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-slate-400">Shelf Level &amp; Z:</span>
                    <span className="font-mono">Level {curr?.shelfLevel} ({curr?.zCoord.toFixed(2)} m)</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-slate-400">Golden Zone:</span>
                    <span className={curr?.isInGoldenZone ? 'text-emerald-400 font-semibold' : 'text-slate-400'}>
                      {curr?.isInGoldenZone ? 'Yes (Ergonomic)' : 'No (High / Low)'}
                    </span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-slate-400">Weight Occupancy:</span>
                    <span className="font-mono">{curr?.weightUtilizationPercent.toFixed(1)}% ({curr?.occupiedWeightKg.toFixed(1)} / {curr?.maxWeightKg.toFixed(1)} kg)</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-slate-400">Volume Occupancy:</span>
                    <span className="font-mono">{curr?.volumeUtilizationPercent.toFixed(1)}%</span>
                  </div>
                </div>
              </div>

              {/* Proposed Location Card */}
              <div className="bg-emerald-950/20 border border-emerald-800/50 rounded-xl p-4">
                <div className="flex items-center justify-between pb-2 border-b border-emerald-800/60 mb-3">
                  <span className="text-xs font-semibold text-emerald-400 uppercase tracking-wider">
                    Proposed Location (Destination)
                  </span>
                  <span className="font-mono text-sm font-bold text-emerald-300">
                    {prop?.locationCode}
                  </span>
                </div>
                <div className="space-y-2 text-xs">
                  <div className="flex justify-between">
                    <span className="text-slate-400">Zone Code:</span>
                    <span className="font-mono text-emerald-200">{prop?.zoneCode}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-slate-400">Distance to Dock:</span>
                    <span className="font-mono text-emerald-200 font-bold">
                      {prop?.distanceToDockMeters.toFixed(1)} m ({detail.travelDistanceDeltaMeters.toFixed(1)} m saved)
                    </span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-slate-400">Shelf Level &amp; Z:</span>
                    <span className="font-mono text-emerald-200">Level {prop?.shelfLevel} ({prop?.zCoord.toFixed(2)} m)</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-slate-400">Golden Zone:</span>
                    <span className={prop?.isInGoldenZone ? 'text-emerald-400 font-semibold' : 'text-slate-400'}>
                      {prop?.isInGoldenZone ? 'Yes (Optimal Pick Face)' : 'No'}
                    </span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-slate-400">Projected Weight Occupancy:</span>
                    <span className="font-mono text-emerald-200">{prop?.weightUtilizationPercent.toFixed(1)}% ({prop?.occupiedWeightKg.toFixed(1)} / {prop?.maxWeightKg.toFixed(1)} kg)</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-slate-400">Projected Volume Occupancy:</span>
                    <span className="font-mono text-emerald-200">{prop?.volumeUtilizationPercent.toFixed(1)}%</span>
                  </div>
                </div>
              </div>
            </div>
          </div>

          {/* Section 2: 6D Placement Suitability Breakdown */}
          <div>
            <div className="flex items-center justify-between mb-3">
              <h3 className="text-sm font-semibold text-white flex items-center gap-2">
                <span className="w-2 h-2 rounded-full bg-violet-400" />
                6D Placement Suitability Breakdown (Score: {detail.suitabilityScore.toFixed(1)} / 100)
              </h3>
            </div>
            <div className="grid grid-cols-2 md:grid-cols-3 gap-3">
              <div className="bg-slate-800/40 border border-slate-800 rounded-lg p-3">
                <div className="flex justify-between text-xs text-slate-400 mb-1">
                  <span>Travel Distance</span>
                  <span className="font-mono font-bold text-slate-200">{detail.travelDistanceDeltaMeters.toFixed(1)}m</span>
                </div>
              </div>
              <div className="bg-slate-800/40 border border-slate-800 rounded-lg p-3">
                <div className="flex justify-between text-xs text-slate-400 mb-1">
                  <span>Ergonomics</span>
                  <span className="font-mono font-bold text-slate-200">{detail.ergonomicScore.toFixed(1)}</span>
                </div>
              </div>
              <div className="bg-slate-800/40 border border-slate-800 rounded-lg p-3">
                <div className="flex justify-between text-xs text-slate-400 mb-1">
                  <span>ABC Affinity</span>
                  <span className="font-mono font-bold text-slate-200">{detail.abcAffinityScore.toFixed(1)}</span>
                </div>
              </div>
              <div className="bg-slate-800/40 border border-slate-800 rounded-lg p-3">
                <div className="flex justify-between text-xs text-slate-400 mb-1">
                  <span>Velocity Affinity</span>
                  <span className="font-mono font-bold text-slate-200">{detail.velocityAffinityScore.toFixed(1)}</span>
                </div>
              </div>
              <div className="bg-slate-800/40 border border-slate-800 rounded-lg p-3">
                <div className="flex justify-between text-xs text-slate-400 mb-1">
                  <span>Pick Density</span>
                  <span className="font-mono font-bold text-slate-200">{detail.pickDensityScore.toFixed(1)}</span>
                </div>
              </div>
              <div className="bg-slate-800/40 border border-slate-800 rounded-lg p-3">
                <div className="flex justify-between text-xs text-slate-400 mb-1">
                  <span>Product Affinity</span>
                  <span className="font-mono font-bold text-slate-200">{detail.productAffinityScore.toFixed(1)}</span>
                </div>
              </div>
            </div>
          </div>

          {/* Section 3: 4-Factor Operational Urgency Breakdown */}
          <div>
            <div className="flex items-center justify-between mb-3">
              <h3 className="text-sm font-semibold text-white flex items-center gap-2">
                <span className="w-2 h-2 rounded-full bg-amber-400" />
                4-Factor Operational Urgency Breakdown (Score: {detail.urgencyScore.toFixed(1)} / 100)
              </h3>
              <span className="text-xs text-amber-300 font-mono">
                {detail.urgencyReason || 'Operational evaluation active'}
              </span>
            </div>
            <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
              <div className="bg-slate-800/40 border border-slate-800 rounded-lg p-3">
                <div className="flex justify-between text-xs text-slate-400 mb-1">
                  <span>Velocity Mismatch</span>
                  <span className="font-mono font-bold text-amber-300">{detail.velocityMismatchScore.toFixed(1)}</span>
                </div>
              </div>
              <div className="bg-slate-800/40 border border-slate-800 rounded-lg p-3">
                <div className="flex justify-between text-xs text-slate-400 mb-1">
                  <span>Congestion</span>
                  <span className="font-mono font-bold text-amber-300">{detail.goldenZoneCongestionScore.toFixed(1)}</span>
                </div>
              </div>
              <div className="bg-slate-800/40 border border-slate-800 rounded-lg p-3">
                <div className="flex justify-between text-xs text-slate-400 mb-1">
                  <span>Seasonal Spike</span>
                  <span className="font-mono font-bold text-amber-300">{detail.seasonalDemandScore.toFixed(1)}</span>
                </div>
              </div>
              <div className="bg-slate-800/40 border border-slate-800 rounded-lg p-3">
                <div className="flex justify-between text-xs text-slate-400 mb-1">
                  <span>Ergonomic Hazard</span>
                  <span className="font-mono font-bold text-amber-300">{detail.ergonomicHazardScore.toFixed(1)}</span>
                </div>
              </div>
            </div>
          </div>

          {/* Section 4: Transparent Relocation Economics */}
          <div>
            <h3 className="text-sm font-semibold text-white mb-3 flex items-center gap-2">
              <span className="w-2 h-2 rounded-full bg-emerald-400" />
              Transparent Relocation Economics Breakdown
            </h3>
            <div className="bg-slate-800/40 border border-slate-800 rounded-xl p-4 grid grid-cols-2 sm:grid-cols-4 gap-4">
              <div>
                <span className="text-slate-400 text-[11px]">Gross Monthly Savings:</span>
                <div className="text-base font-bold text-cyan-400 font-mono mt-0.5">
                  ${detail.grossMonthlySavingsUsd.toFixed(2)}/mo
                </div>
              </div>

              <div>
                <span className="text-slate-400 text-[11px]">Total Relocation Cost:</span>
                <div className="text-base font-bold text-rose-400 font-mono mt-0.5">
                  ${detail.totalRelocationCostUsd.toFixed(2)}
                </div>
                <div className="text-[10px] text-slate-500 mt-0.5 font-mono">
                  H:${detail.handlingCostUsd.toFixed(1)} + M:${detail.movementCostUsd.toFixed(1)} + D:${detail.disruptionCostUsd.toFixed(1)}
                </div>
              </div>

              <div>
                <span className="text-slate-400 text-[11px]">Amortized Cost / Mo:</span>
                <div className="text-base font-bold text-slate-300 font-mono mt-0.5">
                  ${detail.amortizedMonthlyCostUsd.toFixed(2)}/mo
                </div>
              </div>

              <div>
                <span className="text-slate-400 text-[11px]">Net Monthly Benefit:</span>
                <div className={`text-base font-bold font-mono mt-0.5 ${detail.netMonthlyBenefitUsd >= 0 ? 'text-emerald-400' : 'text-rose-400'}`}>
                  ${detail.netMonthlyBenefitUsd.toFixed(2)}/mo
                </div>
              </div>
            </div>
          </div>

          {/* Section 5: Constraint Evaluation Audit Log */}
          <div>
            <h3 className="text-sm font-semibold text-white mb-3 flex items-center gap-2">
              <span className="w-2 h-2 rounded-full bg-blue-400" />
              Constraint Audit: 9 Hard Gates &amp; 2D Geometric Packing
            </h3>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
              {hardGatesList.map(gate => (
                <div
                  key={gate.name}
                  className={`flex items-center justify-between px-3 py-2 rounded-lg border text-xs ${
                    gate.ok
                      ? 'bg-emerald-950/20 border-emerald-800/40 text-emerald-300'
                      : 'bg-rose-950/30 border-rose-800/50 text-rose-300'
                  }`}
                >
                  <span>{gate.name}</span>
                  <span className="font-semibold">{gate.ok ? 'PASSED' : 'VIOLATED'}</span>
                </div>
              ))}
            </div>
          </div>
        </div>

        {/* Modal Footer */}
        <div className="px-6 py-3 border-t border-slate-800 bg-slate-900/90 flex justify-end">
          <button
            onClick={onClose}
            className="px-4 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs font-medium border border-slate-700 transition-colors"
          >
            Close
          </button>
        </div>
      </div>
    </div>
  );
};
