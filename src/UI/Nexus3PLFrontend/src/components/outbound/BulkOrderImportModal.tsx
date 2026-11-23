import React, { useEffect, useRef, useState } from 'react';
import { Modal } from '../common/Modal';
import { getImportStatusBadgeClass } from './OutboundStatusBadge';
import { outboundApi } from '../../services/outboundApi';
import { ClientDto, FacilityDto } from '../../types/auth';
import {
  CarrierServiceLevel,
  CreateOutboundOrderRequest,
  OrderImportBatchDto,
  OutboundOrderType,
} from '../../types/outbound';

interface ParsedImportPreviewRow {
  rowIndex: number;
  externalOrderReference?: string;
  recipientName: string;
  lineCount: number;
  valid: boolean;
  validationMessage?: string;
  payload?: CreateOutboundOrderRequest;
}

export interface BulkOrderImportModalProps {
  isOpen: boolean;
  onClose: () => void;
  onImported: (batch: OrderImportBatchDto) => void;
  activeFacility: FacilityDto | null;
  isStaff: boolean;
  canLookupClients: boolean;
}

const REQUIRED_CSV_COLUMNS = [
  'recipientName',
  'addressLine1',
  'city',
  'stateOrProvince',
  'postalCode',
  'countryCode',
  'itemId',
  'quantityOrdered',
];

function splitCsvLine(line: string): string[] {
  const result: string[] = [];
  let current = '';
  let inQuotes = false;
  for (let i = 0; i < line.length; i++) {
    const ch = line[i];
    if (ch === '"') {
      if (inQuotes && line[i + 1] === '"') {
        current += '"';
        i++;
      } else {
        inQuotes = !inQuotes;
      }
    } else if (ch === ',' && !inQuotes) {
      result.push(current.trim());
      current = '';
    } else {
      current += ch;
    }
  }
  result.push(current.trim());
  return result;
}

export function parseCsvOrders(
  csvText: string,
  facilityId: string
): { rows: ParsedImportPreviewRow[]; parseError?: string } {
  const rawLines = csvText
    .split(/\r?\n/)
    .map((l) => l.trim())
    .filter((l) => l.length > 0);
  if (rawLines.length < 2) {
    return { rows: [], parseError: 'CSV must contain a header row and at least one data row.' };
  }

  const headers = splitCsvLine(rawLines[0]);
  const headerMap = new Map<string, number>();
  headers.forEach((h, idx) => headerMap.set(h.toLowerCase(), idx));

  const missingCols = REQUIRED_CSV_COLUMNS.filter((col) => !headerMap.has(col.toLowerCase()));
  if (missingCols.length > 0) {
    return {
      rows: [],
      parseError: `CSV missing required columns: ${missingCols.join(', ')}.`,
    };
  }

  const getCell = (cells: string[], name: string): string => {
    const idx = headerMap.get(name.toLowerCase());
    return idx !== undefined && idx < cells.length ? cells[idx] : '';
  };

  const grouped = new Map<
    string,
    { order: CreateOutboundOrderRequest; rowNumbers: number[]; errors: string[] }
  >();
  const previewRows: ParsedImportPreviewRow[] = [];

  for (let i = 1; i < rawLines.length; i++) {
    const cells = splitCsvLine(rawLines[i]);
    const extRef = getCell(cells, 'externalOrderReference');
    const orderNumber = getCell(cells, 'orderNumber');
    const groupKey = extRef || orderNumber || `csv-row-${i}`;
    const qty = Number(getCell(cells, 'quantityOrdered'));
    const itemId = getCell(cells, 'itemId');
    const recipientName = getCell(cells, 'recipientName');
    const addressLine1 = getCell(cells, 'addressLine1');
    const city = getCell(cells, 'city');
    const stateOrProvince = getCell(cells, 'stateOrProvince');
    const postalCode = getCell(cells, 'postalCode');
    const countryCode = getCell(cells, 'countryCode');

    const rowErrors: string[] = [];
    if (!itemId) rowErrors.push('itemId is required');
    if (!Number.isFinite(qty) || qty <= 0) rowErrors.push('quantityOrdered must be > 0');
    if (!recipientName || !addressLine1 || !city || !stateOrProvince || !postalCode || !countryCode) {
      rowErrors.push('shipping address fields are incomplete');
    }

    const rawType = getCell(cells, 'orderType') || OutboundOrderType.B2BSalesOrder;
    const rawSvc = getCell(cells, 'serviceLevel') || CarrierServiceLevel.StandardGround;
    const priorityVal = Number(getCell(cells, 'priority') || '1');

    if (!grouped.has(groupKey)) {
      grouped.set(groupKey, {
        rowNumbers: [],
        errors: [...rowErrors],
        order: {
          facilityId,
          orderNumber: orderNumber || undefined,
          externalOrderReference: extRef || undefined,
          orderType: Object.values(OutboundOrderType).includes(rawType as OutboundOrderType)
            ? (rawType as OutboundOrderType)
            : OutboundOrderType.B2BSalesOrder,
          priority: Number.isInteger(priorityVal) && priorityVal >= 1 ? priorityVal : 1,
          serviceLevel: Object.values(CarrierServiceLevel).includes(rawSvc as CarrierServiceLevel)
            ? (rawSvc as CarrierServiceLevel)
            : CarrierServiceLevel.StandardGround,
          carrierCode: getCell(cells, 'carrierCode') || undefined,
          requiredShipDateUtc: getCell(cells, 'requiredShipDateUtc') || undefined,
          shippingAddress: {
            recipientName,
            addressLine1,
            city,
            stateOrProvince,
            postalCode,
            countryCode: countryCode.toUpperCase(),
          },
          lines: [],
        },
      });
    } else if (rowErrors.length > 0) {
      grouped.get(groupKey)!.errors.push(...rowErrors);
    }

    const entry = grouped.get(groupKey)!;
    entry.rowNumbers.push(i);
    entry.order.lines.push({
      itemId,
      quantityOrdered: Number.isFinite(qty) ? qty : 0,
      requiredLotNumber: getCell(cells, 'requiredLotNumber') || undefined,
      minExpiryDateUtc: getCell(cells, 'minExpiryDateUtc') || undefined,
    });
  }

  let idx = 1;
  grouped.forEach((group) => {
    const valid = group.errors.length === 0 && group.order.lines.length > 0;
    previewRows.push({
      rowIndex: idx++,
      externalOrderReference: group.order.externalOrderReference,
      recipientName: group.order.shippingAddress.recipientName || 'Unknown',
      lineCount: group.order.lines.length,
      valid,
      validationMessage: valid ? undefined : group.errors.join('; '),
      payload: valid ? group.order : undefined,
    });
  });

  return { rows: previewRows };
}

export function parseJsonOrders(
  jsonText: string,
  facilityId: string
): { rows: ParsedImportPreviewRow[]; parseError?: string } {
  try {
    const raw = JSON.parse(jsonText);
    const list: any[] = Array.isArray(raw) ? raw : Array.isArray(raw?.orders) ? raw.orders : null;
    if (!list) {
      return { rows: [], parseError: 'JSON must be an array of orders or an object with an "orders" array.' };
    }
    if (list.length === 0) {
      return { rows: [], parseError: 'JSON orders array is empty.' };
    }

    const previewRows: ParsedImportPreviewRow[] = list.map((item, idx) => {
      const errs: string[] = [];
      const addr = item?.shippingAddress;
      if (
        !addr?.recipientName ||
        !addr?.addressLine1 ||
        !addr?.city ||
        !addr?.stateOrProvince ||
        !addr?.postalCode ||
        !addr?.countryCode
      ) {
        errs.push('Missing required shippingAddress fields');
      }
      if (!Array.isArray(item?.lines) || item.lines.length === 0) {
        errs.push('At least one order line is required');
      } else {
        item.lines.forEach((l: any, li: number) => {
          if (!l?.itemId) errs.push(`Line ${li + 1} missing itemId`);
          if (!Number(l?.quantityOrdered) || Number(l.quantityOrdered) <= 0) {
            errs.push(`Line ${li + 1} quantityOrdered must be > 0`);
          }
        });
      }

      const valid = errs.length === 0;
      const normalized: CreateOutboundOrderRequest | undefined = valid
        ? {
            facilityId: item.facilityId || facilityId,
            destinationFacilityId: item.destinationFacilityId || undefined,
            customerId: item.customerId || undefined,
            orderNumber: item.orderNumber || undefined,
            externalOrderReference: item.externalOrderReference || undefined,
            orderType: item.orderType || OutboundOrderType.B2BSalesOrder,
            priority: Number(item.priority || 1),
            serviceLevel: item.serviceLevel || CarrierServiceLevel.StandardGround,
            carrierCode: item.carrierCode || undefined,
            requiredShipDateUtc: item.requiredShipDateUtc || undefined,
            earliestShipDateUtc: item.earliestShipDateUtc || undefined,
            notes: item.notes || undefined,
            shippingAddress: addr,
            lines: item.lines.map((l: any) => ({
              itemId: l.itemId,
              quantityOrdered: Number(l.quantityOrdered),
              requiredLotNumber: l.requiredLotNumber || undefined,
              minExpiryDateUtc: l.minExpiryDateUtc || undefined,
              notes: l.notes || undefined,
            })),
          }
        : undefined;

      return {
        rowIndex: idx + 1,
        externalOrderReference: item?.externalOrderReference,
        recipientName: addr?.recipientName || 'Unknown',
        lineCount: Array.isArray(item?.lines) ? item.lines.length : 0,
        valid,
        validationMessage: valid ? undefined : errs.join('; '),
        payload: normalized,
      };
    });

    return { rows: previewRows };
  } catch (err: any) {
    return { rows: [], parseError: `Invalid JSON: ${err.message}` };
  }
}

export const BulkOrderImportModal: React.FC<BulkOrderImportModalProps> = ({
  isOpen,
  onClose,
  onImported,
  activeFacility,
  isStaff,
  canLookupClients,
}) => {
  const [clients, setClients] = useState<ClientDto[]>([]);
  const [selectedClientId, setSelectedClientId] = useState('');
  const [clientLookupError, setClientLookupError] = useState<string | null>(null);

  const [format, setFormat] = useState<'json' | 'csv'>('json');
  const [sourceFileName, setSourceFileName] = useState('outbound_orders_import.json');
  const [rawContent, setRawContent] = useState('');
  const [previewRows, setPreviewRows] = useState<ParsedImportPreviewRow[]>([]);
  const [parseError, setParseError] = useState<string | null>(null);
  const [confirmReady, setConfirmReady] = useState(false);

  const [submitError, setSubmitError] = useState<string | null>(null);
  const [batchResult, setBatchResult] = useState<OrderImportBatchDto | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const inFlightRef = useRef(false);

  useEffect(() => {
    if (!isOpen) return;
    setParseError(null);
    setSubmitError(null);
    setBatchResult(null);
    setConfirmReady(false);
    setPreviewRows([]);
    setRawContent('');

    if (!isStaff) return;
    if (!canLookupClients) {
      setClientLookupError('Client directory permission (clients:view) is required for staff bulk imports.');
      return;
    }

    const ctrl = new AbortController();
    setClientLookupError(null);
    outboundApi
      .getAuthorizedClients(ctrl.signal)
      .then((res) => {
        setClients(res.items);
        if (res.items.length > 0) {
          setSelectedClientId((prev) => prev || res.items[0].id);
        } else {
          setClientLookupError('No authorized client tenants available.');
        }
      })
      .catch((err: any) => {
        if (err.name !== 'AbortError') {
          setClientLookupError(err.message || 'Unable to load authorized clients.');
        }
      });
    return () => ctrl.abort();
  }, [isOpen, isStaff, canLookupClients]);

  useEffect(() => {
    if (isOpen && activeFacility && rawContent.trim() && parseError?.includes('Select an active facility')) {
      parseInput(rawContent, format);
    }
  }, [isOpen, activeFacility?.id]);

  const parseInput = (content: string, selectedFormat: 'json' | 'csv') => {
    setBatchResult(null);
    setConfirmReady(false);
    setSubmitError(null);
    if (!content.trim()) {
      setPreviewRows([]);
      setParseError(null);
      return;
    }
    if (!activeFacility) {
      setParseError('Select an active facility before parsing orders.');
      return;
    }
    const parsed =
      selectedFormat === 'json'
        ? parseJsonOrders(content, activeFacility.id)
        : parseCsvOrders(content, activeFacility.id);
    setPreviewRows(parsed.rows);
    setParseError(parsed.parseError ?? null);
  };

  const handleFileSelected = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setSourceFileName(file.name);
    const detectedFormat = file.name.toLowerCase().endsWith('.csv') ? 'csv' : 'json';
    setFormat(detectedFormat);
    const text = await file.text();
    setRawContent(text);
    parseInput(text, detectedFormat);
  };

  const submitPayloads = async (ordersToSend: CreateOutboundOrderRequest[], fileName: string) => {
    if (inFlightRef.current || isSubmitting || !activeFacility) return;
    setSubmitError(null);
    inFlightRef.current = true;
    setIsSubmitting(true);
    try {
      const batch = await outboundApi.bulkImportOrders(
        {
          facilityId: activeFacility.id,
          sourceFileName: fileName,
          orders: ordersToSend,
        },
        isStaff ? selectedClientId : undefined
      );
      setBatchResult(batch);
      setConfirmReady(false);
      onImported(batch);
    } catch (err: any) {
      setSubmitError(err.message || 'Bulk order import failed.');
    } finally {
      inFlightRef.current = false;
      setIsSubmitting(false);
    }
  };

  const validRows = previewRows.filter((r) => r.valid && r.payload);
  const hasInvalidPreview = previewRows.some((r) => !r.valid);

  // Exclude already succeeded AND duplicate rows when offering retry
  const retryablePayloads = batchResult
    ? batchResult.rows
        .filter((r) => !r.isSuccess && !r.isDuplicate)
        .map((r) => previewRows[r.rowNumber - 1]?.payload)
        .filter((p): p is CreateOutboundOrderRequest => Boolean(p))
    : [];

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title="Bulk Import Outbound Orders"
      subtitle="Structured JSON or CSV conversion submitted to POST /api/v1/outbound/orders/bulk-import"
      icon="Upload"
      maxWidth="860px"
    >
      <div className="outbound-form-stack">
        {clientLookupError && (
          <div className="outbound-alert error" role="alert">
            {clientLookupError}
          </div>
        )}
        {parseError && (
          <div className="outbound-alert error" role="alert">
            {parseError}
          </div>
        )}
        {submitError && (
          <div className="outbound-alert error" role="alert">
            {submitError}
          </div>
        )}

        <div className="outbound-form-grid">
          {isStaff && (
            <div className="form-field">
              <label htmlFor="import-client">Client Tenant (X-Tenant-Id) *</label>
              <select
                id="import-client"
                value={selectedClientId}
                onChange={(e) => setSelectedClientId(e.target.value)}
                disabled={clients.length === 0}
              >
                {clients.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.companyName} ({c.accountCode})
                  </option>
                ))}
              </select>
            </div>
          )}

          <div className="form-field">
            <label htmlFor="import-format">Input Format</label>
            <select
              id="import-format"
              value={format}
              onChange={(e) => {
                const next = e.target.value as 'json' | 'csv';
                setFormat(next);
                parseInput(rawContent, next);
              }}
            >
              <option value="json">Structured JSON Array / Orders Object</option>
              <option value="csv">CSV Column Mapping</option>
            </select>
          </div>

          <div className="form-field">
            <label htmlFor="import-filename">Source File Name</label>
            <input
              id="import-filename"
              type="text"
              value={sourceFileName}
              onChange={(e) => setSourceFileName(e.target.value)}
            />
          </div>

          <div className="form-field">
            <label htmlFor="import-file-input">Upload JSON / CSV File</label>
            <input
              id="import-file-input"
              type="file"
              accept=".json,.csv,application/json,text/csv"
              onChange={handleFileSelected}
            />
          </div>
        </div>

        <div className="form-field">
          <label htmlFor="import-raw-textarea">
            Paste {format.toUpperCase()} Payload
            {format === 'csv' &&
              ' (Required columns: recipientName, addressLine1, city, stateOrProvince, postalCode, countryCode, itemId, quantityOrdered)'}
          </label>
          <textarea
            id="import-raw-textarea"
            rows={5}
            value={rawContent}
            onChange={(e) => {
              setRawContent(e.target.value);
              parseInput(e.target.value, format);
            }}
            placeholder={
              format === 'json'
                ? '[{"externalOrderReference":"EXT-101","shippingAddress":{...},"lines":[{"itemId":"...","quantityOrdered":5}]}]'
                : 'externalOrderReference,recipientName,addressLine1,city,stateOrProvince,postalCode,countryCode,itemId,quantityOrdered'
            }
          />
        </div>

        {previewRows.length > 0 && (
          <div className="import-preview-block">
            <h4>
              Parsed Preview ({previewRows.length} order{previewRows.length === 1 ? '' : 's'},{' '}
              {validRows.length} valid)
            </h4>
            <div className="outbound-table-wrap">
              <table className="outbound-table">
                <thead>
                  <tr>
                    <th>Row</th>
                    <th>External Ref</th>
                    <th>Recipient</th>
                    <th>Lines</th>
                    <th>Validation</th>
                  </tr>
                </thead>
                <tbody>
                  {previewRows.map((row) => (
                    <tr key={row.rowIndex}>
                      <td>{row.rowIndex}</td>
                      <td>{row.externalOrderReference || '—'}</td>
                      <td>{row.recipientName}</td>
                      <td>{row.lineCount}</td>
                      <td>
                        {row.valid ? (
                          <span className="badge badge-success">Valid</span>
                        ) : (
                          <span className="badge badge-danger">{row.validationMessage}</span>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {validRows.length > 0 && !batchResult && (
              <label className="confirm-checkbox">
                <input
                  type="checkbox"
                  checked={confirmReady}
                  onChange={(e) => setConfirmReady(e.target.checked)}
                />
                <span>
                  I confirm submission of {validRows.length} validated order(s) to{' '}
                  {activeFacility?.name || 'selected facility'}
                  {hasInvalidPreview ? ' (invalid rows will be excluded).' : '.'}
                </span>
              </label>
            )}
          </div>
        )}

        {batchResult && (
          <div className="import-summary-card" data-testid="bulk-import-summary">
            <div className="summary-header-row">
              <div>
                <strong>Batch {batchResult.batchNumber}</strong> &bull; {batchResult.sourceFileName}
              </div>
              <span className={`badge ${getImportStatusBadgeClass(batchResult.status)}`}>
                {batchResult.status}
              </span>
            </div>
            <div className="summary-metrics-inline">
              <span>Total: {batchResult.totalRows}</span>
              <span>Succeeded: {batchResult.successfulRows}</span>
              <span>Duplicates: {batchResult.duplicateRows}</span>
              <span>Failed: {batchResult.failedRows}</span>
            </div>
            <div className="outbound-table-wrap">
              <table className="outbound-table">
                <thead>
                  <tr>
                    <th>Row</th>
                    <th>External Ref</th>
                    <th>Outcome</th>
                    <th>Order ID / Details</th>
                  </tr>
                </thead>
                <tbody>
                  {batchResult.rows.map((r) => (
                    <tr key={`${r.rowNumber}-${r.externalOrderReference || ''}`}>
                      <td>{r.rowNumber}</td>
                      <td>{r.externalOrderReference || '—'}</td>
                      <td>
                        {r.isSuccess ? (
                          <span className="badge badge-success">Imported</span>
                        ) : r.isDuplicate ? (
                          <span className="badge badge-warning">Duplicate</span>
                        ) : (
                          <span className="badge badge-danger">Failed</span>
                        )}
                      </td>
                      <td>{r.createdOutboundOrderId || r.errorMessage || '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}

        <div className="modal-inline-actions">
          <button type="button" className="btn btn-secondary" onClick={onClose} disabled={isSubmitting}>
            Close
          </button>
          {batchResult && retryablePayloads.length > 0 && (
            <button
              type="button"
              className="btn btn-outline"
              disabled={isSubmitting}
              onClick={() => submitPayloads(retryablePayloads, `retry_${sourceFileName}`)}
            >
              Retry Failed Rows ({retryablePayloads.length})
            </button>
          )}
          {!batchResult && (
            <button
              type="button"
              className="btn btn-primary"
              disabled={
                isSubmitting ||
                !confirmReady ||
                validRows.length === 0 ||
                Boolean(parseError) ||
                Boolean(clientLookupError) ||
                (isStaff && !selectedClientId)
              }
              onClick={() =>
                submitPayloads(
                  validRows.map((r) => r.payload!),
                  sourceFileName.trim() || 'bulk_import.json'
                )
              }
              data-testid="submit-bulk-import"
            >
              {isSubmitting ? 'Importing Batch...' : `Import ${validRows.length} Order(s)`}
            </button>
          )}
        </div>
      </div>
    </Modal>
  );
};
