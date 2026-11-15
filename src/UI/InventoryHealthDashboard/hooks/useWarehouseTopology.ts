// ============================================================================
// Phase 6.3.6.4: useWarehouseTopology Hook
// ============================================================================

import { useState, useEffect, useCallback } from 'react';
import { ZoneSpatialRollupDto } from '../types/inventoryHealth.types';
import { inventoryHealthApi } from '../services/inventoryHealthApi';

export interface UseWarehouseTopologyResult {
  zones: ZoneSpatialRollupDto[];
  isLoading: boolean;
  error: string | null;
  refetch: () => Promise<void>;
  selectedZone: ZoneSpatialRollupDto | null;
  selectZone: (zoneId: string | null) => void;
  selectedAisle: string | null;
  selectAisle: (aisleCode: string | null) => void;
}

export function useWarehouseTopology(
  facilityId: string,
  asOfDate?: string
): UseWarehouseTopologyResult {
  const [zones, setZones] = useState<ZoneSpatialRollupDto[]>([]);
  const [selectedZoneId, setSelectedZoneId] = useState<string | null>(null);
  const [selectedAisle, setSelectedAisle] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  const fetchTopology = useCallback(async () => {
    if (!facilityId) return;
    try {
      setIsLoading(true);
      setError(null);
      const data = await inventoryHealthApi.getZoneSpatialRollups(facilityId, asOfDate);
      setZones(data);
    } catch (err: any) {
      setError(err?.message || 'Failed to load warehouse topology rollups');
    } finally {
      setIsLoading(false);
    }
  }, [facilityId, asOfDate]);

  useEffect(() => {
    fetchTopology();
  }, [fetchTopology]);

  const selectZone = useCallback((zoneId: string | null) => {
    setSelectedZoneId(zoneId);
    setSelectedAisle(null); // Reset aisle selection when zone changes
  }, []);

  const selectAisle = useCallback((aisleCode: string | null) => {
    setSelectedAisle(aisleCode);
  }, []);

  const selectedZone = zones.find(z => z.zoneId === selectedZoneId) || null;

  return {
    zones,
    isLoading,
    error,
    refetch: fetchTopology,
    selectedZone,
    selectZone,
    selectedAisle,
    selectAisle
  };
}
