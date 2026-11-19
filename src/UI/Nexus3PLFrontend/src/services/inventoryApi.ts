import { apiClient } from './apiClient';
import { StockOnHandDto, StockOnHandQuery, TransferInventoryRequest } from '../types/inventory';
import { PagedResult } from '../types/auth';

export const inventoryApi = {
  getStockOnHand: (query?: StockOnHandQuery, signal?: AbortSignal) => {
    const params = new URLSearchParams();
    if (query?.facilityId) params.append('facilityId', query.facilityId);
    if (query?.clientId) params.append('clientId', query.clientId);
    if (query?.itemId) params.append('itemId', query.itemId);
    if (query?.locationId) params.append('locationId', query.locationId);
    if (query?.searchSku) params.append('searchSku', query.searchSku);
    if (query?.pageNumber) params.append('pageNumber', String(query.pageNumber));
    if (query?.pageSize) params.append('pageSize', String(query.pageSize));

    const qs = params.toString();
    const endpoint = `/api/v1/inventory/stock${qs ? `?${qs}` : ''}`;
    return apiClient<PagedResult<StockOnHandDto>>(endpoint, { signal });
  },

  transferStock: (data: TransferInventoryRequest, signal?: AbortSignal) => {
    return apiClient<{ transactionId: string; message: string }>('/api/v1/inventory/transfer', {
      method: 'POST',
      body: JSON.stringify(data),
      signal,
    });
  },
};
