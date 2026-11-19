import { apiClient } from './apiClient';
import {
  ReceivingSessionDto,
  ReceiptLineDto,
  ReceivingDiscrepancyDto,
  StartReceivingSessionRequest,
  ScanReceiptLineRequest,
  RecordReceivingDiscrepancyRequest,
} from '../types/inbound';
import { PagedResult } from '../types/auth';

export interface ReceivingSessionQueryParams {
  facilityId?: string | null;
  clientId?: string | null;
  dockDoorId?: string | null;
  status?: number | null;
  dateFrom?: string | null;
  dateTo?: string | null;
  searchTerm?: string | null;
  pageNumber?: number;
  pageSize?: number;
}

export const receivingApi = {
  getSessions: (query?: ReceivingSessionQueryParams, signal?: AbortSignal) => {
    const params = new URLSearchParams();
    if (query?.facilityId) params.append('facilityId', query.facilityId);
    if (query?.clientId) params.append('clientId', query.clientId);
    if (query?.dockDoorId) params.append('dockDoorId', query.dockDoorId);
    if (query?.status !== undefined && query?.status !== null) params.append('status', String(query.status));
    if (query?.dateFrom) params.append('dateFrom', query.dateFrom);
    if (query?.dateTo) params.append('dateTo', query.dateTo);
    if (query?.searchTerm) params.append('searchTerm', query.searchTerm);
    if (query?.pageNumber) params.append('pageNumber', String(query.pageNumber));
    if (query?.pageSize) params.append('pageSize', String(query.pageSize));

    const qs = params.toString();
    const endpoint = `/api/v1/receiving/sessions${qs ? `?${qs}` : ''}`;
    return apiClient<PagedResult<ReceivingSessionDto>>(endpoint, { signal });
  },

  getSessionById: (id: string, signal?: AbortSignal) => {
    return apiClient<ReceivingSessionDto>(`/api/v1/receiving/sessions/${id}`, { signal });
  },

  startSession: (data: StartReceivingSessionRequest, signal?: AbortSignal) => {
    return apiClient<ReceivingSessionDto>('/api/v1/receiving/sessions/start', {
      method: 'POST',
      body: JSON.stringify(data),
      signal,
    });
  },

  scanLine: (sessionId: string, data: ScanReceiptLineRequest, signal?: AbortSignal) => {
    return apiClient<ReceiptLineDto>(`/api/v1/receiving/sessions/${sessionId}/lines`, {
      method: 'POST',
      body: JSON.stringify(data),
      signal,
    });
  },

  recordDiscrepancy: (sessionId: string, data: RecordReceivingDiscrepancyRequest, signal?: AbortSignal) => {
    return apiClient<ReceivingDiscrepancyDto>(`/api/v1/receiving/sessions/${sessionId}/discrepancies`, {
      method: 'POST',
      body: JSON.stringify(data),
      signal,
    });
  },

  completeSession: (sessionId: string, signal?: AbortSignal) => {
    return apiClient<ReceivingSessionDto>(`/api/v1/receiving/sessions/${sessionId}/complete`, {
      method: 'PATCH',
      signal,
    });
  },
};
