// ============================================================================
// Phase 6.3.6.4: WarehouseBreadcrumb Component
// ============================================================================

import React from 'react';
import { ZoneSpatialRollupDto } from '../types/inventoryHealth.types';

interface WarehouseBreadcrumbProps {
  facilityName: string;
  facilityCode: string;
  selectedZone: ZoneSpatialRollupDto | null;
  selectedAisle: string | null;
  selectedLevel: number | null;
  onSelectFacility: () => void;
  onSelectZone: (zone: ZoneSpatialRollupDto | null) => void;
  onSelectAisle: (aisle: string | null) => void;
  onSelectLevel: (level: number | null) => void;
}

export const WarehouseBreadcrumb: React.FC<WarehouseBreadcrumbProps> = ({
  facilityName,
  facilityCode,
  selectedZone,
  selectedAisle,
  selectedLevel,
  onSelectFacility,
  onSelectZone,
  onSelectAisle,
  onSelectLevel
}) => {
  return (
    <nav className="flex items-center space-x-2 text-sm text-slate-600 bg-white px-6 py-2.5 border-b border-slate-200" aria-label="Breadcrumb">
      {/* 1. Facility Root */}
      <button
        onClick={onSelectFacility}
        className="font-medium text-slate-800 hover:text-indigo-600 transition-colors flex items-center gap-1 cursor-pointer"
      >
        <svg className="w-4 h-4 text-slate-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 21V5a2 2 0 00-2-2H7a2 2 0 00-2 2v16m14 0h2m-2 0h-5m-9 0H3m2 0h5M9 7h1m-1 4h1m4-4h1m-1 4h1m-5 10v-5a1 1 0 011-1h2a1 1 0 011 1v5m-4 0h4" />
        </svg>
        <span>{facilityCode || facilityName || 'Facility'}</span>
      </button>

      {/* 2. Zone */}
      {selectedZone && (
        <>
          <span className="text-slate-400">/</span>
          <button
            onClick={() => {
              onSelectAisle(null);
              onSelectLevel(null);
            }}
            className={`hover:text-indigo-600 transition-colors cursor-pointer ${
              !selectedAisle ? 'font-semibold text-indigo-700' : 'text-slate-700'
            }`}
          >
            Zone: {selectedZone.zoneCode}
          </button>
        </>
      )}

      {/* 3. Aisle */}
      {selectedAisle && (
        <>
          <span className="text-slate-400">/</span>
          <button
            onClick={() => onSelectLevel(null)}
            className={`hover:text-indigo-600 transition-colors cursor-pointer ${
              selectedLevel === null ? 'font-semibold text-indigo-700' : 'text-slate-700'
            }`}
          >
            Aisle: {selectedAisle}
          </button>
        </>
      )}

      {/* 4. Shelf Level (VIS-01) */}
      {selectedLevel !== null && (
        <>
          <span className="text-slate-400">/</span>
          <span className="font-semibold text-indigo-700 bg-indigo-50 px-2 py-0.5 rounded text-xs border border-indigo-200">
            Level {selectedLevel}
          </span>
          <button
            onClick={() => onSelectLevel(null)}
            className="text-xs text-slate-400 hover:text-slate-600 ml-1 cursor-pointer"
            title="Clear Level Filter"
          >
            &times;
          </button>
        </>
      )}
    </nav>
  );
};
