import { apiClient } from './apiClient';
import { ClientDto, PagedResult } from '../types/auth';
import {
  AllocationExecutionResultDto,
  AllocationStrategyType,
  ConfirmPickTaskRequest,
  DeallocationExecutionResultDto,
  EligiblePickOperatorDto,
  GetOutboundOrdersParams,
  GetPickTasksParams,
  GetPickWavesParams,
  ImportOutboundOrdersBulkRequest,
  OrderImportBatchDto,
  OutboundItemOptionDto,
  OutboundOrderDto,
  PickConfirmationResultDto,
  PickTaskDto,
  PickWaveDto,
  PickWaveGenerationRequest,
  ReleaseAllocationResultDto,
  ReportShortPickRequest,
  ShortPickExecutionResultDto,
  StockAvailabilityAssessmentDto,
  WarehouseLocationNodeDto,
  CreateOutboundOrderRequest,
  normalizeAllocationStatus,
  normalizeImportBatchStatus,
  normalizeLineStatus,
  normalizeOrderStatus,
  normalizeOrderType,
  normalizeServiceLevel,
  normalizeShortPickReason,
  normalizeShortageReason,
  normalizeStorageTempClass,
  normalizeTaskStatus,
  normalizeWaveStatus,
  normalizeWaveType,
} from '../types/outbound';

function assertPagedResult<T>(data: unknown, entityLabel: string): PagedResult<T> {
  if (
    !data ||
    typeof data !== 'object' ||
    !Array.isArray((data as PagedResult<T>).items) ||
    typeof (data as PagedResult<T>).totalCount !== 'number'
  ) {
    throw new Error(`Received malformed or incomplete ${entityLabel} page response from server.`);
  }
  return data as PagedResult<T>;
}

function normalizeOrderDto(raw: unknown): OutboundOrderDto {
  if (!raw || typeof raw !== 'object') {
    throw new Error('Received malformed or incomplete outbound order response from server.');
  }
  const order = raw as OutboundOrderDto;
  if (!order.id || !order.orderNumber || !order.facilityId) {
    throw new Error('Received incomplete outbound order record missing required identifiers.');
  }
  return {
    ...order,
    orderType: normalizeOrderType(order.orderType),
    status: normalizeOrderStatus(order.status),
    statusBeforeHold: order.statusBeforeHold ? normalizeOrderStatus(order.statusBeforeHold) : null,
    serviceLevel: normalizeServiceLevel(order.serviceLevel),
    lines: Array.isArray(order.lines)
      ? order.lines.map((l) => ({
          ...l,
          status: normalizeLineStatus(l.status),
          quantityOrdered: Number(l.quantityOrdered ?? 0),
          quantityAllocated: Number(l.quantityAllocated ?? 0),
          quantityShipped: Number(l.quantityShipped ?? 0),
          remainingDemand: Number(l.remainingDemand ?? 0),
        }))
      : [],
    statusHistories: Array.isArray(order.statusHistories)
      ? order.statusHistories.map((h) => ({
          ...h,
          fromStatus: normalizeOrderStatus(h.fromStatus),
          toStatus: normalizeOrderStatus(h.toStatus),
        }))
      : [],
  };
}

export function normalizePickTaskDto(raw: unknown): PickTaskDto {
  if (!raw || typeof raw !== 'object') {
    throw new Error('Received malformed or incomplete pick task response from server.');
  }
  const t = raw as PickTaskDto;
  if (!t.id || !t.pickWaveId || !t.outboundOrderId || !t.itemId || !t.sku || !t.sourceLocationCode) {
    throw new Error('Received incomplete pick task record missing required identifiers.');
  }
  return {
    ...t,
    status: normalizeTaskStatus(t.status),
    shortPickReason:
      t.shortPickReason !== undefined && t.shortPickReason !== null
        ? normalizeShortPickReason(t.shortPickReason)
        : null,
    quantityToPick: Number(t.quantityToPick ?? 0),
    quantityPicked: Number(t.quantityPicked ?? 0),
    shortPickQuantity: Number(t.shortPickQuantity ?? 0),
    pickSequence: Number(t.pickSequence ?? 0),
    version: Number(t.version ?? 0),
  };
}

function normalizePickWaveDto(raw: unknown): PickWaveDto {
  if (!raw || typeof raw !== 'object') {
    throw new Error('Received malformed or incomplete pick wave response from server.');
  }
  const wave = raw as PickWaveDto;
  if (!wave.id || !wave.waveNumber || !wave.facilityId) {
    throw new Error('Received incomplete pick wave record missing required identifiers.');
  }
  return {
    ...wave,
    waveType: normalizeWaveType(wave.waveType),
    status: normalizeWaveStatus(wave.status),
    tempClass:
      wave.tempClass !== undefined && wave.tempClass !== null
        ? normalizeStorageTempClass(wave.tempClass)
        : null,
    totalQuantityToPick: Number(wave.totalQuantityToPick ?? 0),
    totalQuantityPicked: Number(wave.totalQuantityPicked ?? 0),
    estimatedTravelMeters:
      wave.estimatedTravelMeters !== undefined && wave.estimatedTravelMeters !== null
        ? Number(wave.estimatedTravelMeters)
        : null,
    waveOrders: Array.isArray(wave.waveOrders)
      ? wave.waveOrders.map((wo) => ({
          ...wo,
          orderStatus: normalizeOrderStatus(wo.orderStatus),
        }))
      : [],
    tasks: Array.isArray(wave.tasks) ? wave.tasks.map(normalizePickTaskDto) : [],
  };
}

function normalizeAllocationResult(raw: unknown): AllocationExecutionResultDto {
  if (!raw || typeof raw !== 'object') {
    throw new Error('Received malformed allocation execution response from server.');
  }
  const res = raw as AllocationExecutionResultDto;
  if (!res.outboundOrderId) {
    throw new Error('Received incomplete allocation response missing outboundOrderId.');
  }
  return {
    ...res,
    status: normalizeOrderStatus(res.status),
    totalQuantityOrdered: Number(res.totalQuantityOrdered ?? 0),
    totalQuantityAllocated: Number(res.totalQuantityAllocated ?? 0),
    totalShortageQuantity: Number(res.totalShortageQuantity ?? 0),
    lineSummaries: Array.isArray(res.lineSummaries)
      ? res.lineSummaries.map((ls) => ({
          ...ls,
          status: normalizeLineStatus(ls.status),
          quantityOrdered: Number(ls.quantityOrdered ?? 0),
          quantityAllocated: Number(ls.quantityAllocated ?? 0),
          remainingDemand: Number(ls.remainingDemand ?? 0),
          allocations: Array.isArray(ls.allocations)
            ? ls.allocations.map((a) => ({
                ...a,
                quantityAllocated: Number(a.quantityAllocated ?? 0),
                status: normalizeAllocationStatus(a.status),
              }))
            : [],
        }))
      : [],
    shortages: Array.isArray(res.shortages)
      ? res.shortages.map((s) => ({
          ...s,
          shortageQuantity: Number(s.shortageQuantity ?? 0),
          reason: normalizeShortageReason(s.reason),
        }))
      : [],
  };
}

export const outboundApi = {
  // =========================================================================
  // 1. Outbound Orders
  // =========================================================================
  getOrders: async (
    params: GetOutboundOrdersParams = {},
    signal?: AbortSignal
  ): Promise<PagedResult<OutboundOrderDto>> => {
    const query = new URLSearchParams();
    if (params.clientId) query.set('clientId', params.clientId);
    if (params.facilityId) query.set('facilityId', params.facilityId);
    if (params.status) query.set('status', params.status);
    if (params.orderType) query.set('orderType', params.orderType);
    if (params.priority !== undefined && params.priority !== null) {
      query.set('priority', String(params.priority));
    }
    if (params.searchTerm?.trim()) query.set('searchTerm', params.searchTerm.trim());
    if (params.fromDateUtc) query.set('fromDateUtc', params.fromDateUtc);
    if (params.toDateUtc) query.set('toDateUtc', params.toDateUtc);
    if (params.pageNumber) query.set('pageNumber', String(params.pageNumber));
    if (params.pageSize) query.set('pageSize', String(params.pageSize));

    const qs = query.toString() ? `?${query.toString()}` : '';
    const data = await apiClient<unknown>(`/api/v1/outbound/orders${qs}`, {
      method: 'GET',
      signal,
    });
    const paged = assertPagedResult<unknown>(data, 'outbound orders');
    return {
      ...paged,
      items: paged.items.map(normalizeOrderDto),
    };
  },

  getOrderById: async (id: string, signal?: AbortSignal): Promise<OutboundOrderDto> => {
    const data = await apiClient<unknown>(`/api/v1/outbound/orders/${id}`, {
      method: 'GET',
      signal,
    });
    return normalizeOrderDto(data);
  },

  getStockAvailability: async (
    id: string,
    signal?: AbortSignal
  ): Promise<StockAvailabilityAssessmentDto> => {
    const data = await apiClient<unknown>(`/api/v1/outbound/orders/${id}/stock-availability`, {
      method: 'GET',
      signal,
    });
    if (!data || typeof data !== 'object' || !Array.isArray((data as StockAvailabilityAssessmentDto).lines)) {
      throw new Error('Received malformed or incomplete stock availability response from server.');
    }
    const assessment = data as StockAvailabilityAssessmentDto;
    return {
      ...assessment,
      overallFulfillmentPercent: Number(assessment.overallFulfillmentPercent ?? 0),
      lines: assessment.lines.map((l) => ({
        ...l,
        quantityRequested: Number(l.quantityRequested ?? 0),
        quantityAvailable: Number(l.quantityAvailable ?? 0),
        shortageQuantity: Number(l.shortageQuantity ?? 0),
      })),
    };
  },

  createOrder: async (
    payload: CreateOutboundOrderRequest,
    clientTenantId?: string,
    signal?: AbortSignal
  ): Promise<OutboundOrderDto> => {
    const headers: Record<string, string> = {};
    if (clientTenantId) {
      headers['X-Tenant-Id'] = clientTenantId;
    }
    const data = await apiClient<unknown>('/api/v1/outbound/orders', {
      method: 'POST',
      headers,
      body: JSON.stringify(payload),
      signal,
    });
    return normalizeOrderDto(data);
  },

  bulkImportOrders: async (
    payload: ImportOutboundOrdersBulkRequest,
    clientTenantId?: string,
    signal?: AbortSignal
  ): Promise<OrderImportBatchDto> => {
    const headers: Record<string, string> = {};
    if (clientTenantId) {
      headers['X-Tenant-Id'] = clientTenantId;
    }
    const data = await apiClient<unknown>('/api/v1/outbound/orders/bulk-import', {
      method: 'POST',
      headers,
      body: JSON.stringify(payload),
      signal,
    });
    if (!data || typeof data !== 'object' || !Array.isArray((data as OrderImportBatchDto).rows)) {
      throw new Error('Received malformed or incomplete bulk import summary response from server.');
    }
    const batch = data as OrderImportBatchDto;
    return {
      ...batch,
      status: normalizeImportBatchStatus(batch.status),
    };
  },

  holdOrder: async (id: string, reason: string, signal?: AbortSignal): Promise<OutboundOrderDto> => {
    const data = await apiClient<unknown>(`/api/v1/outbound/orders/${id}/hold`, {
      method: 'PATCH',
      body: JSON.stringify({ reason }),
      signal,
    });
    return normalizeOrderDto(data);
  },

  releaseOrder: async (
    id: string,
    notes?: string,
    signal?: AbortSignal
  ): Promise<OutboundOrderDto> => {
    const data = await apiClient<unknown>(`/api/v1/outbound/orders/${id}/release`, {
      method: 'PATCH',
      body: JSON.stringify({ notes: notes || null }),
      signal,
    });
    return normalizeOrderDto(data);
  },

  cancelOrder: async (id: string, reason: string, signal?: AbortSignal): Promise<OutboundOrderDto> => {
    const data = await apiClient<unknown>(`/api/v1/outbound/orders/${id}/cancel`, {
      method: 'PATCH',
      body: JSON.stringify({ reason }),
      signal,
    });
    return normalizeOrderDto(data);
  },

  allocateOrder: async (
    id: string,
    payload: { strategyOverride?: AllocationStrategyType; idempotencyKey?: string } = {},
    signal?: AbortSignal
  ): Promise<AllocationExecutionResultDto> => {
    const data = await apiClient<unknown>(`/api/v1/outbound/orders/${id}/allocate`, {
      method: 'POST',
      body: JSON.stringify({
        strategyOverride: payload.strategyOverride ?? null,
        idempotencyKey: payload.idempotencyKey ?? null,
      }),
      signal,
    });
    return normalizeAllocationResult(data);
  },

  deallocateOrder: async (
    id: string,
    reason: string,
    signal?: AbortSignal
  ): Promise<DeallocationExecutionResultDto> => {
    const data = await apiClient<unknown>(`/api/v1/outbound/orders/${id}/deallocate`, {
      method: 'POST',
      body: JSON.stringify({ reason }),
      signal,
    });
    if (!data || typeof data !== 'object' || !(data as DeallocationExecutionResultDto).outboundOrderId) {
      throw new Error('Received malformed deallocation response from server.');
    }
    const res = data as DeallocationExecutionResultDto;
    return {
      ...res,
      restoredStatus: normalizeOrderStatus(res.restoredStatus),
      totalQuantityDeallocated: Number(res.totalQuantityDeallocated ?? 0),
    };
  },

  releaseAllocation: async (
    allocationId: string,
    payload: { quantityToRelease?: number | null; reason: string },
    signal?: AbortSignal
  ): Promise<ReleaseAllocationResultDto> => {
    const data = await apiClient<unknown>(`/api/v1/outbound/orders/allocations/${allocationId}/release`, {
      method: 'POST',
      body: JSON.stringify({
        quantityToRelease: payload.quantityToRelease ?? null,
        reason: payload.reason,
      }),
      signal,
    });
    if (!data || typeof data !== 'object' || !(data as ReleaseAllocationResultDto).allocationId) {
      throw new Error('Received malformed allocation release response from server.');
    }
    const res = data as ReleaseAllocationResultDto;
    return {
      ...res,
      quantityReleased: Number(res.quantityReleased ?? 0),
      activeRemainingQuantity: Number(res.activeRemainingQuantity ?? 0),
      allocationStatus: normalizeAllocationStatus(res.allocationStatus),
      orderStatus: normalizeOrderStatus(res.orderStatus),
      lineStatus: normalizeLineStatus(res.lineStatus),
    };
  },

  // =========================================================================
  // 2. Pick Waves
  // =========================================================================
  getPickWaves: async (
    params: GetPickWavesParams,
    signal?: AbortSignal
  ): Promise<PagedResult<PickWaveDto>> => {
    const query = new URLSearchParams();
    query.set('facilityId', params.facilityId);
    if (params.status) query.set('status', params.status);
    if (params.waveType) query.set('waveType', params.waveType);
    if (params.pageNumber) query.set('pageNumber', String(params.pageNumber));
    if (params.pageSize) query.set('pageSize', String(params.pageSize));

    const data = await apiClient<unknown>(`/api/v1/outbound/pick-waves?${query.toString()}`, {
      method: 'GET',
      signal,
    });
    const paged = assertPagedResult<unknown>(data, 'pick waves');
    return {
      ...paged,
      items: paged.items.map(normalizePickWaveDto),
    };
  },

  getPickWaveById: async (id: string, signal?: AbortSignal): Promise<PickWaveDto> => {
    const data = await apiClient<unknown>(`/api/v1/outbound/pick-waves/${id}`, {
      method: 'GET',
      signal,
    });
    return normalizePickWaveDto(data);
  },

  /**
   * GeneratePickWaveCommand wraps PickWaveGenerationRequest inside a nested `request` property.
   */
  generatePickWave: async (
    requestPayload: PickWaveGenerationRequest,
    signal?: AbortSignal
  ): Promise<PickWaveDto> => {
    const data = await apiClient<unknown>('/api/v1/outbound/pick-waves', {
      method: 'POST',
      body: JSON.stringify({ request: requestPayload }),
      signal,
    });
    return normalizePickWaveDto(data);
  },

  releasePickWave: async (id: string, signal?: AbortSignal): Promise<PickWaveDto> => {
    const data = await apiClient<unknown>(`/api/v1/outbound/pick-waves/${id}/release`, {
      method: 'POST',
      signal,
    });
    return normalizePickWaveDto(data);
  },

  // =========================================================================
  // 3. Authorized Reference Data Lookups
  // =========================================================================
  getAuthorizedClients: async (
    signal?: AbortSignal,
    facilityId?: string
  ): Promise<PagedResult<ClientDto>> => {
    const query = new URLSearchParams();
    if (facilityId) query.set('facilityId', facilityId);
    query.set('pageNumber', '1');
    query.set('pageSize', '100');

    const data = await apiClient<unknown>(
      `/api/v1/outbound/orders/reference-clients?${query.toString()}`,
      {
        method: 'GET',
        signal,
      }
    );
    return assertPagedResult<ClientDto>(data, 'outbound reference clients');
  },

  getEligibleWaveClients: async (
    signal?: AbortSignal,
    facilityId?: string
  ): Promise<PagedResult<ClientDto>> => {
    const query = new URLSearchParams();
    if (facilityId) query.set('facilityId', facilityId);
    query.set('pageNumber', '1');
    query.set('pageSize', '100');

    const data = await apiClient<unknown>(
      `/api/v1/outbound/pick-waves/eligible-clients?${query.toString()}`,
      {
        method: 'GET',
        signal,
      }
    );
    return assertPagedResult<ClientDto>(data, 'eligible wave clients');
  },

  getAuthorizedItems: async (
    params: {
      clientId?: string;
      facilityId?: string;
      searchTerm?: string;
      pageNumber?: number;
      pageSize?: number;
    } = {},
    signal?: AbortSignal
  ): Promise<PagedResult<OutboundItemOptionDto>> => {
    const query = new URLSearchParams();
    if (params.clientId) query.set('clientId', params.clientId);
    if (params.facilityId) query.set('facilityId', params.facilityId);
    if (params.searchTerm?.trim()) query.set('searchTerm', params.searchTerm.trim());
    query.set('pageNumber', String(params.pageNumber ?? 1));
    query.set('pageSize', String(params.pageSize ?? 100));

    const data = await apiClient<unknown>(`/api/v1/outbound/orders/catalog-items?${query.toString()}`, {
      method: 'GET',
      signal,
    });
    const paged = assertPagedResult<OutboundItemOptionDto>(data, 'outbound catalog items');
    return {
      ...paged,
      items: paged.items.map((item) => ({
        ...item,
        storageTempClass:
          item.storageTempClass !== undefined && item.storageTempClass !== null
            ? normalizeStorageTempClass(item.storageTempClass)
            : undefined,
      })),
    };
  },

  getLocationTree: async (
    facilityId: string,
    signal?: AbortSignal
  ): Promise<WarehouseLocationNodeDto[]> => {
    const data = await apiClient<unknown>(`/api/v1/locations/tree?facilityId=${encodeURIComponent(facilityId)}`, {
      method: 'GET',
      signal,
    });
    if (!Array.isArray(data)) {
      throw new Error('Received malformed location tree response from server.');
    }
    return data as WarehouseLocationNodeDto[];
  },

  // =========================================================================
  // 4. Directed Pick Execution & Task Queue (Phase 7.4.2)
  // =========================================================================
  getPickTasks: async (
    params: GetPickTasksParams,
    signal?: AbortSignal
  ): Promise<PagedResult<PickTaskDto>> => {
    const query = new URLSearchParams();
    query.set('facilityId', params.facilityId);
    if (params.clientId) query.set('clientId', params.clientId);
    if (params.pickWaveId) query.set('pickWaveId', params.pickWaveId);
    if (params.outboundOrderId) query.set('outboundOrderId', params.outboundOrderId);
    if (params.assignedUserId) query.set('assignedUserId', params.assignedUserId);
    if (params.statuses && params.statuses.length > 0) {
      params.statuses.forEach((s) => query.append('statuses', s));
    } else if (params.status) {
      query.set('status', params.status);
    }
    if (params.searchTerm?.trim()) query.set('searchTerm', params.searchTerm.trim());
    if (params.pageNumber) query.set('pageNumber', String(params.pageNumber));
    if (params.pageSize) query.set('pageSize', String(params.pageSize));

    const data = await apiClient<unknown>(`/api/v1/outbound/pick-tasks?${query.toString()}`, {
      method: 'GET',
      signal,
    });
    const paged = assertPagedResult<unknown>(data, 'pick tasks');
    return {
      ...paged,
      items: paged.items.map(normalizePickTaskDto),
    };
  },

  getPickTaskById: async (id: string, signal?: AbortSignal): Promise<PickTaskDto> => {
    const data = await apiClient<unknown>(`/api/v1/outbound/pick-tasks/${id}`, {
      method: 'GET',
      signal,
    });
    return normalizePickTaskDto(data);
  },

  /**
   * Strictly read-only execution endpoint deriving operator identity from server JWT.
   * Returns null on 204 No Content.
   */
  getNextPickTask: async (facilityId: string, signal?: AbortSignal): Promise<PickTaskDto | null> => {
    const data = await apiClient<unknown>(
      `/api/v1/outbound/pick-tasks/next?facilityId=${encodeURIComponent(facilityId)}`,
      {
        method: 'GET',
        signal,
      }
    );
    if (data === null || data === undefined) {
      return null;
    }
    return normalizePickTaskDto(data);
  },

  claimPickTask: async (id: string, signal?: AbortSignal): Promise<PickTaskDto> => {
    const data = await apiClient<unknown>(`/api/v1/outbound/pick-tasks/${id}/claim`, {
      method: 'POST',
      signal,
    });
    return normalizePickTaskDto(data);
  },

  assignPickTask: async (
    id: string,
    operatorUserId: string,
    signal?: AbortSignal
  ): Promise<PickTaskDto> => {
    const data = await apiClient<unknown>(`/api/v1/outbound/pick-tasks/${id}/assign`, {
      method: 'POST',
      body: JSON.stringify({ operatorUserId }),
      signal,
    });
    return normalizePickTaskDto(data);
  },

  getEligiblePickOperators: async (
    facilityId?: string,
    signal?: AbortSignal
  ): Promise<PagedResult<EligiblePickOperatorDto>> => {
    const query = new URLSearchParams();
    if (facilityId) query.set('facilityId', facilityId);
    query.set('pageNumber', '1');
    query.set('pageSize', '100');

    const data = await apiClient<unknown>(
      `/api/v1/outbound/pick-tasks/eligible-operators?${query.toString()}`,
      {
        method: 'GET',
        signal,
      }
    );
    return assertPagedResult<EligiblePickOperatorDto>(data, 'eligible pick operators');
  },

  confirmPickTask: async (
    id: string,
    payload: ConfirmPickTaskRequest,
    signal?: AbortSignal
  ): Promise<PickConfirmationResultDto> => {
    const data = await apiClient<unknown>(`/api/v1/outbound/pick-tasks/${id}/confirm`, {
      method: 'POST',
      body: JSON.stringify(payload),
      signal,
    });
    if (!data || typeof data !== 'object' || !(data as PickConfirmationResultDto).pickTaskId) {
      throw new Error('Received malformed or incomplete pick confirmation response from server.');
    }
    const res = data as PickConfirmationResultDto;
    return {
      ...res,
      taskStatus: normalizeTaskStatus(res.taskStatus),
      quantityPicked: Number(res.quantityPicked ?? 0),
      shortPickQuantity: Number(res.shortPickQuantity ?? 0),
      inventoryTransactionId: Number(res.inventoryTransactionId ?? 0),
    };
  },

  reportShortPick: async (
    id: string,
    payload: ReportShortPickRequest,
    signal?: AbortSignal
  ): Promise<ShortPickExecutionResultDto> => {
    const data = await apiClient<unknown>(`/api/v1/outbound/pick-tasks/${id}/short-pick`, {
      method: 'POST',
      body: JSON.stringify(payload),
      signal,
    });
    if (!data || typeof data !== 'object' || !(data as ShortPickExecutionResultDto).pickTaskId) {
      throw new Error('Received malformed or incomplete short-pick response from server.');
    }
    const res = data as ShortPickExecutionResultDto;
    return {
      ...res,
      taskStatus: normalizeTaskStatus(res.taskStatus),
      quantityPicked: Number(res.quantityPicked ?? 0),
      discrepancyQuantity: Number(res.discrepancyQuantity ?? 0),
    };
  },
};
