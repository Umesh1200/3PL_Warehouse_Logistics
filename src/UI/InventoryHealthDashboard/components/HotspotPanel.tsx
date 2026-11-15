// ============================================================================
// Phase 6.3.6.4: HotspotPanel Component (VIS-06 Hotspot Drill-Down & VIS-10 Read-Only)
// ============================================================================

import React from 'react';
import { SpatialHotspotDto, HeatmapVisualTier } from '../types/inventoryHealth.types';

interface HotspotPanelProps {
  hotspots: SpatialHotspotDto[];
  selectedHotspot: SpatialHotspotDto | null;
  onSelectHotspot: (hotspot: SpatialHotspotDto | null) => void;
  onFocusCoordinates: (x: number, y: number) => void;
  isOpen: boolean;
  onToggle: () => void;
}

export const HotspotPanel: React.FC<HotspotPanelProps> = ({
  hotspots,
  selectedHotspot,
  onSelectHotspot,
  onFocusCoordinates,
  isOpen,
  onToggle
}) => {
  const getSeverityBadge = (tier: HeatmapVisualTier) => {
    switch (tier) {
      case 'Emergency':
        return 'bg-red-600 text-white animate-pulse';
      case 'Critical':
        return 'bg-red-100 text-red-800 border-red-300';
      case 'Warning':
        return 'bg-amber-100 text-amber-800 border-amber-300';
      default:
        return 'bg-slate-100 text-slate-700 border-slate-300';
    }
  };

  return (
    <div className="bg-white rounded-xl border border-slate-200 shadow-sm overflow-hidden flex flex-col">
      {/* Panel Header */}
      <div
        onClick={onToggle}
        className="px-4 py-3 bg-slate-50 border-b border-slate-200 flex items-center justify-between cursor-pointer hover:bg-slate-100 transition-colors select-none"
      >
        <div className="flex items-center gap-2">
          <span className="text-base font-bold text-slate-900">
            Operational Hotspots
          </span>
          <span className="px-2 py-0.5 rounded-full text-xs font-bold bg-red-100 text-red-700 border border-red-200">
            {hotspots.length} Detected
          </span>
        </div>
        <button className="text-slate-500 hover:text-slate-700 text-sm font-semibold">
          {isOpen ? 'Collapse ▲' : 'Expand ▼'}
        </button>
      </div>

      {isOpen && (
        <div className="p-4 space-y-3 max-h-[420px] overflow-y-auto">
          {/* VIS-10 Read-Only Safeguard Banner */}
          <div className="p-2.5 rounded-lg bg-indigo-50 border border-indigo-200 text-indigo-900 text-xs flex items-start gap-2">
            <span className="text-indigo-600 text-base mt-0.5">ℹ️</span>
            <div>
              <strong className="font-semibold">Advisory Analytics Only (VIS-10):</strong>
              <p className="text-[11px] text-indigo-700 mt-0.5">
                Hotspot recommendations are advisory intelligence to guide floor supervisors. No operational stock movements or ledger mutations are executed automatically.
              </p>
            </div>
          </div>

          {hotspots.length === 0 ? (
            <div className="text-center py-6 text-slate-500 text-xs">
              ✅ No active operational bottlenecks or overcapacity hotspots detected in current layout.
            </div>
          ) : (
            hotspots.map(spot => {
              const isSelected = selectedHotspot?.hotspotId === spot.hotspotId;
              return (
                <div
                  key={spot.hotspotId}
                  onClick={() => {
                    onSelectHotspot(isSelected ? null : spot);
                    onFocusCoordinates(spot.centerXMeters, spot.centerYMeters);
                  }}
                  className={`p-3 rounded-lg border text-xs cursor-pointer transition-all ${
                    isSelected
                      ? 'border-indigo-600 bg-indigo-50/60 ring-2 ring-indigo-500/20 shadow-sm'
                      : 'border-slate-200 hover:border-slate-300 hover:bg-slate-50'
                  }`}
                >
                  {/* Header: Type, Aisle, Severity */}
                  <div className="flex items-center justify-between pb-1.5 border-b border-slate-200/60">
                    <div className="font-bold text-slate-900 flex items-center gap-1.5">
                      <span>🔥 {spot.hotspotType}</span>
                      <span className="text-slate-500 font-normal">
                        ({spot.zoneCode} &bull; Aisle {spot.aisleCode})
                      </span>
                    </div>
                    <span className={`px-2 py-0.5 rounded-full text-[10px] font-bold uppercase tracking-wider border ${getSeverityBadge(spot.severityTier)}`}>
                      {spot.severityTier}
                    </span>
                  </div>

                  {/* Hotspot Metrics */}
                  <div className="grid grid-cols-2 gap-2 mt-2 text-[11px] text-slate-600">
                    <div>
                      <span className="text-slate-400">Contiguous Locs:</span>{' '}
                      <strong>{spot.contiguousLocationsCount}</strong>
                    </div>
                    <div>
                      <span className="text-slate-400">Weighted Fill:</span>{' '}
                      <strong className="text-red-600">{spot.weightedCubeUtilizationPercent.toFixed(1)}%</strong>
                    </div>
                    <div>
                      <span className="text-slate-400">Affected CBM:</span>{' '}
                      <strong>{spot.totalOccupiedCbm.toFixed(1)} / {spot.totalUsableCbm.toFixed(1)}</strong>
                    </div>
                    {spot.totalExcessCbm > 0 && (
                      <div>
                        <span className="text-slate-400">Excess Vol:</span>{' '}
                        <strong className="text-red-600">+{spot.totalExcessCbm.toFixed(1)} CBM</strong>
                      </div>
                    )}
                    {spot.totalDeadStockUnits > 0 && (
                      <div className="col-span-2 text-amber-700">
                        <span>Dead Stock:</span> <strong>{spot.totalDeadStockUnits} units (${spot.totalDeadStockValue.toLocaleString()})</strong>
                      </div>
                    )}
                  </div>

                  {/* Action Recommendation */}
                  <div className="mt-2.5 pt-2 border-t border-slate-200/60 flex items-start justify-between gap-2">
                    <p className="text-[11px] text-slate-700 italic">
                      💡 {spot.recommendedAction}
                    </p>
                    <button
                      type="button"
                      onClick={(e) => {
                        e.stopPropagation();
                        onSelectHotspot(spot);
                        onFocusCoordinates(spot.centerXMeters, spot.centerYMeters);
                      }}
                      className="px-2 py-1 rounded bg-indigo-600 text-white font-medium text-[10px] hover:bg-indigo-700 shrink-0 cursor-pointer shadow-2xs"
                    >
                      Focus Map 🎯
                    </button>
                  </div>
                </div>
              );
            })
          )}
        </div>
      )}
    </div>
  );
};
