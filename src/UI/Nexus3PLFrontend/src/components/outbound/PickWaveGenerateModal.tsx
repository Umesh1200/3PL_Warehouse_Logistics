import React, { useEffect, useRef, useState } from 'react';
import { Modal } from '../common/Modal';
import { ApiError } from '../../services/apiClient';
import { outboundApi } from '../../services/outboundApi';
import { ClientDto, FacilityDto } from '../../types/auth';
import {
  OutboundOrderDto,
  OutboundOrderStatus,
  PickWaveDto,
  PickWaveGenerationRequest,
  PickWaveType,
  StorageTempClass,
  WarehouseLocationNodeDto,
} from '../../types/outbound';

export interface PickWaveGenerateModalProps {
  isOpen: boolean;
  onClose: () => void;
  onGenerated: (wave: PickWaveDto) => void;
  activeFacility: FacilityDto | null;
  canLookupClients: boolean;
  canLookupOrders: boolean;
  canLookupLocations: boolean;
}

interface ZoneOption {
  id: string;
  label: string;
}

function extractZones(nodes: WarehouseLocationNodeDto[]): ZoneOption[] {
  const seen = new Map<string, string>();
  const walk = (node: WarehouseLocationNodeDto) => {
    const typeLower = String(node.locationType).toLowerCase();
    if (typeLower === 'zone' || node.locationType === 1) {
      seen.set(node.id, `${node.locationCode}`);
    } else if (node.zoneId && !seen.has(node.zoneId)) {
      seen.set(node.zoneId, `Zone ${node.zoneId.slice(0, 8)}...`);
    }
    if (Array.isArray(node.children)) {
      node.children.forEach(walk);
    }
  };
  nodes.forEach(walk);
  return Array.from(seen.entries()).map(([id, label]) => ({ id, label }));
}

export const PickWaveGenerateModal: React.FC<PickWaveGenerateModalProps> = ({
  isOpen,
  onClose,
  onGenerated,
  activeFacility,
  canLookupClients,
  canLookupOrders,
  canLookupLocations,
}) => {
  const [clients, setClients] = useState<ClientDto[]>([]);
  const [clientId, setClientId] = useState('');
  const [waveType, setWaveType] = useState<PickWaveType>(PickWaveType.BatchPick);
  const [zoneId, setZoneId] = useState('');
  const [zones, setZones] = useState<ZoneOption[]>([]);
  const [tempClass, setTempClass] = useState<string>('');
  const [priority, setPriority] = useState('');
  const [maxOrders, setMaxOrders] = useState('20');
  const [waveNumber, setWaveNumber] = useState('');

  const [eligibleOrders, setEligibleOrders] = useState<OutboundOrderDto[]>([]);
  const [selectedOrderIds, setSelectedOrderIds] = useState<string[]>([]);

  const [refError, setRefError] = useState<string | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const inFlightRef = useRef(false);

  useEffect(() => {
    if (!isOpen || !activeFacility) return;
    const ctrl = new AbortController();
    setRefError(null);
    setSubmitError(null);
    setSelectedOrderIds([]);

    const loadRefs = async () => {
      if (!canLookupClients) {
        setRefError('Pick wave management permission (outbound:waves:manage) is required to select an eligible client.');
        return;
      }
      try {
        const clientRes = await outboundApi.getEligibleWaveClients(ctrl.signal, activeFacility.id);
        setClients(clientRes.items);
        if (clientRes.items.length > 0) {
          setClientId((prev) => prev || clientRes.items[0].id);
        } else {
          setRefError('No authorized active clients found for this facility.');
        }

        if (canLookupLocations) {
          try {
            const tree = await outboundApi.getLocationTree(activeFacility.id, ctrl.signal);
            setZones(extractZones(tree));
          } catch (_) {
            setZones([]);
          }
        }
      } catch (err: any) {
        if (err.name !== 'AbortError') {
          setRefError(err.message || 'Unable to load authorized clients for wave generation.');
        }
      }
    };

    loadRefs();
    return () => ctrl.abort();
  }, [isOpen, activeFacility, canLookupClients, canLookupLocations]);

  useEffect(() => {
    if (!isOpen || !activeFacility || !clientId || !canLookupOrders) {
      setEligibleOrders([]);
      return;
    }
    const ctrl = new AbortController();
    outboundApi
      .getOrders(
        {
          facilityId: activeFacility.id,
          clientId,
          pageNumber: 1,
          pageSize: 50,
        },
        ctrl.signal
      )
      .then((res) => {
        const allocated = res.items.filter(
          (o) =>
            o.status === OutboundOrderStatus.Allocated ||
            o.status === OutboundOrderStatus.PartiallyAllocated
        );
        setEligibleOrders(allocated);
      })
      .catch(() => {
        setEligibleOrders([]);
      });
    return () => ctrl.abort();
  }, [isOpen, activeFacility, clientId, canLookupOrders]);

  const toggleOrderSelection = (id: string) => {
    setSelectedOrderIds((prev) => {
      if (prev.includes(id)) return prev.filter((x) => x !== id);
      if (waveType === PickWaveType.DiscreteOrder) return [id];
      return [...prev, id];
    });
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (inFlightRef.current || isSubmitting || !activeFacility) return;
    setSubmitError(null);

    if (!clientId) {
      setSubmitError('Select a client tenant.');
      return;
    }
    if (waveType === PickWaveType.ZonePick && !zoneId.trim()) {
      setSubmitError('Zone ID is required when generating a ZonePick wave.');
      return;
    }
    if (waveType === PickWaveType.DiscreteOrder && selectedOrderIds.length > 1) {
      setSubmitError('DiscreteOrder wave type supports at most one selected order.');
      return;
    }

    const requestPayload: PickWaveGenerationRequest = {
      clientId,
      facilityId: activeFacility.id,
      waveType,
      zoneId: waveType === PickWaveType.ZonePick ? zoneId.trim() : undefined,
      tempClass: tempClass ? (tempClass as StorageTempClass) : undefined,
      priority: priority.trim() ? Number(priority) : undefined,
      maxOrders:
        waveType === PickWaveType.DiscreteOrder
          ? 1
          : maxOrders.trim()
          ? Number(maxOrders)
          : undefined,
      waveNumber: waveNumber.trim() || undefined,
      orderIds: selectedOrderIds.length > 0 ? selectedOrderIds : undefined,
    };

    inFlightRef.current = true;
    setIsSubmitting(true);
    try {
      const wave = await outboundApi.generatePickWave(requestPayload);
      onGenerated(wave);
      onClose();
    } catch (err: any) {
      if (err instanceof ApiError && (err.status === 403 || err.status === 409 || err.status === 422)) {
        setSubmitError(`HTTP ${err.status}: ${err.message}`);
      } else {
        setSubmitError(err.message || 'Failed to generate pick wave.');
      }
    } finally {
      inFlightRef.current = false;
      setIsSubmitting(false);
    }
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title="Generate Pick Wave"
      subtitle={activeFacility ? `Facility: ${activeFacility.name} (${activeFacility.facilityCode})` : undefined}
      icon="Layers"
      maxWidth="700px"
    >
      <form onSubmit={handleSubmit} className="outbound-form-stack">
        {refError && (
          <div className="outbound-alert error" role="alert">
            {refError}
          </div>
        )}
        {submitError && (
          <div className="outbound-alert error" role="alert">
            {submitError}
          </div>
        )}

        <div className="outbound-form-grid">
          <div className="form-field">
            <label htmlFor="wave-client">Client Tenant *</label>
            <select
              id="wave-client"
              value={clientId}
              onChange={(e) => setClientId(e.target.value)}
              disabled={clients.length === 0}
            >
              {clients.length === 0 && <option value="">No authorized clients</option>}
              {clients.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.companyName} ({c.accountCode})
                </option>
              ))}
            </select>
          </div>

          <div className="form-field">
            <label htmlFor="wave-type">Wave Type *</label>
            <select
              id="wave-type"
              value={waveType}
              onChange={(e) => {
                const next = e.target.value as PickWaveType;
                setWaveType(next);
                if (next === PickWaveType.DiscreteOrder && selectedOrderIds.length > 1) {
                  setSelectedOrderIds(selectedOrderIds.slice(0, 1));
                }
              }}
            >
              <option value={PickWaveType.DiscreteOrder}>Discrete Order</option>
              <option value={PickWaveType.BatchPick}>Batch Pick</option>
              <option value={PickWaveType.ZonePick}>Zone Pick</option>
              <option value={PickWaveType.BulkPick}>Bulk Pick</option>
            </select>
          </div>

          {waveType === PickWaveType.ZonePick && (
            <div className="form-field">
              <label htmlFor="wave-zone">Warehouse Zone *</label>
              {zones.length > 0 ? (
                <select id="wave-zone" value={zoneId} onChange={(e) => setZoneId(e.target.value)}>
                  <option value="">Select warehouse zone...</option>
                  {zones.map((z) => (
                    <option key={z.id} value={z.id}>
                      {z.label}
                    </option>
                  ))}
                </select>
              ) : (
                <input
                  id="wave-zone"
                  type="text"
                  value={zoneId}
                  onChange={(e) => setZoneId(e.target.value)}
                  placeholder="Enter Zone UUID"
                />
              )}
            </div>
          )}

          <div className="form-field">
            <label htmlFor="wave-temp">Storage Temperature Class</label>
            <select
              id="wave-temp"
              value={tempClass}
              onChange={(e) => setTempClass(e.target.value)}
            >
              <option value="">All Temperature Classes</option>
              {Object.values(StorageTempClass).map((tc) => (
                <option key={tc} value={tc}>
                  {tc}
                </option>
              ))}
            </select>
          </div>

          <div className="form-field">
            <label htmlFor="wave-priority">Priority Override</label>
            <input
              id="wave-priority"
              type="number"
              min={1}
              max={10}
              value={priority}
              onChange={(e) => setPriority(e.target.value)}
              placeholder="Derived from orders"
            />
          </div>

          {waveType !== PickWaveType.DiscreteOrder && (
            <div className="form-field">
              <label htmlFor="wave-max-orders">Max Orders</label>
              <input
                id="wave-max-orders"
                type="number"
                min={1}
                max={100}
                value={maxOrders}
                onChange={(e) => setMaxOrders(e.target.value)}
              />
            </div>
          )}

          <div className="form-field">
            <label htmlFor="wave-number">Wave Number (Optional)</label>
            <input
              id="wave-number"
              type="text"
              value={waveNumber}
              onChange={(e) => setWaveNumber(e.target.value)}
              placeholder="Auto-generated if blank"
            />
          </div>
        </div>

        {eligibleOrders.length > 0 && (
          <fieldset className="outbound-fieldset">
            <legend>
              Allocated Orders ({selectedOrderIds.length} selected; leave unchecked to auto-prioritize)
            </legend>
            <div className="eligible-orders-list">
              {eligibleOrders.map((ord) => (
                <label key={ord.id} className="eligible-order-checkbox">
                  <input
                    type={waveType === PickWaveType.DiscreteOrder ? 'radio' : 'checkbox'}
                    name="wave-order-pick"
                    checked={selectedOrderIds.includes(ord.id)}
                    onChange={() => toggleOrderSelection(ord.id)}
                  />
                  <span>
                    <strong>{ord.orderNumber}</strong> &bull; {ord.customerName} (P{ord.priority})
                  </span>
                </label>
              ))}
            </div>
          </fieldset>
        )}

        <div className="modal-inline-actions">
          <button type="button" className="btn btn-secondary" onClick={onClose} disabled={isSubmitting}>
            Cancel
          </button>
          <button
            type="submit"
            className="btn btn-primary"
            disabled={isSubmitting || Boolean(refError) || !clientId}
            data-testid="submit-generate-wave"
          >
            {isSubmitting ? 'Generating Wave...' : 'Generate Pick Wave'}
          </button>
        </div>
      </form>
    </Modal>
  );
};
