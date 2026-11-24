import { PagedResult } from './auth';

/**
 * Authoritative Outbound Orders, Allocation, and Pick Waves Type Contracts
 * Directly mirrors .NET 10 WebApi DTOs and Domain Enums (JsonStringEnumConverter).
 */

export enum OutboundOrderType {
  B2BSalesOrder = 'B2BSalesOrder',
  StockTransferOrder = 'StockTransferOrder',
  EcommerceFulfillment = 'EcommerceFulfillment',
}

export enum OutboundOrderStatus {
  Draft = 'Draft',
  Released = 'Released',
  Allocated = 'Allocated',
  InProgress = 'InProgress',
  Completed = 'Completed',
  Cancelled = 'Cancelled',
  OnHold = 'OnHold',
  PartiallyAllocated = 'PartiallyAllocated',
}

export enum OutboundOrderLineStatus {
  Pending = 'Pending',
  PartiallyAllocated = 'PartiallyAllocated',
  Allocated = 'Allocated',
  Completed = 'Completed',
  Cancelled = 'Cancelled',
}

export enum AllocationStrategyType {
  FIFO = 'FIFO',
  FEFO = 'FEFO',
  Manual = 'Manual',
}

export enum AllocationStatus {
  Allocated = 'Allocated',
  PartiallyPicked = 'PartiallyPicked',
  Picked = 'Picked',
  Released = 'Released',
  Cancelled = 'Cancelled',
}

export enum ShortageReason {
  InsufficientStock = 'InsufficientStock',
  NoValidLotsAvailable = 'NoValidLotsAvailable',
  ExpiredLotsOnly = 'ExpiredLotsOnly',
  LotRestrictionUnmet = 'LotRestrictionUnmet',
  LocationLocked = 'LocationLocked',
  TempClassMismatch = 'TempClassMismatch',
}

export enum CarrierServiceLevel {
  StandardGround = 'StandardGround',
  NextDayAir = 'NextDayAir',
  SecondDayAir = 'SecondDayAir',
  PriorityOvernight = 'PriorityOvernight',
  FreightLTL = 'FreightLTL',
  FreightFTL = 'FreightFTL',
  CustomerPickup = 'CustomerPickup',
}

export enum PickWaveType {
  DiscreteOrder = 'DiscreteOrder',
  BatchPick = 'BatchPick',
  ZonePick = 'ZonePick',
  BulkPick = 'BulkPick',
}

export enum PickWaveStatus {
  Planning = 'Planning',
  Released = 'Released',
  InProgress = 'InProgress',
  Completed = 'Completed',
  Cancelled = 'Cancelled',
}

export enum PickTaskStatus {
  Pending = 'Pending',
  Assigned = 'Assigned',
  InProgress = 'InProgress',
  Completed = 'Completed',
  ShortPicked = 'ShortPicked',
  Cancelled = 'Cancelled',
}

export enum ShortPickReason {
  BinEmpty = 'BinEmpty',
  DamagedStock = 'DamagedStock',
  BarcodeUnreadable = 'BarcodeUnreadable',
  WrongItemInBin = 'WrongItemInBin',
  LotExpired = 'LotExpired',
  BinInaccessible = 'BinInaccessible',
}

export enum PackingSessionStatus {
  Active = 'Active',
  Completed = 'Completed',
  Suspended = 'Suspended',
  Aborted = 'Aborted',
}

export enum PackageStatus {
  Open = 'Open',
  Packed = 'Packed',
  LabelPrinted = 'LabelPrinted',
  Staged = 'Staged',
  Loaded = 'Loaded',
  Cancelled = 'Cancelled',
}

export enum ShippingLabelStatus {
  Generated = 'Generated',
  Printed = 'Printed',
  Voided = 'Voided',
}

export enum PackagingMaterialType {
  CorrugatedBox = 'CorrugatedBox',
  PaddedEnvelope = 'PaddedEnvelope',
  Pallet = 'Pallet',
  CustomCrate = 'CustomCrate',
}

export enum HandlingUnitStatus {
  Available = 'Available',
  InUse = 'InUse',
  InTransit = 'InTransit',
  AtPackingStation = 'AtPackingStation',
  Damaged = 'Damaged',
}

export enum StorageTempClass {
  Ambient = 'Ambient',
  TemperatureControlled = 'TemperatureControlled',
  Refrigerated = 'Refrigerated',
  Frozen = 'Frozen',
}

export enum ImportBatchStatus {
  Processing = 'Processing',
  Completed = 'Completed',
  CompletedWithErrors = 'CompletedWithErrors',
  Failed = 'Failed',
}

function normalizeStringEnum<T extends string>(
  raw: unknown,
  numMap: Record<number, T>,
  allowed: readonly T[],
  enumName: string
): T {
  if (typeof raw === 'number' && Number.isInteger(raw) && raw in numMap) {
    return numMap[raw];
  }
  if (typeof raw === 'string') {
    const trimmed = raw.trim();
    if (trimmed !== '') {
      const numeric = Number(trimmed);
      if (Number.isInteger(numeric) && numeric in numMap) {
        return numMap[numeric];
      }
      const found = allowed.find((v) => v.toLowerCase() === trimmed.toLowerCase());
      if (found) return found;
    }
  }
  throw new Error(`Invalid or unrecognized ${enumName} value from server: ${String(raw)}`);
}

const ORDER_STATUS_NUM: Record<number, OutboundOrderStatus> = {
  1: OutboundOrderStatus.Draft,
  2: OutboundOrderStatus.Released,
  3: OutboundOrderStatus.Allocated,
  4: OutboundOrderStatus.InProgress,
  5: OutboundOrderStatus.Completed,
  6: OutboundOrderStatus.Cancelled,
  7: OutboundOrderStatus.OnHold,
  8: OutboundOrderStatus.PartiallyAllocated,
};

const ORDER_TYPE_NUM: Record<number, OutboundOrderType> = {
  1: OutboundOrderType.B2BSalesOrder,
  2: OutboundOrderType.StockTransferOrder,
  3: OutboundOrderType.EcommerceFulfillment,
};

const LINE_STATUS_NUM: Record<number, OutboundOrderLineStatus> = {
  1: OutboundOrderLineStatus.Pending,
  2: OutboundOrderLineStatus.PartiallyAllocated,
  3: OutboundOrderLineStatus.Allocated,
  4: OutboundOrderLineStatus.Completed,
  5: OutboundOrderLineStatus.Cancelled,
};

const SERVICE_LEVEL_NUM: Record<number, CarrierServiceLevel> = {
  1: CarrierServiceLevel.StandardGround,
  2: CarrierServiceLevel.NextDayAir,
  3: CarrierServiceLevel.SecondDayAir,
  4: CarrierServiceLevel.PriorityOvernight,
  5: CarrierServiceLevel.FreightLTL,
  6: CarrierServiceLevel.FreightFTL,
  7: CarrierServiceLevel.CustomerPickup,
};

const WAVE_STATUS_NUM: Record<number, PickWaveStatus> = {
  1: PickWaveStatus.Planning,
  2: PickWaveStatus.Released,
  3: PickWaveStatus.InProgress,
  4: PickWaveStatus.Completed,
  5: PickWaveStatus.Cancelled,
};

const WAVE_TYPE_NUM: Record<number, PickWaveType> = {
  1: PickWaveType.DiscreteOrder,
  2: PickWaveType.BatchPick,
  3: PickWaveType.ZonePick,
  4: PickWaveType.BulkPick,
};

const TASK_STATUS_NUM: Record<number, PickTaskStatus> = {
  1: PickTaskStatus.Pending,
  2: PickTaskStatus.Assigned,
  3: PickTaskStatus.InProgress,
  4: PickTaskStatus.Completed,
  5: PickTaskStatus.ShortPicked,
  6: PickTaskStatus.Cancelled,
};

const SHORT_PICK_REASON_NUM: Record<number, ShortPickReason> = {
  1: ShortPickReason.BinEmpty,
  2: ShortPickReason.DamagedStock,
  3: ShortPickReason.BarcodeUnreadable,
  4: ShortPickReason.WrongItemInBin,
  5: ShortPickReason.LotExpired,
  6: ShortPickReason.BinInaccessible,
};

const ALLOC_STATUS_NUM: Record<number, AllocationStatus> = {
  1: AllocationStatus.Allocated,
  2: AllocationStatus.PartiallyPicked,
  3: AllocationStatus.Picked,
  4: AllocationStatus.Released,
  5: AllocationStatus.Cancelled,
};

const IMPORT_STATUS_NUM: Record<number, ImportBatchStatus> = {
  1: ImportBatchStatus.Processing,
  2: ImportBatchStatus.Completed,
  3: ImportBatchStatus.CompletedWithErrors,
  4: ImportBatchStatus.Failed,
};

const SHORTAGE_REASON_NUM: Record<number, ShortageReason> = {
  1: ShortageReason.InsufficientStock,
  2: ShortageReason.NoValidLotsAvailable,
  3: ShortageReason.ExpiredLotsOnly,
  4: ShortageReason.LotRestrictionUnmet,
  5: ShortageReason.LocationLocked,
  6: ShortageReason.TempClassMismatch,
};

const PACKING_SESSION_STATUS_NUM: Record<number, PackingSessionStatus> = {
  1: PackingSessionStatus.Active,
  2: PackingSessionStatus.Completed,
  3: PackingSessionStatus.Suspended,
  4: PackingSessionStatus.Aborted,
};

const PACKAGE_STATUS_NUM: Record<number, PackageStatus> = {
  1: PackageStatus.Open,
  2: PackageStatus.Packed,
  3: PackageStatus.LabelPrinted,
  4: PackageStatus.Staged,
  5: PackageStatus.Loaded,
  6: PackageStatus.Cancelled,
};

const SHIPPING_LABEL_STATUS_NUM: Record<number, ShippingLabelStatus> = {
  1: ShippingLabelStatus.Generated,
  2: ShippingLabelStatus.Printed,
  3: ShippingLabelStatus.Voided,
};

const PACKAGING_MATERIAL_TYPE_NUM: Record<number, PackagingMaterialType> = {
  1: PackagingMaterialType.CorrugatedBox,
  2: PackagingMaterialType.PaddedEnvelope,
  3: PackagingMaterialType.Pallet,
  4: PackagingMaterialType.CustomCrate,
};

const HANDLING_UNIT_STATUS_NUM: Record<number, HandlingUnitStatus> = {
  1: HandlingUnitStatus.Available,
  2: HandlingUnitStatus.InUse,
  3: HandlingUnitStatus.InTransit,
  4: HandlingUnitStatus.AtPackingStation,
  5: HandlingUnitStatus.Damaged,
};

const STORAGE_TEMP_NUM: Record<number, StorageTempClass> = {
  1: StorageTempClass.Ambient,
  2: StorageTempClass.TemperatureControlled,
  3: StorageTempClass.Refrigerated,
  4: StorageTempClass.Frozen,
};

export const normalizePackingSessionStatus = (v: unknown) =>
  normalizeStringEnum(
    v,
    PACKING_SESSION_STATUS_NUM,
    Object.values(PackingSessionStatus),
    'PackingSessionStatus'
  );

export const normalizePackageStatus = (v: unknown) =>
  normalizeStringEnum(v, PACKAGE_STATUS_NUM, Object.values(PackageStatus), 'PackageStatus');

export const normalizeShippingLabelStatus = (v: unknown) =>
  normalizeStringEnum(
    v,
    SHIPPING_LABEL_STATUS_NUM,
    Object.values(ShippingLabelStatus),
    'ShippingLabelStatus'
  );

export const normalizePackagingMaterialType = (v: unknown) =>
  normalizeStringEnum(
    v,
    PACKAGING_MATERIAL_TYPE_NUM,
    Object.values(PackagingMaterialType),
    'PackagingMaterialType'
  );

export const normalizeHandlingUnitStatus = (v: unknown) =>
  normalizeStringEnum(
    v,
    HANDLING_UNIT_STATUS_NUM,
    Object.values(HandlingUnitStatus),
    'HandlingUnitStatus'
  );

export const normalizeOrderStatus = (v: unknown) =>
  normalizeStringEnum(v, ORDER_STATUS_NUM, Object.values(OutboundOrderStatus), 'OutboundOrderStatus');

export const normalizeOrderType = (v: unknown) =>
  normalizeStringEnum(v, ORDER_TYPE_NUM, Object.values(OutboundOrderType), 'OutboundOrderType');

export const normalizeLineStatus = (v: unknown) =>
  normalizeStringEnum(v, LINE_STATUS_NUM, Object.values(OutboundOrderLineStatus), 'OutboundOrderLineStatus');

export const normalizeServiceLevel = (v: unknown) =>
  normalizeStringEnum(v, SERVICE_LEVEL_NUM, Object.values(CarrierServiceLevel), 'CarrierServiceLevel');

export const normalizeWaveStatus = (v: unknown) =>
  normalizeStringEnum(v, WAVE_STATUS_NUM, Object.values(PickWaveStatus), 'PickWaveStatus');

export const normalizeWaveType = (v: unknown) =>
  normalizeStringEnum(v, WAVE_TYPE_NUM, Object.values(PickWaveType), 'PickWaveType');

export const normalizeTaskStatus = (v: unknown) =>
  normalizeStringEnum(v, TASK_STATUS_NUM, Object.values(PickTaskStatus), 'PickTaskStatus');

export const normalizeShortPickReason = (v: unknown) =>
  normalizeStringEnum(v, SHORT_PICK_REASON_NUM, Object.values(ShortPickReason), 'ShortPickReason');

export const normalizeAllocationStatus = (v: unknown) =>
  normalizeStringEnum(v, ALLOC_STATUS_NUM, Object.values(AllocationStatus), 'AllocationStatus');

export const normalizeImportBatchStatus = (v: unknown) =>
  normalizeStringEnum(v, IMPORT_STATUS_NUM, Object.values(ImportBatchStatus), 'ImportBatchStatus');

export const normalizeShortageReason = (v: unknown) =>
  normalizeStringEnum(v, SHORTAGE_REASON_NUM, Object.values(ShortageReason), 'ShortageReason');

export const normalizeStorageTempClass = (v: unknown) =>
  normalizeStringEnum(v, STORAGE_TEMP_NUM, Object.values(StorageTempClass), 'StorageTempClass');

export interface ShippingAddressDto {
  recipientName: string;
  companyName?: string | null;
  addressLine1: string;
  addressLine2?: string | null;
  city: string;
  stateOrProvince: string;
  postalCode: string;
  countryCode: string;
  phoneNumber?: string | null;
  deliveryInstructions?: string | null;
  isAmended?: boolean;
  originalAddressSnapshot?: string | null;
  amendmentReason?: string | null;
  amendedAtUtc?: string | null;
}

export interface OutboundOrderLineDto {
  id: string;
  outboundOrderId: string;
  itemId: string;
  sku: string;
  itemDescription: string;
  packagingUnitId?: string | null;
  packagingUnitName?: string | null;
  lineNumber: number;
  quantityOrdered: number;
  quantityAllocated: number;
  quantityShipped: number;
  remainingDemand: number;
  requiredLotNumber?: string | null;
  minExpiryDateUtc?: string | null;
  status: OutboundOrderLineStatus;
  notes?: string | null;
}

export interface OrderStatusHistoryDto {
  fromStatus: OutboundOrderStatus;
  toStatus: OutboundOrderStatus;
  reason?: string | null;
  notes?: string | null;
  changedByUserId: string;
  changedAtUtc: string;
}

export interface OutboundOrderDto {
  id: string;
  clientId: string;
  facilityId: string;
  facilityName: string;
  destinationFacilityId?: string | null;
  destinationFacilityName?: string | null;
  customerId?: string | null;
  customerCode?: string | null;
  customerName: string;
  orderNumber: string;
  externalOrderReference?: string | null;
  orderType: OutboundOrderType;
  status: OutboundOrderStatus;
  statusBeforeHold?: OutboundOrderStatus | null;
  priority: number;
  serviceLevel: CarrierServiceLevel;
  carrierCode?: string | null;
  destinationAddress?: string | null;
  orderDateUtc: string;
  requiredShipDateUtc?: string | null;
  earliestShipDateUtc?: string | null;
  holdReason?: string | null;
  heldAtUtc?: string | null;
  cancellationReason?: string | null;
  cancelledAtUtc?: string | null;
  notes?: string | null;
  version: number;
  createdAt: string;
  shippingAddress?: ShippingAddressDto | null;
  lines: OutboundOrderLineDto[];
  statusHistories: OrderStatusHistoryDto[];
}

export interface LineStockAvailabilityDto {
  lineId: string;
  lineNumber: number;
  itemId: string;
  sku: string;
  itemDescription: string;
  quantityRequested: number;
  quantityAvailable: number;
  shortageQuantity: number;
  isFullyAvailable: boolean;
  requiredLotNumber?: string | null;
  minExpiryDateUtc?: string | null;
}

export interface StockAvailabilityAssessmentDto {
  orderId: string;
  orderNumber: string;
  isFullyAvailable: boolean;
  overallFulfillmentPercent: number;
  lines: LineStockAvailabilityDto[];
}

export interface AllocationItemDto {
  allocationId: string;
  stockOnHandId: string;
  locationId: string;
  locationCode: string;
  lotId?: string | null;
  lotNumber?: string | null;
  lpnCode?: string | null;
  quantityAllocated: number;
  status: AllocationStatus;
}

export interface LineAllocationSummaryDto {
  lineId: string;
  lineNumber: number;
  itemId: string;
  sku: string;
  quantityOrdered: number;
  quantityAllocated: number;
  remainingDemand: number;
  status: OutboundOrderLineStatus;
  allocations: AllocationItemDto[];
}

export interface AllocationShortageDto {
  shortageId: string;
  lineId: string;
  itemId: string;
  shortageQuantity: number;
  reason: ShortageReason | string;
  notes: string;
}

export interface AllocationExecutionResultDto {
  outboundOrderId: string;
  orderNumber: string;
  status: OutboundOrderStatus;
  totalQuantityOrdered: number;
  totalQuantityAllocated: number;
  totalShortageQuantity: number;
  isFullyAllocated: boolean;
  isDuplicateRequest: boolean;
  attemptCount: number;
  lineSummaries: LineAllocationSummaryDto[];
  shortages: AllocationShortageDto[];
}

export interface DeallocationExecutionResultDto {
  outboundOrderId: string;
  orderNumber: string;
  restoredStatus: OutboundOrderStatus;
  totalQuantityDeallocated: number;
  allocationsReleasedCount: number;
  reason: string;
}

export interface ReleaseAllocationResultDto {
  allocationId: string;
  outboundOrderId: string;
  lineId: string;
  quantityReleased: number;
  activeRemainingQuantity: number;
  allocationStatus: AllocationStatus;
  orderStatus: OutboundOrderStatus;
  lineStatus: OutboundOrderLineStatus;
}

export interface CreateShippingAddressRequest {
  recipientName: string;
  companyName?: string;
  addressLine1: string;
  addressLine2?: string;
  city: string;
  stateOrProvince: string;
  postalCode: string;
  countryCode: string;
  phoneNumber?: string;
  deliveryInstructions?: string;
}

export interface CreateOutboundOrderLineRequest {
  itemId: string;
  quantityOrdered: number;
  packagingUnitId?: string;
  requiredLotNumber?: string;
  minExpiryDateUtc?: string;
  notes?: string;
}

export interface CreateOutboundOrderRequest {
  facilityId: string;
  destinationFacilityId?: string;
  customerId?: string;
  orderNumber?: string;
  externalOrderReference?: string;
  orderType: OutboundOrderType;
  priority: number;
  serviceLevel: CarrierServiceLevel;
  carrierCode?: string;
  requiredShipDateUtc?: string;
  earliestShipDateUtc?: string;
  notes?: string;
  shippingAddress: CreateShippingAddressRequest;
  lines: CreateOutboundOrderLineRequest[];
}

export interface ImportOutboundOrdersBulkRequest {
  facilityId: string;
  sourceFileName: string;
  orders: CreateOutboundOrderRequest[];
}

export interface OrderImportBatchRowDto {
  rowNumber: number;
  externalOrderReference?: string | null;
  isSuccess: boolean;
  isDuplicate: boolean;
  errorMessage?: string | null;
  createdOutboundOrderId?: string | null;
}

export interface OrderImportBatchDto {
  batchId: string;
  batchNumber: string;
  sourceFileName: string;
  totalRows: number;
  successfulRows: number;
  failedRows: number;
  duplicateRows: number;
  status: ImportBatchStatus;
  startedAtUtc: string;
  completedAtUtc?: string | null;
  rows: OrderImportBatchRowDto[];
}

export interface GetOutboundOrdersParams {
  clientId?: string;
  facilityId?: string;
  status?: OutboundOrderStatus;
  orderType?: OutboundOrderType;
  priority?: number;
  searchTerm?: string;
  fromDateUtc?: string;
  toDateUtc?: string;
  pageNumber?: number;
  pageSize?: number;
}

export interface PickWaveOrderDto {
  id: string;
  outboundOrderId: string;
  orderNumber: string;
  customerName: string;
  priority: number;
  sequenceNumber: number;
  orderStatus: OutboundOrderStatus;
}

export interface PickTaskDto {
  id: string;
  pickWaveId: string;
  outboundOrderId: string;
  orderNumber: string;
  outboundOrderLineId: string;
  inventoryAllocationId: string;
  stockOnHandId: string;
  itemId: string;
  sku: string;
  itemDescription: string;
  sourceLocationId: string;
  sourceLocationCode: string;
  destinationLocationId?: string | null;
  destinationLocationCode?: string | null;
  lotId?: string | null;
  lotNumber?: string | null;
  expectedLpnCode?: string | null;
  targetHandlingUnitId?: string | null;
  targetToteBarcode?: string | null;
  quantityToPick: number;
  quantityPicked: number;
  shortPickQuantity: number;
  pickSequence: number;
  status: PickTaskStatus;
  shortPickReason?: ShortPickReason | string | null;
  assignedUserId?: string | null;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
  version: number;
}

export interface GetPickTasksParams {
  facilityId: string;
  clientId?: string;
  pickWaveId?: string;
  outboundOrderId?: string;
  status?: PickTaskStatus;
  statuses?: PickTaskStatus[];
  assignedUserId?: string;
  searchTerm?: string;
  pageNumber?: number;
  pageSize?: number;
}

export interface EligiblePickOperatorDto {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  assignedFacilityId?: string | null;
}

export interface ConfirmPickTaskRequest {
  scannedLocationBarcode: string;
  scannedItemBarcode: string;
  scannedLotNumber?: string | null;
  scannedToteLpn: string;
  quantity: number;
  idempotencyKey?: string;
}

export interface PickConfirmationResultDto {
  isSuccess: boolean;
  pickTaskId: string;
  taskStatus: PickTaskStatus;
  quantityPicked: number;
  shortPickQuantity: number;
  destinationStockId: string;
  destinationHandlingUnitId: string;
  destinationToteBarcode: string;
  inventoryTransactionId: number;
  waveCompleted: boolean;
  orderCompleted: boolean;
  message: string;
}

export interface ReportShortPickRequest {
  quantityPicked: number;
  reason: ShortPickReason;
  notes?: string;
  scannedLocationBarcode?: string;
  scannedItemBarcode?: string;
  scannedToteLpn?: string;
  idempotencyKey?: string;
}

export interface ShortPickExecutionResultDto {
  isSuccess: boolean;
  pickTaskId: string;
  taskStatus: PickTaskStatus;
  quantityPicked: number;
  discrepancyQuantity: number;
  pickExceptionId: string;
  generatedCycleCountId?: string | null;
  locationLockApplied: boolean;
  message: string;
}

export interface PickWaveDto {
  id: string;
  clientId: string;
  facilityId: string;
  waveNumber: string;
  waveType: PickWaveType;
  status: PickWaveStatus;
  priority: number;
  zoneId?: string | null;
  tempClass?: StorageTempClass | null;
  assignedToUserId?: string | null;
  releasedAtUtc?: string | null;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
  createdAt: string;
  ordersCount: number;
  tasksCount: number;
  totalQuantityToPick: number;
  totalQuantityPicked: number;
  estimatedTravelMeters?: number | null;
  waveOrders: PickWaveOrderDto[];
  tasks: PickTaskDto[];
}

export interface PickWaveGenerationRequest {
  clientId: string;
  facilityId: string;
  waveType: PickWaveType;
  priority?: number;
  zoneId?: string;
  tempClass?: StorageTempClass;
  orderIds?: string[];
  maxOrders?: number;
  waveNumber?: string;
}

export interface GetPickWavesParams {
  facilityId: string;
  status?: PickWaveStatus;
  waveType?: PickWaveType;
  pageNumber?: number;
  pageSize?: number;
}

export interface OutboundItemOptionDto {
  id: string;
  clientId: string;
  sku: string;
  description: string;
  shortName?: string | null;
  storageTempClass?: StorageTempClass | string;
  isLotTracked: boolean;
  requiresExpiryDate: boolean;
}

export interface WarehouseLocationNodeDto {
  id: string;
  facilityId: string;
  zoneId?: string | null;
  locationType: string | number;
  locationCode: string;
  barcode: string;
  isActive: boolean;
  children?: WarehouseLocationNodeDto[];
}

export interface PackingStationLocationDto {
  id: string;
  locationCode: string;
  barcode: string;
  aisleCode: string;
  isActive: boolean;
}

export interface PackagingMaterialDto {
  id: string;
  clientId?: string | null;
  facilityId: string;
  code: string;
  name: string;
  materialType: PackagingMaterialType;
  lengthCm: number;
  widthCm: number;
  heightCm: number;
  maxWeightKg: number;
  tareWeightKg: number;
  costPerUnit?: number | null;
}

export interface PackableOrderSummaryDto {
  id: string;
  clientId: string;
  orderNumber: string;
  customerName: string;
  status: OutboundOrderStatus;
  priority: number;
  serviceLevel: CarrierServiceLevel;
  totalAllocatedUnits: number;
  totalPickedUnits: number;
  totalPackedUnits: number;
}

export interface PackingSessionSummaryDto {
  id: string;
  clientId: string;
  facilityId: string;
  packingStationLocationId: string;
  packingStationLocationCode: string;
  outboundOrderId: string;
  orderNumber: string;
  operatorUserId: string;
  operatorEmail: string;
  status: PackingSessionStatus;
  startedAtUtc: string;
  completedAtUtc?: string | null;
  activePackagesCount: number;
  notes?: string | null;
}

export interface PackingSessionOrderLineDto {
  orderLineId: string;
  itemId: string;
  sku: string;
  description: string;
  quantityOrdered: number;
  quantityAllocated: number;
  quantityPicked: number;
  quantityPacked: number;
  remainingToPack: number;
  lineStatus: OutboundOrderLineStatus;
}

export interface PackingToteSummaryDto {
  handlingUnitId: string;
  lpnCode: string;
  status: HandlingUnitStatus;
  locationCode?: string | null;
  totalQuantity: number;
}

export interface PackageSummaryDto {
  id: string;
  clientId: string;
  facilityId: string;
  outboundOrderId: string;
  orderNumber: string;
  shipmentId?: string | null;
  handlingUnitId: string;
  handlingUnitLpn: string;
  packagingMaterialId?: string | null;
  packageNumber: string;
  cartonSequence: number;
  grossWeightKg: number;
  tareWeightKg: number;
  netWeightKg: number;
  lengthCm: number;
  widthCm: number;
  heightCm: number;
  volumetricWeightKg: number;
  carrierCode?: string | null;
  trackingNumber?: string | null;
  status: PackageStatus;
  packedByUserId?: string | null;
  packedAtUtc?: string | null;
  hasActiveLabel: boolean;
  latestLabelId?: string | null;
}

export interface PackingSessionDetailDto extends PackingSessionSummaryDto {
  orderLines: PackingSessionOrderLineDto[];
  packages: PackageSummaryDto[];
  pickedTotes: PackingToteSummaryDto[];
}

export interface StartPackingSessionRequest {
  facilityId: string;
  packingStationLocationId: string;
  outboundOrderId: string;
}

export interface PackingSessionResultDto {
  isSuccess: boolean;
  sessionId: string;
  status: PackingSessionStatus;
  outboundOrderId: string;
  orderNumber: string;
  packingStationLocationId: string;
  stationLocationCode: string;
  operatorUserId: string;
  startedAtUtc: string;
  activePackagesCount: number;
  message: string;
}

export interface CartonizationRecommendRequest {
  clientId: string;
  facilityId: string;
  outboundOrderId?: string;
  preferredPackagingMaterialId?: string;
  volumeUtilizationFactor?: number;
}

export interface CartonItemRecommendationDto {
  itemId: string;
  sku: string;
  quantity: number;
  totalWeightKg: number;
  totalVolumeCm3: number;
}

export interface PackageRecommendationDto {
  packageIndex: number;
  packagingMaterialId: string;
  packagingMaterialCode: string;
  packagingMaterialName: string;
  materialType: PackagingMaterialType;
  boxLengthCm: number;
  boxWidthCm: number;
  boxHeightCm: number;
  boxMaxWeightKg: number;
  tareWeightKg: number;
  estimatedNetWeightKg: number;
  estimatedGrossWeightKg: number;
  estimatedVolumeCm3: number;
  boxCapacityVolumeCm3: number;
  volumetricWeightKg: number;
  volumeUtilizationPercentage: number;
  items: CartonItemRecommendationDto[];
}

export interface CartonizationRecommendationDto {
  isSuccess: boolean;
  recommendedPackageCount: number;
  recommendedPackages: PackageRecommendationDto[];
  totalEstimatedGrossWeightKg: number;
  totalEstimatedVolumeCm3: number;
  message: string;
  warnings: string[];
}

export interface CreatePackageRequest {
  packingSessionId: string;
  packagingMaterialId?: string | null;
  overrideReason?: string;
}

export interface CreatePackageResultDto {
  isSuccess: boolean;
  packageId: string;
  packageNumber: string;
  handlingUnitId: string;
  handlingUnitLpn: string;
  status: PackageStatus;
  packagingMaterialId?: string | null;
  packagingMaterialCode?: string | null;
  lengthCm: number;
  widthCm: number;
  heightCm: number;
  tareWeightKg: number;
  message: string;
}

export interface PackItemRequest {
  packingSessionId: string;
  packageId: string;
  scannedToteLpn: string;
  scannedItemBarcode: string;
  scannedLotNumber?: string | null;
  quantity: number;
  idempotencyKey?: string;
}

export interface PackItemResultDto {
  isSuccess: boolean;
  packageId: string;
  packageNumber: string;
  packageLineId: string;
  itemId: string;
  sku: string;
  lotNumber?: string | null;
  quantityPacked: number;
  remainingToteQuantity: number;
  remainingOrderLineDemand: number;
  sourceToteLpn: string;
  cartonLpn: string;
  inventoryTransactionId: number;
  isOrderLineComplete: boolean;
  isOrderFullyPacked: boolean;
  message: string;
}

export interface ClosePackageResultDto {
  isSuccess: boolean;
  packageId: string;
  packageNumber: string;
  grossWeightKg: number;
  tareWeightKg: number;
  netWeightKg: number;
  volumetricWeightKg: number;
  status: PackageStatus;
  message: string;
}

export interface ReleaseToteResultDto {
  isSuccess: boolean;
  handlingUnitId: string;
  lpnCode: string;
  status: HandlingUnitStatus;
  message: string;
}

export interface CompletePackingSessionResultDto {
  isSuccess: boolean;
  sessionId: string;
  status: PackingSessionStatus;
  completedAtUtc?: string | null;
  totalPackagesCount: number;
  totalWeightKg: number;
  message: string;
}

export interface CarrierRateQuoteDto {
  carrierCode: string;
  carrierName: string;
  serviceLevel: CarrierServiceLevel;
  serviceDescription: string;
  totalAmount: number;
  currency: string;
  estimatedDeliveryDays: number;
  isGuaranteed: boolean;
}

export interface GetPackageRatesRequest {
  packageId: string;
  preferredCarrierCode?: string;
  preferredServiceLevel?: CarrierServiceLevel;
}

export interface GenerateShippingLabelRequest {
  packageId: string;
  carrierCode: string;
  serviceLevel: CarrierServiceLevel;
  labelFormat: string;
  idempotencyKey?: string;
  simulateFailure?: boolean;
}

export interface GenerateShippingLabelResultDto {
  isSuccess: boolean;
  packageId: string;
  packageNumber: string;
  labelId: string;
  trackingNumber: string;
  carrierCode: string;
  serviceLevel: CarrierServiceLevel;
  labelFormat: string;
  labelData: string;
  routingCode?: string | null;
  rateCharged: number;
  packageStatus: PackageStatus;
  labelStatus: ShippingLabelStatus;
  isSimulated: boolean;
  message: string;
  errorMessage?: string | null;
}

export interface VoidShippingLabelRequest {
  reason: string;
  idempotencyKey?: string;
  simulateFailure?: boolean;
}

export interface VoidShippingLabelResultDto {
  isSuccess: boolean;
  labelId: string;
  packageId: string;
  trackingNumber: string;
  labelStatus: ShippingLabelStatus;
  packageStatus: PackageStatus;
  voidConfirmationNumber: string;
  message: string;
  errorMessage?: string | null;
}

export interface ShippingLabelDto {
  id: string;
  packageId: string;
  packageNumber: string;
  trackingNumber: string;
  carrierCode: string;
  serviceLevel: CarrierServiceLevel;
  labelFormat: string;
  labelData: string;
  routingCode?: string | null;
  status: ShippingLabelStatus;
  generatedAtUtc: string;
  printedAtUtc?: string | null;
  voidedAtUtc?: string | null;
  voidReason?: string | null;
}

export interface GetPackingSessionsParams {
  facilityId: string;
  clientId?: string;
  status?: PackingSessionStatus;
  outboundOrderId?: string;
  packingStationLocationId?: string;
  searchTerm?: string;
  pageNumber?: number;
  pageSize?: number;
}

export interface GetPackingPackagesParams {
  facilityId: string;
  clientId?: string;
  packingSessionId?: string;
  outboundOrderId?: string;
  status?: PackageStatus;
  searchTerm?: string;
  pageNumber?: number;
  pageSize?: number;
}

export type OutboundPagedResult<T> = PagedResult<T>;
