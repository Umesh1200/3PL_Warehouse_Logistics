export interface StockOnHandDto {
  id: string;
  clientId: string;
  facilityId: string;
  itemId: string;
  sku: string;
  itemDescription: string;
  locationId: string;
  locationCode: string;
  locationBarcode: string;
  lotId?: string | null;
  lotNumber?: string | null;
  expirationDate?: string | null;
  qtyAvailable: number;
  qtyAllocated: number;
  qtyReserved: number;
  qtyQuarantined: number;
  qtyDamaged: number;
  qtyOnHold: number;
  totalQuantity: number;
  lastCountedAt?: string | null;
  version: number;
}

export interface StockOnHandQuery {
  facilityId?: string | null;
  clientId?: string | null;
  itemId?: string | null;
  locationId?: string | null;
  searchSku?: string | null;
  pageNumber?: number;
  pageSize?: number;
}

export interface TransferInventoryRequest {
  clientId: string;
  facilityId: string;
  itemId: string;
  fromLocationId: string;
  toLocationId: string;
  quantity: number;
  lotId?: string | null;
  notes?: string | null;
}
