import React from 'react';
import { SlottingBatchSummaryDto, PrioritizedRelocationRecommendationDto } from '../types/slotting.types';

interface SlottingKpiCardsProps {
  batch: SlottingBatchSummaryDto | null;
  recommendations: PrioritizedRelocationRecommendationDto[];
}

export const SlottingKpiCards: React.FC<SlottingKpiCardsProps> = ({ batch, recommendations }) => {
  const feasibleMoves = recommendations.filter(r => r.isOperationallyFeasible);
  const totalMoves = recommendations.length;
  const avgPriority = totalMoves > 0
    ? (recommendations.reduce((sum, r) => sum + r.priorityScore, 0) / totalMoves).toFixed(1)
    : '0.0';

  const grossMonthlySavings = recommendations.reduce((sum, r) => sum + r.grossMonthlySavingsUsd, 0);
  const totalRelocationCost = recommendations.reduce((sum, r) => sum + r.totalRelocationCostUsd, 0);
  const netMonthlyBenefit = recommendations.reduce((sum, r) => sum + r.netMonthlyBenefitUsd, 0);

  // Payback period in months: Cost / Gross Monthly Savings
  const paybackMonths = grossMonthlySavings > 0
    ? (totalRelocationCost / grossMonthlySavings).toFixed(1)
    : 'N/A';

  return (
    <div className="space-y-3">
      {/* Disclaimer Banner */}
      <div className="bg-amber-950/30 border border-amber-800/40 rounded-lg px-4 py-2 flex items-center justify-between text-xs text-amber-200">
        <div className="flex items-center gap-2">
          <svg className="w-4 h-4 text-amber-400 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
          </svg>
          <span>
            <strong>Simulation / Planning Context:</strong> Financial figures displayed below are algorithmic projections derived from historical travel deltas and labor benchmarks. They are advisory estimates and do not represent realized general ledger transactions.
          </span>
        </div>
        <span className="font-mono text-[10px] text-amber-400/80 px-2 py-0.5 rounded bg-amber-900/40 border border-amber-700/50">
          Zero Ledger Impact
        </span>
      </div>

      {/* KPI Cards Grid */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-6 gap-3">
        {/* Card 1: Projected Gross Savings */}
        <div className="bg-slate-900/90 border border-slate-800 rounded-xl p-4 flex flex-col justify-between relative overflow-hidden">
          <div className="absolute top-2 right-2 px-1.5 py-0.5 rounded text-[9px] font-semibold bg-cyan-500/10 text-cyan-400 border border-cyan-500/20">
            Projected
          </div>
          <div>
            <span className="text-[11px] font-medium text-slate-400 tracking-wide uppercase">
              Gross Travel Savings
            </span>
            <div className="mt-2 flex items-baseline gap-1">
              <span className="text-2xl font-bold text-white tracking-tight">
                ${grossMonthlySavings.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
              </span>
              <span className="text-xs text-slate-400">/mo</span>
            </div>
          </div>
          <div className="mt-3 text-[11px] text-slate-400 flex items-center justify-between border-t border-slate-800/80 pt-2">
            <span>Annual Run-rate:</span>
            <span className="font-mono text-cyan-300 font-medium">
              ${(grossMonthlySavings * 12).toLocaleString('en-US', { minimumFractionDigits: 0, maximumFractionDigits: 0 })}/yr
            </span>
          </div>
        </div>

        {/* Card 2: Relocation Cost */}
        <div className="bg-slate-900/90 border border-slate-800 rounded-xl p-4 flex flex-col justify-between relative overflow-hidden">
          <div className="absolute top-2 right-2 px-1.5 py-0.5 rounded text-[9px] font-semibold bg-rose-500/10 text-rose-400 border border-rose-500/20">
            Est. Cost
          </div>
          <div>
            <span className="text-[11px] font-medium text-slate-400 tracking-wide uppercase">
              Relocation Cost
            </span>
            <div className="mt-2 flex items-baseline gap-1">
              <span className="text-2xl font-bold text-rose-400 tracking-tight">
                ${totalRelocationCost.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
              </span>
              <span className="text-xs text-slate-400">upfront</span>
            </div>
          </div>
          <div className="mt-3 text-[11px] text-slate-400 flex items-center justify-between border-t border-slate-800/80 pt-2">
            <span>Payback Period:</span>
            <span className="font-mono text-slate-300 font-medium">{paybackMonths} mo</span>
          </div>
        </div>

        {/* Card 3: Net Monthly Benefit */}
        <div className="bg-slate-900/90 border border-slate-800 rounded-xl p-4 flex flex-col justify-between relative overflow-hidden">
          <div className="absolute top-2 right-2 px-1.5 py-0.5 rounded text-[9px] font-semibold bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
            Net Value
          </div>
          <div>
            <span className="text-[11px] font-medium text-slate-400 tracking-wide uppercase">
              Net Monthly Benefit
            </span>
            <div className="mt-2 flex items-baseline gap-1">
              <span className={`text-2xl font-bold tracking-tight ${netMonthlyBenefit >= 0 ? 'text-emerald-400' : 'text-rose-400'}`}>
                ${netMonthlyBenefit.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
              </span>
              <span className="text-xs text-slate-400">/mo</span>
            </div>
          </div>
          <div className="mt-3 text-[11px] text-slate-400 flex items-center justify-between border-t border-slate-800/80 pt-2">
            <span>Amortized Benefit:</span>
            <span className="font-mono text-emerald-300 font-medium">B_gross - C_mo</span>
          </div>
        </div>

        {/* Card 4: Move Feasibility */}
        <div className="bg-slate-900/90 border border-slate-800 rounded-xl p-4 flex flex-col justify-between relative overflow-hidden">
          <div className="absolute top-2 right-2 px-1.5 py-0.5 rounded text-[9px] font-semibold bg-indigo-500/10 text-indigo-400 border border-indigo-500/20">
            Hard Gates
          </div>
          <div>
            <span className="text-[11px] font-medium text-slate-400 tracking-wide uppercase">
              Operational Feasibility
            </span>
            <div className="mt-2 flex items-baseline gap-1">
              <span className="text-2xl font-bold text-white tracking-tight">
                {feasibleMoves.length}
              </span>
              <span className="text-xs text-slate-400">/ {totalMoves} candidates</span>
            </div>
          </div>
          <div className="mt-3 text-[11px] text-slate-400 flex items-center justify-between border-t border-slate-800/80 pt-2">
            <span>9 Physical Hard Gates:</span>
            <span className="font-mono text-indigo-300 font-medium">
              {totalMoves > 0 ? `${Math.round((feasibleMoves.length / totalMoves) * 100)}% valid` : '0%'}
            </span>
          </div>
        </div>

        {/* Card 5: Average Priority Score */}
        <div className="bg-slate-900/90 border border-slate-800 rounded-xl p-4 flex flex-col justify-between relative overflow-hidden">
          <div className="absolute top-2 right-2 px-1.5 py-0.5 rounded text-[9px] font-semibold bg-violet-500/10 text-violet-400 border border-violet-500/20">
            Algorithm
          </div>
          <div>
            <span className="text-[11px] font-medium text-slate-400 tracking-wide uppercase">
              Avg Priority Score (P)
            </span>
            <div className="mt-2 flex items-baseline gap-1">
              <span className="text-2xl font-bold text-violet-300 tracking-tight">
                {avgPriority}
              </span>
              <span className="text-xs text-slate-400">/ 100.0</span>
            </div>
          </div>
          <div className="mt-3 text-[11px] text-slate-400 flex items-center justify-between border-t border-slate-800/80 pt-2">
            <span>Deterministic Rank:</span>
            <span className="font-mono text-slate-300 font-medium">P &#8595; B_net &#8595; U &#8595;</span>
          </div>
        </div>

        {/* Card 6: Negative Benefit Disqualified */}
        <div className="bg-slate-900/90 border border-slate-800 rounded-xl p-4 flex flex-col justify-between relative overflow-hidden">
          <div className="absolute top-2 right-2 px-1.5 py-0.5 rounded text-[9px] font-semibold bg-amber-500/10 text-amber-400 border border-amber-500/20">
            Policy Gate
          </div>
          <div>
            <span className="text-[11px] font-medium text-slate-400 tracking-wide uppercase">
              Disqualified Moves
            </span>
            <div className="mt-2 flex items-baseline gap-1">
              <span className="text-2xl font-bold text-amber-400 tracking-tight">
                {batch?.disqualifiedNegativeBenefitCount ?? 0}
              </span>
              <span className="text-xs text-slate-400">negative B_net</span>
            </div>
          </div>
          <div className="mt-3 text-[11px] text-slate-400 flex items-center justify-between border-t border-slate-800/80 pt-2">
            <span>Policy Filter:</span>
            <span className="font-mono text-amber-300 font-medium">B_net &lt; 0 Gated</span>
          </div>
        </div>
      </div>
    </div>
  );
};
