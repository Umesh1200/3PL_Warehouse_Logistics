import React, { useEffect, useRef, useState } from 'react';
import { Modal } from '../common/Modal';
import { outboundApi } from '../../services/outboundApi';
import { EligiblePickOperatorDto, PickTaskDto } from '../../types/outbound';

export interface AssignPickTaskModalProps {
  isOpen: boolean;
  onClose: () => void;
  task: PickTaskDto | null;
  facilityId: string | undefined;
  onAssigned: (updated: PickTaskDto) => void;
}

export const AssignPickTaskModal: React.FC<AssignPickTaskModalProps> = ({
  isOpen,
  onClose,
  task,
  facilityId,
  onAssigned,
}) => {
  const [operators, setOperators] = useState<EligiblePickOperatorDto[]>([]);
  const [selectedOperatorId, setSelectedOperatorId] = useState('');
  const [loadingOps, setLoadingOps] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const inFlightRef = useRef(false);

  useEffect(() => {
    if (!isOpen || !task || !facilityId) return;
    const ctrl = new AbortController();
    setError(null);
    setSelectedOperatorId(task.assignedUserId || '');
    setLoadingOps(true);

    outboundApi
      .getEligiblePickOperators(facilityId, ctrl.signal)
      .then((res) => {
        setOperators(res.items);
        if (!task.assignedUserId && res.items.length > 0) {
          setSelectedOperatorId(res.items[0].id);
        }
        if (res.items.length === 0) {
          setError('No active staff operators are assigned to this facility.');
        }
      })
      .catch((err: any) => {
        if (err.name !== 'AbortError') {
          setError(err.message || 'Unable to load eligible facility operators.');
        }
      })
      .finally(() => setLoadingOps(false));

    return () => ctrl.abort();
  }, [isOpen, task?.id, facilityId]);

  if (!task) return null;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (inFlightRef.current || isSubmitting || !selectedOperatorId) return;

    inFlightRef.current = true;
    setIsSubmitting(true);
    setError(null);
    try {
      const updated = await outboundApi.assignPickTask(task.id, selectedOperatorId);
      onAssigned(updated);
    } catch (err: any) {
      setError(err.message || 'Failed to assign pick task.');
    } finally {
      inFlightRef.current = false;
      setIsSubmitting(false);
    }
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title="Assign Pick Task to Operator"
      subtitle={`${task.sku} • Order ${task.orderNumber} • Bin ${task.sourceLocationCode}`}
      icon="Users"
      maxWidth="520px"
    >
      <form onSubmit={handleSubmit} className="outbound-form-stack" data-testid="assign-pick-task-form">
        {error && (
          <div className="outbound-alert error" role="alert" data-testid="assign-task-error">
            {error}
          </div>
        )}

        <div className="form-field">
          <label htmlFor="assign-operator-select">Facility Warehouse Staff Operator *</label>
          <select
            id="assign-operator-select"
            value={selectedOperatorId}
            onChange={(e) => setSelectedOperatorId(e.target.value)}
            disabled={loadingOps || operators.length === 0}
            data-testid="assign-operator-select"
          >
            {operators.length === 0 ? (
              <option value="">{loadingOps ? 'Loading operators...' : 'No eligible operators'}</option>
            ) : (
              operators.map((op) => (
                <option key={op.id} value={op.id}>
                  {op.firstName} {op.lastName} ({op.email})
                </option>
              ))
            )}
          </select>
        </div>

        <div className="modal-inline-actions">
          <button type="button" className="btn btn-secondary" onClick={onClose} disabled={isSubmitting}>
            Cancel
          </button>
          <button
            type="submit"
            className="btn btn-primary"
            disabled={isSubmitting || loadingOps || !selectedOperatorId}
            data-testid="submit-assign-task-btn"
          >
            {isSubmitting ? 'Assigning...' : 'Confirm Assignment'}
          </button>
        </div>
      </form>
    </Modal>
  );
};
