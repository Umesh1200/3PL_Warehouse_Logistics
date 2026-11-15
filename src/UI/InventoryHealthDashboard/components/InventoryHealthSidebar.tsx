// ============================================================================
// Phase 6.3.6.4: InventoryHealthSidebar Component
// Multi-Tier Rollup Tree, Health Alerts, and Selected Location Inspector
// ============================================================================

import React, { useState } from 'react';
import {
  ZoneSpatialRollupDto,
  HeatmapLocationCellDto,
  HealthAlertDto,
  HeatmapFilterState
} from '../types/inventoryHealth.types';

interface InventoryHealthSidebarProps {
  zones: ZoneSpatialRollupDto[];
  selectedZone: ZoneSpatialRollupDto | null;
  onSelectZone: (zone: ZoneSpatialRollupDto | null) => void;
  selectedAisle: string | null;
  onSelectAisle: (aisle: string | null) => void;
  selectedLocation: HeatmapLocationCellDto | null;
  onSelectLocation: (loc: HeatmapLocationCellDto | null) => void;
  alerts: HealthAlertDto[];
  filter: HeatmapFilterState;
  onFilterChange: (partial: Partial<HeatmapFilterState>) => void;
}

export const InventoryHealthSidebar: React.FC<InventoryHealthSidebarProps> = ({
  zones,
  selectedZone,
  onSelectZone,
  selectedAisle,
  onSelectAisle,
  selectedLocation,
  onSelectLocation,
  alerts,
  filter,
  onFilterChange
}) => {
  const [activeTab, setActiveTab] = useState<'rollups' | 'alerts' | 'inspector'>('rollups');

  // Auto-switch to inspector tab when a location is clicked
  React.useEffect(() => {
    if (selectedLocation) {
      setActiveTab('inspector');
    }
  }, [selectedLocation]);

  return (
    <div className="w-full lg:w-96 bg-white border-l border-slate-200 flex flex-col h-[720px] shadow-sm">
      {/* 1. Search Bar */}
      <div className="p-3 border-b border-slate-200 bg-slate-50">
        <div className="relative">
          <input
            type="text"
            placeholder="Search Location, Aisle, Rack..."
            value={filter.searchKeyword}
            onChange={(e) => onFilterChange({ searchKeyword: e.target.value })}
            className="w-full pl-9 pr-3 py-1.5 text-xs bg-white border border-slate-300 rounded-lg text-slate-800 placeholder-slate-400 focus:outline-hidden focus:ring-2 focus:ring-indigo-500"
          />
          <svg className="w-4 h-4 text-slate-400 absolute left-2.5 top-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
          </svg>
          {filter.searchKeyword && (
            <button
              onClick={() => onFilterChange({ searchKeyword: '' })}
              className="absolute right-2.5 top-1.5 text-xs text-slate-400 hover:text-slate-600 cursor-pointer"
            >
              &times;
            </button>
          )}
        </div>
      </div>

      {/* 2. Navigation Tabs */}
      <div className="flex border-b border-slate-200 bg-white text-xs">
        <button
          onClick={() => setActiveTab('rollups')}
          className={`flex-1 py-2.5 font-semibold text-center border-b-2 transition-colors cursor-pointer ${
            activeTab === 'rollups'
              ? 'border-indigo-600 text-indigo-700 bg-indigo-50/40'
              : 'border-transparent text-slate-500 hover:text-slate-700'
          }`}
        >
          Hierarchy
        </button>
        <button
          onClick={() => setActiveTab('alerts')}
          className={`flex-1 py-2.5 font-semibold text-center border-b-2 transition-colors cursor-pointer relative ${
            activeTab === 'alerts'
              ? 'border-indigo-600 text-indigo-700 bg-indigo-50/40'
              : 'border-transparent text-slate-500 hover:text-slate-700'
          }`}
        >
          Alerts
          {alerts.length > 0 && (
            <span className="ml-1.5 px-1.5 py-0.2 rounded-full text-[10px] bg-red-100 text-red-700 font-bold">
              {alerts.length}
            </span>
          )}
        </button>
        <button
          onClick={() => setActiveTab('inspector')}
          className={`flex-1 py-2.5 font-semibold text-center border-b-2 transition-colors cursor-pointer ${
            activeTab === 'inspector'
              ? 'border-indigo-600 text-indigo-700 bg-indigo-50/40'
              : 'border-transparent text-slate-500 hover:text-slate-700'
          }`}
        >
          Inspector
          {selectedLocation && (
            <span className="ml-1 inline-block w-2 h-2 rounded-full bg-emerald-500" />
          )}
        </button>
      </div>

      {/* 3. Tab Contents */}
      <div className="flex-1 overflow-y-auto p-4">
        {/* TAB 1: Multi-Tier Hierarchical Rollups (HM-04, HM-05) */}
        {activeTab === 'rollups' && (
          <div className="space-y-4">
            <div className="text-[11px] text-slate-500 flex justify-between items-center pb-2 border-b border-slate-100">
              <span className="font-semibold uppercase tracking-wider">Zone & Aisle Rollups</span>
              <span>Weighted CBM (HM-04)</span>
            </div>

            {zones.map(z => {
              const isZoneSelected = selectedZone?.zoneId === z.zoneId;
              return (
                <div key={z.zoneId} className="border border-slate-200 rounded-lg overflow-hidden">
                  {/* Zone Header */}
                  <div
                    onClick={() => {
                      onSelectZone(isZoneSelected ? null : z);
                      onSelectAisle(null);
                    }}
                    className={`p-3 cursor-pointer flex items-center justify-between transition-colors ${
                      isZoneSelected ? 'bg-indigo-50/80 font-bold text-indigo-900' : 'bg-slate-50 hover:bg-slate-100 text-slate-900'
                    }`}
                  >
                    <div>
                      <div className="text-xs font-bold">{z.zoneCode} &bull; {z.zoneName}</div>
                      <div className="text-[11px] text-slate-500 font-normal">
                        {z.locationCount} locations &bull; ${Math.round(z.totalInventoryValue).toLocaleString()}
                      </div>
                    </div>
                    <div className="text-right">
                      <div className={`text-xs font-bold ${
                        z.weightedCubeUtilizationPercent > 95 ? 'text-red-600' :
                        z.weightedCubeUtilizationPercent > 80 ? 'text-amber-600' :
                        'text-emerald-600'
                      }`}>
                        {z.weightedCubeUtilizationPercent.toFixed(1)}%
                      </div>
                      <div className="text-[10px] text-slate-400">
                        {z.totalOccupiedCbm.toFixed(1)} / {z.totalUsableCbm.toFixed(1)} m³
                      </div>
                    </div>
                  </div>

                  {/* Aisles list within Zone */}
                  {isZoneSelected && z.aisles && z.aisles.length > 0 && (
                    <div className="p-2 space-y-1.5 bg-white border-t border-slate-200">
                      {z.aisles.map(a => {
                        const isAisleSelected = selectedAisle === a.aisleCode;
                        return (
                          <div
                            key={a.aisleCode}
                            onClick={() => onSelectAisle(isAisleSelected ? null : a.aisleCode)}
                            className={`p-2 rounded text-xs cursor-pointer flex items-center justify-between transition-colors ${
                              isAisleSelected
                                ? 'bg-indigo-100 text-indigo-900 font-bold'
                                : 'hover:bg-slate-50 text-slate-700'
                            }`}
                          >
                            <div>
                              <span>Aisle {a.aisleCode}</span>
                              <span className="text-slate-400 text-[10px] ml-1">({a.locationCount} locs)</span>
                            </div>
                            <div className="flex items-center gap-2">
                              <span className="text-[11px] font-semibold">
                                {a.weightedCubeUtilizationPercent.toFixed(1)}%
                              </span>
                              <div className="w-12 bg-slate-200 rounded-full h-1.5 overflow-hidden">
                                <div
                                  className={`h-full rounded-full ${
                                    a.weightedCubeUtilizationPercent > 95 ? 'bg-red-500' :
                                    a.weightedCubeUtilizationPercent > 80 ? 'bg-amber-500' :
                                    'bg-emerald-500'
                                  }`}
                                  style={{ width: `${Math.min(a.weightedCubeUtilizationPercent, 100)}%` }}
                                />
                              </div>
                            </div>
                          </div>
                        );
                      })}
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        )}

        {/* TAB 2: Active Health Alerts (Phase 6.3.6.1) */}
        {activeTab === 'alerts' && (
          <div className="space-y-3">
            <div className="text-[11px] text-slate-500 pb-2 border-b border-slate-100 flex justify-between">
              <span className="font-semibold uppercase tracking-wider">Active Inventory Alerts</span>
              <span>{alerts.length} Total</span>
            </div>

            {alerts.length === 0 ? (
              <div className="text-center py-8 text-slate-400 text-xs">
                ✅ No critical alerts active for this facility.
              </div>
            ) : (
              alerts.map(alert => (
                <div
                  key={alert.alertId}
                  className="p-3 rounded-lg border border-slate-200 text-xs bg-white shadow-2xs space-y-1.5"
                >
                  <div className="flex items-center justify-between">
                    <span className="font-bold text-slate-900">{alert.title}</span>
                    <span className={`px-2 py-0.5 rounded-full text-[10px] font-bold uppercase ${
                      alert.severity === 'Emergency' ? 'bg-red-600 text-white' :
                      alert.severity === 'Critical' ? 'bg-red-100 text-red-800' :
                      alert.severity === 'Warning' ? 'bg-amber-100 text-amber-800' :
                      'bg-blue-100 text-blue-800'
                    }`}>
                      {alert.severity}
                    </span>
                  </div>
                  <p className="text-slate-600 text-[11px]">{alert.message}</p>
                  <div className="text-[10px] text-indigo-700 bg-indigo-50 p-1.5 rounded border border-indigo-100 font-medium">
                    Recommendation: {alert.actionRecommendation}
                  </div>
                </div>
              ))
            )}
          </div>
        )}

        {/* TAB 3: Selected Location Inspector */}
        {activeTab === 'inspector' && (
          <div>
            {!selectedLocation ? (
              <div className="text-center py-12 text-slate-400 text-xs">
                👉 Click any storage cell on the floor map to inspect its real-time inventory and capacity metrics.
              </div>
            ) : (
              <div className="space-y-4 text-xs">
                {/* Location Code & Status */}
                <div className="p-3 bg-slate-900 text-white rounded-lg">
                  <div className="flex items-center justify-between">
                    <span className="text-sm font-bold font-mono text-indigo-300">
                      {selectedLocation.locationCode}
                    </span>
                    <span
                      className="px-2 py-0.5 rounded text-[10px] font-bold uppercase text-white"
                      style={{ backgroundColor: selectedLocation.colorHex }}
                    >
                      {selectedLocation.visualTier}
                    </span>
                  </div>
                  <div className="text-slate-400 text-[11px] mt-1">
                    Zone: {selectedLocation.zoneCode} &bull; Aisle: {selectedLocation.aisleCode || '-'} &bull; Rack: {selectedLocation.rackCode || '-'} &bull; Level: {selectedLocation.shelfLevel ?? '-'}
                  </div>
                </div>

                {/* Coordinates */}
                <div className="p-3 bg-slate-50 border border-slate-200 rounded-lg">
                  <div className="font-semibold text-slate-700 text-xs mb-1">Spatial Coordinates:</div>
                  <div className="grid grid-cols-3 gap-2 font-mono text-[11px] text-slate-800">
                    <div>X: {selectedLocation.coordinate.x?.toFixed(2) ?? '-'} m</div>
                    <div>Y: {selectedLocation.coordinate.y?.toFixed(2) ?? '-'} m</div>
                    <div>Z: {selectedLocation.coordinate.z?.toFixed(2) ?? '-'} m</div>
                  </div>
                  <div className="text-[10px] text-slate-500 mt-1">
                    Physical: {selectedLocation.coordinate.lengthCm ?? 0}cm &times; {selectedLocation.coordinate.widthCm ?? 0}cm &times; {selectedLocation.coordinate.heightCm ?? 0}cm
                  </div>
                </div>

                {/* Capacity & Space Metrics */}
                <div className="p-3 bg-white border border-slate-200 rounded-lg space-y-2">
                  <div className="font-semibold text-slate-700 text-xs">Space Utilization:</div>
                  <div className="flex justify-between text-slate-600">
                    <span>Cube Fill:</span>
                    <strong className="text-slate-900">{selectedLocation.cubeUtilizationPercent.toFixed(1)}%</strong>
                  </div>
                  <div className="flex justify-between text-slate-600">
                    <span>Occupied CBM:</span>
                    <span>{selectedLocation.occupiedCbm.toFixed(2)} / {selectedLocation.usableCbm.toFixed(2)} m³</span>
                  </div>
                  <div className="flex justify-between text-slate-600">
                    <span>Pallet Positions:</span>
                    <span>{selectedLocation.occupiedPallets} / {selectedLocation.usablePallets} ({selectedLocation.palletOccupancyPercent.toFixed(0)}%)</span>
                  </div>
                  {selectedLocation.isOverCapacity && (
                    <div className="text-red-600 font-bold text-[11px]">
                      ⚠️ Overcapacity Excess: +{selectedLocation.excessCbm.toFixed(2)} CBM
                    </div>
                  )}
                </div>

                {/* Stock Details */}
                <div className="p-3 bg-white border border-slate-200 rounded-lg space-y-2">
                  <div className="font-semibold text-slate-700 text-xs">Inventory Health:</div>
                  <div className="flex justify-between text-slate-600">
                    <span>Total Units:</span>
                    <strong className="text-slate-900">{selectedLocation.totalUnits.toLocaleString()} units</strong>
                  </div>
                  <div className="flex justify-between text-slate-600">
                    <span>Distinct SKUs:</span>
                    <span>{selectedLocation.distinctSkuCount} SKUs</span>
                  </div>
                  {selectedLocation.hasDeadStock && (
                    <div className="flex justify-between text-amber-700">
                      <span>Dead Stock:</span>
                      <span>{selectedLocation.deadStockUnits} units (${selectedLocation.deadStockValue.toLocaleString()})</span>
                    </div>
                  )}
                  {selectedLocation.hasExpiredStock && (
                    <div className="flex justify-between text-red-700">
                      <span>Expired Units:</span>
                      <span>{selectedLocation.expiredUnits} units</span>
                    </div>
                  )}
                </div>

                {/* Deselect Action */}
                <button
                  onClick={() => onSelectLocation(null)}
                  className="w-full py-2 rounded-lg bg-slate-100 hover:bg-slate-200 text-slate-700 font-medium text-xs transition-colors cursor-pointer"
                >
                  Clear Selection
                </button>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
};
