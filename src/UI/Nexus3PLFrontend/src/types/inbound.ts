export enum ReceivingStatus {
  Started = 1,
  InProgress = 2,
  Completed = 3,
  Flagged = 4,
  Cancelled = 5,
}

export enum ReceiptConditionStatus {
  Sound = 1,
  Damaged = 2,
  Defective = 3,
}

export enum PutawayTaskStatus {
  Pending = 1,
  Assigned = 2,
  InTransit = 3,
  Completed = 4,
  ExceptionHold = 5,
  ReRouted = 6,
  Cancelled = 7,
}

export enum PutawayPriority {
  Normal = 1,
  High = 2,
  Expedite = 3,
}

export enum DiscrepancyType {
  QuantityOverage = 1,
  QuantityShortage = 2,
  DamagedGoods = 3,
  TempExcursion = 4,
  UnexpectedSKU = 5,
}

export enum DiscrepancyResolutionStatus {
  PendingReview = 1,
  Approved = 2,
  Rejected = 3,
  Quarantined = 4,
}

export interface ReceiptLineDto {
  id: string;
  receivingSessionId: string;
  itemId: string;
  sku: string;
  itemDescription: string;
  purchaseOrderLineId?: string | null;
  asnLineItemId?: string | null;
  lpn: string;
  quantityReceived: number;
  quantityExpected: number;
  lotNumber?: string | null;
  serialNumber?: string | null;
  catchWeightKg?: number | null;
  manufactureDate?: string | null;
  expirationDate?: string | null;
  condition: ReceiptConditionStatus;
  stagingLocationId?: string | null;
  stagingLocationCode?: string | null;
  notes?: string | null;
  scannedAtUtc: string;
  version: number;
}

export interface ReceivingDiscrepancyDto {
  id: string;
  receivingSessionId: string;
  receiptLineId?: string | null;
  lpn?: string | null;
  type: DiscrepancyType;
  discrepantQuantity: number;
  notes: string;
  status: DiscrepancyResolutionStatus;
  reportedAtUtc: string;
  resolvedAtUtc?: string | null;
  resolvedByUserId?: string | null;
  resolutionNotes?: string | null;
  version: number;
}

export interface ReceivingSessionDto {
  id: string;
  facilityId: string;
  facilityCode: string;
  dockDoorId?: string | null;
  doorNumber?: string | null;
  yardVisitId?: string | null;
  visitNumber?: string | null;
  trailerNumber?: string | null;
  clientId?: string | null;
  clientName?: string | null;
  asnId?: string | null;
  asnNumber?: string | null;
  purchaseOrderId?: string | null;
  poNumber?: string | null;
  sessionNumber: string;
  status: ReceivingStatus;
  startedAtUtc: string;
  completedAtUtc?: string | null;
  receivedByUserId: string;
  receivedByUserName?: string | null;
  initialProbeTemperature?: number | null;
  sealNumber?: string | null;
  sealIntact?: boolean | null;
  notes?: string | null;
  isBlindReceiving: boolean;
  requiresSupervisorApproval: boolean;
  isSupervisorApproved: boolean;
  approvedByUserId?: string | null;
  approvedAtUtc?: string | null;
  version: number;
  lines: ReceiptLineDto[];
  discrepancies: ReceivingDiscrepancyDto[];
}

export interface StartReceivingSessionRequest {
  facilityId: string;
  dockDoorId?: string | null;
  yardVisitId?: string | null;
  clientId?: string | null;
  asnId?: string | null;
  purchaseOrderId?: string | null;
  sealNumber?: string | null;
  sealIntact?: boolean | null;
  notes?: string | null;
  isBlindReceiving?: boolean;
}

export interface ScanReceiptLineRequest {
  itemId: string;
  quantityReceived: number;
  lpn?: string | null;
  purchaseOrderLineId?: string | null;
  asnLineItemId?: string | null;
  lotNumber?: string | null;
  serialNumber?: string | null;
  catchWeightKg?: number | null;
  manufactureDate?: string | null;
  expirationDate?: string | null;
  condition: ReceiptConditionStatus;
  stagingLocationId?: string | null;
  notes?: string | null;
}

export interface RecordReceivingDiscrepancyRequest {
  receiptLineId?: string | null;
  type: DiscrepancyType;
  discrepantQuantity: number;
  notes: string;
}

export interface PutawayTaskDto {
  id: string;
  clientId: string;
  facilityId: string;
  itemId: string;
  sku: string;
  itemDescription: string;
  lotId?: string | null;
  lotNumber?: string | null;
  lpnId?: string | null;
  lpnCode?: string | null;
  sourceLocationId: string;
  sourceLocationCode: string;
  suggestedLocationId: string;
  suggestedLocationCode: string;
  suggestedLocationBarcode: string;
  targetLocationId?: string;
  targetLocationCode?: string;
  actualLocationId?: string | null;
  actualLocationCode?: string | null;
  quantity: number;
  status: PutawayTaskStatus;
  priority: PutawayPriority;
  assignedUserId?: string | null;
  createdAtUtc: string;
  completedAtUtc?: string | null;
  completedAt?: string | null;
  exceptionReason?: string | null;
  notes?: string | null;
  version: number;
}

export interface GeneratePutawayTasksRequest {
  facilityId: string;
  clientId?: string | null;
}

export interface ConfirmPutawayTaskRequest {
  scannedLpn: string;
  scannedLocationBarcode: string;
  allowOverride?: boolean;
}

export interface ReportExceptionRequest {
  exceptionReason: string;
  autoReRoute?: boolean;
}

export interface AssignTaskRequest {
  userId?: string | null;
}

export interface GetPutawayTasksQuery {
  facilityId?: string | null;
  clientId?: string | null;
  status?: PutawayTaskStatus | null;
  itemId?: string | null;
  searchTerm?: string | null;
  pageNumber?: number;
  pageSize?: number;
}
