// ============================================================================
// Phase 6.3.6.4: InventoryHealthDashboard Page Component (Next.js App Router)
// ============================================================================

import React, { useState, useCallback } from 'react';
import { useInventoryHealth } from './hooks/useInventoryHealth';
import { useWarehouseTopology } from './hooks/useWarehouseTopology';
import { useSpatialHeatmap } from './hooks/useSpatialHeatmap';
import { InventoryHealthHeader } from './components/InventoryHealthHeader';
import { InventoryHealthKpiCards } from './components/InventoryHealthKpiCards';
import { WarehouseBreadcrumb } from './components/WarehouseBreadcrumb';
import { HeatmapLayerControls } from './components/HeatmapLayerControls';
import { WarehouseSpatialViewer } from './components/WarehouseSpatialViewer';
import { HeatmapLegend } from './components/HeatmapLegend';
import { WarehouseLocationTooltip } from './components/WarehouseLocationTooltip';
import { HotspotPanel } from './components/HotspotPanel';
import { InventoryHealthSidebar } from './components/InventoryHealthSidebar';
import { HeatmapLocationCellDto } from './types/inventoryHealth.types';

interface InventoryHealthDashboardProps {
  facilityId?: string;
  clientId?: string;
}

export default function InventoryHealthDashboardPage({
  facilityId = '00000000-0000-0000-0000-000000000001',
  clientId
}: InventoryHealthDashboardProps) {
  // Hooks
  const { dashboard, isLoading: isDashboardLoading, refetch: refetchDashboard, isStale } = useInventoryHealth(
    facilityId,
    clientId
  );

  const {
    zones,
    selectedZone,
    selectZone,
    selectedAisle,
    selectAisle,
    refetch: refetchTopology
  } = useWarehouseTopology(facilityId);

  const {
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
    selectedHotspot,
    setSelectedHotspot,
    isLoading: isHeatmapLoading,
    refetch: refetchHeatmap
  } = useSpatialHeatmap(facilityId, {
    zoneId: selectedZone?.zoneId,
    aisleCode: selectedAisle,
    shelfLevel: null
  });

  // UI Local State
  const [showGrid, setShowGrid] = useState<boolean>(true);
  const [isHotspotPanelOpen, setIsHotspotPanelOpen] = useState<boolean>(true);
  const [hoveredLoc, setHoveredLoc] = useState<HeatmapLocationCellDto | null>(null);
  const [tooltipPos, setTooltipPos] = useState<{ x: number; y: number }>({ x: 0, y: 0 });

  const handleRefreshAll = useCallback(async () => {
    await Promise.all([refetchDashboard(), refetchTopology(), refetchHeatmap()]);
  }, [refetchDashboard, refetchTopology, refetchHeatmap]);

  const handleHoverLocation = useCallback(
    (loc: HeatmapLocationCellDto | null, clientX: number, clientY: number) => {
      setHoveredLoc(loc);
      setTooltipPos({ x: clientX, y: clientY });
    },
    []
  );

  // Focus map on specific coordinates (e.g. from hotspot click)
  const handleFocusCoordinates = useCallback(
    (x: number, y: number) => {
      // Reset zoom to 2.5x and center on (x, y)
      // Pan offset is calculated relative to center
      resetZoom();
      pan(-x + 75, -y + 50);
    },
    [resetZoom, pan]
  );

  const facilityName = projection?.facilityName || dashboard?.facilityName || 'Main Distribution Hub';
  const facilityCode = projection?.facilityCode || dashboard?.facilityCode || 'DC-01';

  return (
    <div className="flex flex-col min-h-screen bg-slate-100 text-slate-900 font-sans antialiased">
      {/* 1. Header with Tenancy & Freshness */}
      <InventoryHealthHeader
        dashboard={dashboard}
        facilityName={facilityName}
        facilityCode={facilityCode}
        isStale={isStale}
        isLoading={isDashboardLoading || isHeatmapLoading}
        onRefresh={handleRefreshAll}
        clientId={clientId}
        clientName={dashboard?.clientName}
      />

      {/* 2. Top-Tier KPI Metric Cards (HM-04 Weighted Utilization) */}
      <InventoryHealthKpiCards
        dashboard={dashboard}
        projection={projection}
      />

      {/* 3. Interactive Breadcrumb Navigation */}
      <WarehouseBreadcrumb
        facilityName={facilityName}
        facilityCode={facilityCode}
        selectedZone={selectedZone}
        selectedAisle={selectedAisle}
        selectedLevel={filter.shelfLevel ?? null}
        onSelectFacility={() => {
          selectZone(null);
          selectAisle(null);
          resetFilter();
        }}
        onSelectZone={(z) => {
          selectZone(z?.zoneId || null);
          updateFilter({ zoneId: z?.zoneId, aisleCode: null });
        }}
        onSelectAisle={(a) => {
          selectAisle(a);
          updateFilter({ aisleCode: a });
        }}
        onSelectLevel={(lvl) => {
          updateFilter({ shelfLevel: lvl });
        }}
      />

      {/* 4. Heatmap Layer Controls (VIS-01 Floor Level, VIS-03 Layers, VIS-04 Alerts) */}
      <HeatmapLayerControls
        filter={filter}
        availableLevels={availableLevels}
        onFilterChange={updateFilter}
        onResetFilter={resetFilter}
        showGrid={showGrid}
        onToggleGrid={setShowGrid}
        totalLocationsCount={projection?.totalLocationsEvaluated ?? 0}
        filteredLocationsCount={filteredLocations.length}
      />

      {/* 5. Main Spatial Workspace & Sidebars */}
      <div className="flex-1 flex flex-col lg:flex-row p-6 gap-6">
        {/* Left Column: Spatial Heatmap Canvas & Hotspots Panel */}
        <div className="flex-1 flex flex-col gap-6">
          {/* Spatial Viewer Container */}
          <div className="bg-white rounded-xl border border-slate-200 shadow-sm overflow-hidden relative">
            {projection && (
              <WarehouseSpatialViewer
                envelope={projection.facilityEnvelope}
                boundingBox={projection.locationBoundingBox}
                locations={filteredLocations}
                gridCells={projection.gridCells}
                hotspots={hotspots}
                zones={zones}
                showGrid={showGrid}
                selectedLevel={filter.shelfLevel ?? null}
                transform={transform}
                onZoomIn={zoomIn}
                onZoomOut={zoomOut}
                onResetZoom={resetZoom}
                onPan={pan}
                selectedLocation={selectedLocation}
                onSelectLocation={setSelectedLocation}
                hoveredLocation={hoveredLoc}
                onHoverLocation={handleHoverLocation}
                selectedHotspot={selectedHotspot}
              />
            )}

            {/* Hover Tooltip (VIS-04) */}
            {hoveredLoc && (
              <WarehouseLocationTooltip
                location={hoveredLoc}
                x={tooltipPos.x}
                y={tooltipPos.y}
                containerWidth={900}
                containerHeight={580}
              />
            )}

            {/* Legend Overlay at bottom of canvas */}
            <div className="absolute bottom-4 left-4 z-20">
              <HeatmapLegend primaryLayer={filter.primaryColorLayer} />
            </div>
          </div>

          {/* Operational Hotspot Management Panel (VIS-06 & VIS-10) */}
          <HotspotPanel
            hotspots={hotspots}
            selectedHotspot={selectedHotspot}
            onSelectHotspot={setSelectedHotspot}
            onFocusCoordinates={handleFocusCoordinates}
            isOpen={isHotspotPanelOpen}
            onToggle={() => setIsHotspotPanelOpen(prev => !prev)}
          />
        </div>

        {/* Right Column: Multi-Tier Hierarchy Rollups, Alerts & Inspector */}
        <div className="w-full lg:w-96 shrink-0">
          <InventoryHealthSidebar
            zones={zones}
            selectedZone={selectedZone}
            onSelectZone={(z) => {
              selectZone(z?.zoneId || null);
              updateFilter({ zoneId: z?.zoneId, aisleCode: null });
            }}
            selectedAisle={selectedAisle}
            onSelectAisle={(a) => {
              selectAisle(a);
              updateFilter({ aisleCode: a });
            }}
            selectedLocation={selectedLocation}
            onSelectLocation={setSelectedLocation}
            alerts={dashboard?.activeAlerts || []}
            filter={filter}
            onFilterChange={updateFilter}
          />
        </div>
      </div>
    </div>
  );
}
