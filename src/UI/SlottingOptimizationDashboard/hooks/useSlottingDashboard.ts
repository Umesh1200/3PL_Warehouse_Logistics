import { useState, useEffect, useCallback } from 'react';
import {
  SlottingPolicyDto,
  SlottingBatchSummaryDto,
  PrioritizedRelocationRecommendationDto,
  SlottingRecommendationDetailDto,
  RelocationPrioritizationRequestDto
} from '../types/slotting.types';
import { slottingApi } from '../services/slottingApi';

export function useSlottingDashboard(initialFacilityId: string, initialClientId?: string | null) {
  const [facilityId, setFacilityId] = useState<string>(initialFacilityId);
  const [clientId, setClientId] = useState<string | null>(initialClientId ?? null);
  const [activePolicy, setActivePolicy] = useState<SlottingPolicyDto | null>(null);
  const [batches, setBatches] = useState<SlottingBatchSummaryDto[]>([]);
  const [selectedBatchId, setSelectedBatchId] = useState<string | null>(null);
  const [recommendations, setRecommendations] = useState<PrioritizedRelocationRecommendationDto[]>([]);
  const [inspectedDetail, setInspectedDetail] = useState<SlottingRecommendationDetailDto | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(false);
  const [isPrioritizing, setIsPrioritizing] = useState<boolean>(false);
  const [isInspecting, setIsInspecting] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  const loadBatchesAndPolicy = useCallback(async () => {
    if (!facilityId) return;
    setIsLoading(true);
    setError(null);
    try {
      const [policyRes, batchesRes] = await Promise.allSettled([
        slottingApi.getActivePolicy(facilityId, clientId),
        slottingApi.getBatchesForFacility(facilityId, clientId, 25)
      ]);

      const errors: string[] = [];

      if (policyRes.status === 'fulfilled') {
        setActivePolicy(policyRes.value);
      } else {
        setActivePolicy(null);
        errors.push(`Active Policy: ${policyRes.reason?.message || 'Unavailable'}`);
      }

      if (batchesRes.status === 'fulfilled') {
        const batchesData = batchesRes.value;
        setBatches(batchesData);
        if (batchesData.length > 0 && !selectedBatchId) {
          setSelectedBatchId(batchesData[0].batchId);
        }
      } else {
        setBatches([]);
        errors.push(`Facility Batches: ${batchesRes.reason?.message || 'Unavailable'}`);
      }

      if (errors.length > 0) {
        setError(errors.join(' | '));
      }
    } catch (err: any) {
      setError(err?.message || 'Failed to load slotting data');
    } finally {
      setIsLoading(false);
    }
  }, [facilityId, clientId, selectedBatchId]);

  useEffect(() => {
    loadBatchesAndPolicy();
  }, [loadBatchesAndPolicy]);

  const loadBatchPlan = useCallback(async (batchId: string) => {
    if (!facilityId || !batchId) return;
    setIsLoading(true);
    setError(null);
    try {
      const planResult = await slottingApi.getPrioritizedPlan(batchId);
      setRecommendations(planResult.prioritizedRecommendations || []);
    } catch (err: any) {
      setError(err?.message || 'Failed to load batch recommendations plan');
    } finally {
      setIsLoading(false);
    }
  }, [facilityId]);

  useEffect(() => {
    if (selectedBatchId) {
      loadBatchPlan(selectedBatchId);
    } else {
      setRecommendations([]);
    }
  }, [selectedBatchId, loadBatchPlan]);

  const runPrioritization = async (overrides?: Partial<RelocationPrioritizationRequestDto>) => {
    if (!facilityId) return;
    setIsPrioritizing(true);
    setError(null);
    try {
      const req: RelocationPrioritizationRequestDto = {
        facilityId,
        clientId,
        policyId: activePolicy?.id,
        asOfDateUtc: new Date().toISOString(),
        shiftType: 'Standard',
        ...overrides
      };
      const result = await slottingApi.prioritizeRelocations(req);
      await loadBatchesAndPolicy();
      setSelectedBatchId(result.batchId);
      setRecommendations(result.prioritizedRecommendations);
      return result;
    } catch (err: any) {
      setError(err?.message || 'Prioritization run failed');
      throw err;
    } finally {
      setIsPrioritizing(false);
    }
  };

  const inspectRecommendation = async (recommendationId: string) => {
    setIsInspecting(true);
    try {
      const detail = await slottingApi.inspectRecommendation(recommendationId);
      setInspectedDetail(detail);
    } catch (err: any) {
      setError(err?.message || 'Inspection failed');
    } finally {
      setIsInspecting(false);
    }
  };

  const closeInspectionModal = () => {
    setInspectedDetail(null);
  };

  const selectedBatch = batches.find(b => b.batchId === selectedBatchId) || null;

  return {
    facilityId,
    setFacilityId,
    clientId,
    setClientId,
    activePolicy,
    batches,
    selectedBatchId,
    setSelectedBatchId,
    selectedBatch,
    recommendations,
    inspectedDetail,
    isLoading,
    isPrioritizing,
    isInspecting,
    error,
    refreshBatches: loadBatchesAndPolicy,
    runPrioritization,
    inspectRecommendation,
    closeInspectionModal
  };
}
