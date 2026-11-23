import React, { useEffect, useRef, useState } from 'react';
import { Modal } from '../common/Modal';
import { ApiError } from '../../services/apiClient';
import { outboundApi } from '../../services/outboundApi';
import { ClientDto, FacilityDto } from '../../types/auth';
import {
  CarrierServiceLevel,
  CreateOutboundOrderRequest,
  OutboundItemOptionDto,
  OutboundOrderDto,
  OutboundOrderType,
} from '../../types/outbound';

interface LineDraft {
  key: string;
  itemId: string;
  quantityOrdered: string;
  requiredLotNumber: string;
  minExpiryDateUtc: string;
  notes: string;
}

export interface OrderCreateModalProps {
  isOpen: boolean;
  onClose: () => void;
  onCreated: (order: OutboundOrderDto) => void;
  activeFacility: FacilityDto | null;
  facilities: FacilityDto[];
  isStaff: boolean;
  canLookupClients: boolean;
  canLookupItems: boolean;
}

const createEmptyLine = (index: number): LineDraft => ({
  key: `line-${Date.now()}-${index}`,
  itemId: '',
  quantityOrdered: '1',
  requiredLotNumber: '',
  minExpiryDateUtc: '',
  notes: '',
});

export const OrderCreateModal: React.FC<OrderCreateModalProps> = ({
  isOpen,
  onClose,
  onCreated,
  activeFacility,
  facilities,
  isStaff,
  canLookupClients,
  canLookupItems,
}) => {
  const [clients, setClients] = useState<ClientDto[]>([]);
  const [selectedClientId, setSelectedClientId] = useState('');
  const [items, setItems] = useState<OutboundItemOptionDto[]>([]);
  const [refError, setRefError] = useState<string | null>(null);
  const [loadingRefs, setLoadingRefs] = useState(false);

  const [orderType, setOrderType] = useState<OutboundOrderType>(OutboundOrderType.B2BSalesOrder);
  const [destinationFacilityId, setDestinationFacilityId] = useState('');
  const [orderNumber, setOrderNumber] = useState('');
  const [externalOrderReference, setExternalOrderReference] = useState('');
  const [priority, setPriority] = useState('5');
  const [serviceLevel, setServiceLevel] = useState<CarrierServiceLevel>(CarrierServiceLevel.StandardGround);
  const [carrierCode, setCarrierCode] = useState('SIM-FEDEX');
  const [earliestShipDate, setEarliestShipDate] = useState('');
  const [requiredShipDate, setRequiredShipDate] = useState('');
  const [notes, setNotes] = useState('');

  const [recipientName, setRecipientName] = useState('');
  const [companyName, setCompanyName] = useState('');
  const [addressLine1, setAddressLine1] = useState('');
  const [addressLine2, setAddressLine2] = useState('');
  const [city, setCity] = useState('');
  const [stateOrProvince, setStateOrProvince] = useState('');
  const [postalCode, setPostalCode] = useState('');
  const [countryCode, setCountryCode] = useState('USA');
  const [phoneNumber, setPhoneNumber] = useState('');
  const [deliveryInstructions, setDeliveryInstructions] = useState('');

  const [lines, setLines] = useState<LineDraft[]>([createEmptyLine(1)]);
  const [validationError, setValidationError] = useState<string | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const inFlightRef = useRef(false);

  useEffect(() => {
    if (!isOpen) return;
    const ctrl = new AbortController();
    setValidationError(null);
    setSubmitError(null);
    setRefError(null);

    const loadReferenceData = async () => {
      if (isStaff && !canLookupClients) {
        setRefError(
          'Client tenant directory permission (clients:view) is required for staff order entry so X-Tenant-Id can be resolved safely.'
        );
        return;
      }
      if (!canLookupItems) {
        setRefError('Product SKU catalog permission (items:view) is required to select authorized order items.');
        return;
      }

      setLoadingRefs(true);
      try {
        let targetClient = selectedClientId;
        if (isStaff) {
          const clientRes = await outboundApi.getAuthorizedClients(ctrl.signal);
          setClients(clientRes.items);
          if (!targetClient && clientRes.items.length > 0) {
            targetClient = clientRes.items[0].id;
            setSelectedClientId(targetClient);
          }
          if (clientRes.items.length === 0) {
            setRefError('No authorized client tenants returned from the server.');
            return;
          }
        }

        const itemRes = await outboundApi.getAuthorizedItems(
          { clientId: isStaff ? targetClient : undefined, pageSize: 100 },
          ctrl.signal
        );
        setItems(itemRes.items);
        if (itemRes.items.length === 0) {
          setRefError('No authorized SKU items exist for the selected tenant.');
        }
      } catch (err: any) {
        if (err.name !== 'AbortError') {
          setRefError(err.message || 'Unable to load authorized reference catalogs for order creation.');
        }
      } finally {
        setLoadingRefs(false);
      }
    };

    loadReferenceData();
    return () => ctrl.abort();
  }, [isOpen, isStaff, canLookupClients, canLookupItems]);

  useEffect(() => {
    if (!isOpen || !isStaff || !selectedClientId || !canLookupItems) return;
    const ctrl = new AbortController();
    setRefError(null);
    outboundApi
      .getAuthorizedItems({ clientId: selectedClientId, pageSize: 100 }, ctrl.signal)
      .then((res) => {
        setItems(res.items);
        if (res.items.length === 0) {
          setRefError('Selected tenant has no configured SKUs in the authorized item catalog.');
        }
      })
      .catch((err: any) => {
        if (err.name !== 'AbortError') {
          setRefError(err.message || 'Failed to refresh SKU catalog for selected client.');
        }
      });
    return () => ctrl.abort();
  }, [isOpen, isStaff, selectedClientId, canLookupItems]);

  const handleAddLine = () => {
    setLines((prev) => [...prev, createEmptyLine(prev.length + 1)]);
  };

  const handleRemoveLine = (key: string) => {
    setLines((prev) => (prev.length > 1 ? prev.filter((l) => l.key !== key) : prev));
  };

  const handleLineChange = (key: string, field: keyof LineDraft, value: string) => {
    setLines((prev) => prev.map((l) => (l.key === key ? { ...l, [field]: value } : l)));
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (inFlightRef.current || isSubmitting) return;
    setValidationError(null);
    setSubmitError(null);

    if (!activeFacility) {
      setValidationError('An active facility must be selected before creating an outbound order.');
      return;
    }
    if (isStaff && !selectedClientId) {
      setValidationError('Select an authorized client tenant.');
      return;
    }
    if (refError) {
      setValidationError('Required reference catalog data is unavailable.');
      return;
    }
    if (orderType === OutboundOrderType.StockTransferOrder) {
      if (!destinationFacilityId) {
        setValidationError('Destination facility is required for Stock Transfer orders.');
        return;
      }
      if (destinationFacilityId === activeFacility.id) {
        setValidationError('Source and destination facilities must be different for Stock Transfer orders.');
        return;
      }
    }
    if (
      !recipientName.trim() ||
      !addressLine1.trim() ||
      !city.trim() ||
      !stateOrProvince.trim() ||
      !postalCode.trim() ||
      !countryCode.trim()
    ) {
      setValidationError('Complete all required shipping address fields (Recipient, Address Line 1, City, State/Province, Postal Code, Country).');
      return;
    }

    const parsedPriority = Number(priority);
    if (!Number.isInteger(parsedPriority) || parsedPriority < 1 || parsedPriority > 10) {
      setValidationError('Priority must be a whole number between 1 and 10.');
      return;
    }

    const parsedLines = [];
    for (let i = 0; i < lines.length; i++) {
      const line = lines[i];
      if (!line.itemId) {
        setValidationError(`Line ${i + 1}: Select an authorized SKU item.`);
        return;
      }
      const qty = Number(line.quantityOrdered);
      if (!Number.isFinite(qty) || qty <= 0) {
        setValidationError(`Line ${i + 1}: Quantity ordered must be greater than zero.`);
        return;
      }
      const itemMeta = items.find((it) => it.id === line.itemId);
      if (itemMeta?.requiresExpiryDate && !line.minExpiryDateUtc) {
        // Note: order-level MinExpiryDateUtc is optional in backend validator, but surface clear guidance if user entered invalid date
      }
      parsedLines.push({
        itemId: line.itemId,
        quantityOrdered: qty,
        requiredLotNumber: line.requiredLotNumber.trim() || undefined,
        minExpiryDateUtc: line.minExpiryDateUtc ? new Date(`${line.minExpiryDateUtc}T00:00:00Z`).toISOString() : undefined,
        notes: line.notes.trim() || undefined,
      });
    }

    const payload: CreateOutboundOrderRequest = {
      facilityId: activeFacility.id,
      destinationFacilityId:
        orderType === OutboundOrderType.StockTransferOrder ? destinationFacilityId : undefined,
      orderNumber: orderNumber.trim() || undefined,
      externalOrderReference: externalOrderReference.trim() || undefined,
      orderType,
      priority: parsedPriority,
      serviceLevel,
      carrierCode: carrierCode.trim() || undefined,
      earliestShipDateUtc: earliestShipDate ? new Date(`${earliestShipDate}T00:00:00Z`).toISOString() : undefined,
      requiredShipDateUtc: requiredShipDate ? new Date(`${requiredShipDate}T00:00:00Z`).toISOString() : undefined,
      notes: notes.trim() || undefined,
      shippingAddress: {
        recipientName: recipientName.trim(),
        companyName: companyName.trim() || undefined,
        addressLine1: addressLine1.trim(),
        addressLine2: addressLine2.trim() || undefined,
        city: city.trim(),
        stateOrProvince: stateOrProvince.trim(),
        postalCode: postalCode.trim(),
        countryCode: countryCode.trim().toUpperCase(),
        phoneNumber: phoneNumber.trim() || undefined,
        deliveryInstructions: deliveryInstructions.trim() || undefined,
      },
      lines: parsedLines,
    };

    inFlightRef.current = true;
    setIsSubmitting(true);
    try {
      const created = await outboundApi.createOrder(
        payload,
        isStaff ? selectedClientId : undefined
      );
      onCreated(created);
      onClose();
    } catch (err: any) {
      if (err instanceof ApiError && err.status === 403) {
        setSubmitError(`Permission or scope denied (403): ${err.message}`);
      } else {
        setSubmitError(err.message || 'Failed to create outbound order.');
      }
    } finally {
      inFlightRef.current = false;
      setIsSubmitting(false);
    }
  };

  const destinationOptions = facilities.filter((f) => f.id !== activeFacility?.id);

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title="Create Outbound Order"
      subtitle={activeFacility ? `Source Facility: ${activeFacility.name} (${activeFacility.facilityCode})` : undefined}
      icon="Send"
      maxWidth="820px"
    >
      <form onSubmit={handleSubmit} className="outbound-form-stack" noValidate>
        {refError && (
          <div className="outbound-alert error" role="alert">
            {refError}
          </div>
        )}
        {validationError && (
          <div className="outbound-alert error" role="alert">
            {validationError}
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
              <label htmlFor="order-client">Client Tenant (X-Tenant-Id) *</label>
              <select
                id="order-client"
                value={selectedClientId}
                onChange={(e) => setSelectedClientId(e.target.value)}
                disabled={loadingRefs || clients.length === 0}
              >
                {clients.length === 0 && <option value="">No authorized clients</option>}
                {clients.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.companyName} ({c.accountCode})
                  </option>
                ))}
              </select>
            </div>
          )}

          <div className="form-field">
            <label htmlFor="order-type">Order Type *</label>
            <select
              id="order-type"
              value={orderType}
              onChange={(e) => setOrderType(e.target.value as OutboundOrderType)}
            >
              <option value={OutboundOrderType.B2BSalesOrder}>B2B Sales Order</option>
              <option value={OutboundOrderType.EcommerceFulfillment}>E-Commerce Fulfillment</option>
              <option value={OutboundOrderType.StockTransferOrder}>Stock Transfer Order</option>
            </select>
          </div>

          {orderType === OutboundOrderType.StockTransferOrder && (
            <div className="form-field">
              <label htmlFor="order-dest-facility">Destination Facility *</label>
              <select
                id="order-dest-facility"
                value={destinationFacilityId}
                onChange={(e) => setDestinationFacilityId(e.target.value)}
              >
                <option value="">Select destination facility...</option>
                {destinationOptions.map((f) => (
                  <option key={f.id} value={f.id}>
                    {f.name} ({f.facilityCode})
                  </option>
                ))}
              </select>
            </div>
          )}

          <div className="form-field">
            <label htmlFor="order-service-level">Service Level *</label>
            <select
              id="order-service-level"
              value={serviceLevel}
              onChange={(e) => setServiceLevel(e.target.value as CarrierServiceLevel)}
            >
              {Object.values(CarrierServiceLevel).map((sl) => (
                <option key={sl} value={sl}>
                  {sl}
                </option>
              ))}
            </select>
          </div>

          <div className="form-field">
            <label htmlFor="order-priority">Priority (1-10)</label>
            <input
              id="order-priority"
              type="number"
              min={1}
              max={10}
              value={priority}
              onChange={(e) => setPriority(e.target.value)}
            />
          </div>

          <div className="form-field">
            <label htmlFor="order-carrier">Carrier Code</label>
            <input
              id="order-carrier"
              type="text"
              value={carrierCode}
              onChange={(e) => setCarrierCode(e.target.value)}
              placeholder="SIM-FEDEX"
            />
          </div>

          <div className="form-field">
            <label htmlFor="order-ext-ref">External Reference</label>
            <input
              id="order-ext-ref"
              type="text"
              value={externalOrderReference}
              onChange={(e) => setExternalOrderReference(e.target.value)}
              placeholder="ERP / PO Reference"
            />
          </div>

          <div className="form-field">
            <label htmlFor="order-number">Order Number (Optional)</label>
            <input
              id="order-number"
              type="text"
              value={orderNumber}
              onChange={(e) => setOrderNumber(e.target.value)}
              placeholder="Auto-generated if blank"
            />
          </div>

          <div className="form-field">
            <label htmlFor="order-earliest-ship">Earliest Ship Date</label>
            <input
              id="order-earliest-ship"
              type="date"
              value={earliestShipDate}
              onChange={(e) => setEarliestShipDate(e.target.value)}
            />
          </div>

          <div className="form-field">
            <label htmlFor="order-required-ship">Required Ship Date</label>
            <input
              id="order-required-ship"
              type="date"
              value={requiredShipDate}
              onChange={(e) => setRequiredShipDate(e.target.value)}
            />
          </div>
        </div>

        <fieldset className="outbound-fieldset">
          <legend>Shipping Address *</legend>
          <div className="outbound-form-grid">
            <div className="form-field">
              <label htmlFor="addr-recipient">Recipient Name *</label>
              <input
                id="addr-recipient"
                type="text"
                value={recipientName}
                onChange={(e) => setRecipientName(e.target.value)}
                required
              />
            </div>
            <div className="form-field">
              <label htmlFor="addr-company">Company Name</label>
              <input
                id="addr-company"
                type="text"
                value={companyName}
                onChange={(e) => setCompanyName(e.target.value)}
              />
            </div>
            <div className="form-field">
              <label htmlFor="addr-line1">Address Line 1 *</label>
              <input
                id="addr-line1"
                type="text"
                value={addressLine1}
                onChange={(e) => setAddressLine1(e.target.value)}
                required
              />
            </div>
            <div className="form-field">
              <label htmlFor="addr-line2">Address Line 2</label>
              <input
                id="addr-line2"
                type="text"
                value={addressLine2}
                onChange={(e) => setAddressLine2(e.target.value)}
              />
            </div>
            <div className="form-field">
              <label htmlFor="addr-city">City *</label>
              <input id="addr-city" type="text" value={city} onChange={(e) => setCity(e.target.value)} required />
            </div>
            <div className="form-field">
              <label htmlFor="addr-state">State / Province *</label>
              <input
                id="addr-state"
                type="text"
                value={stateOrProvince}
                onChange={(e) => setStateOrProvince(e.target.value)}
                required
              />
            </div>
            <div className="form-field">
              <label htmlFor="addr-postal">Postal Code *</label>
              <input
                id="addr-postal"
                type="text"
                value={postalCode}
                onChange={(e) => setPostalCode(e.target.value)}
                required
              />
            </div>
            <div className="form-field">
              <label htmlFor="addr-country">Country Code *</label>
              <input
                id="addr-country"
                type="text"
                value={countryCode}
                onChange={(e) => setCountryCode(e.target.value)}
                required
              />
            </div>
            <div className="form-field">
              <label htmlFor="addr-phone">Phone Number</label>
              <input
                id="addr-phone"
                type="tel"
                value={phoneNumber}
                onChange={(e) => setPhoneNumber(e.target.value)}
              />
            </div>
            <div className="form-field">
              <label htmlFor="addr-instructions">Delivery Instructions</label>
              <input
                id="addr-instructions"
                type="text"
                value={deliveryInstructions}
                onChange={(e) => setDeliveryInstructions(e.target.value)}
              />
            </div>
          </div>
        </fieldset>

        <fieldset className="outbound-fieldset">
          <legend>Order Lines *</legend>
          {lines.map((line, index) => (
            <div key={line.key} className="order-line-row">
              <div className="form-field line-item-select">
                <label htmlFor={`line-item-${line.key}`}>Line {index + 1} SKU *</label>
                <select
                  id={`line-item-${line.key}`}
                  value={line.itemId}
                  onChange={(e) => handleLineChange(line.key, 'itemId', e.target.value)}
                  disabled={items.length === 0}
                >
                  <option value="">Select item SKU...</option>
                  {items.map((item) => (
                    <option key={item.id} value={item.id}>
                      {item.sku} - {item.description}
                    </option>
                  ))}
                </select>
              </div>
              <div className="form-field line-qty">
                <label htmlFor={`line-qty-${line.key}`}>Qty *</label>
                <input
                  id={`line-qty-${line.key}`}
                  type="number"
                  step="any"
                  min="0.01"
                  value={line.quantityOrdered}
                  onChange={(e) => handleLineChange(line.key, 'quantityOrdered', e.target.value)}
                />
              </div>
              <div className="form-field">
                <label htmlFor={`line-lot-${line.key}`}>Required Lot</label>
                <input
                  id={`line-lot-${line.key}`}
                  type="text"
                  value={line.requiredLotNumber}
                  onChange={(e) => handleLineChange(line.key, 'requiredLotNumber', e.target.value)}
                  placeholder="Optional"
                />
              </div>
              <div className="form-field">
                <label htmlFor={`line-expiry-${line.key}`}>Min Expiry</label>
                <input
                  id={`line-expiry-${line.key}`}
                  type="date"
                  value={line.minExpiryDateUtc}
                  onChange={(e) => handleLineChange(line.key, 'minExpiryDateUtc', e.target.value)}
                />
              </div>
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={() => handleRemoveLine(line.key)}
                disabled={lines.length === 1}
                aria-label={`Remove order line ${index + 1}`}
              >
                Remove
              </button>
            </div>
          ))}
          <button type="button" className="btn btn-outline btn-sm" onClick={handleAddLine}>
            + Add Order Line
          </button>
        </fieldset>

        <div className="form-field">
          <label htmlFor="order-notes">Operational Notes</label>
          <input
            id="order-notes"
            type="text"
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            placeholder="Special handling instructions"
          />
        </div>

        <div className="modal-inline-actions">
          <button type="button" className="btn btn-secondary" onClick={onClose} disabled={isSubmitting}>
            Cancel
          </button>
          <button
            type="submit"
            className="btn btn-primary"
            disabled={isSubmitting || Boolean(refError) || loadingRefs}
            data-testid="submit-create-order"
          >
            {isSubmitting ? 'Submitting Order...' : 'Submit Outbound Order'}
          </button>
        </div>
      </form>
    </Modal>
  );
};
