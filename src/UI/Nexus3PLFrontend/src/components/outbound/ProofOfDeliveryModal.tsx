import React, { useEffect, useRef, useState } from 'react';
import { dispatchApi } from '../../services/dispatchApi';
import {
  DeliveryConfirmationResultDto,
  PodEvidenceSource,
  ProofOfDeliveryDto,
} from '../../types/outbound';
import { generateActionIdempotencyKey } from './OutboundStatusBadge';
import { Modal } from '../common/Modal';

export interface ProofOfDeliveryModalProps {
  isOpen: boolean;
  packageId: string | null;
  packageNumber?: string | null;
  canViewPod: boolean;
  canExecuteDelivery: boolean;
  canManageRuns: boolean;
  isDriver: boolean;
  onClose: () => void;
  onCompleted: () => Promise<void> | void;
  onFeedback: (fb: { type: 'success' | 'warning' | 'error'; message: string }) => void;
}

export const ProofOfDeliveryModal: React.FC<ProofOfDeliveryModalProps> = ({
  isOpen,
  packageId,
  packageNumber,
  canViewPod,
  canExecuteDelivery,
  canManageRuns,
  isDriver,
  onClose,
  onCompleted,
  onFeedback,
}) => {
  const [existingPod, setExistingPod] = useState<ProofOfDeliveryDto | null>(null);
  const [loadingPod, setLoadingPod] = useState(false);
  const [showSupersedeForm, setShowSupersedeForm] = useState(false);

  // Submission / Supersession Form State
  const [recipientName, setRecipientName] = useState('');
  const [locationDesc, setLocationDesc] = useState('');
  const [evidenceSource, setEvidenceSource] = useState<PodEvidenceSource>(
    isDriver ? PodEvidenceSource.DriverHandheld : PodEvidenceSource.ManualVerification
  );
  const [verificationReason, setVerificationReason] = useState('');
  const [supersedeReason, setSupersedeReason] = useState('');
  const [isSigned, setIsSigned] = useState(true);
  const [signatureFile, setSignatureFile] = useState<File | null>(null);
  const [photoFile, setPhotoFile] = useState<File | null>(null);
  const [latitude, setLatitude] = useState('');
  const [longitude, setLongitude] = useState('');

  const [submitting, setSubmitting] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [confirmationResult, setConfirmationResult] = useState<DeliveryConfirmationResultDto | null>(
    null
  );
  const [error, setError] = useState<string | null>(null);

  const inFlightRef = useRef(false);
  const idempKeyRef = useRef<string | null>(null);

  useEffect(() => {
    if (!isOpen || !packageId) return;
    setError(null);
    setConfirmationResult(null);
    setShowSupersedeForm(false);
    setRecipientName('');
    setLocationDesc('');
    setVerificationReason('');
    setSupersedeReason('');
    setSignatureFile(null);
    setPhotoFile(null);
    setLatitude('');
    setLongitude('');
    setIsSigned(true);
    setEvidenceSource(
      isDriver ? PodEvidenceSource.DriverHandheld : PodEvidenceSource.ManualVerification
    );
    idempKeyRef.current = null;

    if (!canViewPod || isDriver) {
      setExistingPod(null);
      return;
    }

    const ctrl = new AbortController();
    setLoadingPod(true);
    dispatchApi
      .getPackagePod(packageId, ctrl.signal)
      .then((pod) => setExistingPod(pod))
      .catch((err: any) => {
        if (err.name !== 'AbortError' && err.status !== 404) {
          setError(err.message || 'Failed to load Proof of Delivery.');
        } else {
          setExistingPod(null);
        }
      })
      .finally(() => setLoadingPod(false));

    return () => ctrl.abort();
  }, [isOpen, packageId, canViewPod, isDriver]);

  if (!isOpen || !packageId) return null;

  const effectiveSource = isDriver ? PodEvidenceSource.DriverHandheld : evidenceSource;

  const fileToBase64 = (file: File): Promise<string> =>
    new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => {
        const result = String(reader.result || '');
        const commaIdx = result.indexOf(',');
        resolve(commaIdx >= 0 ? result.slice(commaIdx + 1) : result);
      };
      reader.onerror = () => reject(new Error(`Failed to read ${file.name}`));
      reader.readAsDataURL(file);
    });

  const handleSubmitPod = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canExecuteDelivery || inFlightRef.current || submitting) return;

    if (!recipientName.trim()) {
      setError('Recipient name is required.');
      return;
    }
    if (effectiveSource === PodEvidenceSource.ManualVerification && !verificationReason.trim()) {
      setError('Audit verification reason is required for Staff manual POD entries.');
      return;
    }

    if (!idempKeyRef.current) {
      idempKeyRef.current = generateActionIdempotencyKey('pod');
    }

    inFlightRef.current = true;
    setSubmitting(true);
    setError(null);
    try {
      const res = await dispatchApi.submitProofOfDelivery(packageId, {
        recipientName: recipientName.trim(),
        deliveryLocationDescription: locationDesc.trim() || null,
        isSigned,
        signatureFile,
        photoFile,
        latitude: latitude.trim() ? Number(latitude.trim()) : null,
        longitude: longitude.trim() ? Number(longitude.trim()) : null,
        idempotencyKey: idempKeyRef.current,
        evidenceSource: effectiveSource,
        verificationReason:
          effectiveSource === PodEvidenceSource.ManualVerification
            ? verificationReason.trim()
            : null,
      });

      idempKeyRef.current = null;
      setExistingPod({
        id: res.proofOfDeliveryId,
        clientId: '',
        facilityId: '',
        packageId: res.packageId,
        packageNumber: res.packageNumber,
        trackingNumber: res.trackingNumber,
        carrierCode: res.carrierCode,
        recipientName: recipientName.trim(),
        deliveryLocationDescription: locationDesc.trim() || null,
        isSigned,
        isStorageFinalized: res.isStorageFinalized,
        preSignedSignatureUrl: res.preSignedSignatureUrl,
        preSignedPhotoUrl: res.preSignedPhotoUrl,
        signatureSha256: res.signatureSha256,
        photoSha256: res.photoSha256,
        evidenceSource: effectiveSource,
        capturedAtUtc: new Date().toISOString(),
        deliveredAtUtc: new Date().toISOString(),
        isSuperseded: false,
      });
      onFeedback({
        type: 'success',
        message: `Proof of Delivery recorded for ${res.packageNumber}. Ready to confirm final delivery.`,
      });
      await onCompleted();
    } catch (err: any) {
      setError(err.message || 'Failed to submit Proof of Delivery.');
    } finally {
      inFlightRef.current = false;
      setSubmitting(false);
    }
  };

  const handleConfirmDelivery = async (podId?: string) => {
    if (!canExecuteDelivery || confirming) return;
    setConfirming(true);
    setError(null);
    try {
      const conf = await dispatchApi.confirmPackageDelivery(packageId, {
        proofOfDeliveryId: podId || existingPod?.id || null,
      });
      setConfirmationResult(conf);
      onFeedback({
        type: 'success',
        message: `Package ${conf.packageNumber} marked Delivered. Order status: ${conf.orderDeliveryStatus}.`,
      });
      await onCompleted();
    } catch (err: any) {
      setError(err.message || 'Failed to confirm package delivery.');
    } finally {
      setConfirming(false);
    }
  };

  const handleSupersedePod = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canManageRuns || !existingPod || submitting) return;
    if (!supersedeReason.trim()) {
      setError('A supersession audit reason is required.');
      return;
    }
    if (!verificationReason.trim()) {
      setError('Manual verification reason is required for replacement POD.');
      return;
    }

    setSubmitting(true);
    setError(null);
    try {
      const sigB64 = signatureFile ? await fileToBase64(signatureFile) : null;
      const photoB64 = photoFile ? await fileToBase64(photoFile) : null;
      const res = await dispatchApi.supersedeProofOfDelivery(existingPod.id, {
        reason: supersedeReason.trim(),
        replacementSubmission: {
          recipientName: recipientName.trim() || existingPod.recipientName || 'Verified Recipient',
          deliveryLocationDescription: locationDesc.trim() || existingPod.deliveryLocationDescription || null,
          isSigned: isSigned || Boolean(sigB64),
          signatureBase64: sigB64,
          signatureContentType: signatureFile?.type || null,
          photoBase64: photoB64,
          photoContentType: photoFile?.type || null,
          evidenceSource: PodEvidenceSource.ManualVerification,
          verificationReason: verificationReason.trim(),
        },
      });
      onFeedback({
        type: 'success',
        message: `POD superseded cleanly (Replacement ID: ${res.proofOfDeliveryId}).`,
      });
      if (canViewPod && !isDriver) {
        const refreshed = await dispatchApi.getPackagePod(packageId);
        setExistingPod(refreshed);
      }
      setShowSupersedeForm(false);
      await onCompleted();
    } catch (err: any) {
      setError(err.message || 'Failed to supersede Proof of Delivery.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title={`Proof of Delivery — ${existingPod?.packageNumber || packageNumber || packageId}`}
      subtitle="Authenticated evidence capture, signed artifact preview, supersession & delivery confirmation."
      icon="PackageCheck"
      maxWidth="720px"
    >
      <div className="outbound-form-stack" data-testid="pod-modal-body">
        {error && (
          <div className="outbound-alert error" role="alert">
            <span>{error}</span>
          </div>
        )}

        {confirmationResult && (
          <div className="outbound-alert success" data-testid="pod-confirmation-banner">
            <span>
              Delivered confirmed at {new Date(confirmationResult.deliveredAtUtc).toLocaleString()} • Order
              Rollup: <strong>{confirmationResult.orderDeliveryStatus}</strong>
            </span>
          </div>
        )}

        {loadingPod && <div className="empty-inline-state">Loading Proof of Delivery record...</div>}

        {!loadingPod && existingPod && !showSupersedeForm && (
          <div className="rollup-summary-card" data-testid="pod-preview-card">
            <div className="dispatch-shipment-header">
              <strong>Active POD Evidence ({existingPod.evidenceSource})</strong>
              <span className="badge badge-success">
                {existingPod.isStorageFinalized ? 'Storage Finalized' : 'Pending Finalization'}
              </span>
            </div>
            <div className="dispatch-shipment-meta">
              <span>Recipient: <strong>{existingPod.recipientName || '—'}</strong></span>
              <span>Location: {existingPod.deliveryLocationDescription || '—'}</span>
              <span>Signed: {existingPod.isSigned ? 'Yes' : 'No'}</span>
              <span>Delivered: {new Date(existingPod.deliveredAtUtc).toLocaleString()}</span>
            </div>
            {(existingPod.preSignedSignatureUrl || existingPod.preSignedPhotoUrl) && (
              <div className="pod-media-grid">
                {existingPod.preSignedSignatureUrl && (
                  <div className="pod-media-box">
                    <span className="bol-party-label">Consignee Signature</span>
                    <img
                      src={existingPod.preSignedSignatureUrl}
                      alt="Consignee signature evidence"
                      data-testid="pod-signature-preview"
                    />
                  </div>
                )}
                {existingPod.preSignedPhotoUrl && (
                  <div className="pod-media-box">
                    <span className="bol-party-label">Delivery Photo</span>
                    <img
                      src={existingPod.preSignedPhotoUrl}
                      alt="Delivery drop location photo"
                      data-testid="pod-photo-preview"
                    />
                  </div>
                )}
              </div>
            )}
            <div className="modal-actions-row">
              {canExecuteDelivery && (
                <button
                  type="button"
                  className="btn btn-primary"
                  disabled={confirming}
                  onClick={() => handleConfirmDelivery(existingPod.id)}
                  data-testid="pod-confirm-delivery-btn"
                >
                  {confirming ? 'Confirming Delivery...' : 'Confirm Package Delivery'}
                </button>
              )}
              {canManageRuns && !isDriver && (
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={() => setShowSupersedeForm(true)}
                  data-testid="pod-open-supersede-btn"
                >
                  Supersede POD Evidence
                </button>
              )}
            </div>
          </div>
        )}

        {(!existingPod || showSupersedeForm) && (canExecuteDelivery || (showSupersedeForm && canManageRuns)) && (
          <form
            onSubmit={showSupersedeForm ? handleSupersedePod : handleSubmitPod}
            className="outbound-form-stack"
            data-testid={showSupersedeForm ? 'pod-supersede-form' : 'pod-submit-form'}
          >
            {showSupersedeForm && (
              <div className="form-field">
                <label htmlFor="pod-supersede-reason">Supersession Reason *</label>
                <input
                  id="pod-supersede-reason"
                  type="text"
                  value={supersedeReason}
                  onChange={(e) => setSupersedeReason(e.target.value)}
                  placeholder="Why previous POD is being replaced"
                  required
                  data-testid="pod-supersede-reason"
                />
              </div>
            )}

            <div className="form-grid-2col">
              <div className="form-field">
                <label htmlFor="pod-recipient">Recipient Name *</label>
                <input
                  id="pod-recipient"
                  type="text"
                  value={recipientName}
                  onChange={(e) => setRecipientName(e.target.value)}
                  placeholder="Consignee / dock receiver name"
                  required
                  data-testid="pod-recipient-input"
                />
              </div>
              <div className="form-field">
                <label htmlFor="pod-location">Drop Location Description</label>
                <input
                  id="pod-location"
                  type="text"
                  value={locationDesc}
                  onChange={(e) => setLocationDesc(e.target.value)}
                  placeholder="e.g. Receiving Bay 3"
                  data-testid="pod-location-input"
                />
              </div>
            </div>

            {!isDriver && !showSupersedeForm && (
              <div className="form-field">
                <label htmlFor="pod-source">Evidence Source</label>
                <select
                  id="pod-source"
                  value={evidenceSource}
                  onChange={(e) => setEvidenceSource(e.target.value as PodEvidenceSource)}
                  data-testid="pod-source-select"
                >
                  <option value={PodEvidenceSource.ManualVerification}>Manual Verification (Staff Audit)</option>
                  <option value={PodEvidenceSource.DriverHandheld}>Driver Handheld</option>
                </select>
              </div>
            )}

            {(effectiveSource === PodEvidenceSource.ManualVerification || showSupersedeForm) && (
              <div className="form-field">
                <label htmlFor="pod-verification-reason">Audit Verification Reason *</label>
                <input
                  id="pod-verification-reason"
                  type="text"
                  value={verificationReason}
                  onChange={(e) => setVerificationReason(e.target.value)}
                  placeholder="Paper manifest / carrier confirmation reference"
                  required
                  data-testid="pod-verification-reason"
                />
              </div>
            )}

            <div className="form-grid-2col">
              <div className="form-field">
                <label htmlFor="pod-sig-file">Signature Image (PNG/JPG/WebP)</label>
                <input
                  id="pod-sig-file"
                  type="file"
                  accept="image/png,image/jpeg,image/webp"
                  onChange={(e) => setSignatureFile(e.target.files?.[0] || null)}
                  data-testid="pod-sig-file"
                />
              </div>
              <div className="form-field">
                <label htmlFor="pod-photo-file">Delivery Photo (PNG/JPG/WebP)</label>
                <input
                  id="pod-photo-file"
                  type="file"
                  accept="image/png,image/jpeg,image/webp"
                  onChange={(e) => setPhotoFile(e.target.files?.[0] || null)}
                  data-testid="pod-photo-file"
                />
              </div>
            </div>

            {!showSupersedeForm && (
              <div className="form-grid-2col">
                <div className="form-field">
                  <label htmlFor="pod-lat">GPS Latitude (Optional)</label>
                  <input
                    id="pod-lat"
                    type="number"
                    step="0.000001"
                    value={latitude}
                    onChange={(e) => setLatitude(e.target.value)}
                    placeholder="41.8781"
                  />
                </div>
                <div className="form-field">
                  <label htmlFor="pod-lng">GPS Longitude (Optional)</label>
                  <input
                    id="pod-lng"
                    type="number"
                    step="0.000001"
                    value={longitude}
                    onChange={(e) => setLongitude(e.target.value)}
                    placeholder="-87.6298"
                  />
                </div>
              </div>
            )}

            <div className="modal-actions-row">
              {showSupersedeForm && (
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={() => setShowSupersedeForm(false)}
                >
                  Back to Preview
                </button>
              )}
              <button
                type="submit"
                className="btn btn-primary"
                disabled={submitting}
                data-testid={showSupersedeForm ? 'pod-supersede-submit-btn' : 'pod-submit-btn'}
              >
                {submitting
                  ? 'Uploading Evidence...'
                  : showSupersedeForm
                  ? 'Confirm POD Supersession'
                  : 'Submit Proof of Delivery'}
              </button>
            </div>
          </form>
        )}

        {!loadingPod && !existingPod && !canExecuteDelivery && (
          <div className="empty-inline-state">
            No Proof of Delivery has been captured for this package yet.
          </div>
        )}
      </div>
    </Modal>
  );
};
