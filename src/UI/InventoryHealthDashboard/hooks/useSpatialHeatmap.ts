// ============================================================================
// Phase 6.3.6.4: useSpatialHeatmap Hook
// ============================================================================

import { useState, useEffect, useCallback, useMemo } from 'react';
import {
  SpatialHeatmapProjectionDto,
  WarehouseHeatmapResponseDto,
  HeatmapLocationCellDto,
  SpatialHotspotDto,
  HeatmapFilterState,
  HeatmapLayerType,
  ZoomPanTransform
} from '../types/inventoryHealth.types';
import { inventoryHealthApi } from '../services/inventoryHealthApi';

export interface UseSpatialHeatmapResult {
  projection: SpatialHeatmapProjectionDto | null;
  heatmap: WarehouseHeatmapResponseDto | null;
  filteredLocations: HeatmapLocationCellDto[];
  hotspots: SpatialHotspotDto[];
  availableLevels: number[];
  filter: HeatmapFilterState;
  updateFilter: (partial: Partial<HeatmapFilterState>) => void;
  resetFilter: () => void;
  transform: ZoomPanTransform;
  zoomIn: () => void;
  zoomOut: () => void;
  resetZoom: () => void;
  pan: (dx: number, dy: number) => void;
  selectedLocation: HeatmapLocationCellDto | null;
  setSelectedLocation: (loc: HeatmapLocationCellDto | null) => void;
  hoveredLocation: HeatmapLocationCellDto | null;
  setHoveredLocation: (loc: HeatmapLocationCellDto | null) => void;
  selectedHotspot: SpatialHotspotDto | null;
  setSelectedHotspot: (hotspot: SpatialHotspotDto | null) => void;
  isLoading: boolean;
  error: string | null;
  refetch: () => Promise<void>;
}

const DEFAULT_FILTER: HeatmapFilterState = {
  zoneId: null,
  aisleCode: null,
  shelfLevel: null,
  activeLayers: HeatmapLayerType.StandardOverview,
  primaryColorLayer: HeatmapLayerType.CubeUtilization,
  searchKeyword: '',
  showSecondaryAlerts: true,
  onlyHotspots: false
};

const DEFAULT_TRANSFORM: ZoomPanTransform = {
  scale: 1.0,
  translateX: 0,
  translateY: 0
};

export function useSpatialHeatmap(
  facilityId: string,
  initialFilter?: Partial<HeatmapFilterState>,
  asOfDate?: string
): UseSpatialHeatmapResult {
  const [filter, setFilter] = useState<HeatmapFilterState>({
    ...DEFAULT_FILTER,
    ...initialFilter
  });
  const [transform, setTransform] = useState<ZoomPanTransform>(DEFAULT_TRANSFORM);
  const [projection, setProjection] = useState<SpatialHeatmapProjectionDto | null>(null);
  const [heatmap, setHeatmap] = useState<WarehouseHeatmapResponseDto | null>(null);
  const [selectedLocation, setSelectedLocation] = useState<HeatmapLocationCellDto | null>(null);
  const [hoveredLocation, setHoveredLocation] = useState<HeatmapLocationCellDto | null>(null);
  const [selectedHotspot, setSelectedHotspot] = useState<SpatialHotspotDto | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  const fetchData = useCallback(async () => {
    if (!facilityId) return;
    try {
      setIsLoading(true);
      setError(null);
      const [projData, heatData] = await Promise.all([
        inventoryHealthApi.getSpatialHeatmapProjection(facilityId, filter, 2.0, asOfDate),
        inventoryHealthApi.getWarehouseHeatmap(facilityId, filter, asOfDate)
      ]);
      setProjection(projData);
      setHeatmap(heatData);
    } catch (err: any) {
      setError(err?.message || 'Failed to load spatial heatmap data');
    } finally {
      setIsLoading(false);
    }
  }, [facilityId, filter.zoneId, filter.aisleCode, filter.shelfLevel, filter.primaryColorLayer, asOfDate]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  const updateFilter = useCallback((partial: Partial<HeatmapFilterState>) => {
    setFilter(prev => ({ ...prev, ...partial }));
  }, []);

  const resetFilter = useCallback(() => {
    setFilter(DEFAULT_FILTER);
    setSelectedLocation(null);
    setSelectedHotspot(null);
  }, []);

  // Zoom and Pan actions (VIS-05)
  const zoomIn = useCallback(() => {
    setTransform(prev => ({
      ...prev,
      scale: Math.min(prev.scale * 1.25, 20.0) // Capped at max 20x
    }));
  }, []);

  const zoomOut = useCallback(() => {
    setTransform(prev => ({
      ...prev,
      scale: Math.max(prev.scale / 1.25, 0.1) // Capped at min 0.1x
    }));
  }, []);

  const resetZoom = useCallback(() => {
    setTransform(DEFAULT_TRANSFORM);
  }, []);

  const pan = useCallback((dx: number, dy: number) => {
    setTransform(prev => ({
      ...prev,
      translateX: prev.translateX + dx,
      translateY: prev.translateY + dy
    }));
  }, []);

  // Dynamically extract all available shelf levels (VIS-01)
  const availableLevels = useMemo(() => {
    if (!heatmap?.Locations) return [1, 2, 3, 4, 5];
    const levels = new Set<number>();
    for (const loc of heatmap.Locations) {
      if (loc.shelfLevel !== null && loc.shelfLevel !== undefined) {
        levels.add(loc.shelfLevel);
      }
    }
    return Array.from(levels).sort((a, b) => a - b);
  }, [heatmap]);

  // Client-side filtering of locations based on keyword, hotspots, and utilization thresholds
  const filteredLocations = useMemo(() => {
    if (!heatmap?.Locations) return [];
    let list = heatmap.Locations;

    // Search keyword filter (LocationCode, Aisle, Rack)
    if (filter.searchKeyword.trim()) {
      const q = filter.searchKeyword.toLowerCase().trim();
      list = list.filter(
        l =>
          l.locationCode.toLowerCase().includes(q) ||
          (l.aisleCode && l.aisleCode.toLowerCase().includes(q)) ||
          (l.rackCode && l.rackCode.toLowerCase().includes(q)) ||
          l.zoneCode.toLowerCase().includes(q)
      );
    }

    // Only hotspots filter (VIS-06)
    if (filter.onlyHotspots && projection?.hotspots) {
      const hotspotLocIds = new Set(projection.hotspots.flatMap(h => h.locationIds));
      list = list.filter(l => hotspotLocIds.has(l.locationId));
    }

    // Utilization range filters
    if (filter.minUtilization !== undefined) {
      list = list.filter(l => l.cubeUtilizationPercent >= filter.minUtilization!);
    }
    if (filter.maxUtilization !== undefined) {
      list = list.filter(l => l.cubeUtilizationPercent <= filter.maxUtilization!);
    }

    return list;
  }, [heatmap, filter, projection]);

  const hotspots = projection?.hotspots || [];

  return {
    projection,
    heatmap,
    filteredLocations,
    hotspots,
    availableLevels,
    filter,
    updateFilter,
    resetFilter,
    transform,
    zoomIn,
    zoomOut,
    resetZoom,
    pan,
    selectedLocation,
    setSelectedLocation,
    hoveredLocation,
    setHoveredLocation,
    selectedHotspot,
    setSelectedHotspot,
    isLoading,
    error,
    refetch: fetchData
  };
}
