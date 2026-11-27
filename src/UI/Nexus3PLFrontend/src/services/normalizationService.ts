import { apiClient } from './apiClient';

export type CountryNormalizationStatusValue = 'Normalized' | 'PendingNormalization';

export interface CountryNormalizationQuarantineItem {
  auditId: string;
  entityType: string;
  entityId: string;
  fieldName: string;
  originalCountryValue: string | null;
  normalizedCountryCode: string | null;
  normalizationStatus: CountryNormalizationStatusValue;
  preservedHistoricalValue: boolean;
  wasInferredFromParent: boolean;
  inferenceSource: string | null;
  reasonCode?: string;
  reason: string;
  facilityId: string | null;
  facilityCode: string | null;
  clientId: string | null;
  correlationBatchId: string;
  recordedAtUtc: string;
}

export interface PagedNormalizationQuarantineResponse {
  items: CountryNormalizationQuarantineItem[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  pendingCount: number;
  normalizedCount: number;
}

export interface NormalizationQuarantineFilters {
  status?: CountryNormalizationStatusValue | 'All';
  entityType?: string;
  facilityId?: string;
  search?: string;
  pageNumber?: number;
  pageSize?: number;
}

export async function fetchNormalizationQuarantine(
  filters: NormalizationQuarantineFilters = {},
  signal?: AbortSignal
): Promise<PagedNormalizationQuarantineResponse> {
  const params = new URLSearchParams();
  if (filters.status === 'All') {
    params.set('includeAllStatuses', 'true');
  } else if (filters.status) {
    params.set('status', filters.status);
  } else {
    params.set('status', 'PendingNormalization');
  }

  if (filters.entityType) params.set('entityType', filters.entityType);
  if (filters.facilityId) params.set('facilityId', filters.facilityId);
  if (filters.search?.trim()) params.set('search', filters.search.trim());
  params.set('pageNumber', String(filters.pageNumber ?? 1));
  params.set('pageSize', String(filters.pageSize ?? 25));

  return await apiClient<PagedNormalizationQuarantineResponse>(
    `/api/v1/reference/normalization-quarantine?${params.toString()}`,
    { signal }
  );
}
