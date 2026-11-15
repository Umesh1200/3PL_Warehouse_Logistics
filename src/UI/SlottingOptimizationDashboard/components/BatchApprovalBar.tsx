import React, { useState } from 'react';
import { SlottingBatchSummaryDto, PrioritizedRelocationRecommendationDto } from '../types/slotting.types';

interface BatchApprovalBarProps {
  batch: SlottingBatchSummaryDto | null;
  recommendations: PrioritizedRelocationRecommendationDto[];
  selectedIds: Set<string>;
  isSubmitting: boolean;
  actionError: string | null;
  onSubmitForReview: () => void;
  onApproveSelected: (notes?: string) => void;
  onRejectSelected: (reasons: Record<string, string>, notes?: string) => void;
  onCancelBatch: (reason: string) => void;
  onClearSelection: () => void;
}

export const BatchApprovalBar: React.FC<BatchApprovalBarProps> = ({
  batch,
  recommendations,
  selectedIds,
  isSubmitting,
  actionError,
  onSubmitForReview,
  onApproveSelected,
  onRejectSelected,
  onCancelBatch,
  onClearSelection
}) => {
  const [showNotesModal, setShowNotesModal] = useState(false);
  const [showRejectModal, setShowRejectModal] = useState(false);
  const [showCancelModal, setShowCancelModal] = useState(false);
  const [approvalNotes, setApprovalNotes] = useState('');
  const [rejectReason, setRejectReason] = useState('');
  const [cancelReason, setCancelReason] = useState('');

  if (!batch) return null;

  const selectedCount = selectedIds.size;
  const selectedMoves = recommendations.filter(r => selectedIds.has(r.recommendationId));
  const selectedCost = selectedMoves.reduce((s, r) => s + r.totalRelocationCostUsd, 0);
  const selectedSavings = selectedMoves.reduce((s, r) => s + r.netMonthlyBenefitUsd, 0);

  const canSubmitForReview = batch.status === 'Simulated';
  const canApproveOrReject = batch.status === 'PendingApproval' && selectedCount > 0;
  const canCancel = batch.status !== 'Approved' && batch.status !== 'Cancelled';

  const handleApproveConfirm = () => {
    setShowNotesModal(false);
    onApproveSelected(approvalNotes);
    setApprovalNotes('');
  };

  const handleRejectConfirm = () => {
    if (!rejectReason.trim()) return;
    const reasonsMap: Record<string, string> = {};
    selectedIds.forEach(id => {
      reasonsMap[id] = rejectReason.trim();
    });
    setShowRejectModal(false);
    onRejectSelected(reasonsMap, `Rejected with reason: ${rejectReason.trim()}`);
    setRejectReason('');
  };

  const handleCancelConfirm = () => {
    if (!cancelReason.trim()) return;
    setShowCancelModal(false);
    onCancelBatch(cancelReason.trim());
    setCancelReason('');
  };

  return (
    <>
      <div className="sticky bottom-0 z-40 bg-slate-900/95 backdrop-blur border-t border-slate-800 px-6 py-3 shadow-2xl">
        <div className="flex flex-col sm:flex-row items-center justify-between gap-3 max-w-7xl mx-auto">
          {/* Status & Selection Info */}
          <div className="flex items-center gap-4 text-xs">
            <div className="flex items-center gap-2">
              <span className="text-slate-400">Batch Status:</span>
              <span className="px-2 py-0.5 rounded font-mono font-semibold bg-slate-800 text-slate-200 border border-slate-700">
                {batch.status}
              </span>
            </div>

            <div className="h-4 w-px bg-slate-800" />

            <div className="flex items-center gap-2">
              <span className="text-slate-400">Selected:</span>
              <span className="font-mono font-bold text-white">
                {selectedCount} of {recommendations.length} moves
              </span>
              {selectedCount > 0 && (
                <button
                  onClick={onClearSelection}
                  className="text-slate-400 hover:text-slate-200 underline text-[11px]"
                >
                  Clear
                </button>
              )}
            </div>

            {selectedCount > 0 && (
              <>
                <div className="h-4 w-px bg-slate-800" />
                <div className="text-[11px] text-slate-400 font-mono">
                  Reloc Cost: <span className="text-rose-400 font-semibold">${selectedCost.toFixed(2)}</span> | Net Benefit: <span className="text-emerald-400 font-semibold">${selectedSavings.toFixed(2)}/mo</span>
                </div>
              </>
            )}
          </div>

          {/* Action Buttons */}
          <div className="flex items-center gap-2">
            {canSubmitForReview && (
              <button
                onClick={onSubmitForReview}
                disabled={isSubmitting}
                className="px-3.5 py-1.5 rounded-lg bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold shadow-md shadow-indigo-900/30 transition-colors disabled:opacity-50"
              >
                Submit for Review
              </button>
            )}

            {canApproveOrReject && (
              <>
                <button
                  onClick={() => setShowRejectModal(true)}
                  disabled={isSubmitting}
                  className="px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-rose-950/60 text-slate-300 hover:text-rose-300 border border-slate-700 text-xs font-medium transition-colors disabled:opacity-50 flex items-center gap-1.5"
                >
                  <svg className="w-3.5 h-3.5 text-rose-400" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                  </svg>
                  <span>Reject Selected ({selectedCount})</span>
                </button>

                <button
                  onClick={() => setShowNotesModal(true)}
                  disabled={isSubmitting}
                  className="px-4 py-1.5 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-semibold shadow-md shadow-emerald-900/30 transition-colors disabled:opacity-50 flex items-center gap-1.5"
                >
                  <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                  </svg>
                  <span>Approve Selected ({selectedCount})</span>
                </button>
              </>
            )}

            {canCancel && (
              <button
                onClick={() => setShowCancelModal(true)}
                disabled={isSubmitting}
                className="px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-rose-900/40 text-slate-400 hover:text-rose-300 border border-slate-700 text-xs font-medium transition-colors disabled:opacity-50"
              >
                Cancel Batch
              </button>
            )}
          </div>
        </div>

        {actionError && (
          <div className="max-w-7xl mx-auto mt-2 text-xs text-rose-400 bg-rose-950/40 border border-rose-800/50 rounded px-3 py-1">
            {actionError}
          </div>
        )}
      </div>

      {/* Approval Confirmation / Notes Modal */}
      {showNotesModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/70 backdrop-blur-sm">
          <div className="bg-slate-900 border border-slate-800 rounded-xl max-w-md w-full p-5 space-y-4 shadow-2xl">
            <h3 className="text-sm font-bold text-white">Confirm Selective Relocation Plan Approval</h3>
            <p className="text-xs text-slate-300">
              You are approving <strong>{selectedCount}</strong> relocation recommendations totaling an upfront cost of <strong>${selectedCost.toFixed(2)}</strong> and projected net monthly savings of <strong>${selectedSavings.toFixed(2)}/mo</strong>.
            </p>
            <div>
              <label className="block text-xs text-slate-400 mb-1 font-medium">Auditable Approval Notes:</label>
              <textarea
                value={approvalNotes}
                onChange={e => setApprovalNotes(e.target.value)}
                placeholder="e.g., Authorized for weekend shift reconfiguration."
                className="w-full bg-slate-800 border border-slate-700 rounded-lg p-2.5 text-xs text-slate-200 outline-none h-20 resize-none"
              />
            </div>
            <div className="flex justify-end gap-2 pt-2 border-t border-slate-800">
              <button
                onClick={() => setShowNotesModal(false)}
                className="px-3 py-1.5 rounded-lg bg-slate-800 text-slate-300 text-xs hover:bg-slate-700"
              >
                Cancel
              </button>
              <button
                onClick={handleApproveConfirm}
                disabled={isSubmitting}
                className="px-4 py-1.5 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-semibold"
              >
                Confirm Approval
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Rejection Modal */}
      {showRejectModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/70 backdrop-blur-sm">
          <div className="bg-slate-900 border border-slate-800 rounded-xl max-w-md w-full p-5 space-y-4 shadow-2xl">
            <h3 className="text-sm font-bold text-white text-rose-400">Reject Selected Recommendations</h3>
            <p className="text-xs text-slate-300">
              You are rejecting <strong>{selectedCount}</strong> recommendations. Please provide an auditable rejection justification.
            </p>
            <div>
              <label className="block text-xs text-slate-400 mb-1 font-medium">Rejection Reason (Required):</label>
              <textarea
                value={rejectReason}
                onChange={e => setRejectReason(e.target.value)}
                placeholder="e.g., Seasonal promotion ending next week; pickface reassignment not warranted."
                className="w-full bg-slate-800 border border-slate-700 rounded-lg p-2.5 text-xs text-slate-200 outline-none h-20 resize-none"
              />
            </div>
            <div className="flex justify-end gap-2 pt-2 border-t border-slate-800">
              <button
                onClick={() => setShowRejectModal(false)}
                className="px-3 py-1.5 rounded-lg bg-slate-800 text-slate-300 text-xs hover:bg-slate-700"
              >
                Back
              </button>
              <button
                onClick={handleRejectConfirm}
                disabled={isSubmitting || !rejectReason.trim()}
                className="px-4 py-1.5 rounded-lg bg-rose-600 hover:bg-rose-500 text-white text-xs font-semibold disabled:opacity-50"
              >
                Confirm Rejection
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Cancel Confirmation Modal */}
      {showCancelModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/70 backdrop-blur-sm">
          <div className="bg-slate-900 border border-slate-800 rounded-xl max-w-md w-full p-5 space-y-4 shadow-2xl">
            <h3 className="text-sm font-bold text-white text-rose-400">Cancel Recommendation Batch</h3>
            <p className="text-xs text-slate-300">
              Are you sure you want to cancel batch <strong>{batch.batchNumber}</strong>? All pending recommendations in this batch will be cancelled.
            </p>
            <div>
              <label className="block text-xs text-slate-400 mb-1 font-medium">Cancellation Reason (Required):</label>
              <textarea
                value={cancelReason}
                onChange={e => setCancelReason(e.target.value)}
                placeholder="e.g., Facility priority shifted to inbound receiving surge."
                className="w-full bg-slate-800 border border-slate-700 rounded-lg p-2.5 text-xs text-slate-200 outline-none h-20 resize-none"
              />
            </div>
            <div className="flex justify-end gap-2 pt-2 border-t border-slate-800">
              <button
                onClick={() => setShowCancelModal(false)}
                className="px-3 py-1.5 rounded-lg bg-slate-800 text-slate-300 text-xs hover:bg-slate-700"
              >
                Back
              </button>
              <button
                onClick={handleCancelConfirm}
                disabled={isSubmitting || !cancelReason.trim()}
                className="px-4 py-1.5 rounded-lg bg-rose-600 hover:bg-rose-500 text-white text-xs font-semibold disabled:opacity-50"
              >
                Confirm Cancellation
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
};
