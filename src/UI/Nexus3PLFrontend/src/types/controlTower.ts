/**
 * Nexus3PL Control Tower Types
 * Strictly aligned with backend DTOs from Warehouse3PL.Application.Common.Models.AnalyticsModels
 */

export type BottleneckSeverity = 'Normal' | 'Minor' | 'Moderate' | 'Critical';

export interface BottleneckStageHealthDto {
  stage: number | string;
  severity: BottleneckSeverity;
  queueDepth: number;
  metricValue: number;
  metricUnit: string;
  summaryMessage: string;
  recommendedAction: string;
}

export interface FacilityBottleneckAnalysisDto {
  facilityId: string;
  evaluatedAtUtc: string;
  receivingCongestion: BottleneckStageHealthDto;
  pickingBacklog: BottleneckStageHealthDto;
  packingDelays: BottleneckStageHealthDto;
  dispatchDelays: BottleneckStageHealthDto;
  overallHealthScore: number;
}

export interface DockYardOperationalStatusDto {
  totalYardSpotsCount: number;
  occupiedYardSpotsCount: number;
  yardOccupancyPercent: number;
  trucksCheckedInCount: number;
  trucksDepartedCount: number;
  trucksWaitingInYardCount: number;
  trucksAtDockCount: number;
  averageYardWaitTimeMinutes: number;
  averageDockServiceTimeMinutes: number;
  averageTotalTurnTimeMinutes: number;
}

export interface OrderSlaItemDto {
  orderId: string;
  orderNumber: string;
  clientId: string;
  facilityId: string;
  customerName: string;
  serviceLevel: string | number;
  carrierCode?: string | null;
  orderDateUtc: string;
  requiredShipDateUtc?: string | null;
  remainingMinutes: number;
  slaStatus: string | number;
  currentOrderStatus: string | number;
}

export interface OrderFulfillmentSlaDashboardDto {
  facilityId?: string | null;
  clientId?: string | null;
  asOfUtc: string;
  onTrackCount: number;
  approachingDeadlineCount: number;
  atRiskCount: number;
  breachedCount: number;
  completedWithinSlaCount: number;
  completedOutsideSlaCount: number;
  slaAdherencePercent: number;
  urgentOrders: OrderSlaItemDto[];
}

export interface ControlTowerSummaryDto {
  facilityId?: string | null;
  asOfUtc: string;

  // Real-time Throughput (Today)
  ordersReceivedToday: number;
  ordersDispatchedToday: number;
  packagesDispatchedToday: number;
  unitsPickedToday: number;
  cartonsPackedToday: number;
  inboundPalletsReceivedToday: number;
  putawayCompletedToday: number;
  returnsInspectedToday: number;

  // Rates & Velocity
  unitsPerLaborHourToday: number;
  averageFulfillmentCycleTimeHours: number;
  sameDayDispatchAdherencePercent: number;

  // Commercial & Carrier
  totalBilledRevenueToday: number;
  netFreightMarginToday: number;
  onTimeDeliveryRatePercent: number;
  activeDeliveryExceptionsCount: number;

  // Capacity
  palletPositionOccupancyPercent: number;
  volumetricCubeFillPercent: number;
}

export interface ControlTowerExecutiveOverviewDto {
  facilityId?: string | null;
  asOfUtc: string;
  summary: ControlTowerSummaryDto;
  slaDashboard: OrderFulfillmentSlaDashboardDto;
  dockYardStatus: DockYardOperationalStatusDto;
  bottlenecks: FacilityBottleneckAnalysisDto;
  freshnessMetadata?: {
    lastProjectionUpdateUtc: string;
    freshnessStatus: string;
  };
}
