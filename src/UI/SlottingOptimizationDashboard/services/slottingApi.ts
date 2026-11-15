import {
  SlottingPolicyDto,
  SlottingBatchSummaryDto,
  PrioritizedRelocationRecommendationDto,
  SlottingRecommendationDetailDto,
  RelocationPrioritizationRequestDto,
  RelocationPrioritizationResultDto,
  RelocationApprovalRequestDto,
  RelocationApprovalResultDto,
  CancelBatchRequestDto
} from '../types/slotting.types';

const BASE_URL = '/api/v1/analytics/slotting';

export const slottingApi = {
  /**
   * Retrieves the active slotting policy for a facility (and optional tenant).
   * Canonical route: GET /facility/{facilityId}/active-policy
   */
  async getActivePolicy(facilityId: string, clientId?: string | null): Promise<SlottingPolicyDto> {
    const params = new URLSearchParams();
    if (clientId) params.append('clientId', clientId);
    const queryString = params.toString() ? `?${params.toString()}` : '';
    const res = await fetch(`${BASE_URL}/facility/${facilityId}/active-policy${queryString}`);
    if (!res.ok) throw new Error(`Failed to fetch active policy: ${res.statusText}`);
    return res.json();
  },

  /**
   * Lists recommendation batches for a facility.
   * Canonical route: GET /facility/{facilityId}/batches
   */
  async getBatchesForFacility(
    facilityId: string,
    clientId?: string | null,
    limit: number = 20
  ): Promise<SlottingBatchSummaryDto[]> {
    const params = new URLSearchParams({ limit: limit.toString() });
    if (clientId) params.append('clientId', clientId);
    const res = await fetch(`${BASE_URL}/facility/${facilityId}/batches?${params.toString()}`);
    if (!res.ok) throw new Error(`Failed to fetch facility batches: ${res.statusText}`);
    return res.json();
  },

  /**
   * Triggers multi-factor relocation candidate prioritization.
   * Canonical route: POST /relocations/prioritize
   */
  async prioritizeRelocations(
    request: RelocationPrioritizationRequestDto
  ): Promise<RelocationPrioritizationResultDto> {
    const res = await fetch(`${BASE_URL}/relocations/prioritize`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request)
    });
    if (!res.ok) throw new Error(`Failed to prioritize relocations: ${res.statusText}`);
    return res.json();
  },

  /**
   * Retrieves a prioritized relocation plan by batch ID.
   * Canonical route: GET /relocations/{batchId}
   */
  async getPrioritizedPlan(
    batchId: string
  ): Promise<RelocationPrioritizationResultDto> {
    const res = await fetch(`${BASE_URL}/relocations/${batchId}`);
    if (!res.ok) throw new Error(`Failed to fetch prioritized plan: ${res.statusText}`);
    return res.json();
  },

  /**
   * Retrieves deep algorithmic explainability and destination comparison for a recommendation.
   * Canonical route: GET /recommendations/{recommendationId}/inspect
   */
  async inspectRecommendation(recommendationId: string): Promise<SlottingRecommendationDetailDto> {
    const res = await fetch(`${BASE_URL}/recommendations/${recommendationId}/inspect`);
    if (!res.ok) throw new Error(`Failed to inspect recommendation: ${res.statusText}`);
    return res.json();
  },

  /**
   * Submits a simulated recommendation batch for supervisory review (transitions to PendingApproval).
   * Canonical route: POST /relocations/{batchId}/submit-review
   */
  async submitBatchForReview(
    batchId: string
  ): Promise<{ batchId: string; status: string }> {
    const res = await fetch(`${BASE_URL}/relocations/${batchId}/submit-review`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' }
    });
    if (!res.ok) throw new Error(`Failed to submit batch for review: ${res.statusText}`);
    return res.json();
  },

  /**
   * Administratively approves or rejects recommendations in a batch.
   * Canonical route: POST /relocations/approve
   */
  async approveRelocationPlan(
    request: RelocationApprovalRequestDto
  ): Promise<RelocationApprovalResultDto> {
    const res = await fetch(`${BASE_URL}/relocations/approve`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request)
    });
    if (!res.ok) throw new Error(`Failed to approve relocation plan: ${res.statusText}`);
    return res.json();
  },

  /**
   * Cancels a recommendation batch prior to execution.
   * Canonical route: POST /relocations/{batchId}/cancel
   */
  async cancelBatch(
    batchId: string,
    reason: string
  ): Promise<{ batchId: string; status: string; reason: string }> {
    const payload: CancelBatchRequestDto = { reason };
    const res = await fetch(`${BASE_URL}/relocations/${batchId}/cancel`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    if (!res.ok) throw new Error(`Failed to cancel batch: ${res.statusText}`);
    return res.json();
  }
};
