import { apiClient } from './apiClient';
import {
  ControlTowerSummaryDto,
  ControlTowerExecutiveOverviewDto,
  DockYardOperationalStatusDto,
  FacilityBottleneckAnalysisDto,
  OrderFulfillmentSlaDashboardDto,
} from '../types/controlTower';

export const controlTowerApi = {
  getSummary: (facilityId?: string | null, asOfUtc?: string, signal?: AbortSignal) => {
    const params = new URLSearchParams();
    if (facilityId) params.append('facilityId', facilityId);
    if (asOfUtc) params.append('asOfUtc', asOfUtc);
    const qs = params.toString();
    return apiClient<ControlTowerSummaryDto>(`/api/v1/control-tower/summary${qs ? `?${qs}` : ''}`, { signal });
  },

  getExecutiveOverview: (facilityId?: string | null, asOfUtc?: string, signal?: AbortSignal) => {
    const params = new URLSearchParams();
    if (facilityId) params.append('facilityId', facilityId);
    if (asOfUtc) params.append('asOfUtc', asOfUtc);
    const qs = params.toString();
    return apiClient<ControlTowerExecutiveOverviewDto>(`/api/v1/control-tower/overview${qs ? `?${qs}` : ''}`, { signal });
  },

  getDockYardStatus: (facilityId: string, asOfUtc?: string, signal?: AbortSignal) => {
    const params = new URLSearchParams({ facilityId });
    if (asOfUtc) params.append('asOfUtc', asOfUtc);
    return apiClient<DockYardOperationalStatusDto>(`/api/v1/control-tower/dock-yard?${params.toString()}`, { signal });
  },

  getBottlenecks: (facilityId: string, asOfUtc?: string, signal?: AbortSignal) => {
    const params = new URLSearchParams({ facilityId });
    if (asOfUtc) params.append('asOfUtc', asOfUtc);
    return apiClient<FacilityBottleneckAnalysisDto>(`/api/v1/control-tower/bottlenecks?${params.toString()}`, { signal });
  },

  getSlaDashboard: (facilityId?: string | null, clientId?: string | null, asOfUtc?: string, signal?: AbortSignal) => {
    const params = new URLSearchParams();
    if (facilityId) params.append('facilityId', facilityId);
    if (clientId) params.append('clientId', clientId);
    if (asOfUtc) params.append('asOfUtc', asOfUtc);
    const qs = params.toString();
    return apiClient<OrderFulfillmentSlaDashboardDto>(`/api/v1/control-tower/sla${qs ? `?${qs}` : ''}`, { signal });
  },
};
