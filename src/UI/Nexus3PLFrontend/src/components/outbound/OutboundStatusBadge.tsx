import React from 'react';
import {
  CarrierServiceLevel,
  DeliveryExceptionSeverity,
  DeliveryExceptionStatus,
  ImportBatchStatus,
  OrderDeliveryStatus,
  OutboundOrderDto,
  OutboundOrderStatus,
  OutboundOrderType,
  PackagingMaterialType,
  PackageStatus,
  PackingSessionStatus,
  PickTaskStatus,
  PickWaveStatus,
  PickWaveType,
  ShipmentCustodyStatus,
  ShipmentStatus,
  ShippingLabelStatus,
  ShortPickReason,
  TrackingMilestoneStatus,
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

export function getPackingSessionStatusBadgeClass(status: PackingSessionStatus): string {
  switch (status) {
    case PackingSessionStatus.Completed:
      return 'badge-success';
    case PackingSessionStatus.Suspended:
      return 'badge-warning';
    case PackingSessionStatus.Aborted:
      return 'badge-danger';
    default:
      return 'badge-info';
  }
}

export function getPackageStatusBadgeClass(status: PackageStatus): string {
  switch (status) {
    case PackageStatus.Packed:
    case PackageStatus.LabelPrinted:
    case PackageStatus.Loaded:
      return 'badge-success';
    case PackageStatus.Open:
    case PackageStatus.Staged:
      return 'badge-warning';
    case PackageStatus.Cancelled:
      return 'badge-danger';
    default:
      return 'badge-info';
  }
}

export function getShippingLabelStatusBadgeClass(status: ShippingLabelStatus): string {
  switch (status) {
    case ShippingLabelStatus.Printed:
    case ShippingLabelStatus.Generated:
      return 'badge-success';
    case ShippingLabelStatus.Voided:
      return 'badge-danger';
    default:
      return 'badge-info';
  }
}

export function formatPackagingMaterialTypeLabel(type: PackagingMaterialType | string): string {
  switch (type) {
    case PackagingMaterialType.CorrugatedBox:
      return 'Corrugated Box';
    case PackagingMaterialType.PaddedEnvelope:
      return 'Padded Envelope';
    case PackagingMaterialType.Pallet:
      return 'Pallet Base';
    case PackagingMaterialType.CustomCrate:
      return 'Custom Crate';
    default:
      return String(type);
  }
}

export function formatServiceLevelLabel(level: CarrierServiceLevel | string): string {
  switch (level) {
    case CarrierServiceLevel.StandardGround:
      return 'Standard Ground';
    case CarrierServiceLevel.NextDayAir:
      return 'Next Day Air';
    case CarrierServiceLevel.SecondDayAir:
      return 'Second Day Air';
    case CarrierServiceLevel.PriorityOvernight:
      return 'Priority Overnight';
    case CarrierServiceLevel.FreightLTL:
      return 'Freight LTL';
    case CarrierServiceLevel.FreightFTL:
      return 'Freight FTL';
    case CarrierServiceLevel.CustomerPickup:
      return 'Customer Pickup';
    default:
      return String(level);
  }
}

export const PackingSessionStatusBadge: React.FC<{ status: PackingSessionStatus }> = ({ status }) => (
  <span className={`badge ${getPackingSessionStatusBadgeClass(status)}`}>{status}</span>
);

export const PackageStatusBadge: React.FC<{ status: PackageStatus }> = ({ status }) => (
  <span className={`badge ${getPackageStatusBadgeClass(status)}`}>{status}</span>
);

export const ShippingLabelStatusBadge: React.FC<{ status: ShippingLabelStatus }> = ({ status }) => (
  <span className={`badge ${getShippingLabelStatusBadgeClass(status)}`}>{status}</span>
);

export function getShipmentStatusBadgeClass(status: ShipmentStatus): string {
  switch (status) {
    case ShipmentStatus.Dispatched:
      return 'badge-success';
    case ShipmentStatus.ReadyForDispatch:
      return 'badge-warning';
    case ShipmentStatus.Cancelled:
      return 'badge-danger';
    default:
      return 'badge-info';
  }
}

export function getShipmentCustodyBadgeClass(status: ShipmentCustodyStatus): string {
  switch (status) {
    case ShipmentCustodyStatus.Confirmed:
      return 'badge-success';
    case ShipmentCustodyStatus.PendingHandover:
      return 'badge-warning';
    case ShipmentCustodyStatus.Revoked:
      return 'badge-danger';
    default:
      return 'badge-info';
  }
}

export const ShipmentStatusBadge: React.FC<{ status: ShipmentStatus }> = ({ status }) => (
  <span className={`badge ${getShipmentStatusBadgeClass(status)}`}>{status}</span>
);

export const ShipmentCustodyBadge: React.FC<{ status: ShipmentCustodyStatus }> = ({ status }) => (
  <span className={`badge ${getShipmentCustodyBadgeClass(status)}`}>{status}</span>
);

export function getTrackingMilestoneBadgeClass(status: TrackingMilestoneStatus): string {
  switch (status) {
    case TrackingMilestoneStatus.Delivered:
      return 'badge-success';
    case TrackingMilestoneStatus.DeliveryException:
    case TrackingMilestoneStatus.ReturnedToSender:
      return 'badge-danger';
    case TrackingMilestoneStatus.OutForDelivery:
    case TrackingMilestoneStatus.PendingHandover:
      return 'badge-warning';
    default:
      return 'badge-info';
  }
}

export function getOrderDeliveryStatusBadgeClass(status: OrderDeliveryStatus): string {
  switch (status) {
    case OrderDeliveryStatus.Delivered:
      return 'badge-success';
    case OrderDeliveryStatus.DeliveryException:
      return 'badge-danger';
    case OrderDeliveryStatus.PartiallyDelivered:
    case OrderDeliveryStatus.PendingShipment:
      return 'badge-warning';
    default:
      return 'badge-info';
  }
}

export function getDeliveryExceptionStatusBadgeClass(status: DeliveryExceptionStatus): string {
  switch (status) {
    case DeliveryExceptionStatus.Resolved:
      return 'badge-success';
    case DeliveryExceptionStatus.Open:
    case DeliveryExceptionStatus.PendingReturnAuthorization:
    case DeliveryExceptionStatus.ReturnToSenderInitiated:
      return 'badge-danger';
    case DeliveryExceptionStatus.Voided:
      return 'badge-info';
    default:
      return 'badge-warning';
  }
}

export function getDeliveryExceptionSeverityBadgeClass(severity: DeliveryExceptionSeverity): string {
  switch (severity) {
    case DeliveryExceptionSeverity.Critical:
    case DeliveryExceptionSeverity.High:
      return 'badge-danger';
    case DeliveryExceptionSeverity.Medium:
      return 'badge-warning';
    default:
      return 'badge-info';
  }
}

export const TrackingMilestoneBadge: React.FC<{ status: TrackingMilestoneStatus }> = ({ status }) => (
  <span className={`badge ${getTrackingMilestoneBadgeClass(status)}`}>{status}</span>
);

export const OrderDeliveryStatusBadge: React.FC<{ status: OrderDeliveryStatus }> = ({ status }) => (
  <span className={`badge ${getOrderDeliveryStatusBadgeClass(status)}`}>{status}</span>
);

export const DeliveryExceptionStatusBadge: React.FC<{ status: DeliveryExceptionStatus }> = ({ status }) => (
  <span className={`badge ${getDeliveryExceptionStatusBadgeClass(status)}`}>{status}</span>
);

export const DeliveryExceptionSeverityBadge: React.FC<{ severity: DeliveryExceptionSeverity }> = ({ severity }) => (
  <span className={`badge ${getDeliveryExceptionSeverityBadgeClass(severity)}`}>{severity}</span>
);
