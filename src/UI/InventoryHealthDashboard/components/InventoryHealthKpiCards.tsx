// ============================================================================
// Phase 6.3.6.4: InventoryHealthKpiCards Component (HM-04 Weighted KPI Cards)
// ============================================================================

import React from 'react';
import { InventoryHealthDashboardDto, SpatialHeatmapProjectionDto } from '../types/inventoryHealth.types';

interface InventoryHealthKpiCardsProps {
  dashboard: InventoryHealthDashboardDto | null;
  projection: SpatialHeatmapProjectionDto | null;
}

export const InventoryHealthKpiCards: React.FC<InventoryHealthKpiCardsProps> = ({
  dashboard,
  projection
}) => {
  // Use weighted projection metrics if available, falling back to unified dashboard
  const cubeUtilization = projection
    ? projection.facilityWeightedCubeUtilizationPercent
    : (dashboard?.cubeFillPercent ?? 0);

  const palletOccupancy = projection
    ? projection.facilityWeightedPalletOccupancyPercent
    : (dashboard?.palletOccupancyPercent ?? 0);

  const totalUsableCbm = projection?.facilityTotalUsableCbm ?? 0;
  const totalOccupiedCbm = projection?.facilityTotalOccupiedCbm ?? 0;
  const totalValue = projection?.facilityTotalInventoryValue ?? (dashboard?.totalInventoryValue ?? 0);
  const deadStockRatio = dashboard?.deadStockRatioPercent ?? 0;
  const fastMoverRatio = dashboard?.fastMoverRatioPercent ?? 0;
  const classAPercent = dashboard?.classAConsumptionValuePercent ?? 0;

  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6 gap-4 p-6 bg-slate-50 border-b border-slate-200">
      {/* 1. Volumetric Space Utilization (HM-04 Weighted Additive Rollup) */}
      <div className="bg-white rounded-xl p-4 border border-slate-200 shadow-2xs">
        <div className="flex items-center justify-between">
          <span className="text-xs font-semibold uppercase tracking-wider text-slate-500">
            Cube Fill (Weighted)
          </span>
          <span className={`text-xs px-2 py-0.5 rounded-full font-bold ${
            cubeUtilization > 95 ? 'bg-red-100 text-red-800' :
            cubeUtilization > 85 ? 'bg-amber-100 text-amber-800' :
            'bg-emerald-100 text-emerald-800'
          }`}>
            {cubeUtilization.toFixed(1)}%
          </span>
        </div>
        <div className="mt-2 text-2xl font-bold text-slate-900">
          {totalOccupiedCbm.toLocaleString(undefined, { maximumFractionDigits: 1 })}
          <span className="text-xs text-slate-500 font-normal ml-1">/ {totalUsableCbm.toLocaleString(undefined, { maximumFractionDigits: 1 })} CBM</span>
        </div>
        <div className="mt-2 w-full bg-slate-100 rounded-full h-1.5 overflow-hidden">
          <div
            className={`h-full rounded-full ${
              cubeUtilization > 95 ? 'bg-red-500' :
              cubeUtilization > 85 ? 'bg-amber-500' :
              'bg-emerald-500'
            }`}
            style={{ width: `${Math.min(cubeUtilization, 100)}%` }}
          />
        </div>
        <div className="mt-2 text-[11px] text-slate-500 flex justify-between">
          <span>Available: {(Math.max(0, totalUsableCbm - totalOccupiedCbm)).toFixed(1)} CBM</span>
          <span>{totalUsableCbm > 0 ? 'Additive HM-04' : ''}</span>
        </div>
      </div>

      {/* 2. Pallet Occupancy (Additive Pallet Positions) */}
      <div className="bg-white rounded-xl p-4 border border-slate-200 shadow-2xs">
        <div className="flex items-center justify-between">
          <span className="text-xs font-semibold uppercase tracking-wider text-slate-500">
            Pallet Positions
          </span>
          <span className={`text-xs px-2 py-0.5 rounded-full font-bold ${
            palletOccupancy > 95 ? 'bg-red-100 text-red-800' :
            palletOccupancy > 85 ? 'bg-amber-100 text-amber-800' :
            'bg-emerald-100 text-emerald-800'
          }`}>
            {palletOccupancy.toFixed(1)}%
          </span>
        </div>
        <div className="mt-2 text-2xl font-bold text-slate-900">
          {(projection?.facilityTotalOccupiedPallets ?? 0).toLocaleString()}
          <span className="text-xs text-slate-500 font-normal ml-1">
            / {(projection?.facilityTotalUsablePallets ?? 0).toLocaleString()} pos
          </span>
        </div>
        <div className="mt-2 w-full bg-slate-100 rounded-full h-1.5 overflow-hidden">
          <div
            className="h-full rounded-full bg-blue-500"
            style={{ width: `${Math.min(palletOccupancy, 100)}%` }}
          />
        </div>
        <div className="mt-2 text-[11px] text-slate-500 flex justify-between">
          <span>Occupancy Ratio</span>
          <span>Discrete</span>
        </div>
      </div>

      {/* 3. Total Stored Inventory Valuation */}
      <div className="bg-white rounded-xl p-4 border border-slate-200 shadow-2xs">
        <div className="flex items-center justify-between">
          <span className="text-xs font-semibold uppercase tracking-wider text-slate-500">
            Stock Valuation
          </span>
          <span className="text-xs px-2 py-0.5 rounded-full font-medium bg-slate-100 text-slate-600">
            USD
          </span>
        </div>
        <div className="mt-2 text-2xl font-bold text-slate-900">
          ${totalValue >= 1_000_000
            ? `${(totalValue / 1_000_000).toFixed(2)}M`
            : totalValue >= 1_000
            ? `${(totalValue / 1_000).toFixed(1)}k`
            : totalValue.toFixed(0)}
        </div>
        <div className="mt-2 text-xs text-slate-600">
          {(dashboard?.totalActiveSkus ?? 0).toLocaleString()} Active SKUs
        </div>
        <div className="mt-2 text-[11px] text-slate-500">
          {(dashboard?.totalInventoryUnits ?? 0).toLocaleString()} Total Units On-Hand
        </div>
      </div>

      {/* 4. Dead Stock & Aging Exposure (Phase 6.3.2) */}
      <div className="bg-white rounded-xl p-4 border border-slate-200 shadow-2xs">
        <div className="flex items-center justify-between">
          <span className="text-xs font-semibold uppercase tracking-wider text-slate-500">
            Dead Stock Ratio
          </span>
          <span className={`text-xs px-2 py-0.5 rounded-full font-bold ${
            deadStockRatio > 15 ? 'bg-red-100 text-red-800' :
            deadStockRatio > 8 ? 'bg-amber-100 text-amber-800' :
            'bg-slate-100 text-slate-700'
          }`}>
            {deadStockRatio.toFixed(1)}%
          </span>
        </div>
        <div className="mt-2 text-2xl font-bold text-slate-900">
          {deadStockRatio.toFixed(1)}%
          <span className="text-xs text-slate-500 font-normal ml-1">inactivity</span>
        </div>
        <div className="mt-2 w-full bg-slate-100 rounded-full h-1.5 overflow-hidden">
          <div
            className={`h-full rounded-full ${deadStockRatio > 15 ? 'bg-red-500' : 'bg-amber-500'}`}
            style={{ width: `${Math.min(deadStockRatio * 3, 100)}%` }}
          />
        </div>
        <div className="mt-2 text-[11px] text-slate-500">
          Holding Cost &gt; 180 Days
        </div>
      </div>

      {/* 5. Fast-Mover SKU Velocity Ratio (Phase 6.3.4) */}
      <div className="bg-white rounded-xl p-4 border border-slate-200 shadow-2xs">
        <div className="flex items-center justify-between">
          <span className="text-xs font-semibold uppercase tracking-wider text-slate-500">
            Velocity Index
          </span>
          <span className="text-xs px-2 py-0.5 rounded-full font-bold bg-indigo-100 text-indigo-800">
            Fast Movers
          </span>
        </div>
        <div className="mt-2 text-2xl font-bold text-slate-900">
          {fastMoverRatio.toFixed(1)}%
        </div>
        <div className="mt-2 w-full bg-slate-100 rounded-full h-1.5 overflow-hidden">
          <div
            className="h-full rounded-full bg-indigo-500"
            style={{ width: `${Math.min(fastMoverRatio * 2, 100)}%` }}
          />
        </div>
        <div className="mt-2 text-[11px] text-slate-500">
          High-frequency pick profiles
        </div>
      </div>

      {/* 6. ABC Pareto Class A Focus (Phase 6.3.5) */}
      <div className="bg-white rounded-xl p-4 border border-slate-200 shadow-2xs">
        <div className="flex items-center justify-between">
          <span className="text-xs font-semibold uppercase tracking-wider text-slate-500">
            ABC Class A
          </span>
          <span className="text-xs px-2 py-0.5 rounded-full font-bold bg-purple-100 text-purple-800">
            Top 80% ACV
          </span>
        </div>
        <div className="mt-2 text-2xl font-bold text-slate-900">
          {classAPercent.toFixed(1)}%
        </div>
        <div className="mt-2 w-full bg-slate-100 rounded-full h-1.5 overflow-hidden">
          <div
            className="h-full rounded-full bg-purple-500"
            style={{ width: `${Math.min(classAPercent, 100)}%` }}
          />
        </div>
        <div className="mt-2 text-[11px] text-slate-500">
          Annual Consumption Value
        </div>
      </div>
    </div>
  );
};
