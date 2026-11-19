import React, { useState } from 'react';

interface TrackingMilestone {
  step: string;
  location: string;
  timestamp: string;
  completed: boolean;
  current: boolean;
}

interface ShipmentInfo {
  awbNumber: string;
  carrierCode: string;
  carrierName: string;
  serviceLevel: string;
  origin: string;
  destination: string;
  status: 'InTransit' | 'Delivered' | 'OutForDelivery' | 'Booked';
  statusBadge: string;
  estimatedDelivery: string;
  weightKg: number;
  packagesCount: number;
  milestones: TrackingMilestone[];
}

const SAMPLE_DATA: Record<string, ShipmentInfo> = {
  'AWB-9842-US': {
    awbNumber: 'AWB-9842-US',
    carrierCode: 'FEDEX',
    carrierName: 'FedEx Priority Express',
    serviceLevel: 'Express',
    origin: 'Facility 1 (Ontario, CA)',
    destination: 'Seattle, WA',
    status: 'OutForDelivery',
    statusBadge: 'badge-info',
    estimatedDelivery: 'Today by 4:30 PM',
    weightKg: 4.85,
    packagesCount: 2,
    milestones: [
      { step: 'Shipment Booked & AWB Issued', location: 'Ontario Facility', timestamp: '2026-11-18 08:30 UTC', completed: true, current: false },
      { step: 'Picked Up by Courier', location: 'Ontario Dock Door 04', timestamp: '2026-11-18 11:15 UTC', completed: true, current: false },
      { step: 'Departed Origin Facility', location: 'Ontario Sortation Center', timestamp: '2026-11-18 16:40 UTC', completed: true, current: false },
      { step: 'Arrived at Destination Hub', location: 'Seattle Regional Hub', timestamp: '2026-11-19 04:10 UTC', completed: true, current: false },
      { step: 'Out for Delivery', location: 'Seattle West Metro', timestamp: '2026-11-19 08:20 UTC', completed: true, current: true },
      { step: 'Delivered & Signed', location: 'Recipient Address', timestamp: 'Pending', completed: false, current: false }
    ]
  },
  'AWB-7719-EXP': {
    awbNumber: 'AWB-7719-EXP',
    carrierCode: 'UPS',
    carrierName: 'UPS Worldwide Saver',
    serviceLevel: 'NextDayAir',
    origin: 'Facility 2 (Dallas, TX)',
    destination: 'New York, NY',
    status: 'Delivered',
    statusBadge: 'badge-success',
    estimatedDelivery: 'Delivered at 10:15 AM',
    weightKg: 12.40,
    packagesCount: 1,
    milestones: [
      { step: 'Shipment Booked & AWB Issued', location: 'Dallas Facility', timestamp: '2026-11-17 09:00 UTC', completed: true, current: false },
      { step: 'Picked Up by Courier', location: 'Dallas Dock Door 12', timestamp: '2026-11-17 14:20 UTC', completed: true, current: false },
      { step: 'Departed Origin Facility', location: 'Dallas Air Hub', timestamp: '2026-11-17 21:00 UTC', completed: true, current: false },
      { step: 'Arrived at Destination Hub', location: 'JFK Air Cargo', timestamp: '2026-11-18 03:45 UTC', completed: true, current: false },
      { step: 'Out for Delivery', location: 'Manhattan Midtown', timestamp: '2026-11-18 07:30 UTC', completed: true, current: false },
      { step: 'Delivered & Signed (J. Miller)', location: 'New York, NY', timestamp: '2026-11-18 10:15 UTC', completed: true, current: false }
    ]
  },
  'AWB-5531-FRT': {
    awbNumber: 'AWB-5531-FRT',
    carrierCode: 'XPO',
    carrierName: 'XPO Freight Logistics',
    serviceLevel: 'LTL_Freight',
    origin: 'Facility 3 (Chicago, IL)',
    destination: 'Atlanta, GA',
    status: 'InTransit',
    statusBadge: 'badge-info',
    estimatedDelivery: 'Tomorrow by 2:00 PM',
    weightKg: 340.00,
    packagesCount: 4,
    milestones: [
      { step: 'Pallet Freight Booked', location: 'Chicago Facility', timestamp: '2026-11-18 10:00 UTC', completed: true, current: false },
      { step: 'Cross-Dock Loaded to Linehaul', location: 'Chicago Bay 08', timestamp: '2026-11-18 17:30 UTC', completed: true, current: false },
      { step: 'In Transit via I-65 South', location: 'Indianapolis Transit Checkpoint', timestamp: '2026-11-19 02:45 UTC', completed: true, current: true },
      { step: 'Arrived at Destination Hub', location: 'Atlanta South Terminal', timestamp: 'Pending', completed: false, current: false },
      { step: 'Final Delivery to Consignee', location: 'Atlanta Warehouse Dock', timestamp: 'Pending', completed: false, current: false }
    ]
  }
};

export const TrackingSimulator: React.FC = () => {
  const [query, setQuery] = useState('AWB-9842-US');
  const [activeShipment, setActiveShipment] = useState<ShipmentInfo | null>(SAMPLE_DATA['AWB-9842-US']);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [announcement, setAnnouncement] = useState('Tracking simulator loaded with sample AWB-9842-US');

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault();
    const trimmed = query.trim().toUpperCase();
    if (SAMPLE_DATA[trimmed]) {
      setActiveShipment(SAMPLE_DATA[trimmed]);
      setErrorMsg(null);
      setAnnouncement(`Loaded simulated shipment ${trimmed} with status ${SAMPLE_DATA[trimmed].status}.`);
    } else {
      // CLEAR STALE SHIPMENT RESULT UPON UNKNOWN SEARCH
      setActiveShipment(null);
      setErrorMsg(`No simulated record found for "${trimmed}". Try one of the sample presets below.`);
      setAnnouncement(`Search failed: no simulated record found for ${trimmed}.`);
    }
  };

  const handlePresetClick = (presetAwb: string) => {
    setQuery(presetAwb);
    setActiveShipment(SAMPLE_DATA[presetAwb]);
    setErrorMsg(null);
    setAnnouncement(`Switched to sample synthetic shipment ${presetAwb}.`);
  };

  return (
    <section id="tracking" className="tracking-section" aria-label="Courier Shipment Tracking Simulator">
      <div className="container">
        <div className="section-header">
          <div className="demo-header-badge">
            <span className="badge badge-warning">SIMULATED COURIER TELEMETRY DEMO &bull; SYNTHETIC AWB FIXTURES</span>
          </div>
          <h2>Live Air Waybill (AWB) Tracking Simulator</h2>
          <p className="subtitle">
            Illustrative user-facing milestone tracker designed against the Phase 4.5 courier tracking architecture.
          </p>
        </div>

        <div className="card tracking-card">
          <form className="tracking-form" onSubmit={handleSearch}>
            <div className="input-group">
              <label htmlFor="awbInput" className="visually-hidden">AWB Tracking Number</label>
              <input
                id="awbInput"
                type="text"
                className="awb-input"
                placeholder="Enter AWB or Tracking ID (e.g. AWB-9842-US)"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                aria-label="Air Waybill Tracking Number"
              />
              <button type="submit" className="btn btn-primary track-btn">
                Track Shipment &rarr;
              </button>
            </div>
            
            <div className="preset-links">
              <span className="preset-label">Sample Synthetic AWB Presets:</span>
              <button type="button" className="preset-btn" onClick={() => handlePresetClick('AWB-9842-US')}>AWB-9842-US (Out for Delivery)</button>
              <button type="button" className="preset-btn" onClick={() => handlePresetClick('AWB-7719-EXP')}>AWB-7719-EXP (Delivered)</button>
              <button type="button" className="preset-btn" onClick={() => handlePresetClick('AWB-5531-FRT')}>AWB-5531-FRT (In Transit)</button>
            </div>
          </form>

          {/* Accessible Live Announcement Region */}
          <div className="visually-hidden" aria-live="polite" aria-atomic="true">
            {announcement}
          </div>

          {errorMsg && (
            <div className="alert-box" role="alert">
              {errorMsg}
            </div>
          )}

          {activeShipment && (
            <div className="tracking-result">
              <div className="shipment-summary-bar">
                <div className="summary-col">
                  <span className="meta-label">Waybill Number</span>
                  <strong className="awb-display">{activeShipment.awbNumber}</strong>
                </div>
                <div className="summary-col">
                  <span className="meta-label">Carrier &amp; Service</span>
                  <strong>{activeShipment.carrierName} ({activeShipment.serviceLevel})</strong>
                </div>
                <div className="summary-col">
                  <span className="meta-label">Route</span>
                  <span>{activeShipment.origin} &rarr; {activeShipment.destination}</span>
                </div>
                <div className="summary-col">
                  <span className="meta-label">Current Status</span>
                  <span className={`badge ${activeShipment.statusBadge}`}>
                    {activeShipment.status}
                  </span>
                </div>
                <div className="summary-col">
                  <span className="meta-label">Est. Delivery</span>
                  <strong className="highlight-text">{activeShipment.estimatedDelivery}</strong>
                </div>
              </div>

              {/* Barcode & Weight Tag Preview */}
              <div className="barcode-tag-row">
                <div className="barcode-sim">
                  <div className="barcode-lines" aria-hidden="true">
                    <span></span><span></span><span></span><span></span><span></span>
                    <span></span><span></span><span></span><span></span><span></span>
                    <span></span><span></span><span></span><span></span><span></span>
                  </div>
                  <span className="barcode-text">*{activeShipment.awbNumber}*</span>
                </div>
                <div className="tag-badges">
                  <span className="meta-tag">Weight: <strong>{activeShipment.weightKg.toFixed(2)} KG</strong></span>
                  <span className="meta-tag">Packages: <strong>{activeShipment.packagesCount} Carton(s)</strong></span>
                  <span className="meta-tag">Data Source: <strong>Synthetic Demo Fixture</strong></span>
                </div>
              </div>

              {/* Milestone Timeline */}
              <div className="timeline-container">
                <h3 className="timeline-heading">Milestone Progression (Normalized Enum Milestones)</h3>
                <div className="milestone-stepper">
                  {activeShipment.milestones.map((m, idx) => (
                    <div 
                      key={idx} 
                      className={`milestone-node ${m.completed ? 'completed' : ''} ${m.current ? 'current' : ''}`}
                    >
                      <div className="node-marker">
                        {m.completed && !m.current ? '✓' : (idx + 1)}
                      </div>
                      <div className="node-content">
                        <strong className="node-title">{m.step}</strong>
                        <span className="node-location">{m.location}</span>
                        <span className="node-time">{m.timestamp}</span>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            </div>
          )}
        </div>
      </div>

      <style>{`
        .tracking-section {
          padding: 80px 0;
          background: #f8fafc;
          border-top: 1px solid var(--border-subtle);
          border-bottom: 1px solid var(--border-subtle);
        }
        .section-header {
          text-align: center;
          max-width: 650px;
          margin: 0 auto 36px;
        }
        .demo-header-badge {
          margin-bottom: 12px;
        }
        .section-header h2 {
          font-size: 28px;
          font-weight: 800;
          color: var(--text-main);
          margin: 0 0 8px;
          letter-spacing: -0.5px;
        }
        .subtitle {
          color: var(--text-muted);
          font-size: 15px;
        }
        .tracking-card {
          max-width: 960px;
          margin: 0 auto;
          background: #ffffff;
          border-radius: var(--radius-lg);
          box-shadow: 0 4px 20px rgba(15, 23, 42, 0.06);
          padding: 32px;
        }
        .input-group {
          display: flex;
          gap: 12px;
        }
        .awb-input {
          flex: 1;
          padding: 14px 18px;
          font-size: 15px;
          font-weight: 550;
          border: 1.5px solid var(--border-actionable);
          border-radius: var(--radius-sm);
          outline: none;
          color: var(--text-main);
          background: #fff;
          transition: border-color var(--transition-fast);
        }
        .awb-input:focus {
          border-color: var(--brand-primary);
          box-shadow: 0 0 0 3px rgba(82, 84, 230, 0.15);
        }
        .track-btn {
          padding: 14px 24px;
          font-size: 14.5px;
        }
        .preset-links {
          display: flex;
          flex-wrap: wrap;
          align-items: center;
          gap: 8px;
          margin-top: 14px;
          font-size: 12.5px;
        }
        .preset-label {
          color: var(--text-muted);
          font-weight: 600;
        }
        .preset-btn {
          color: var(--brand-primary);
          font-weight: 600;
          background: var(--brand-primary-light);
          padding: 3px 9px;
          border-radius: var(--radius-xs);
          border: 1px solid rgba(82, 84, 230, 0.15);
        }
        .preset-btn:hover {
          background: #dfdfff;
        }
        .alert-box {
          margin-top: 18px;
          padding: 14px 18px;
          background: #fff1f2;
          border: 1px solid #fecdd3;
          border-radius: var(--radius-sm);
          color: #9f1239;
          font-size: 13.5px;
        }
        .tracking-result {
          margin-top: 32px;
          padding-top: 24px;
          border-top: 1px solid var(--border-subtle);
        }
        .shipment-summary-bar {
          display: grid;
          grid-template-columns: repeat(auto-fit, minmax(170px, 1fr));
          gap: 16px;
          background: #f8fafc;
          padding: 18px 22px;
          border-radius: var(--radius-md);
          border: 1px solid var(--border-subtle);
        }
        .summary-col {
          display: flex;
          flex-direction: column;
          gap: 4px;
          font-size: 13.5px;
        }
        .meta-label {
          font-size: 11.5px;
          font-weight: 650;
          text-transform: uppercase;
          color: var(--text-muted);
          letter-spacing: 0.5px;
        }
        .awb-display {
          color: var(--brand-primary);
          font-size: 15px;
        }
        .highlight-text {
          color: #0369a1;
        }
        .barcode-tag-row {
          display: flex;
          align-items: center;
          justify-content: space-between;
          flex-wrap: wrap;
          gap: 16px;
          margin-top: 24px;
          padding: 14px 20px;
          background: #ffffff;
          border: 1px dashed var(--border-actionable);
          border-radius: var(--radius-sm);
        }
        .barcode-sim {
          display: flex;
          flex-direction: column;
          align-items: center;
          gap: 4px;
        }
        .barcode-lines {
          display: flex;
          gap: 2.5px;
          height: 32px;
        }
        .barcode-lines span {
          width: 2px;
          background: #1e293b;
          border-radius: 1px;
        }
        .barcode-lines span:nth-child(2n) { width: 4px; }
        .barcode-lines span:nth-child(3n) { width: 1.5px; }
        .barcode-lines span:nth-child(5n) { width: 3px; }
        .barcode-text {
          font-family: monospace;
          font-size: 11px;
          letter-spacing: 2px;
          color: #475569;
        }
        .tag-badges {
          display: flex;
          gap: 12px;
          flex-wrap: wrap;
        }
        .meta-tag {
          font-size: 12.5px;
          color: #334155;
          background: #f1f5f9;
          padding: 4px 10px;
          border-radius: var(--radius-xs);
        }
        .timeline-container {
          margin-top: 32px;
        }
        .timeline-heading {
          font-size: 16px;
          font-weight: 700;
          color: var(--text-main);
          margin-bottom: 20px;
        }
        .milestone-stepper {
          display: flex;
          flex-direction: column;
          gap: 16px;
          position: relative;
          padding-left: 28px;
        }
        .milestone-stepper::before {
          content: '';
          position: absolute;
          left: 11px;
          top: 10px;
          bottom: 10px;
          width: 2px;
          background: var(--border-subtle);
        }
        .milestone-node {
          position: relative;
          display: flex;
          gap: 16px;
          align-items: flex-start;
        }
        /* High-contrast node-marker: Slate 900 (#0f172a) on Slate 200 (#e2e8f0) gives 12.8:1 contrast */
        .node-marker {
          position: absolute;
          left: -28px;
          top: 2px;
          width: 24px;
          height: 24px;
          border-radius: 50%;
          background: #e2e8f0;
          color: #0f172a;
          display: flex;
          align-items: center;
          justify-content: center;
          font-size: 11.5px;
          font-weight: 800;
          z-index: 1;
        }
        .milestone-node.completed .node-marker {
          background: var(--status-success-text);
          color: #ffffff;
        }
        .milestone-node.current .node-marker {
          background: var(--brand-primary);
          color: #ffffff;
          box-shadow: 0 0 0 4px rgba(82, 84, 230, 0.2);
        }
        .node-content {
          display: flex;
          flex-direction: column;
          gap: 2px;
        }
        .node-title {
          font-size: 14px;
          color: var(--text-main);
        }
        .node-location {
          font-size: 12.5px;
          color: var(--text-muted);
        }
        .node-time {
          font-size: 11.5px;
          color: #475569;
        }
        .visually-hidden {
          position: absolute;
          width: 1px;
          height: 1px;
          margin: -1px;
          padding: 0;
          overflow: hidden;
          clip: rect(0, 0, 0, 0);
          border: 0;
        }
        @media (max-width: 640px) {
          .input-group { flex-direction: column; }
          .shipment-summary-bar { grid-template-columns: 1fr; }
          .tracking-card { padding: 20px; }
        }
      `}</style>
    </section>
  );
};
