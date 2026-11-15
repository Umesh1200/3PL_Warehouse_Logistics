// ============================================================================
// Phase 6.3.6.4: useInventoryHealth Hook
// ============================================================================

import { useState, useEffect, useCallback } from 'react';
import { InventoryHealthDashboardDto } from '../types/inventoryHealth.types';
import { inventoryHealthApi } from '../services/inventoryHealthApi';

export interface UseInventoryHealthResult {
  dashboard: InventoryHealthDashboardDto | null;
  isLoading: boolean;
  error: string | null;
  refetch: () => Promise<void>;
  isStale: boolean;
}

export function useInventoryHealth(
  facilityId: string,
  clientId?: string,
  asOfDate?: string
): UseInventoryHealthResult {
  const [dashboard, setDashboard] = useState<InventoryHealthDashboardDto | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  const fetchDashboard = useCallback(async () => {
    if (!facilityId) return;
    try {
      setIsLoading(true);
      setError(null);
      const data = await inventoryHealthApi.getUnifiedDashboard(facilityId, clientId, asOfDate);
      setDashboard(data);
    } catch (err: any) {
      setError(err?.message || 'Failed to load inventory health metrics');
    } finally {
      setIsLoading(false);
    }
  }, [facilityId, clientId, asOfDate]);

  useEffect(() => {
    fetchDashboard();
  }, [fetchDashboard]);

  // VIS-07: Data freshness detection (> 60 minutes is flagged stale)
  const isStale = (dashboard?.projectionFreshnessMinutes ?? 0) > 60;

  return {
    dashboard,
    isLoading,
    error,
    refetch: fetchDashboard,
    isStale
  };
}
