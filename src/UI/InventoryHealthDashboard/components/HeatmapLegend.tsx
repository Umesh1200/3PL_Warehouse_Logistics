// ============================================================================
// Phase 6.3.6.4: HeatmapLegend Component
// ============================================================================

import React from 'react';
import { HeatmapLayerType } from '../types/inventoryHealth.types';

interface HeatmapLegendProps {
  primaryLayer: HeatmapLayerType;
}

export const HeatmapLegend: React.FC<HeatmapLegendProps> = ({ primaryLayer }) => {
  const getLegendItems = () => {
    switch (primaryLayer) {
      case HeatmapLayerType.CubeUtilization:
      case HeatmapLayerType.PalletOccupancy:
        return [
          { label: 'Empty (0%)', color: '#E2E8F0', border: '#CBD5E1' },
          { label: 'Low (<50%)', color: '#93C5FD', border: '#60A5FA' },
          { label: 'Optimal (50-85%)', color: '#22C55E', border: '#16A34A' },
          { label: 'Near Full (86-100%)', color: '#F59E0B', border: '#D97706' },
          { label: 'Overcapacity (>100%)', color: '#EF4444', border: '#DC2626' },
          { label: 'Blocked / Maint', color: '#64748B', border: '#475569' },
        ];
      case HeatmapLayerType.InventoryAging:
        return [
          { label: 'Fresh (<30d)', color: '#22C55E', border: '#16A34A' },
          { label: 'Normal (30-90d)', color: '#93C5FD', border: '#60A5FA' },
          { label: 'Aging (91-180d)', color: '#F59E0B', border: '#D97706' },
          { label: 'Dead Stock (>180d)', color: '#EF4444', border: '#DC2626' },
          { label: 'Expired Stock', color: '#7F1D1D', border: '#450A0A' },
        ];
      case HeatmapLayerType.SkuVelocity:
        return [
          { label: 'Fast Movers (Top 20%)', color: '#6366F1', border: '#4F46E5' },
          { label: 'Medium Movers', color: '#38BDF8', border: '#0284C7' },
          { label: 'Slow Movers', color: '#FBBF24', border: '#D97706' },
          { label: 'Non-Moving (Zero Pick)', color: '#F43F5E', border: '#E11D48' },
        ];
      case HeatmapLayerType.AbcClassification:
        return [
          { label: 'Class A (80% ACV)', color: '#8B5CF6', border: '#7C3AED' },
          { label: 'Class B (15% ACV)', color: '#3B82F6', border: '#2563EB' },
          { label: 'Class C (5% ACV)', color: '#94A3B8', border: '#64748B' },
        ];
      case HeatmapLayerType.BlockedQuarantine:
        return [
          { label: 'Active Unlocked', color: '#22C55E', border: '#16A34A' },
          { label: 'Damaged Location', color: '#F97316', border: '#EA580C' },
          { label: 'Quarantine Lock', color: '#EF4444', border: '#DC2626' },
          { label: 'Maintenance Hold', color: '#64748B', border: '#475569' },
        ];
      default:
        return [
          { label: 'Empty', color: '#E2E8F0', border: '#CBD5E1' },
          { label: 'Optimal', color: '#22C55E', border: '#16A34A' },
          { label: 'Warning', color: '#F59E0B', border: '#D97706' },
          { label: 'Critical', color: '#EF4444', border: '#DC2626' },
        ];
    }
  };

  const items = getLegendItems();

  return (
    <div className="bg-white/95 backdrop-blur-xs px-4 py-2.5 rounded-lg border border-slate-200 shadow-sm text-xs flex flex-wrap items-center gap-4">
      <span className="font-semibold text-slate-700 uppercase tracking-wider text-[11px]">
        Scale:
      </span>
      <div className="flex items-center gap-3 flex-wrap">
        {items.map((item, idx) => (
          <div key={idx} className="flex items-center gap-1.5">
            <span
              className="inline-block w-3.5 h-3.5 rounded-sm shadow-2xs"
              style={{ backgroundColor: item.color, borderColor: item.border, borderWidth: '1px' }}
            />
            <span className="text-slate-600 font-medium">{item.label}</span>
          </div>
        ))}
      </div>
      <div className="border-l border-slate-200 pl-3 flex items-center gap-3 text-slate-500">
        <span className="flex items-center gap-1">
          <span className="text-red-500 font-bold">⚠️</span> Overcapacity
        </span>
        <span className="flex items-center gap-1">
          <span className="text-amber-500 font-bold">⏳</span> Dead Stock
        </span>
        <span className="flex items-center gap-1">
          <span className="text-slate-500 font-bold">🔒</span> Blocked
        </span>
      </div>
    </div>
  );
};
