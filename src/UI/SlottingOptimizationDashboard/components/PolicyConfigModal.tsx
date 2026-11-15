import React from 'react';
import { SlottingPolicyDto } from '../types/slotting.types';

interface PolicyConfigModalProps {
  policy: SlottingPolicyDto | null;
  isOpen: boolean;
  onClose: () => void;
}

export const PolicyConfigModal: React.FC<PolicyConfigModalProps> = ({ policy, isOpen, onClose }) => {
  if (!isOpen || !policy) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/70 backdrop-blur-sm">
      <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-2xl max-h-[85vh] flex flex-col shadow-2xl overflow-hidden">
        {/* Header */}
        <div className="px-6 py-4 border-b border-slate-800 bg-slate-900/90 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <span className="w-2.5 h-2.5 rounded-full bg-cyan-400" />
            <h2 className="text-base font-bold text-white">
              Slotting Policy: {policy.name}
            </h2>
            <span className="px-2 py-0.5 rounded text-[10px] font-semibold bg-slate-800 text-slate-300 border border-slate-700">
              v{policy.version}
            </span>
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

        {/* Content */}
        <div className="p-6 overflow-y-auto space-y-5 text-xs text-slate-300">
          {/* Section: Prioritization Weights */}
          <div>
            <h3 className="text-xs font-semibold text-slate-400 uppercase tracking-wider mb-2">
              Multi-Factor Prioritization Formula Weights (Sum = 1.0)
            </h3>
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-2 font-mono">
              <div className="bg-slate-800/60 p-2.5 rounded-lg border border-slate-800">
                <span className="text-[10px] text-slate-400">Financial Benefit (w_b):</span>
                <div className="text-sm font-bold text-emerald-400">{policy.weightFinancialBenefit?.toFixed(2) ?? '0.35'}</div>
              </div>
              <div className="bg-slate-800/60 p-2.5 rounded-lg border border-slate-800">
                <span className="text-[10px] text-slate-400">Urgency (w_u):</span>
                <div className="text-sm font-bold text-amber-400">{policy.weightOperationalUrgency?.toFixed(2) ?? '0.30'}</div>
              </div>
              <div className="bg-slate-800/60 p-2.5 rounded-lg border border-slate-800">
                <span className="text-[10px] text-slate-400">Suitability (w_s):</span>
                <div className="text-sm font-bold text-violet-400">{policy.weightPlacementSuitability?.toFixed(2) ?? '0.20'}</div>
              </div>
              <div className="bg-slate-800/60 p-2.5 rounded-lg border border-slate-800">
                <span className="text-[10px] text-slate-400">Disruption (w_d):</span>
                <div className="text-sm font-bold text-cyan-400">{policy.weightDisruptionPenalty?.toFixed(2) ?? '0.15'}</div>
              </div>
            </div>
          </div>

          {/* Section: Economic Parameters */}
          <div>
            <h3 className="text-xs font-semibold text-slate-400 uppercase tracking-wider mb-2">
              Labor &amp; Operational Cost Parameters
            </h3>
            <div className="grid grid-cols-2 sm:grid-cols-3 gap-2 font-mono">
              <div className="bg-slate-800/60 p-2.5 rounded-lg border border-slate-800">
                <span className="text-[10px] text-slate-400">Labor Hourly Rate:</span>
                <div className="text-sm font-bold text-white">${policy.laborHourlyRateUsd.toFixed(2)}/hr</div>
              </div>
              <div className="bg-slate-800/60 p-2.5 rounded-lg border border-slate-800">
                <span className="text-[10px] text-slate-400">Est. Move Labor:</span>
                <div className="text-sm font-bold text-white">{policy.estimatedRelocationLaborMinutes.toFixed(1)} min</div>
              </div>
              <div className="bg-slate-800/60 p-2.5 rounded-lg border border-slate-800">
                <span className="text-[10px] text-slate-400">Unit Handling Cost:</span>
                <div className="text-sm font-bold text-white">${policy.handlingCostPerUnitUsd.toFixed(2)}/unit</div>
              </div>
              <div className="bg-slate-800/60 p-2.5 rounded-lg border border-slate-800">
                <span className="text-[10px] text-slate-400">Heavy Surcharge / Tier:</span>
                <div className="text-sm font-bold text-white">${policy.heavyHandlingSurchargePerTierUsd.toFixed(2)}</div>
              </div>
              <div className="bg-slate-800/60 p-2.5 rounded-lg border border-slate-800">
                <span className="text-[10px] text-slate-400">Base Disruption Cost:</span>
                <div className="text-sm font-bold text-white">${policy.baseDisruptionCostUsd.toFixed(2)}</div>
              </div>
              <div className="bg-slate-800/60 p-2.5 rounded-lg border border-slate-800">
                <span className="text-[10px] text-slate-400">Amortization Horizon:</span>
                <div className="text-sm font-bold text-white">{policy.amortizationMonths} months</div>
              </div>
            </div>
          </div>

          {/* Section: Negative Net Benefit Policy */}
          <div>
            <h3 className="text-xs font-semibold text-slate-400 uppercase tracking-wider mb-2">
              Policy Gating &amp; Urgency Boundaries
            </h3>
            <div className="bg-slate-800/60 p-3 rounded-lg border border-slate-800 space-y-2">
              <div className="flex justify-between">
                <span className="text-slate-400">Negative Net Benefit Policy:</span>
                <span className="font-mono font-bold text-amber-300">{policy.negativeNetBenefitPolicy}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-slate-400">Urgency Threshold for Negative Benefit:</span>
                <span className="font-mono font-bold text-white">{policy.urgencyThresholdForNegativeBenefit.toFixed(1)} / 100</span>
              </div>
              <div className="flex justify-between">
                <span className="text-slate-400">Max Relocations Per Batch:</span>
                <span className="font-mono font-bold text-white">{policy.maxRelocationsPerBatch} moves</span>
              </div>
            </div>
          </div>

          {/* Section: Physical Golden Zone Definitions */}
          <div>
            <h3 className="text-xs font-semibold text-slate-400 uppercase tracking-wider mb-2">
              Golden Zone Ergonomic Boundaries
            </h3>
            <div className="bg-slate-800/60 p-3 rounded-lg border border-slate-800 grid grid-cols-3 gap-2 font-mono">
              <div>
                <span className="text-[10px] text-slate-400">Min Height (Z):</span>
                <div className="font-bold text-white">{policy.goldenZoneMinZMeters.toFixed(2)} m</div>
              </div>
              <div>
                <span className="text-[10px] text-slate-400">Max Height (Z):</span>
                <div className="font-bold text-white">{policy.goldenZoneMaxZMeters.toFixed(2)} m</div>
              </div>
              <div>
                <span className="text-[10px] text-slate-400">Shelf Levels:</span>
                <div className="font-bold text-white">{policy.goldenZoneShelfLevels}</div>
              </div>
            </div>
          </div>
        </div>

        {/* Footer */}
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
