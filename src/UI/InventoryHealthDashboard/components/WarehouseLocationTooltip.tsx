// ============================================================================
// Phase 6.3.6.4: WarehouseLocationTooltip Component (VIS-04 Secondary Alerts)
// ============================================================================

import React from 'react';
import { HeatmapLocationCellDto } from '../types/inventoryHealth.types';

interface WarehouseLocationTooltipProps {
  location: HeatmapLocationCellDto | null;
  x: number;
  y: number;
  containerWidth: number;
  containerHeight: number;
}

export const WarehouseLocationTooltip: React.FC<WarehouseLocationTooltipProps> = ({
  location,
  x,
  y,
  containerWidth,
  containerHeight
}) => {
  if (!location) return null;

  // Tooltip position clamping to avoid overflow off the screen
  const tooltipWidth = 280;
  const tooltipHeight = 260;
  const left = x + tooltipWidth + 20 > containerWidth ? x - tooltipWidth - 10 : x + 15;
  const top = y + tooltipHeight + 20 > containerHeight ? y - tooltipHeight - 10 : y + 15;

  const coord = location.coordinate;

  return (
    <div
      className="absolute z-50 pointer-events-none bg-slate-900/95 text-white p-3.5 rounded-xl shadow-xl text-xs w-[280px] border border-slate-700/80 backdrop-blur-md animate-in fade-in zoom-in-95 duration-100"
      style={{
        left: `${left}px`,
        top: `${top}px`,
      }}
    >
      {/* Header: Location Code & Visual Tier Badge */}
      <div className="flex items-center justify-between pb-2 border-b border-slate-800">
        <div>
          <span className="font-bold text-sm text-indigo-300 font-mono">
            {location.locationCode}
          </span>
          <div className="text-[11px] text-slate-400">
            Zone: {location.zoneCode} &bull; Aisle: {location.aisleCode || 'N/A'}
          </div>
        </div>
        <span
          className="px-2 py-0.5 rounded-full text-[10px] font-bold uppercase tracking-wider text-white"
          style={{ backgroundColor: location.colorHex || '#6366F1' }}
        >
          {location.visualTier}
        </span>
      </div>

      {/* Grid Coordinates (VIS-02) */}
      <div className="py-2 border-b border-slate-800 text-[11px] text-slate-300 flex justify-between">
        <span>Rack: {location.rackCode || '-'} (Lvl {location.shelfLevel ?? '-'})</span>
        <span className="font-mono text-slate-400">
          ({coord.x?.toFixed(1) ?? '?'}, {coord.y?.toFixed(1) ?? '?'}, {coord.z?.toFixed(1) ?? '?'}m)
        </span>
      </div>

      {/* Volumetric Space Metrics */}
      <div className="py-2 space-y-1.5 border-b border-slate-800">
        <div className="flex justify-between items-center">
          <span className="text-slate-400">Volumetric Fill:</span>
          <span className={`font-bold ${
            location.cubeUtilizationPercent > 100 ? 'text-red-400' :
            location.cubeUtilizationPercent > 85 ? 'text-amber-400' :
            'text-emerald-400'
          }`}>
            {location.cubeUtilizationPercent.toFixed(1)}%
          </span>
        </div>
        <div className="flex justify-between text-slate-300 text-[11px]">
          <span>Occupied / Usable:</span>
          <span>{location.occupiedCbm.toFixed(2)} / {location.usableCbm.toFixed(2)} CBM</span>
        </div>
        <div className="flex justify-between text-slate-300 text-[11px]">
          <span>Pallets (Occ/Usable):</span>
          <span>{location.occupiedPallets} / {location.usablePallets} ({location.palletOccupancyPercent.toFixed(0)}%)</span>
        </div>
        {location.isOverCapacity && (
          <div className="text-red-400 font-semibold text-[11px] flex items-center gap-1">
            <span>⚠️</span> Overcapacity by +{location.excessCbm.toFixed(2)} CBM!
          </div>
        )}
      </div>

      {/* Inventory & Stock Health */}
      <div className="py-2 space-y-1 border-b border-slate-800 text-[11px]">
        <div className="flex justify-between text-slate-300">
          <span className="text-slate-400">Inventory Units:</span>
          <span className="font-semibold">{location.totalUnits.toLocaleString()} units ({location.distinctSkuCount} SKUs)</span>
        </div>
        {location.hasDeadStock && (
          <div className="flex justify-between text-amber-400">
            <span>Dead Stock Exposure:</span>
            <span>{location.deadStockUnits} units (${location.deadStockValue.toLocaleString()})</span>
          </div>
        )}
        {location.hasExpiredStock && (
          <div className="flex justify-between text-red-400">
            <span>Expired Units:</span>
            <span>{location.expiredUnits} units</span>
          </div>
        )}
        {location.isBlocked && (
          <div className="text-slate-400 italic">
            🔒 Blocked: {location.blockReason || 'Operational Lock'}
          </div>
        )}
      </div>

      {/* VIS-04: Simultaneous Conditions Display */}
      {location.activeConditions && location.activeConditions.length > 0 && (
        <div className="pt-2">
          <span className="text-[10px] uppercase font-semibold text-slate-400 tracking-wider">
            All Active Conditions:
          </span>
          <div className="flex flex-wrap gap-1 mt-1">
            {location.activeConditions.map((cond, idx) => (
              <span
                key={idx}
                className="px-1.5 py-0.5 rounded bg-slate-800 text-indigo-300 text-[10px] font-mono border border-slate-700"
              >
                {cond}
              </span>
            ))}
          </div>
        </div>
      )}
    </div>
  );
};
