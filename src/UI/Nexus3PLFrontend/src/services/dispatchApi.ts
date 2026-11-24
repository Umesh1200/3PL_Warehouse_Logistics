import { apiClient } from './apiClient';
import { PagedResult } from '../types/auth';
import {
  AcknowledgeShipmentCustodyRequest,
  AcknowledgeShipmentCustodyResultDto,
  AssignShipmentDriverRequest,
  AssignShipmentDriverResultDto,
  ConfirmPackageDeliveryRequest,
  CustomerContactMethod,
  CustomerContactOutcome,
  DeliveryConfirmationResultDto,
  DeliveryExceptionQueueItemDto,
  DeliveryExceptionResolution,
  DeliveryExceptionResultDto,
  DispatchBoardShipmentDto,
  EvaluateOrderDeliveryRollupResultDto,
  GetDeliveryExceptionsParams,
  GetDispatchBoardParams,
  PackageTrackingTimelineDto,
  ProofOfDeliveryDto,
  ReportDriverExceptionRequest,
  SubmitProofOfDeliveryRequest,
  SubmitProofOfDeliveryResultDto,
  SupersedeProofOfDeliveryJsonRequest,
  SyncShipmentTrackingResultDto,
  normalizeDeliveryExceptionSeverity,
  normalizeDeliveryExceptionSource,
  normalizeDeliveryExceptionStatus,
  normalizeDeliveryExceptionType,
  normalizeDeliveryExceptionResolution,
  normalizeOrderDeliveryStatus,
  normalizePackageStatus,
  normalizePodEvidenceSource,
  normalizeServiceLevel,
  normalizeShipmentCustodyStatus,
  normalizeShipmentStatus,
  normalizeTrackingMilestoneStatus,
} from '../types/outbound';

function assertPagedResult<T>(data: unknown, resourceLabel: string): PagedResult<T> {
  if (!data || typeof data !== 'object') {
    throw new Error(`Received malformed ${resourceLabel} response from server.`);
  }
  const paged = data as PagedResult<T>;
  if (!Array.isArray(paged.items)) {
    throw new Error(`Received invalid ${resourceLabel} collection payload from server.`);
  }
  return paged;
}

function normalizeBoardShipmentDto(raw: unknown): DispatchBoardShipmentDto {
  if (!raw || typeof raw !== 'object' || !(raw as DispatchBoardShipmentDto).shipmentId) {
    throw new Error('Received malformed or incomplete dispatch board shipment from server.');
  }
  const s = raw as DispatchBoardShipmentDto;
  return {
    ...s,
    carrierServiceLevel: normalizeServiceLevel(s.carrierServiceLevel),
    status: normalizeShipmentStatus(s.status),
    custodyStatus: normalizeShipmentCustodyStatus(s.custodyStatus ?? 'Unassigned'),
    deliveryStatus: normalizeTrackingMilestoneStatus(s.deliveryStatus ?? 'PendingHandover'),
    totalPackagesCount: Number(s.totalPackagesCount ?? 0),
    deliveredPackagesCount: Number(s.deliveredPackagesCount ?? 0),
    openExceptionsCount: Number(s.openExceptionsCount ?? 0),
    totalWeightKg: Number(s.totalWeightKg ?? 0),
    version: Number(s.version ?? 1),
    packages: Array.isArray(s.packages)
      ? s.packages.map((p) => ({
          ...p,
          packageStatus: normalizePackageStatus(p.packageStatus),
          trackingStatus: normalizeTrackingMilestoneStatus(p.trackingStatus ?? 'PendingHandover'),
          hasActivePod: Boolean(p.hasActivePod),
        }))
      : [],
  };
}

function normalizeExceptionQueueItemDto(raw: unknown): DeliveryExceptionQueueItemDto {
  if (!raw || typeof raw !== 'object' || !(raw as DeliveryExceptionQueueItemDto).exceptionId) {
    throw new Error('Received malformed delivery exception queue item from server.');
  }
  const e = raw as DeliveryExceptionQueueItemDto;
  return {
    ...e,
    exceptionType: normalizeDeliveryExceptionType(e.exceptionType),
    status: normalizeDeliveryExceptionStatus(e.status),
    severity: normalizeDeliveryExceptionSeverity(e.severity),
    resolution: normalizeDeliveryExceptionResolution(e.resolution),
    source: normalizeDeliveryExceptionSource(e.source),
    attemptNumber: Number(e.attemptNumber ?? 1),
    reattemptCount: Number(e.reattemptCount ?? 0),
    contactAttemptsCount: Number(e.contactAttemptsCount ?? 0),
  };
}

function normalizeExceptionResultDto(raw: unknown): DeliveryExceptionResultDto {
  if (!raw || typeof raw !== 'object' || !(raw as DeliveryExceptionResultDto).exceptionId) {
    throw new Error('Received malformed delivery exception result from server.');
  }
  const r = raw as DeliveryExceptionResultDto;
  return {
    ...r,
    exceptionType: normalizeDeliveryExceptionType(r.exceptionType),
    status: normalizeDeliveryExceptionStatus(r.status),
    severity: normalizeDeliveryExceptionSeverity(r.severity),
    packageTrackingStatus: normalizeTrackingMilestoneStatus(r.packageTrackingStatus),
    orderDeliveryStatus: normalizeOrderDeliveryStatus(r.orderDeliveryStatus),
  };
}

export const dispatchApi = {
  getDispatchBoard: async (
    params: GetDispatchBoardParams = {},
    signal?: AbortSignal
  ): Promise<PagedResult<DispatchBoardShipmentDto>> => {
    const query = new URLSearchParams();
    if (params.facilityId) query.set('facilityId', params.facilityId);
    if (params.clientId) query.set('clientId', params.clientId);
    if (params.status) query.set('status', params.status);
    if (params.deliveryStatus) query.set('deliveryStatus', params.deliveryStatus);
    if (params.carrierCode?.trim()) query.set('carrierCode', params.carrierCode.trim());
    if (params.assignedDriverUserId) query.set('assignedDriverUserId', params.assignedDriverUserId);
    if (params.hasOpenExceptions !== undefined) query.set('hasOpenExceptions', String(params.hasOpenExceptions));
    if (params.searchTerm?.trim()) query.set('searchTerm', params.searchTerm.trim());
    if (params.pageNumber) query.set('pageNumber', String(params.pageNumber));
    if (params.pageSize) query.set('pageSize', String(params.pageSize));

    const data = await apiClient<unknown>(`/api/v1/outbound/dispatch/board?${query.toString()}`, {
      method: 'GET',
      signal,
    });
    const paged = assertPagedResult<unknown>(data, 'dispatch board');
    return {
      ...paged,
      items: paged.items.map(normalizeBoardShipmentDto),
    };
  },

  getCourierAssignedShipments: async (
    signal?: AbortSignal
  ): Promise<DispatchBoardShipmentDto[]> => {
    const data = await apiClient<unknown>('/api/v1/outbound/dispatch/courier/shipments', {
      method: 'GET',
      signal,
    });
    if (!Array.isArray(data)) {
      throw new Error('Received malformed courier assignments response from server.');
    }
    return data.map(normalizeBoardShipmentDto);
  },

  getPackageTimeline: async (
    packageId: string,
    signal?: AbortSignal
  ): Promise<PackageTrackingTimelineDto> => {
    const data = await apiClient<unknown>(
      `/api/v1/outbound/dispatch/packages/${encodeURIComponent(packageId)}/timeline`,
      { method: 'GET', signal }
    );
    if (!data || typeof data !== 'object' || !(data as PackageTrackingTimelineDto).packageId) {
      throw new Error('Received malformed package tracking timeline from server.');
    }
    const t = data as PackageTrackingTimelineDto;
    return {
      ...t,
      currentTrackingStatus: normalizeTrackingMilestoneStatus(t.currentTrackingStatus),
      events: Array.isArray(t.events)
        ? t.events.map((ev) => ({
            ...ev,
            milestoneStatus: normalizeTrackingMilestoneStatus(ev.milestoneStatus),
          }))
        : [],
    };
  },

  getOrderDeliveryStatus: async (
    orderId: string,
    signal?: AbortSignal
  ): Promise<EvaluateOrderDeliveryRollupResultDto> => {
    const data = await apiClient<unknown>(
      `/api/v1/outbound/dispatch/orders/${encodeURIComponent(orderId)}/delivery-status`,
      { method: 'GET', signal }
    );
    if (!data || typeof data !== 'object' || !(data as EvaluateOrderDeliveryRollupResultDto).outboundOrderId) {
      throw new Error('Received malformed order delivery rollup from server.');
    }
    const r = data as EvaluateOrderDeliveryRollupResultDto;
    return {
      ...r,
      deliveryStatus: normalizeOrderDeliveryStatus(r.deliveryStatus),
      totalPackages: Number(r.totalPackages ?? 0),
      deliveredPackages: Number(r.deliveredPackages ?? 0),
      inTransitPackages: Number(r.inTransitPackages ?? 0),
      exceptionPackages: Number(r.exceptionPackages ?? 0),
    };
  },

  getPackagePod: async (
    packageId: string,
    signal?: AbortSignal
  ): Promise<ProofOfDeliveryDto | null> => {
    const data = await apiClient<unknown>(
      `/api/v1/outbound/dispatch/packages/${encodeURIComponent(packageId)}/pod`,
      { method: 'GET', signal }
    );
    if (!data) return null;
    if (typeof data !== 'object' || !(data as ProofOfDeliveryDto).id) {
      throw new Error('Received malformed Proof of Delivery response from server.');
    }
    const pod = data as ProofOfDeliveryDto;
    return {
      ...pod,
      evidenceSource: normalizePodEvidenceSource(pod.evidenceSource),
      isSigned: Boolean(pod.isSigned),
      isStorageFinalized: Boolean(pod.isStorageFinalized),
      isSuperseded: Boolean(pod.isSuperseded),
    };
  },

  getDeliveryExceptions: async (
    params: GetDeliveryExceptionsParams = {},
    signal?: AbortSignal
  ): Promise<PagedResult<DeliveryExceptionQueueItemDto>> => {
    const query = new URLSearchParams();
    if (params.facilityId) query.set('facilityId', params.facilityId);
    if (params.clientId) query.set('clientId', params.clientId);
    if (params.packageId) query.set('packageId', params.packageId);
    if (params.shipmentId) query.set('shipmentId', params.shipmentId);
    if (params.orderId) query.set('orderId', params.orderId);
    if (params.status) query.set('status', params.status);
    if (params.severity) query.set('severity', params.severity);
    if (params.exceptionType) query.set('exceptionType', params.exceptionType);
    if (params.openOnly !== undefined) query.set('openOnly', String(params.openOnly));
    if (params.searchTerm?.trim()) query.set('searchTerm', params.searchTerm.trim());
    if (params.pageNumber) query.set('pageNumber', String(params.pageNumber));
    if (params.pageSize) query.set('pageSize', String(params.pageSize));

    const data = await apiClient<unknown>(`/api/v1/outbound/dispatch/exceptions?${query.toString()}`, {
      method: 'GET',
      signal,
    });
    const paged = assertPagedResult<unknown>(data, 'delivery exceptions');
    return {
      ...paged,
      items: paged.items.map(normalizeExceptionQueueItemDto),
    };
  },

  assignShipmentDriver: async (
    shipmentId: string,
    payload: AssignShipmentDriverRequest,
    signal?: AbortSignal
  ): Promise<AssignShipmentDriverResultDto> => {
    const data = await apiClient<unknown>(
      `/api/v1/outbound/dispatch/shipments/${encodeURIComponent(shipmentId)}/assign-driver`,
      {
        method: 'POST',
        body: JSON.stringify({
          driverUserId: payload.driverUserId || null,
          expectedVersion: payload.expectedVersion ?? null,
          reason: payload.reason?.trim() || null,
        }),
        signal,
      }
    );
    if (!data || typeof data !== 'object' || !(data as AssignShipmentDriverResultDto).shipmentId) {
      throw new Error('Received malformed assign-driver response from server.');
    }
    const r = data as AssignShipmentDriverResultDto;
    return {
      ...r,
      shipmentStatus: normalizeShipmentStatus(r.shipmentStatus),
      custodyStatus: normalizeShipmentCustodyStatus(r.custodyStatus),
      version: Number(r.version ?? 1),
    };
  },

  acknowledgeShipmentCustody: async (
    shipmentId: string,
    payload: AcknowledgeShipmentCustodyRequest,
    signal?: AbortSignal
  ): Promise<AcknowledgeShipmentCustodyResultDto> => {
    const data = await apiClient<unknown>(
      `/api/v1/outbound/dispatch/shipments/${encodeURIComponent(shipmentId)}/acknowledge-custody`,
      {
        method: 'POST',
        body: JSON.stringify({
          driverSignature: payload.driverSignature,
          driverLicenseNumber: payload.driverLicenseNumber?.trim() || null,
          trailerNumber: payload.trailerNumber?.trim() || null,
          sealNumber: payload.sealNumber?.trim() || null,
          expectedVersion: payload.expectedVersion ?? null,
          notes: payload.notes?.trim() || null,
        }),
        signal,
      }
    );
    if (!data || typeof data !== 'object' || !(data as AcknowledgeShipmentCustodyResultDto).shipmentId) {
      throw new Error('Received malformed acknowledge-custody response from server.');
    }
    const r = data as AcknowledgeShipmentCustodyResultDto;
    return {
      ...r,
      custodyStatus: normalizeShipmentCustodyStatus(r.custodyStatus),
      version: Number(r.version ?? 1),
    };
  },

  syncShipmentTracking: async (
    shipmentId: string,
    signal?: AbortSignal
  ): Promise<SyncShipmentTrackingResultDto> => {
    const data = await apiClient<unknown>(
      `/api/v1/outbound/dispatch/shipments/${encodeURIComponent(shipmentId)}/sync-tracking`,
      { method: 'POST', signal }
    );
    if (!data || typeof data !== 'object' || !(data as SyncShipmentTrackingResultDto).shipmentId) {
      throw new Error('Received malformed sync-tracking response from server.');
    }
    const r = data as SyncShipmentTrackingResultDto;
    return {
      ...r,
      packagesEvaluated: Number(r.packagesEvaluated ?? 0),
      newEventsIngested: Number(r.newEventsIngested ?? 0),
      duplicateEventsSkipped: Number(r.duplicateEventsSkipped ?? 0),
      shipmentDeliveryStatus: normalizeTrackingMilestoneStatus(r.shipmentDeliveryStatus),
    };
  },

  submitProofOfDelivery: async (
    packageId: string,
    payload: SubmitProofOfDeliveryRequest,
    signal?: AbortSignal
  ): Promise<SubmitProofOfDeliveryResultDto> => {
    let body: BodyInit;
    if (payload.signatureFile || payload.photoFile) {
      const form = new FormData();
      if (payload.signatureFile) form.append('signature', payload.signatureFile);
      if (payload.photoFile) form.append('photo', payload.photoFile);
      if (payload.recipientName?.trim()) form.append('recipientName', payload.recipientName.trim());
      if (payload.deliveryLocationDescription?.trim()) {
        form.append('deliveryLocationDescription', payload.deliveryLocationDescription.trim());
      }
      form.append('isSigned', String(Boolean(payload.isSigned || payload.signatureFile)));
      if (payload.latitude !== null && payload.latitude !== undefined) {
        form.append('latitude', String(payload.latitude));
      }
      if (payload.longitude !== null && payload.longitude !== undefined) {
        form.append('longitude', String(payload.longitude));
      }
      if (payload.idempotencyKey) form.append('idempotencyKey', payload.idempotencyKey);
      form.append('evidenceSource', payload.evidenceSource);
      if (payload.deliveredAtUtc) form.append('deliveredAtUtc', payload.deliveredAtUtc);
      if (payload.verificationReason?.trim()) {
        form.append('verificationReason', payload.verificationReason.trim());
      }
      body = form;
    } else {
      body = JSON.stringify({
        recipientName: payload.recipientName?.trim() || null,
        deliveryLocationDescription: payload.deliveryLocationDescription?.trim() || null,
        isSigned: Boolean(payload.isSigned),
        signatureBase64: payload.signatureBase64 || null,
        signatureContentType: payload.signatureContentType || null,
        photoBase64: payload.photoBase64 || null,
        photoContentType: payload.photoContentType || null,
        latitude: payload.latitude ?? null,
        longitude: payload.longitude ?? null,
        carrierConfirmationNumber: payload.carrierConfirmationNumber?.trim() || null,
        idempotencyKey: payload.idempotencyKey || null,
        evidenceSource: payload.evidenceSource,
        deliveredAtUtc: payload.deliveredAtUtc || null,
        verificationReason: payload.verificationReason?.trim() || null,
        consigneeAcknowledgmentText: payload.consigneeAcknowledgmentText?.trim() || null,
      });
    }

    const data = await apiClient<unknown>(
      `/api/v1/outbound/dispatch/packages/${encodeURIComponent(packageId)}/pod`,
      {
        method: 'POST',
        body,
        signal,
      }
    );
    if (!data || typeof data !== 'object' || !(data as SubmitProofOfDeliveryResultDto).proofOfDeliveryId) {
      throw new Error('Received malformed submit POD response from server.');
    }
    return data as SubmitProofOfDeliveryResultDto;
  },

  supersedeProofOfDelivery: async (
    podId: string,
    payload: SupersedeProofOfDeliveryJsonRequest,
    signal?: AbortSignal
  ): Promise<SubmitProofOfDeliveryResultDto> => {
    const data = await apiClient<unknown>(
      `/api/v1/outbound/dispatch/pod/${encodeURIComponent(podId)}/supersede`,
      {
        method: 'POST',
        body: JSON.stringify(payload),
        signal,
      }
    );
    if (!data || typeof data !== 'object' || !(data as SubmitProofOfDeliveryResultDto).proofOfDeliveryId) {
      throw new Error('Received malformed supersede POD response from server.');
    }
    return data as SubmitProofOfDeliveryResultDto;
  },

  confirmPackageDelivery: async (
    packageId: string,
    payload: ConfirmPackageDeliveryRequest = {},
    signal?: AbortSignal
  ): Promise<DeliveryConfirmationResultDto> => {
    const data = await apiClient<unknown>(
      `/api/v1/outbound/dispatch/packages/${encodeURIComponent(packageId)}/confirm-delivery`,
      {
        method: 'POST',
        body: JSON.stringify({
          proofOfDeliveryId: payload.proofOfDeliveryId ?? null,
          deliveredAtUtcOverride: payload.deliveredAtUtcOverride ?? null,
          confirmationNotes: payload.confirmationNotes?.trim() || null,
        }),
        signal,
      }
    );
    if (!data || typeof data !== 'object' || !(data as DeliveryConfirmationResultDto).packageId) {
      throw new Error('Received malformed confirm-delivery response from server.');
    }
    const r = data as DeliveryConfirmationResultDto;
    return {
      ...r,
      packageTrackingStatus: normalizeTrackingMilestoneStatus(r.packageTrackingStatus),
      orderDeliveryStatus: normalizeOrderDeliveryStatus(r.orderDeliveryStatus),
    };
  },

  reportDriverException: async (
    payload: ReportDriverExceptionRequest,
    signal?: AbortSignal
  ): Promise<DeliveryExceptionResultDto> => {
    const data = await apiClient<unknown>('/api/v1/outbound/dispatch/exceptions/driver-report', {
      method: 'POST',
      body: JSON.stringify({
        packageId: payload.packageId,
        exceptionType: payload.exceptionType,
        driverNotes: payload.driverNotes.trim(),
        occurredAtUtc: payload.occurredAtUtc ?? null,
        latitude: payload.latitude ?? null,
        longitude: payload.longitude ?? null,
      }),
      signal,
    });
    return normalizeExceptionResultDto(data);
  },

  recordCustomerContact: async (
    exceptionId: string,
    payload: {
      contactMethod: CustomerContactMethod;
      outcome: CustomerContactOutcome;
      notes: string;
      resolutionAction?: string | null;
    },
    signal?: AbortSignal
  ): Promise<DeliveryExceptionResultDto> => {
    const data = await apiClient<unknown>(
      `/api/v1/outbound/dispatch/exceptions/${encodeURIComponent(exceptionId)}/contact`,
      {
        method: 'POST',
        body: JSON.stringify({
          contactMethod: payload.contactMethod,
          outcome: payload.outcome,
          notes: payload.notes.trim(),
          resolutionAction: payload.resolutionAction?.trim() || null,
        }),
        signal,
      }
    );
    return normalizeExceptionResultDto(data);
  },

  scheduleReattempt: async (
    exceptionId: string,
    payload: { scheduledDateUtc: string; notes: string },
    signal?: AbortSignal
  ): Promise<DeliveryExceptionResultDto> => {
    const data = await apiClient<unknown>(
      `/api/v1/outbound/dispatch/exceptions/${encodeURIComponent(exceptionId)}/reattempt`,
      {
        method: 'POST',
        body: JSON.stringify({
          scheduledDateUtc: payload.scheduledDateUtc,
          notes: payload.notes.trim(),
        }),
        signal,
      }
    );
    return normalizeExceptionResultDto(data);
  },

  rescheduleReattempt: async (
    exceptionId: string,
    payload: { newScheduledDateUtc: string; reason: string },
    signal?: AbortSignal
  ): Promise<DeliveryExceptionResultDto> => {
    const data = await apiClient<unknown>(
      `/api/v1/outbound/dispatch/exceptions/${encodeURIComponent(exceptionId)}/reschedule`,
      {
        method: 'POST',
        body: JSON.stringify({
          newScheduledDateUtc: payload.newScheduledDateUtc,
          reason: payload.reason.trim(),
        }),
        signal,
      }
    );
    return normalizeExceptionResultDto(data);
  },

  cancelReattempt: async (
    exceptionId: string,
    reason: string,
    signal?: AbortSignal
  ): Promise<DeliveryExceptionResultDto> => {
    const data = await apiClient<unknown>(
      `/api/v1/outbound/dispatch/exceptions/${encodeURIComponent(exceptionId)}/cancel-reattempt`,
      {
        method: 'POST',
        body: JSON.stringify({ reason: reason.trim() }),
        signal,
      }
    );
    return normalizeExceptionResultDto(data);
  },

  updateDeliveryAddress: async (
    exceptionId: string,
    payload: { correctedAddress: string; notes: string },
    signal?: AbortSignal
  ): Promise<DeliveryExceptionResultDto> => {
    const data = await apiClient<unknown>(
      `/api/v1/outbound/dispatch/exceptions/${encodeURIComponent(exceptionId)}/update-address`,
      {
        method: 'POST',
        body: JSON.stringify({
          correctedAddress: payload.correctedAddress.trim(),
          notes: payload.notes.trim(),
        }),
        signal,
      }
    );
    return normalizeExceptionResultDto(data);
  },

  redirectToDepot: async (
    exceptionId: string,
    payload: { depotLocation: string; notes: string },
    signal?: AbortSignal
  ): Promise<DeliveryExceptionResultDto> => {
    const data = await apiClient<unknown>(
      `/api/v1/outbound/dispatch/exceptions/${encodeURIComponent(exceptionId)}/redirect-depot`,
      {
        method: 'POST',
        body: JSON.stringify({
          depotLocation: payload.depotLocation.trim(),
          notes: payload.notes.trim(),
        }),
        signal,
      }
    );
    return normalizeExceptionResultDto(data);
  },

  resolveException: async (
    exceptionId: string,
    payload: { resolution: DeliveryExceptionResolution; resolutionNotes: string },
    signal?: AbortSignal
  ): Promise<DeliveryExceptionResultDto> => {
    const data = await apiClient<unknown>(
      `/api/v1/outbound/dispatch/exceptions/${encodeURIComponent(exceptionId)}/resolve`,
      {
        method: 'POST',
        body: JSON.stringify({
          resolution: payload.resolution,
          resolutionNotes: payload.resolutionNotes.trim(),
        }),
        signal,
      }
    );
    return normalizeExceptionResultDto(data);
  },

  reopenException: async (
    exceptionId: string,
    reason: string,
    signal?: AbortSignal
  ): Promise<DeliveryExceptionResultDto> => {
    const data = await apiClient<unknown>(
      `/api/v1/outbound/dispatch/exceptions/${encodeURIComponent(exceptionId)}/reopen`,
      {
        method: 'POST',
        body: JSON.stringify({ reason: reason.trim() }),
        signal,
      }
    );
    return normalizeExceptionResultDto(data);
  },

  voidException: async (
    exceptionId: string,
    reason: string,
    signal?: AbortSignal
  ): Promise<DeliveryExceptionResultDto> => {
    const data = await apiClient<unknown>(
      `/api/v1/outbound/dispatch/exceptions/${encodeURIComponent(exceptionId)}/void`,
      {
        method: 'POST',
        body: JSON.stringify({ reason: reason.trim() }),
        signal,
      }
    );
    return normalizeExceptionResultDto(data);
  },
};
