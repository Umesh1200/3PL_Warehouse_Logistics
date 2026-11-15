// ============================================================================
// Phase 6.3.6.4: InventoryHealthHeader Component
// ============================================================================

import React from 'react';
import { InventoryHealthDashboardDto } from '../types/inventoryHealth.types';

interface InventoryHealthHeaderProps {
  dashboard: InventoryHealthDashboardDto | null;
  facilityName: string;
  facilityCode: string;
  isStale: boolean;
  isLoading: boolean;
  onRefresh: () => void;
  clientId?: string | null;
  clientName?: string | null;
}

export const InventoryHealthHeader: React.FC<InventoryHealthHeaderProps> = ({
  dashboard,
  facilityName,
  facilityCode,
  isStale,
  isLoading,
  onRefresh,
  clientId,
  clientName
}) => {
  const score = dashboard?.compositeHealthScore ?? 0;
  const grade = dashboard?.healthGrade ?? '-';
  const freshnessMinutes = dashboard?.projectionFreshnessMinutes ?? 0;

  const getScoreBadgeColor = (g: string) => {
    switch (g.toUpperCase()) {
      case 'A': return 'bg-emerald-100 text-emerald-800 border-emerald-300';
      case 'B': return 'bg-blue-100 text-blue-800 border-blue-300';
      case 'C': return 'bg-amber-100 text-amber-800 border-amber-300';
      case 'D': return 'bg-orange-100 text-orange-800 border-orange-300';
      case 'F': return 'bg-red-100 text-red-800 border-red-300';
      default: return 'bg-slate-100 text-slate-800 border-slate-300';
    }
  };

  return (
    <header className="bg-white border-b border-slate-200 px-6 py-4 shadow-sm">
      <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-4">
        {/* Left: Facility Identity & Breadcrumb */}
        <div>
          <div className="flex items-center gap-3">
            <h1 className="text-2xl font-bold text-slate-900 tracking-tight">
              Inventory Health & Spatial Heatmap
            </h1>
            <span className="px-2.5 py-0.5 text-xs font-semibold rounded-full bg-slate-100 text-slate-700 border border-slate-200">
              {facilityCode || 'FACILITY'}
            </span>
            {clientId && (
              <span className="px-2.5 py-0.5 text-xs font-medium rounded-full bg-indigo-50 text-indigo-700 border border-indigo-200" title="Tenant Isolation Active (VIS-08)">
                Client: {clientName || clientId.slice(0, 8)}
              </span>
            )}
          </div>
          <p className="text-sm text-slate-500 mt-1">
            {facilityName} &bull; Authoritative Physical Layout & Multi-Tier Spatial Rollup
          </p>
        </div>

        {/* Right: Freshness, Health Grade & Refresh */}
        <div className="flex items-center gap-4">
          {/* Data Freshness Indicator (VIS-07) */}
          <div className="flex items-center gap-2 text-xs">
            <span
              className={`inline-block w-2.5 h-2.5 rounded-full ${
                isStale ? 'bg-amber-500 animate-pulse' : 'bg-emerald-500'
              }`}
            />
            <span className={isStale ? 'text-amber-700 font-semibold' : 'text-slate-600'}>
              {isStale
                ? `Stale Data (${freshnessMinutes}m old)`
                : `Fresh (${freshnessMinutes}m ago)`}
            </span>
          </div>

          {/* Health Score Pill */}
          <div className={`flex items-center gap-2 px-3.5 py-1.5 rounded-lg border font-medium ${getScoreBadgeColor(grade)}`}>
            <span className="text-xs uppercase tracking-wider text-slate-500">Score</span>
            <span className="text-lg font-bold">{Math.round(score)}</span>
            <span className="text-xs font-bold px-1.5 py-0.5 rounded bg-white/70 shadow-2xs">
              {grade}
            </span>
          </div>

          {/* Refresh Action */}
          <button
            onClick={onRefresh}
            disabled={isLoading}
            className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm font-medium bg-slate-900 text-white hover:bg-slate-800 disabled:opacity-50 transition-colors shadow-2xs cursor-pointer"
            title="Refresh spatial projections"
          >
            <svg
              className={`w-4 h-4 ${isLoading ? 'animate-spin' : ''}`}
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
            >
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
            </svg>
            <span>{isLoading ? 'Syncing...' : 'Sync'}</span>
          </button>
        </div>
      </div>
    </header>
  );
};
