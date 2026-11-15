// ============================================================================
// Phase 6.3.6.4: WarehouseSpatialViewer Component
// Interactive SVG 2D Warehouse Spatial Heatmap Viewer
// Implements VIS-01 (Level Slice), VIS-02 (Origin Offset), VIS-05 (Zoom/Pan), VIS-09 (Performance)
// ============================================================================

import React, { useRef, useState, useCallback, useMemo } from 'react';
import {
  FacilitySpatialEnvelopeDto,
  WarehouseBoundingBoxDto,
  HeatmapLocationCellDto,
  SpatialGridCellDto,
  SpatialHotspotDto,
  ZoneSpatialRollupDto,
  ZoomPanTransform
} from '../types/inventoryHealth.types';

interface WarehouseSpatialViewerProps {
  envelope: FacilitySpatialEnvelopeDto;
  boundingBox: WarehouseBoundingBoxDto;
  locations: HeatmapLocationCellDto[];
  gridCells: SpatialGridCellDto[];
  hotspots: SpatialHotspotDto[];
  zones: ZoneSpatialRollupDto[];
  showGrid: boolean;
  selectedLevel: number | null;
  transform: ZoomPanTransform;
  onZoomIn: () => void;
  onZoomOut: () => void;
  onResetZoom: () => void;
  onPan: (dx: number, dy: number) => void;
  selectedLocation: HeatmapLocationCellDto | null;
  onSelectLocation: (loc: HeatmapLocationCellDto | null) => void;
  hoveredLocation: HeatmapLocationCellDto | null;
  onHoverLocation: (loc: HeatmapLocationCellDto | null, clientX: number, clientY: number) => void;
  selectedHotspot: SpatialHotspotDto | null;
}

export const WarehouseSpatialViewer: React.FC<WarehouseSpatialViewerProps> = ({
  envelope,
  locations,
  gridCells,
  hotspots,
  zones,
  showGrid,
  selectedLevel,
  transform,
  onZoomIn,
  onZoomOut,
  onResetZoom,
  onPan,
  selectedLocation,
  onSelectLocation,
  onHoverLocation,
  selectedHotspot
}) => {
  const containerRef = useRef<HTMLDivElement>(null);
  const [isDragging, setIsDragging] = useState(false);
  const [dragStart, setDragStart] = useState({ x: 0, y: 0 });

  // Authoritative facility envelope dimensions in meters (VIS-02)
  const envX = envelope.originXMeters || 0;
  const envY = envelope.originYMeters || 0;
  const envW = envelope.lengthMeters || 150; // Warehouse X is Length
  const envH = envelope.widthMeters || 100;   // Warehouse Y is Width
  const padding = 10; // 10 meters margin around facility envelope

  // SVG base viewBox coordinate space
  const baseViewBoxX = envX - padding;
  const baseViewBoxY = envY - padding;
  const baseViewBoxW = envW + padding * 2;
  const baseViewBoxH = envH + padding * 2;

  // Zoom/Pan transformation of SVG viewBox (VIS-05)
  const currentViewBoxW = baseViewBoxW / transform.scale;
  const currentViewBoxH = baseViewBoxH / transform.scale;
  const currentViewBoxX = baseViewBoxX - transform.translateX + (baseViewBoxW - currentViewBoxW) / 2;
  const currentViewBoxY = baseViewBoxY - transform.translateY + (baseViewBoxH - currentViewBoxH) / 2;

  // Mouse drag handlers for panning
  const handleMouseDown = (e: React.MouseEvent) => {
    if (e.button !== 0) return; // Only left mouse button
    setIsDragging(true);
    setDragStart({ x: e.clientX, y: e.clientY });
  };

  const handleMouseMove = (e: React.MouseEvent) => {
    if (isDragging) {
      const dx = (e.clientX - dragStart.x) * (currentViewBoxW / (containerRef.current?.clientWidth || 800));
      const dy = (e.clientY - dragStart.y) * (currentViewBoxH / (containerRef.current?.clientHeight || 600));
      onPan(dx, dy);
      setDragStart({ x: e.clientX, y: e.clientY });
    }
  };

  const handleMouseUp = () => {
    setIsDragging(false);
  };

  const handleWheel = (e: React.WheelEvent) => {
    e.preventDefault();
    if (e.deltaY < 0) {
      onZoomIn();
    } else {
      onZoomOut();
    }
  };

  // Set of location IDs belonging to selected hotspot
  const hotspotLocationIds = useMemo(() => {
    return selectedHotspot ? new Set(selectedHotspot.locationIds) : null;
  }, [selectedHotspot]);

  return (
    <div
      ref={containerRef}
      onMouseDown={handleMouseDown}
      onMouseMove={handleMouseMove}
      onMouseUp={handleMouseUp}
      onMouseLeave={() => {
        setIsDragging(false);
        onHoverLocation(null, 0, 0);
      }}
      onWheel={handleWheel}
      className={`relative w-full h-[580px] bg-slate-900 overflow-hidden select-none cursor-${
        isDragging ? 'grabbing' : 'grab'
      }`}
    >
      {/* Interactive Controls Overlay */}
      <div className="absolute top-4 right-4 z-20 flex flex-col gap-1.5 bg-slate-800/90 backdrop-blur-md p-1.5 rounded-lg border border-slate-700 shadow-md">
        <button
          onClick={onZoomIn}
          className="w-8 h-8 rounded bg-slate-700 hover:bg-slate-600 text-white font-bold text-base flex items-center justify-center transition-colors cursor-pointer"
          title="Zoom In"
        >
          +
        </button>
        <button
          onClick={onZoomOut}
          className="w-8 h-8 rounded bg-slate-700 hover:bg-slate-600 text-white font-bold text-base flex items-center justify-center transition-colors cursor-pointer"
          title="Zoom Out"
        >
          &minus;
        </button>
        <button
          onClick={onResetZoom}
          className="w-8 h-8 rounded bg-slate-700 hover:bg-slate-600 text-white text-xs font-medium flex items-center justify-center transition-colors cursor-pointer"
          title="Reset Zoom & Pan"
        >
          1:1
        </button>
      </div>

      {/* Level & Scale Status Badge in Corner */}
      <div className="absolute top-4 left-4 z-20 flex items-center gap-2 bg-slate-800/90 backdrop-blur-md px-3 py-1.5 rounded-lg border border-slate-700 text-xs text-slate-300">
        <span className="inline-block w-2 h-2 rounded-full bg-indigo-500 animate-pulse" />
        <span>
          View: <strong>{selectedLevel !== null ? `Level ${selectedLevel}` : 'All Levels (2D Composite)'}</strong>
        </span>
        <span className="text-slate-500">&bull;</span>
        <span className="font-mono text-slate-400">Zoom: {(transform.scale * 100).toFixed(0)}%</span>
      </div>

      {/* SVG Canvas */}
      <svg
        className="w-full h-full"
        viewBox={`${currentViewBoxX} ${currentViewBoxY} ${currentViewBoxW} ${currentViewBoxH}`}
        preserveAspectRatio="xMidYMid meet"
      >
        <defs>
          {/* Subtle floor grid pattern */}
          <pattern id="floor-subgrid" width="10" height="10" patternUnits="userSpaceOnUse">
            <path d="M 10 0 L 0 0 0 10" fill="none" stroke="#1E293B" strokeWidth="0.2" />
          </pattern>
          {/* Out-of-bounds warning pattern */}
          <pattern id="out-of-bounds-stripes" width="4" height="4" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
            <line x1="0" y1="0" x2="0" y2="4" stroke="#7F1D1D" strokeWidth="1" />
          </pattern>
        </defs>

        {/* 1. Facility Floor Background */}
        <rect
          x={baseViewBoxX}
          y={baseViewBoxY}
          width={baseViewBoxW}
          height={baseViewBoxH}
          fill="#0F172A"
        />

        {/* 2. Authoritative Facility Spatial Envelope (HM-01, VIS-02) */}
        <rect
          x={envX}
          y={envY}
          width={envW}
          height={envH}
          fill="url(#floor-subgrid)"
          stroke="#475569"
          strokeWidth="0.8"
          strokeDasharray="4 2"
        />

        {/* Facility Envelope Label */}
        <text
          x={envX + 2}
          y={envY + 4}
          fill="#64748B"
          fontSize="2.5"
          fontFamily="monospace"
          fontWeight="bold"
        >
          PHYSICAL FACILITY ENVELOPE: {envW}m x {envH}m ({envelope.totalFloorAreaSqM.toLocaleString()} m²)
        </text>

        {/* 3. Optional 2.0m Spatial Grid Overlay (HM-03) */}
        {showGrid && gridCells.map((g, idx) => {
          const half = g.cellSizeMeters / 2.0;
          return (
            <rect
              key={idx}
              x={g.centerXMeters - half}
              y={g.centerYMeters - half}
              width={g.cellSizeMeters}
              height={g.cellSizeMeters}
              fill={g.primaryColorHex || '#1E293B'}
              fillOpacity={0.15}
              stroke="#334155"
              strokeWidth="0.1"
            />
          );
        })}

        {/* 4. Warehouse Zones Boundaries */}
        {zones.map((zone) => {
          // If zone has bounding or locations, we can draw structural boundaries
          return null;
        })}

        {/* 5. Location Storage Cells (Racks, Bins, Pallet Positions) */}
        {locations.map((loc) => {
          const coord = loc.coordinate;
          if (!coord.hasCoordinates || coord.x === null || coord.y === null) return null;

          const isSelected = selectedLocation?.locationId === loc.locationId;
          const isHotspotMember = hotspotLocationIds?.has(loc.locationId);

          // Physical footprint in meters (lengthCm / 100, widthCm / 100)
          // Default to 1.2m x 1.0m standard pallet rack bin if dimensions unspecified
          const locW = (coord.lengthCm ? coord.lengthCm / 100.0 : 1.2);
          const locH = (coord.widthCm ? coord.widthCm / 100.0 : 1.0);
          const posX = coord.x - locW / 2.0;
          const posY = coord.y - locH / 2.0;

          // Distinct stroke styling for selections and conditions
          let strokeColor = '#334155';
          let strokeWidth = 0.15;

          if (isSelected) {
            strokeColor = '#FFFFFF';
            strokeWidth = 0.5;
          } else if (isHotspotMember) {
            strokeColor = '#EF4444';
            strokeWidth = 0.4;
          } else if (loc.isOverCapacity) {
            strokeColor = '#DC2626';
            strokeWidth = 0.3;
          } else if (loc.isBlocked) {
            strokeColor = '#64748B';
          }

          return (
            <g
              key={loc.locationId}
              onClick={(e) => {
                e.stopPropagation();
                onSelectLocation(isSelected ? null : loc);
              }}
              onMouseEnter={(e) => {
                const rect = containerRef.current?.getBoundingClientRect();
                const clientX = e.clientX - (rect?.left || 0);
                const clientY = e.clientY - (rect?.top || 0);
                onHoverLocation(loc, clientX, clientY);
              }}
              onMouseLeave={() => onHoverLocation(null, 0, 0)}
              className="cursor-pointer transition-transform duration-75"
            >
              {/* Location Cell Rectangle */}
              <rect
                x={posX}
                y={posY}
                width={locW}
                height={locH}
                fill={loc.colorHex || '#64748B'}
                fillOpacity={isSelected ? 1.0 : isHotspotMember ? 0.95 : 0.85}
                stroke={strokeColor}
                strokeWidth={strokeWidth}
                rx={0.1}
              />

              {/* Special condition indicator icons (Dead stock, Blocked, Overcapacity) */}
              {loc.isOverCapacity && (
                <circle
                  cx={posX + locW - 0.2}
                  cy={posY + 0.2}
                  r={0.2}
                  fill="#EF4444"
                  stroke="#FFFFFF"
                  strokeWidth="0.05"
                />
              )}
              {loc.hasDeadStock && !loc.isOverCapacity && (
                <circle
                  cx={posX + locW - 0.2}
                  cy={posY + 0.2}
                  r={0.2}
                  fill="#F59E0B"
                  stroke="#FFFFFF"
                  strokeWidth="0.05"
                />
              )}
              {loc.isBlocked && (
                <line
                  x1={posX}
                  y1={posY}
                  x2={posX + locW}
                  y2={posY + locH}
                  stroke="#FFFFFF"
                  strokeWidth="0.15"
                />
              )}
            </g>
          );
        })}

        {/* 6. Hotspot Clusters Bounding Overlay (VIS-06) */}
        {hotspots.map((spot) => {
          const isSelected = selectedHotspot?.hotspotId === spot.hotspotId;
          const radius = Math.max(spot.boundingRadiusMeters, 2.5);
          return (
            <g key={spot.hotspotId} className="pointer-events-none">
              <circle
                cx={spot.centerXMeters}
                cy={spot.centerYMeters}
                r={radius}
                fill="#EF4444"
                fillOpacity={isSelected ? 0.25 : 0.12}
                stroke="#EF4444"
                strokeWidth={isSelected ? 0.6 : 0.3}
                strokeDasharray={isSelected ? undefined : '1.5 1'}
              />
              <text
                x={spot.centerXMeters}
                y={spot.centerYMeters - radius - 0.5}
                fill="#F87171"
                fontSize="1.8"
                fontWeight="bold"
                textAnchor="middle"
              >
                🔥 {spot.hotspotType} ({spot.contiguousLocationsCount} locs)
              </text>
            </g>
          );
        })}
      </svg>
    </div>
  );
};
