import React from 'react';
import { SlottingPolicyDto, SlottingBatchSummaryDto } from '../types/slotting.types';

interface SlottingDashboardHeaderProps {
  facilityId: string;
  clientId: string | null;
  activePolicy: SlottingPolicyDto | null;
  batches: SlottingBatchSummaryDto[];
  selectedBatchId: string | null;
  isLoading: boolean;
  isPrioritizing: boolean;
  onFacilityChange: (id: string) => void;
  onClientChange: (id: string | null) => void;
  onBatchSelect: (batchId: string) => void;
  onRunPrioritization: () => void;
  onRefresh: () => void;
  onOpenPolicyModal: () => void;
}

export const SlottingDashboardHeader: React.FC<SlottingDashboardHeaderProps> = ({
  facilityId,
  clientId,
  activePolicy,
  batches,
  selectedBatchId,
  isLoading,
  isPrioritizing,
  onFacilityChange,
  onClientChange,
  onBatchSelect,
  onRunPrioritization,
  onRefresh,
  onOpenPolicyModal
}) => {
  return (
    <header className="bg-slate-900 border-b border-slate-800 px-6 py-4 text-white">
      <div className="flex flex-col lg:flex-row lg:items-center lg:justify-between gap-4">
        {/* Title & Status Badging */}
        <div>
          <div className="flex items-center gap-3">
            <h1 className="text-xl font-bold tracking-tight text-white flex items-center gap-2">
              <span className="inline-block w-2.5 h-2.5 rounded-full bg-emerald-500 animate-pulse" />
              Slotting Optimization & Prioritization Engine
            </h1>
            <span className="px-2.5 py-0.5 text-xs font-semibold rounded-full bg-indigo-500/20 text-indigo-300 border border-indigo-500/30">
              Phase 6.3.8
            </span>
            <span className="px-2 py-0.5 text-xs rounded bg-amber-500/20 text-amber-300 border border-amber-500/30 font-mono">
              Advisory Only (Zero Auto-Move)
            </span>
          </div>
          <p className="text-xs text-slate-400 mt-1">
            Deterministic multi-factor relocation ranking, 6D suitability explainability, and multi-tenant ledger-safe approvals.
          </p>
        </div>

        {/* Global Controls & Actions */}
        <div className="flex flex-wrap items-center gap-3">
          {/* Facility Input */}
          <div className="flex items-center gap-1.5 bg-slate-800/80 rounded-lg px-3 py-1.5 border border-slate-700">
            <label className="text-xs font-medium text-slate-400">Facility:</label>
            <input
              type="text"
              value={facilityId}
              onChange={e => onFacilityChange(e.target.value)}
              placeholder="Facility GUID"
              className="bg-transparent text-xs text-slate-200 outline-none w-36 font-mono"
            />
          </div>

          {/* Client Tenant Input */}
          <div className="flex items-center gap-1.5 bg-slate-800/80 rounded-lg px-3 py-1.5 border border-slate-700">
            <label className="text-xs font-medium text-slate-400">Client:</label>
            <input
              type="text"
              value={clientId || ''}
              onChange={e => onClientChange(e.target.value.trim() ? e.target.value.trim() : null)}
              placeholder="All Tenants"
              className="bg-transparent text-xs text-slate-200 outline-none w-32 font-mono"
            />
          </div>

          {/* Active Batch Selector */}
          {batches.length > 0 && (
            <div className="flex items-center gap-1.5 bg-slate-800/80 rounded-lg px-3 py-1.5 border border-slate-700">
              <label className="text-xs font-medium text-slate-400">Batch:</label>
              <select
                value={selectedBatchId || ''}
                onChange={e => onBatchSelect(e.target.value)}
                className="bg-transparent text-xs text-slate-200 outline-none font-mono cursor-pointer"
              >
                {batches.map(b => (
                  <option key={b.batchId} value={b.batchId} className="bg-slate-800 text-slate-200">
                    {b.batchNumber} ({b.status} - {b.totalRecommendations} moves)
                  </option>
                ))}
              </select>
            </div>
          )}

          {/* Policy Badge & Config Button */}
          {activePolicy ? (
            <button
              onClick={onOpenPolicyModal}
              title="Inspect Active Policy Weights & Constraints"
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 border border-slate-700 text-xs text-slate-300 transition-colors"
            >
              <span className="w-2 h-2 rounded-full bg-cyan-400" />
              <span className="font-medium">{activePolicy.name}</span>
              <span className="text-slate-500 text-[10px]">v{activePolicy.version}</span>
            </button>
          ) : (
            <button
              onClick={onOpenPolicyModal}
              className="px-2.5 py-1.5 rounded-lg bg-slate-800 border border-slate-700 text-xs text-slate-400"
            >
              No Policy
            </button>
          )}

          {/* Action Buttons */}
          <button
            onClick={onRefresh}
            disabled={isLoading}
            className="p-2 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-300 border border-slate-700 transition-colors disabled:opacity-50"
            title="Refresh Batches"
          >
            <svg className={`w-4 h-4 ${isLoading ? 'animate-spin' : ''}`} fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
            </svg>
          </button>

          <button
            onClick={onRunPrioritization}
            disabled={isPrioritizing || isLoading}
            className="flex items-center gap-2 px-4 py-1.5 rounded-lg bg-emerald-600 hover:bg-emerald-500 font-medium text-xs text-white shadow-lg shadow-emerald-900/30 transition-all disabled:opacity-50"
          >
            {isPrioritizing ? (
              <>
                <svg className="w-4 h-4 animate-spin" fill="none" viewBox="0 0 24 24">
                  <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                  <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z" />
                </svg>
                <span>Evaluating...</span>
              </>
            ) : (
              <>
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 10V3L4 14h7v7l9-11h-7z" />
                </svg>
                <span>Run Prioritization</span>
              </>
            )}
          </button>
        </div>
      </div>
    </header>
  );
};
