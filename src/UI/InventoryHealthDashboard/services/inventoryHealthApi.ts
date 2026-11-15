// ============================================================================
// Phase 6.3.6.4: Inventory Health & Spatial Heatmap API Service
// ============================================================================

import {
  InventoryHealthDashboardDto,
  WarehouseHeatmapResponseDto,
  SpatialHeatmapProjectionDto,
  SpatialHotspotDto,
  ZoneSpatialRollupDto,
  HeatmapFilterState
} from '../types/inventoryHealth.types';

const API_BASE = process.env.NEXT_PUBLIC_API_URL || '/api/v1/analytics/inventory-health';

export const inventoryHealthApi = {
  /**
   * Fetches the consolidated unified inventory health dashboard (Phase 6.3.6.1)
   */
  async getUnifiedDashboard(
    facilityId: string,
    clientId?: string,
    asOfDate?: string
  ): Promise<InventoryHealthDashboardDto> {
    const params = new URLSearchParams();
    if (clientId) params.append('clientId', clientId);
    if (asOfDate) params.append('asOfDateUtc', asOfDate);

    const res = await fetch(`${API_BASE}/facility/${facilityId}/dashboard?${params.toString()}`, {
      headers: {
        'Accept': 'application/json'
      }
    });

    if (!res.ok) {
      throw new Error(`Failed to load inventory health dashboard: ${res.statusText}`);
    }
    return res.json();
  },

  /**
   * Fetches the 2D/3D location heatmap cells (Phase 6.3.6.2)
   */
  async getWarehouseHeatmap(
    facilityId: string,
    filter?: Partial<HeatmapFilterState>,
    asOfDate?: string
  ): Promise<WarehouseHeatmapResponseDto> {
    const params = new URLSearchParams();
    if (filter?.zoneId) params.append('zoneId', filter.zoneId);
    if (filter?.aisleCode) params.append('aisleCode', filter.aisleCode);
    if (filter?.shelfLevel !== undefined && filter?.shelfLevel !== null) {
      params.append('shelfLevel', filter.shelfLevel.toString());
    }
    if (filter?.activeLayers !== undefined) {
      params.append('activeLayers', filter.activeLayers.toString());
    }
    if (filter?.primaryColorLayer !== undefined) {
      params.append('primaryColorLayer', filter.primaryColorLayer.toString());
    }
    if (asOfDate) params.append('asOfDateUtc', asOfDate);

    const res = await fetch(`${API_BASE}/facility/${facilityId}/heatmap?${params.toString()}`, {
      headers: {
        'Accept': 'application/json'
      }
    });

    if (!res.ok) {
      throw new Error(`Failed to load warehouse heatmap: ${res.statusText}`);
    }
    return res.json();
  },

  /**
   * Fetches the spatial heatmap projection with uniform grid binning & multi-tier rollups (Phase 6.3.6.3)
   */
  async getSpatialHeatmapProjection(
    facilityId: string,
    filter?: Partial<HeatmapFilterState>,
    gridCellSizeMeters: number = 2.0,
    asOfDate?: string
  ): Promise<SpatialHeatmapProjectionDto> {
    const params = new URLSearchParams();
    params.append('gridCellSizeMeters', gridCellSizeMeters.toString());
    if (filter?.zoneId) params.append('zoneId', filter.zoneId);
    if (filter?.aisleCode) params.append('aisleCode', filter.aisleCode);
    if (filter?.shelfLevel !== undefined && filter?.shelfLevel !== null) {
      params.append('shelfLevel', filter.shelfLevel.toString());
    }
    if (asOfDate) params.append('asOfDateUtc', asOfDate);

    const res = await fetch(`${API_BASE}/facility/${facilityId}/spatial-projection?${params.toString()}`, {
      headers: {
        'Accept': 'application/json'
      }
    });

    if (!res.ok) {
      throw new Error(`Failed to load spatial heatmap projection: ${res.statusText}`);
    }
    return res.json();
  },

  /**
   * Fetches topologically clustered operational hotspots (Phase 6.3.6.3)
   */
  async getSpatialHotspots(
    facilityId: string,
    asOfDate?: string
  ): Promise<SpatialHotspotDto[]> {
    const params = new URLSearchParams();
    if (asOfDate) params.append('asOfDateUtc', asOfDate);

    const res = await fetch(`${API_BASE}/facility/${facilityId}/hotspots?${params.toString()}`, {
      headers: {
        'Accept': 'application/json'
      }
    });

    if (!res.ok) {
      throw new Error(`Failed to load operational hotspots: ${res.statusText}`);
    }
    return res.json();
  },

  /**
   * Fetches hierarchical zone spatial rollups (Phase 6.3.6.3)
   */
  async getZoneSpatialRollups(
    facilityId: string,
    asOfDate?: string
  ): Promise<ZoneSpatialRollupDto[]> {
    const params = new URLSearchParams();
    if (asOfDate) params.append('asOfDateUtc', asOfDate);

    const res = await fetch(`${API_BASE}/facility/${facilityId}/zone-rollups?${params.toString()}`, {
      headers: {
        'Accept': 'application/json'
      }
    });

    if (!res.ok) {
      throw new Error(`Failed to load zone spatial rollups: ${res.statusText}`);
    }
    return res.json();
  }
};
