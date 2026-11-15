import React, { useState, useMemo } from 'react';
import { PrioritizedRelocationRecommendationDto } from '../types/slotting.types';

interface PrioritizedRecommendationsTableProps {
  recommendations: PrioritizedRelocationRecommendationDto[];
  selectedIds: Set<string>;
  onToggleSelect: (id: string) => void;
  onSelectAll: (ids: string[]) => void;
  onClearSelection: () => void;
  onInspect: (recommendationId: string) => void;
  isLoading: boolean;
}

export const PrioritizedRecommendationsTable: React.FC<PrioritizedRecommendationsTableProps> = ({
  recommendations,
  selectedIds,
  onToggleSelect,
  onSelectAll,
  onClearSelection,
  onInspect,
  isLoading
}) => {
  const [searchTerm, setSearchTerm] = useState('');
  const [feasibilityFilter, setFeasibilityFilter] = useState<'all' | 'feasible' | 'violated'>('all');
  const [urgencyFilter, setUrgencyFilter] = useState<'all' | 'high' | 'medium' | 'low'>('all');
  const [minPriority, setMinPriority] = useState<number>(0);

  const filtered = useMemo(() => {
    return recommendations.filter(rec => {
      if (feasibilityFilter === 'feasible' && !rec.isOperationallyFeasible) return false;
      if (feasibilityFilter === 'violated' && rec.isOperationallyFeasible) return false;

      if (urgencyFilter === 'high' && rec.urgencyScore < 70) return false;
      if (urgencyFilter === 'medium' && (rec.urgencyScore < 40 || rec.urgencyScore >= 70)) return false;
      if (urgencyFilter === 'low' && rec.urgencyScore >= 40) return false;

      if (rec.priorityScore < minPriority) return false;

      if (searchTerm.trim()) {
        const term = searchTerm.toLowerCase();
        const skuMatch = rec.itemSku.toLowerCase().includes(term);
        const currMatch = rec.currentLocationCode.toLowerCase().includes(term);
        const propMatch = rec.proposedLocationCode.toLowerCase().includes(term);
        const reasonMatch = rec.reasonCode.toLowerCase().includes(term);
        if (!skuMatch && !currMatch && !propMatch && !reasonMatch) return false;
      }

      return true;
    });
  }, [recommendations, searchTerm, feasibilityFilter, urgencyFilter, minPriority]);

  const allFilteredSelected = filtered.length > 0 && filtered.every(r => selectedIds.has(r.recommendationId));
  const someFilteredSelected = filtered.some(r => selectedIds.has(r.recommendationId)) && !allFilteredSelected;

  const handleSelectAllToggle = () => {
    if (allFilteredSelected) {
      onClearSelection();
    } else {
      onSelectAll(filtered.map(r => r.recommendationId));
    }
  };

  const getPriorityBadgeClass = (score: number) => {
    if (score >= 80) return 'bg-emerald-500/20 text-emerald-300 border-emerald-500/30';
    if (score >= 60) return 'bg-blue-500/20 text-blue-300 border-blue-500/30';
    if (score >= 40) return 'bg-amber-500/20 text-amber-300 border-amber-500/30';
    return 'bg-rose-500/20 text-rose-300 border-rose-500/30';
  };

  const getUrgencyBadgeClass = (score: number) => {
    if (score >= 80) return 'bg-rose-500/20 text-rose-300 border-rose-500/30';
    if (score >= 50) return 'bg-amber-500/20 text-amber-300 border-amber-500/30';
    return 'bg-slate-700/50 text-slate-300 border-slate-600/50';
  };

  return (
    <div className="bg-slate-900 border border-slate-800 rounded-xl overflow-hidden shadow-xl flex flex-col">
      {/* Table Header Controls */}
      <div className="p-4 border-b border-slate-800 bg-slate-900/80 flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <span className="text-sm font-semibold text-white">
            Prioritized Relocation Recommendations ({filtered.length} of {recommendations.length})
          </span>
          <span className="text-xs text-slate-400 font-mono">
            Sorted: P &#8595; | B_net &#8595; | U &#8595; | SKU &#8593; | Dest &#8593;
          </span>
        </div>

        {/* Filters */}
        <div className="flex flex-wrap items-center gap-2 text-xs">
          {/* Search */}
          <div className="relative">
            <input
              type="text"
              placeholder="Search SKU or Location..."
              value={searchTerm}
              onChange={e => setSearchTerm(e.target.value)}
              className="bg-slate-800 border border-slate-700 rounded-lg pl-8 pr-3 py-1.5 text-xs text-slate-200 placeholder-slate-500 outline-none w-48"
            />
            <svg className="w-3.5 h-3.5 text-slate-400 absolute left-2.5 top-2.5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
            </svg>
          </div>

          {/* Feasibility Filter */}
          <select
            value={feasibilityFilter}
            onChange={e => setFeasibilityFilter(e.target.value as any)}
            className="bg-slate-800 border border-slate-700 rounded-lg px-2.5 py-1.5 text-slate-300 outline-none"
          >
            <option value="all">All Feasibility</option>
            <option value="feasible">Feasible Only (9 Gates Passed)</option>
            <option value="violated">Hard Gate Violated</option>
          </select>

          {/* Urgency Filter */}
          <select
            value={urgencyFilter}
            onChange={e => setUrgencyFilter(e.target.value as any)}
            className="bg-slate-800 border border-slate-700 rounded-lg px-2.5 py-1.5 text-slate-300 outline-none"
          >
            <option value="all">All Urgencies</option>
            <option value="high">High Urgency (&ge;70)</option>
            <option value="medium">Medium Urgency (40-69)</option>
            <option value="low">Low Urgency (&lt;40)</option>
          </select>

          {/* Min Priority Slider/Input */}
          <div className="flex items-center gap-1.5 bg-slate-800 border border-slate-700 rounded-lg px-2.5 py-1">
            <span className="text-[11px] text-slate-400">Min P:</span>
            <input
              type="number"
              min="0"
              max="100"
              value={minPriority}
              onChange={e => setMinPriority(Number(e.target.value))}
              className="w-12 bg-transparent text-xs text-slate-200 outline-none font-mono"
            />
          </div>
        </div>
      </div>

      {/* Table Body */}
      <div className="overflow-x-auto">
        <table className="w-full text-left text-xs">
          <thead className="bg-slate-800/60 text-slate-400 uppercase font-mono tracking-wider border-b border-slate-800 text-[10px]">
            <tr>
              <th className="py-3 px-3 w-10 text-center">
                <input
                  type="checkbox"
                  checked={allFilteredSelected}
                  ref={input => {
                    if (input) input.indeterminate = someFilteredSelected;
                  }}
                  onChange={handleSelectAllToggle}
                  className="rounded border-slate-700 text-emerald-500 focus:ring-0 cursor-pointer"
                />
              </th>
              <th className="py-3 px-3 w-12 text-center">Rank</th>
              <th className="py-3 px-4">Item SKU / Qty</th>
              <th className="py-3 px-4">Current &#8594; Proposed Location</th>
              <th className="py-3 px-3 text-right">Priority (P)</th>
              <th className="py-3 px-3 text-right">Urgency (U)</th>
              <th className="py-3 px-3 text-right">Suitability (S)</th>
              <th className="py-3 px-3 text-right">Net Benefit</th>
              <th className="py-3 px-3 text-right">Move Cost</th>
              <th className="py-3 px-3 text-center">Hard Gates</th>
              <th className="py-3 px-4 text-center">Action</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-800/60 text-slate-300">
            {isLoading ? (
              <tr>
                <td colSpan={11} className="py-12 text-center text-slate-500">
                  <div className="flex flex-col items-center gap-2">
                    <svg className="w-6 h-6 animate-spin text-emerald-500" fill="none" viewBox="0 0 24 24">
                      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z" />
                    </svg>
                    <span>Loading prioritized relocation candidates...</span>
                  </div>
                </td>
              </tr>
            ) : filtered.length === 0 ? (
              <tr>
                <td colSpan={11} className="py-12 text-center text-slate-500">
                  No recommendations match the specified criteria or filters.
                </td>
              </tr>
            ) : (
              filtered.map(rec => {
                const isSelected = selectedIds.has(rec.recommendationId);
                return (
                  <tr
                    key={rec.recommendationId}
                    className={`hover:bg-slate-800/40 transition-colors ${isSelected ? 'bg-emerald-950/20' : ''}`}
                  >
                    {/* Checkbox */}
                    <td className="py-3 px-3 text-center">
                      <input
                        type="checkbox"
                        checked={isSelected}
                        onChange={() => onToggleSelect(rec.recommendationId)}
                        className="rounded border-slate-700 text-emerald-500 focus:ring-0 cursor-pointer"
                      />
                    </td>

                    {/* Rank Badge */}
                    <td className="py-3 px-3 text-center">
                      <span className="font-mono text-xs font-semibold text-slate-400">
                        #{rec.rankingRank}
                      </span>
                    </td>

                    {/* Item SKU & Quantity */}
                    <td className="py-3 px-4">
                      <div className="font-medium text-white font-mono">{rec.itemSku}</div>
                      <div className="text-[11px] text-slate-400 mt-0.5">
                        Qty: <span className="text-slate-200 font-mono">{rec.quantity}</span> | {rec.reasonCode}
                      </div>
                    </td>

                    {/* Locations Source -> Destination */}
                    <td className="py-3 px-4">
                      <div className="flex items-center gap-2 font-mono">
                        <span className="px-2 py-0.5 rounded bg-slate-800 border border-slate-700 text-slate-300">
                          {rec.currentLocationCode}
                        </span>
                        <svg className="w-3.5 h-3.5 text-slate-500 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M14 5l7 7m0 0l-7 7m7-7H3" />
                        </svg>
                        <span className="px-2 py-0.5 rounded bg-emerald-950/60 border border-emerald-800 text-emerald-300 font-semibold">
                          {rec.proposedLocationCode}
                        </span>
                      </div>
                    </td>

                    {/* Priority Score P */}
                    <td className="py-3 px-3 text-right">
                      <span className={`inline-block px-2 py-0.5 rounded font-mono font-bold text-xs border ${getPriorityBadgeClass(rec.priorityScore)}`}>
                        {rec.priorityScore.toFixed(1)}
                      </span>
                    </td>

                    {/* Urgency Score U */}
                    <td className="py-3 px-3 text-right">
                      <div className="flex flex-col items-end">
                        <span className={`inline-block px-2 py-0.5 rounded font-mono font-semibold text-xs border ${getUrgencyBadgeClass(rec.urgencyScore)}`}>
                          {rec.urgencyScore.toFixed(1)}
                        </span>
                        {rec.urgencyReason && (
                          <span className="text-[10px] text-slate-400 mt-0.5 max-w-[120px] truncate" title={rec.urgencyReason}>
                            {rec.urgencyReason}
                          </span>
                        )}
                      </div>
                    </td>

                    {/* Suitability Score */}
                    <td className="py-3 px-3 text-right font-mono text-slate-300">
                      {rec.suitabilityScore.toFixed(1)}
                    </td>

                    {/* Net Benefit */}
                    <td className="py-3 px-3 text-right font-mono">
                      <span className={rec.netMonthlyBenefitUsd >= 0 ? 'text-emerald-400 font-semibold' : 'text-rose-400 font-semibold'}>
                        {rec.netMonthlyBenefitUsd >= 0 ? '+' : ''}${rec.netMonthlyBenefitUsd.toFixed(2)}/mo
                      </span>
                    </td>

                    {/* Move Cost */}
                    <td className="py-3 px-3 text-right font-mono text-slate-400">
                      ${rec.totalRelocationCostUsd.toFixed(2)}
                    </td>

                    {/* Feasibility (9 Hard Gates) */}
                    <td className="py-3 px-3 text-center">
                      {rec.isOperationallyFeasible ? (
                        <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 text-[10px] font-medium">
                          <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                          </svg>
                          Valid (9/9)
                        </span>
                      ) : (
                        <span
                          className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-rose-500/10 text-rose-400 border border-rose-500/20 text-[10px] font-medium cursor-help"
                          title={rec.violatedConstraints?.join('; ') || 'Hard constraint violated'}
                        >
                          <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                          </svg>
                          Violated
                        </span>
                      )}
                    </td>

                    {/* Action Button: Explainability Inspection */}
                    <td className="py-3 px-4 text-center">
                      <button
                        onClick={() => onInspect(rec.recommendationId)}
                        className="px-2.5 py-1 rounded bg-slate-800 hover:bg-slate-700 text-slate-200 border border-slate-700 text-[11px] font-medium transition-colors flex items-center gap-1.5 mx-auto"
                      >
                        <svg className="w-3.5 h-3.5 text-cyan-400" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z" />
                        </svg>
                        <span>Explain</span>
                      </button>
                    </td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
};
