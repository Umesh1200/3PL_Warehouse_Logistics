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
};
