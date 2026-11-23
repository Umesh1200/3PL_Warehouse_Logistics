import React from 'react';
import {
  ImportBatchStatus,
  OutboundOrderDto,
  OutboundOrderStatus,
  OutboundOrderType,
  PickTaskStatus,
  PickWaveStatus,
  PickWaveType,
  ShortPickReason,
} from '../../types/outbound';

export function getOrderStatusBadgeClass(status: OutboundOrderStatus): string {
  switch (status) {
    case OutboundOrderStatus.Allocated:
    case OutboundOrderStatus.Completed:
      return 'badge-success';
    case OutboundOrderStatus.PartiallyAllocated:
    case OutboundOrderStatus.OnHold:
      return 'badge-warning';
    case OutboundOrderStatus.Cancelled:
      return 'badge-danger';
    default:
      return 'badge-info';
  }
}

export function getWaveStatusBadgeClass(status: PickWaveStatus): string {
  switch (status) {
    case PickWaveStatus.Completed:
    case PickWaveStatus.Released:
      return 'badge-success';
    case PickWaveStatus.Planning:
      return 'badge-warning';
    case PickWaveStatus.Cancelled:
      return 'badge-danger';
    default:
      return 'badge-info';
  }
}

export function getTaskStatusBadgeClass(status: PickTaskStatus): string {
  switch (status) {
    case PickTaskStatus.Completed:
      return 'badge-success';
    case PickTaskStatus.ShortPicked:
    case PickTaskStatus.Pending:
      return 'badge-warning';
    case PickTaskStatus.Cancelled:
      return 'badge-danger';
    default:
      return 'badge-info';
  }
}

export function getImportStatusBadgeClass(status: ImportBatchStatus): string {
  switch (status) {
    case ImportBatchStatus.Completed:
      return 'badge-success';
    case ImportBatchStatus.CompletedWithErrors:
      return 'badge-warning';
    case ImportBatchStatus.Failed:
      return 'badge-danger';
    default:
      return 'badge-info';
  }
}

export function formatOrderTypeLabel(type: OutboundOrderType): string {
  switch (type) {
    case OutboundOrderType.B2BSalesOrder:
      return 'B2B Sales';
    case OutboundOrderType.StockTransferOrder:
      return 'Stock Transfer';
    case OutboundOrderType.EcommerceFulfillment:
      return 'E-Commerce';
    default:
      return String(type);
  }
}

export function formatWaveTypeLabel(type: PickWaveType): string {
  switch (type) {
    case PickWaveType.DiscreteOrder:
      return 'Discrete Order';
    case PickWaveType.BatchPick:
      return 'Batch Pick';
    case PickWaveType.ZonePick:
      return 'Zone Pick';
    case PickWaveType.BulkPick:
      return 'Bulk Pick';
    default:
      return String(type);
  }
}

export function formatShortPickReasonLabel(reason: ShortPickReason): string {
  switch (reason) {
    case ShortPickReason.BinEmpty:
      return 'Bin Empty (Zero Physical Stock)';
    case ShortPickReason.DamagedStock:
      return 'Damaged / Unusable Stock';
    case ShortPickReason.BarcodeUnreadable:
      return 'Barcode Unreadable';
    case ShortPickReason.WrongItemInBin:
      return 'Wrong Item in Bin';
    case ShortPickReason.LotExpired:
      return 'Lot Expired';
    case ShortPickReason.BinInaccessible:
      return 'Bin Inaccessible / Obstructed';
    default:
      return String(reason);
  }
}

export function computeOrderAllocationProgress(order: OutboundOrderDto): {
  ordered: number;
  allocated: number;
  percent: number;
} {
  const ordered = order.lines.reduce((sum, l) => sum + (Number(l.quantityOrdered) || 0), 0);
  const allocated = order.lines.reduce((sum, l) => sum + (Number(l.quantityAllocated) || 0), 0);
  const percent = ordered > 0 ? Math.min(100, Math.round((allocated / ordered) * 100)) : 0;
  return { ordered, allocated, percent };
}

export function generateActionIdempotencyKey(prefix: string): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return `${prefix}-${crypto.randomUUID()}`;
  }
  return `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
}

export const OrderStatusBadge: React.FC<{ status: OutboundOrderStatus }> = ({ status }) => (
  <span className={`badge ${getOrderStatusBadgeClass(status)}`}>{status}</span>
);

export const WaveStatusBadge: React.FC<{ status: PickWaveStatus }> = ({ status }) => (
  <span className={`badge ${getWaveStatusBadgeClass(status)}`}>{status}</span>
);

export const TaskStatusBadge: React.FC<{ status: PickTaskStatus }> = ({ status }) => (
  <span className={`badge ${getTaskStatusBadgeClass(status)}`}>{status}</span>
);
