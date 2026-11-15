import { useState } from 'react';
import { slottingApi } from '../services/slottingApi';
import { RelocationApprovalRequestDto, RelocationApprovalResultDto } from '../types/slotting.types';

export function useRelocationApproval(onActionSuccess?: () => void) {
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [rejectionReasons, setRejectionReasons] = useState<Record<string, string>>({});
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [lastActionResult, setLastActionResult] = useState<RelocationApprovalResultDto | null>(null);

  const toggleSelect = (id: string) => {
    setSelectedIds(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const selectAll = (allIds: string[]) => {
    setSelectedIds(new Set(allIds));
  };

  const clearSelection = () => {
    setSelectedIds(new Set());
  };

  const setReasonForRejection = (id: string, reason: string) => {
    setRejectionReasons(prev => ({ ...prev, [id]: reason }));
  };

  const submitForReview = async (batchId: string) => {
    setIsSubmitting(true);
    setActionError(null);
    try {
      const res = await slottingApi.submitBatchForReview(batchId);
      onActionSuccess?.();
      return res;
    } catch (err: any) {
      setActionError(err?.message || 'Failed to submit batch for supervisory review');
      throw err;
    } finally {
      setIsSubmitting(false);
    }
  };

  const approveSelected = async (
    batchId: string,
    notes?: string
  ): Promise<RelocationApprovalResultDto> => {
    setIsSubmitting(true);
    setActionError(null);
    try {
      const approvedList = Array.from(selectedIds);
      const payload: RelocationApprovalRequestDto = {
        batchId,
        approvedRecommendationIds: approvedList,
        rejectedRecommendationIds: [],
        rejectionReasonsByRecommendationId: rejectionReasons,
        approvalNotes: notes
      };
      const res = await slottingApi.approveRelocationPlan(payload);
      setLastActionResult(res);
      clearSelection();
      onActionSuccess?.();
      return res;
    } catch (err: any) {
      setActionError(err?.message || 'Approval failed');
      throw err;
    } finally {
      setIsSubmitting(false);
    }
  };

  const rejectSelected = async (
    batchId: string,
    rejectedIds: string[],
    reasons: Record<string, string>,
    notes?: string
  ): Promise<RelocationApprovalResultDto> => {
    setIsSubmitting(true);
    setActionError(null);
    try {
      const payload: RelocationApprovalRequestDto = {
        batchId,
        approvedRecommendationIds: [],
        rejectedRecommendationIds: rejectedIds,
        rejectionReasonsByRecommendationId: reasons,
        approvalNotes: notes
      };
      const res = await slottingApi.approveRelocationPlan(payload);
      setLastActionResult(res);
      clearSelection();
      onActionSuccess?.();
      return res;
    } catch (err: any) {
      setActionError(err?.message || 'Rejection failed');
      throw err;
    } finally {
      setIsSubmitting(false);
    }
  };

  const cancelBatch = async (batchId: string, reason: string) => {
    setIsSubmitting(true);
    setActionError(null);
    try {
      const res = await slottingApi.cancelBatch(batchId, reason);
      clearSelection();
      onActionSuccess?.();
      return res;
    } catch (err: any) {
      setActionError(err?.message || 'Cancellation failed');
      throw err;
    } finally {
      setIsSubmitting(false);
    }
  };

  return {
    selectedIds,
    toggleSelect,
    selectAll,
    clearSelection,
    rejectionReasons,
    setReasonForRejection,
    isSubmitting,
    actionError,
    lastActionResult,
    submitForReview,
    approveSelected,
    rejectSelected,
    cancelBatch
  };
}
