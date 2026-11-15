// ============================================================================
// Phase 6.3.6.4: HeatmapLayerControls Component
// Implements VIS-01 (Floor/Level Selection), VIS-03 (Layer Independence), VIS-04 (Secondary Alerts)
// ============================================================================

import React from 'react';
import { HeatmapLayerType, HeatmapFilterState } from '../types/inventoryHealth.types';

interface HeatmapLayerControlsProps {
  filter: HeatmapFilterState;
  availableLevels: number[];
  onFilterChange: (partial: Partial<HeatmapFilterState>) => void;
  onResetFilter: () => void;
  showGrid: boolean;
  onToggleGrid: (show: boolean) => void;
  totalLocationsCount: number;
  filteredLocationsCount: number;
}

export const HeatmapLayerControls: React.FC<HeatmapLayerControlsProps> = ({
  filter,
  availableLevels,
  onFilterChange,
  onResetFilter,
  showGrid,
  onToggleGrid,
  totalLocationsCount,
  filteredLocationsCount
}) => {
  // Layer definitions for independent toggling (VIS-03)
  const layers = [
    { type: HeatmapLayerType.CubeUtilization, label: 'Cube Fill', icon: '📦' },
    { type: HeatmapLayerType.PalletOccupancy, label: 'Pallet Occupancy', icon: '🪜' },
    { type: HeatmapLayerType.WeightCapacity, label: 'Weight Limit', icon: '⚖️' },
    { type: HeatmapLayerType.InventoryAging, label: 'Aging & Dead Stock', icon: '⏳' },
    { type: HeatmapLayerType.SkuVelocity, label: 'SKU Velocity', icon: '⚡' },
    { type: HeatmapLayerType.AbcClassification, label: 'ABC Pareto', icon: '📊' },
    { type: HeatmapLayerType.BlockedQuarantine, label: 'Blocked / Hold', icon: '🚫' },
  ];

  const toggleLayer = (layerType: HeatmapLayerType) => {
    const isEnabled = (filter.activeLayers & layerType) === layerType;
    const nextLayers = isEnabled
      ? filter.activeLayers & ~layerType
      : filter.activeLayers | layerType;
    onFilterChange({ activeLayers: nextLayers });
  };

  const isLayerActive = (layerType: HeatmapLayerType) => {
    return (filter.activeLayers & layerType) === layerType;
  };

  return (
    <div className="bg-white border-b border-slate-200 px-6 py-3 flex flex-wrap items-center justify-between gap-4 text-sm">
      {/* 1. VIS-01: Shelf Level Selection (Crucial for 2D vs 3D elevation partitioning) */}
      <div className="flex items-center gap-2">
        <label className="font-semibold text-slate-700 text-xs uppercase tracking-wider flex items-center gap-1">
          <svg className="w-4 h-4 text-indigo-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10" />
          </svg>
          Level (Z-Slice):
        </label>
        <div className="inline-flex rounded-lg border border-slate-300 p-0.5 bg-slate-100">
          <button
            onClick={() => onFilterChange({ shelfLevel: null })}
            className={`px-2.5 py-1 text-xs font-medium rounded-md transition-colors cursor-pointer ${
              filter.shelfLevel === null
                ? 'bg-white text-indigo-700 shadow-2xs font-semibold'
                : 'text-slate-600 hover:text-slate-900'
            }`}
          >
            All (2D Comp)
          </button>
          {availableLevels.map(lvl => (
            <button
              key={lvl}
              onClick={() => onFilterChange({ shelfLevel: lvl })}
              className={`px-2.5 py-1 text-xs font-medium rounded-md transition-colors cursor-pointer ${
                filter.shelfLevel === lvl
                  ? 'bg-indigo-600 text-white shadow-2xs font-semibold'
                  : 'text-slate-600 hover:text-slate-900'
              }`}
            >
              L{lvl}
            </button>
          ))}
        </div>
      </div>

      {/* 2. Primary Color Metric Selector */}
      <div className="flex items-center gap-2">
        <label className="font-semibold text-slate-700 text-xs uppercase tracking-wider">
          Color By:
        </label>
        <select
          value={filter.primaryColorLayer}
          onChange={(e) => onFilterChange({ primaryColorLayer: Number(e.target.value) as HeatmapLayerType })}
          className="text-xs bg-slate-50 border border-slate-300 rounded-lg px-2.5 py-1.5 font-medium text-slate-800 focus:outline-hidden focus:ring-2 focus:ring-indigo-500"
        >
          <option value={HeatmapLayerType.CubeUtilization}>Cube Fill %</option>
          <option value={HeatmapLayerType.PalletOccupancy}>Pallet Occupancy %</option>
          <option value={HeatmapLayerType.InventoryAging}>Aging & Dead Stock</option>
          <option value={HeatmapLayerType.SkuVelocity}>SKU Velocity (Fast/Slow)</option>
          <option value={HeatmapLayerType.AbcClassification}>ABC Classification (A/B/C)</option>
          <option value={HeatmapLayerType.BlockedQuarantine}>Blocked / Structural</option>
        </select>
      </div>

      {/* 3. VIS-03: Independent Active Layer Toggles */}
      <div className="flex items-center gap-1.5 flex-wrap">
        <span className="text-xs font-semibold uppercase tracking-wider text-slate-500 mr-1">
          Layers:
        </span>
        {layers.map(layer => {
          const active = isLayerActive(layer.type);
          return (
            <button
              key={layer.type}
              onClick={() => toggleLayer(layer.type)}
              className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-full text-xs font-medium transition-all cursor-pointer ${
                active
                  ? 'bg-indigo-100 text-indigo-800 border border-indigo-300 shadow-2xs'
                  : 'bg-slate-100 text-slate-400 border border-slate-200 hover:bg-slate-200'
              }`}
              title={`Toggle ${layer.label}`}
            >
              <span>{layer.icon}</span>
              <span>{layer.label}</span>
            </button>
          );
        })}
      </div>

      {/* 4. VIS-04 & Grid & Hotspot Toggles */}
      <div className="flex items-center gap-3">
        {/* Secondary Alerts (VIS-04) */}
        <label className="inline-flex items-center gap-1.5 text-xs text-slate-700 cursor-pointer select-none">
          <input
            type="checkbox"
            checked={filter.showSecondaryAlerts}
            onChange={(e) => onFilterChange({ showSecondaryAlerts: e.target.checked })}
            className="w-3.5 h-3.5 text-indigo-600 rounded border-slate-300 focus:ring-indigo-500"
          />
          <span>Secondary Alerts</span>
        </label>

        {/* 2m Spatial Grid (HM-03) */}
        <label className="inline-flex items-center gap-1.5 text-xs text-slate-700 cursor-pointer select-none">
          <input
            type="checkbox"
            checked={showGrid}
            onChange={(e) => onToggleGrid(e.target.checked)}
            className="w-3.5 h-3.5 text-indigo-600 rounded border-slate-300 focus:ring-indigo-500"
          />
          <span>2m Grid</span>
        </label>

        {/* Hotspots Only (VIS-06) */}
        <label className="inline-flex items-center gap-1.5 text-xs text-slate-700 cursor-pointer select-none">
          <input
            type="checkbox"
            checked={filter.onlyHotspots}
            onChange={(e) => onFilterChange({ onlyHotspots: e.target.checked })}
            className="w-3.5 h-3.5 text-red-600 rounded border-slate-300 focus:ring-red-500"
          />
          <span className="font-medium text-red-700">Hotspots Only</span>
        </label>

        {/* Location Match Counter */}
        <span className="text-xs text-slate-500 border-l border-slate-200 pl-3">
          Showing <strong className="text-slate-800">{filteredLocationsCount}</strong> / {totalLocationsCount}
        </span>
      </div>
    </div>
  );
};
