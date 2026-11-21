import { apiClient } from './apiClient';
import {
  PutawayTaskDto,
  GeneratePutawayTasksRequest,
  ConfirmPutawayTaskRequest,
  ReportExceptionRequest,
  AssignTaskRequest,
  GetPutawayTasksQuery,
} from '../types/inbound';
import { PagedResult } from '../types/auth';

export const putawayApi = {
  getTasks: (query?: GetPutawayTasksQuery, signal?: AbortSignal) => {
    const params = new URLSearchParams();
    if (query?.facilityId) params.append('facilityId', query.facilityId);
    if (query?.clientId) params.append('clientId', query.clientId);
    if (query?.statuses && query.statuses.length > 0) {
      query.statuses.forEach((s) => params.append('statuses', String(s)));
    } else if (query?.status !== undefined && query?.status !== null) {
      params.append('status', String(query.status));
    }
    if (query?.itemId) params.append('itemId', query.itemId);
    if (query?.searchTerm) params.append('searchTerm', query.searchTerm);
    if (query?.pageNumber) params.append('pageNumber', String(query.pageNumber));
    if (query?.pageSize) params.append('pageSize', String(query.pageSize));

    const qs = params.toString();
    const endpoint = `/api/v1/putaway/tasks${qs ? `?${qs}` : ''}`;
    return apiClient<PagedResult<PutawayTaskDto>>(endpoint, { signal });
  },

  getNextTask: (facilityId: string, operatorUserId?: string, signal?: AbortSignal) => {
    const params = new URLSearchParams({ facilityId });
    if (operatorUserId) params.append('operatorUserId', operatorUserId);
    return apiClient<PutawayTaskDto | null>(`/api/v1/putaway/tasks/next?${params.toString()}`, { signal });
  },

  generateTasks: (data: GeneratePutawayTasksRequest, signal?: AbortSignal) => {
    return apiClient<PutawayTaskDto[]>('/api/v1/putaway/tasks/generate', {
      method: 'POST',
      body: JSON.stringify(data),
      signal,
    });
  },

  assignTask: (taskId: string, data: AssignTaskRequest, signal?: AbortSignal) => {
    return apiClient<PutawayTaskDto>(`/api/v1/putaway/tasks/${taskId}/assign`, {
      method: 'POST',
      body: JSON.stringify(data),
      signal,
    });
  },

  confirmTask: (taskId: string, data: ConfirmPutawayTaskRequest, signal?: AbortSignal) => {
    return apiClient<PutawayTaskDto>(`/api/v1/putaway/tasks/${taskId}/confirm`, {
      method: 'POST',
      body: JSON.stringify(data),
      signal,
    });
  },

  reportException: (taskId: string, data: ReportExceptionRequest, signal?: AbortSignal) => {
    return apiClient<PutawayTaskDto>(`/api/v1/putaway/tasks/${taskId}/exception`, {
      method: 'POST',
      body: JSON.stringify(data),
      signal,
    });
  },
};
