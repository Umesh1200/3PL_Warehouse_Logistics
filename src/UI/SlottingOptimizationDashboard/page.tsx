import React, { useState } from 'react';
import { useSlottingDashboard } from './hooks/useSlottingDashboard';
import { useRelocationApproval } from './hooks/useRelocationApproval';
import { SlottingDashboardHeader } from './components/SlottingDashboardHeader';
import { SlottingKpiCards } from './components/SlottingKpiCards';
import { PrioritizedRecommendationsTable } from './components/PrioritizedRecommendationsTable';
import { RecommendationDetailModal } from './components/RecommendationDetailModal';
import { BatchApprovalBar } from './components/BatchApprovalBar';
import { PolicyConfigModal } from './components/PolicyConfigModal';

interface SlottingOptimizationDashboardProps {
  initialFacilityId?: string;
  initialClientId?: string | null;
}

export const SlottingOptimizationDashboard: React.FC<SlottingOptimizationDashboardProps> = ({
  initialFacilityId = '11111111-1111-1111-1111-111111111111',
  initialClientId = null
}) => {
  const [isPolicyModalOpen, setIsPolicyModalOpen] = useState(false);

  const {
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
    error,
    refreshBatches,
    runPrioritization,
    inspectRecommendation,
    closeInspectionModal
  } = useSlottingDashboard(initialFacilityId, initialClientId);

  const {
    selectedIds,
    toggleSelect,
    selectAll,
    clearSelection,
    isSubmitting,
    actionError,
    submitForReview,
    approveSelected,
    rejectSelected,
    cancelBatch
  } = useRelocationApproval(refreshBatches);

  return (
    <div className="min-h-screen bg-slate-950 text-slate-100 flex flex-col font-sans">
      {/* Dashboard Top Header */}
      <SlottingDashboardHeader
        facilityId={facilityId}
        clientId={clientId}
        activePolicy={activePolicy}
        batches={batches}
        selectedBatchId={selectedBatchId}
        isLoading={isLoading}
        isPrioritizing={isPrioritizing}
        onFacilityChange={setFacilityId}
        onClientChange={setClientId}
        onBatchSelect={setSelectedBatchId}
        onRunPrioritization={() => runPrioritization()}
        onRefresh={refreshBatches}
        onOpenPolicyModal={() => setIsPolicyModalOpen(true)}
      />

      {/* Main View Area */}
      <main className="flex-1 p-6 space-y-6 max-w-7xl w-full mx-auto">
        {/* Error Notification */}
        {error && (
          <div className="bg-rose-950/40 border border-rose-800/60 rounded-xl p-4 text-xs text-rose-300 flex items-center justify-between">
            <div className="flex items-center gap-2">
              <svg className="w-5 h-5 text-rose-400 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
              </svg>
              <span>{error}</span>
            </div>
            <button
              onClick={refreshBatches}
              className="px-2.5 py-1 rounded bg-rose-900/40 hover:bg-rose-900/60 text-white font-medium text-xs transition-colors"
            >
              Retry
            </button>
          </div>
        )}

        {/* Top KPI Cards with Projected vs Realized Disclaimers */}
        <SlottingKpiCards
          batch={selectedBatch}
          recommendations={recommendations}
        />

        {/* Prioritized Relocation Recommendations Table */}
        <PrioritizedRecommendationsTable
          recommendations={recommendations}
          selectedIds={selectedIds}
          onToggleSelect={toggleSelect}
          onSelectAll={selectAll}
          onClearSelection={clearSelection}
          onInspect={inspectRecommendation}
          isLoading={isLoading}
        />
      </main>

      {/* Sticky Administrative Review & Approval Bar */}
      <BatchApprovalBar
        batch={selectedBatch}
        recommendations={recommendations}
        selectedIds={selectedIds}
        isSubmitting={isSubmitting}
        actionError={actionError}
        onSubmitForReview={() => selectedBatchId && submitForReview(selectedBatchId)}
        onApproveSelected={notes => selectedBatchId && approveSelected(selectedBatchId, notes)}
        onRejectSelected={(reasons, notes) => selectedBatchId && rejectSelected(selectedBatchId, Array.from(selectedIds), reasons, notes)}
        onCancelBatch={reason => selectedBatchId && cancelBatch(selectedBatchId, reason)}
        onClearSelection={clearSelection}
      />

      {/* Deep Algorithmic Explainability Inspection Modal */}
      <RecommendationDetailModal
        detail={inspectedDetail}
        isOpen={!!inspectedDetail}
        onClose={closeInspectionModal}
      />

      {/* Slotting Policy Configuration Modal */}
      <PolicyConfigModal
        policy={activePolicy}
        isOpen={isPolicyModalOpen}
        onClose={() => setIsPolicyModalOpen(false)}
      />
    </div>
  );
};

export default SlottingOptimizationDashboard;
