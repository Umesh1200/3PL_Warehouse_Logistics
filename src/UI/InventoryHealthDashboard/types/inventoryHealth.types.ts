// ============================================================================
// Phase 6.3.6.4: Interactive Dashboard Components & Spatial Heatmap Visualizer
// TypeScript Type Definitions & API Contracts (1:1 Parity with .NET C# Models)
// ============================================================================

export type LocationType =
  | 'Bin'
  | 'Rack'
  | 'PalletRack'
  | 'Cantilever'
  | 'FloorBulk'
  | 'Staging'
  | 'Dock'
  | 'QualityHold';

export type HeatmapVisualTier =
  | 'Empty'
  | 'Optimal'
  | 'Elevated'
  | 'Warning'
  | 'Blocked'
  | 'Critical'
  | 'Emergency';

export enum HeatmapLayerType {
  None = 0,
  CubeUtilization = 1 << 0,     // 1
  PalletOccupancy = 1 << 1,     // 2
  WeightCapacity = 1 << 2,      // 4
  InventoryAging = 1 << 3,      // 8
  SkuVelocity = 1 << 4,         // 16
  AbcClassification = 1 << 5,   // 32
  BlockedQuarantine = 1 << 6,   // 64
  StandardOverview = CubeUtilization | PalletOccupancy | InventoryAging | BlockedQuarantine,
  All = 127
}

export type HeatmapCoordinateStatus =
  | 'Valid'
  | 'MissingCoordinates'
  | 'OutOfBounds'
  | 'DuplicateCoordinates'
  | 'Interpolated';

export interface WarehouseCoordinateDto {
  x: number | null;
  y: number | null;
  z: number | null;
  lengthCm: number | null;
  widthCm: number | null;
  heightCm: number | null;
  status: HeatmapCoordinateStatus;
  hasCoordinates: boolean;
}

export interface FacilitySpatialEnvelopeDto {
  originXMeters: number;
  originYMeters: number;
  originZMeters: number;
  lengthMeters: number;
  widthMeters: number;
  clearHeightMeters: number;
  totalFloorAreaSqM: number;
  totalBuildingVolumeCbm: number;
  isAuthoritative: boolean;
}

export interface WarehouseBoundingBoxDto {
  minX: number;
  maxX: number;
  minY: number;
  maxY: number;
  minZ: number;
  maxZ: number;
  spanXMeters: number;
  spanYMeters: number;
  spanZMeters: number;
  hasValidBounds: boolean;
}

export interface WarehouseZoomPanBoundsDto {
  minZoom: number;
  maxZoom: number;
  defaultZoom: number;
  panPaddingMeters: number;
  boundingBox: WarehouseBoundingBoxDto;
}

export interface HeatmapLocationCellDto {
  locationId: string;
  locationCode: string;
  zoneId?: string | null;
  zoneCode: string;
  aisleCode?: string | null;
  rackCode?: string | null;
  shelfLevel?: number | null;
  binNumber?: number | null;
  locationType: LocationType;
  coordinate: WarehouseCoordinateDto;
  usableCbm: number;
  occupiedCbm: number;
  cubeUtilizationPercent: number;
  usablePallets: number;
  occupiedPallets: number;
  palletOccupancyPercent: number;
  occupiedWeightKg: number;
  weightUtilizationPercent: number;
  isOverCapacity: boolean;
  excessCbm: number;
  isBlocked: boolean;
  blockReason?: string | null;
  distinctSkuCount: number;
  totalUnits: number;
  hasMissingDimensions: boolean;
  deadStockUnits: number;
  deadStockValue: number;
  hasDeadStock: boolean;
  expiredUnits: number;
  hasExpiredStock: boolean;
  dominantVelocityTier?: string | null;
  dominantAbcClass?: string | null;
  visualTier: HeatmapVisualTier;
  colorHex: string;
  activeConditions: string[];
}

export interface SpatialGridCellDto {
  gridX: number;
  gridY: number;
  gridZ: number;
  centerXMeters: number;
  centerYMeters: number;
  centerZMeters: number;
  cellSizeMeters: number;
  locationCount: number;
  totalUsableCbm: number;
  totalOccupiedCbm: number;
  weightedCubeUtilizationPercent: number;
  totalUsablePallets: number;
  totalOccupiedPallets: number;
  weightedPalletOccupancyPercent: number;
  hasOverCapacity: boolean;
  hasBlocked: boolean;
  locationIds: string[];
  primaryColorHex: string;
}

export interface RackSpatialRollupDto {
  zoneId: string;
  zoneCode: string;
  aisleCode: string;
  rackCode: string;
  locationCount: number;
  totalUsableCbm: number;
  totalOccupiedCbm: number;
  weightedCubeUtilizationPercent: number;
  totalUsablePallets: number;
  totalOccupiedPallets: number;
  weightedPalletOccupancyPercent: number;
  dominantTier: HeatmapVisualTier;
  conditionCounts: Record<string, number>;
}

export interface AisleSpatialRollupDto {
  zoneId: string;
  zoneCode: string;
  aisleCode: string;
  rackCount: number;
  locationCount: number;
  totalUsableCbm: number;
  totalOccupiedCbm: number;
  weightedCubeUtilizationPercent: number;
  totalUsablePallets: number;
  totalOccupiedPallets: number;
  weightedPalletOccupancyPercent: number;
  dominantTier: HeatmapVisualTier;
  conditionCounts: Record<string, number>;
  racks: RackSpatialRollupDto[];
}

export interface ZoneSpatialRollupDto {
  zoneId: string;
  zoneCode: string;
  zoneName: string;
  locationCount: number;
  totalUsableCbm: number;
  totalOccupiedCbm: number;
  weightedCubeUtilizationPercent: number;
  totalUsablePallets: number;
  totalOccupiedPallets: number;
  weightedPalletOccupancyPercent: number;
  totalInventoryValue: number;
  dominantTier: HeatmapVisualTier;
  conditionCounts: Record<string, number>;
  aisles: AisleSpatialRollupDto[];
}

export interface SpatialHotspotDto {
  hotspotId: string;
  facilityId: string;
  zoneId: string;
  zoneCode: string;
  aisleCode: string;
  hotspotType: string;
  severityTier: HeatmapVisualTier;
  centerXMeters: number;
  centerYMeters: number;
  centerZMeters: number;
  boundingRadiusMeters: number;
  contiguousLocationsCount: number;
  totalUsableCbm: number;
  totalOccupiedCbm: number;
  weightedCubeUtilizationPercent: number;
  totalExcessCbm: number;
  totalDeadStockUnits: number;
  totalDeadStockValue: number;
  totalExpiredUnits: number;
  recommendedAction: string;
  locationIds: string[];
}

export interface SpatialHeatmapProjectionDto {
  facilityId: string;
  facilityCode: string;
  facilityName: string;
  snapshotDateUtc: string;
  gridCellSizeMeters: number;
  facilityEnvelope: FacilitySpatialEnvelopeDto;
  locationBoundingBox: WarehouseBoundingBoxDto;
  zoomPanBounds: WarehouseZoomPanBoundsDto;
  facilityTotalUsableCbm: number;
  facilityTotalOccupiedCbm: number;
  facilityWeightedCubeUtilizationPercent: number;
  facilityTotalUsablePallets: number;
  facilityTotalOccupiedPallets: number;
  facilityWeightedPalletOccupancyPercent: number;
  facilityTotalInventoryValue: number;
  totalLocationsEvaluated: number;
  outOfBoundsLocationsCount: number;
  missingCoordinatesLocationsCount: number;
  duplicateCoordinatesLocationsCount: number;
  zones: ZoneSpatialRollupDto[];
  gridCells: SpatialGridCellDto[];
  hotspots: SpatialHotspotDto[];
  facilityConditionSummary: Record<string, number>;
}

// Unified Health Dashboard DTOs (Phase 6.3.6.1)
export interface HealthCategoryScoreDto {
  categoryName: string;
  score: number; // 0 - 100
  weight: number; // 0.0 - 1.0
  weightedContribution: number;
  status: 'Healthy' | 'Caution' | 'Critical';
  summaryMetrics: Record<string, string>;
}

export interface HealthAlertDto {
  alertId: string;
  category: string;
  severity: 'Info' | 'Warning' | 'Critical' | 'Emergency';
  title: string;
  message: string;
  affectedCount: number;
  actionRecommendation: string;
}

export interface InventoryHealthDashboardDto {
  facilityId: string;
  facilityCode: string;
  facilityName: string;
  clientId?: string | null;
  clientName?: string | null;
  asOfDateUtc: string;
  compositeHealthScore: number;
  healthGrade: string; // 'A' | 'B' | 'C' | 'D' | 'F'
  healthCategoryScores: HealthCategoryScoreDto[];
  activeAlerts: HealthAlertDto[];
  totalLocations: number;
  totalActiveSkus: number;
  totalInventoryUnits: number;
  totalInventoryValue: number;
  cubeFillPercent: number;
  palletOccupancyPercent: number;
  deadStockRatioPercent: number;
  fastMoverRatioPercent: number;
  classAConsumptionValuePercent: number;
  projectionFreshnessMinutes: number;
}

// UI Navigation and Filter State
export interface HeatmapFilterState {
  zoneId?: string | null;
  aisleCode?: string | null;
  shelfLevel?: number | null; // VIS-01 Floor/Level Filter
  activeLayers: HeatmapLayerType; // VIS-03
  primaryColorLayer: HeatmapLayerType;
  searchKeyword: string;
  showSecondaryAlerts: boolean; // VIS-04
  minUtilization?: number;
  maxUtilization?: number;
  onlyHotspots: boolean;
}

export interface ZoomPanTransform {
  scale: number;
  translateX: number;
  translateY: number;
}
