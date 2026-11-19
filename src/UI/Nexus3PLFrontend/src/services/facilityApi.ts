import { FacilityDto } from '../types/auth';
import { apiClient } from './apiClient';

export const facilityApi = {
  /**
   * Retrieve authorized facilities accessible to the current authenticated caller.
   * Scoped to client tenant if caller is ClientUser; returns all active facilities for staff.
   */
  getAuthorizedFacilities: async (signal?: AbortSignal): Promise<FacilityDto[]> => {
    return apiClient<FacilityDto[]>('/api/v1/facilities', {
      method: 'GET',
      signal,
    });
  },

  /**
   * Retrieve a specific authorized facility by ID with server-side tenant boundary validation.
   */
  getFacilityById: async (facilityId: string, signal?: AbortSignal): Promise<FacilityDto> => {
    return apiClient<FacilityDto>(`/api/v1/facilities/${facilityId}`, {
      method: 'GET',
      signal,
    });
  },
};
