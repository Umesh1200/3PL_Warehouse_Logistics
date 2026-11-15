// ============================================================================
// Phase 6.3.8: Slotting Optimization & Prioritization TypeScript Type Contracts
// Exact 1:1 Parity with Backend C# DTOs in Warehouse3PL.Application.Common.Models
// ============================================================================

export type NegativeBenefitPolicy =
  | 'AllowAll'
  | 'AllowIfHighUrgency'
  | 'DisqualifyNegativeBenefit';

export type SlottingBatchStatus =
  | 'Draft'
  | 'Simulated'
  | 'PendingApproval'
  | 'Approved'
  | 'PartiallyApproved'
  | 'Rejected'
  | 'Cancelled';

export type SlottingOptimizationGoal =
  | 'MinimizeTravelDistance'
  | 'ErgonomicGoldenZone'
  | 'MaximizeCubeUtilization'
  | 'BalanceAisleCongestion'
  | 'CoLocateAffinityProducts';

export interface SlottingPolicyDto {
  id: string;
  facilityId?: string | null;
  clientId?: string | null;
  zoneId?: string | null;
  name: string;
  description?: string | null;
  primaryGoal: SlottingOptimizationGoal | number;
  secondaryGoal?: SlottingOptimizationGoal | number | null;
  goldenZoneMinZMeters: number;
  goldenZoneMaxZMeters: number;
  goldenZoneShelfLevels: string;
  targetClassAPickFace: boolean;
  targetClassBIntermediate: boolean;
  targetClassCReserve: boolean;
  protectDeadStockFromUnnecessaryRelocation: boolean;
  maxRelocationsPerBatch: number;
  minLaborSavingsUsd: number;
  estimatedRelocationLaborMinutes: number;
  laborHourlyRateUsd: number;
  disruptionPenaltyWeight: number;
  weightTravelDistance: number;
  weightErgonomics: number;
  weightPickDensity: number;
  weightAbcAffinity: number;
  weightVelocityAffinity: number;
  weightCubeFill: number;
  weightProductAffinity: number;
  weightFinancialBenefit: number;
  weightOperationalUrgency: number;
  weightPlacementSuitability: number;
  weightDisruptionPenalty: number;
  negativeNetBenefitPolicy: NegativeBenefitPolicy;
  urgencyThresholdForNegativeBenefit: number;
  targetMonthlySavingsUsd: number;
  maxAcceptableRelocationCostUsd: number;
  handlingCostPerUnitUsd: number;
  heavyHandlingSurchargePerTierUsd: number;
  baseDisruptionCostUsd: number;
  goldenZoneOpportunityGainUsd: number;
  amortizationMonths: number;
  maxProjectionAgeDays: number;
  version: number;
  effectiveFromUtc: string;
  effectiveToUtc?: string | null;
  isActive: boolean;
}

export interface SlottingBatchSummaryDto {
  batchId: string;
  batchNumber: string;
  facilityId: string;
  clientId?: string | null;
  policyId: string;
  policyName: string;
  status: SlottingBatchStatus;
  snapshotDateUtc: string;
  totalProposedMoves: number;
  approvedMovesCount: number;
  rejectedMovesCount: number;
  estimatedMonthlyNetSavingsUsd: number;
  averageSuitabilityScore: number;
  approvedBy?: string | null;
  approvedAtUtc?: string | null;
  notes?: string | null;
}

export interface PrioritizedRelocationRecommendationDto {
  recommendationId: string;
  itemId: string;
  itemSku: string;
  clientId: string;
  currentLocationId: string;
  currentLocationCode: string;
  proposedLocationId: string;
  proposedLocationCode: string;
  quantity: number;
  grossMonthlySavingsUsd: number;
  handlingCostUsd: number;
  movementCostUsd: number;
  disruptionCostUsd: number;
  totalRelocationCostUsd: number;
  amortizedMonthlyCostUsd: number;
  netMonthlyBenefitUsd: number;
  urgencyScore: number;
  urgencyReason: string;
  suitabilityScore: number;
  disruptionPenaltyScore: number;
  disruptionMitigationScore: number;
  priorityScore: number;
  disruptionLevel: string;
  isOperationallyFeasible: boolean;
  rankingRank: number;
  reasonCode: string;
  violatedConstraints: string[];
}

export interface LocationComparisonDetailDto {
  locationId: string;
  locationCode: string;
  zoneCode: string;
  zoneType: number | string;
  shelfLevel: number;
  zCoord: number;
  distanceToDockMeters: number;
  isInGoldenZone: boolean;
  occupiedWeightKg: number;
  maxWeightKg: number;
  weightUtilizationPercent: number;
  occupiedVolumeCm3: number;
  maxVolumeCm3: number;
  volumeUtilizationPercent: number;
}

export interface SlottingRecommendationDetailDto {
  recommendationId: string;
  batchId: string;
  batchNumber: string;
  itemId: string;
  itemSku: string;
  itemDescription?: string | null;
  clientId: string;
  quantity: number;
  status: string;
  rejectionReason?: string | null;
  reasonCode: string;
  isOperationallyFeasible: boolean;
  isHardConstraintViolated: boolean;
  violatedConstraints: string[];

  // 6D Placement Suitability
  suitabilityScore: number;
  ergonomicScore: number;
  abcAffinityScore: number;
  velocityAffinityScore: number;
  pickDensityScore: number;
  travelDistanceDeltaMeters: number;
  productAffinityScore: number;

  // 4-Factor Operational Urgency
  urgencyScore: number;
  urgencyReason: string;
  velocityMismatchScore: number;
  goldenZoneCongestionScore: number;
  seasonalDemandScore: number;
  ergonomicHazardScore: number;

  // Transparent Economics Breakdown
  grossMonthlySavingsUsd: number;
  handlingCostUsd: number;
  movementCostUsd: number;
  disruptionCostUsd: number;
  totalRelocationCostUsd: number;
  amortizedMonthlyCostUsd: number;
  netMonthlyBenefitUsd: number;
  disruptionPenaltyScore: number;
  disruptionMitigationScore: number;
  priorityScore: number;
  disruptionLevel: string;

  // Destination Comparison
  currentLocation: LocationComparisonDetailDto;
  proposedLocation: LocationComparisonDetailDto;
}

export interface RelocationPrioritizationRequestDto {
  facilityId: string;
  clientId?: string | null;
  policyId?: string | null;
  shiftType?: string;
  maxMoves?: number | null;
  negativeNetBenefitPolicyOverride?: string | null;
  urgencyThresholdOverride?: number | null;
  asOfDateUtc?: string | null;
  notes?: string | null;
}

export interface RelocationPrioritizationResultDto {
  batchId: string;
  batchNumber: string;
  facilityId: string;
  clientId?: string | null;
  policyId: string;
  policyName: string;
  evaluatedAtUtc: string;
  totalCandidatesEvaluated: number;
  feasibleCandidatesCount: number;
  disqualifiedNegativeBenefitCount: number;
  highUrgencyNegativeBenefitCount: number;
  totalProjectedGrossMonthlySavingsUsd: number;
  totalProjectedOneTimeRelocationCostUsd: number;
  totalProjectedNetMonthlyBenefitUsd: number;
  physicalInventoryTransfersTriggered: number;
  ledgerMutationsOccurred: number;
  prioritizedRecommendations: PrioritizedRelocationRecommendationDto[];
}

export interface RelocationApprovalRequestDto {
  batchId: string;
  approvedRecommendationIds: string[];
  rejectedRecommendationIds?: string[];
  rejectionReasonsByRecommendationId?: Record<string, string>;
  approvalNotes?: string | null;
}

export interface RelocationApprovalResultDto {
  batchId: string;
  status: SlottingBatchStatus;
  approvedCount: number;
  rejectedCount: number;
  skippedCount: number;
  physicalInventoryTransfersTriggered: number;
  ledgerMutationsOccurred: number;
  processedAtUtc: string;
  approvedBy?: string | null;
  message: string;
}

export interface CancelBatchRequestDto {
  reason: string;
}
