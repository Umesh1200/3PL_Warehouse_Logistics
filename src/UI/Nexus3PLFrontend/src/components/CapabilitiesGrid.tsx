import React from 'react';

interface CapabilityItem {
  icon: string;
  tag: string;
  title: string;
  desc: string;
  specLink: string;
}

const CAPABILITIES: CapabilityItem[] = [
  {
    icon: '🗺️',
    tag: 'Phase 6.3 Spatial WMS',
    title: '3D Spatial Warehouse Heatmap',
    desc: 'Interactive 2D/3D warehouse topology viewer with real-time level utilization, SKU velocity heatmaps, and spatial hotspot identification.',
    specLink: '#topology'
  },
  {
    icon: '📦',
    tag: 'Phase 3 Inventory Control',
    title: 'Directed Putaway & Cycle Counting',
    desc: 'Velocity-based putaway strategies, multi-attribute stock balance explorer (Lot, LPN, Expiry, Zone), and blind cycle count reconciliations.',
    specLink: '#inventory'
  },
  {
    icon: '⚡',
    tag: 'Phase 4 Outbound Engine',
    title: 'Batch Pick Waves & Packing Station',
    desc: 'Optimized wave planning, pick path optimization, integrated cartonization, scale weight capture, and cross-docking bypass.',
    specLink: '#fulfillment'
  },
  {
    icon: '🚚',
    tag: 'Phase 6.4 Transportation',
    title: 'Multi-Carrier Routing & AWB Dispatch',
    desc: 'Dynamic courier rate quoting, automated Air Waybill (AWB) generation with Code128 barcodes, dispatch manifests, and milestone telemetry.',
    specLink: '#transportation'
  },
  {
    icon: '💰',
    tag: 'Phase 5 Financial Reconciliation',
    title: 'Freight Margin Engine & Leakage Audits',
    desc: 'Buy-side carrier rate agreements vs sell-side client tariffs, automated accessorial leakage classification, and discrepancy quarantine queues.',
    specLink: '#billing'
  },
  {
    icon: '📊',
    tag: 'Phase 6.8 Analytics Governance',
    title: 'Executive Control Tower & AI Decisions',
    desc: 'Real-time throughput metrics, carrier performance scorecards, evidence-based labor allocation advice, and immutable projection journals.',
    specLink: '#analytics'
  }
];

export const CapabilitiesGrid: React.FC = () => {
  return (
    <section id="capabilities" className="capabilities-section" aria-label="Platform Capabilities">
      <div className="container">
        <div className="section-head">
          <span className="badge badge-info">Modular Enterprise Architecture</span>
          <h2>Comprehensive 3PL Operating Capabilities</h2>
          <p className="section-sub">
            Engineered with strict tenant isolation, immutable event journaling, and clean separation between operational execution and financial governance.
          </p>
        </div>

        <div className="capabilities-grid">
          {CAPABILITIES.map((cap, i) => (
            <div key={i} className="card cap-card">
              <div className="cap-header">
                <span className="cap-icon" aria-hidden="true">{cap.icon}</span>
                <span className="badge badge-warning cap-tag">{cap.tag}</span>
              </div>
              <h3 className="cap-title">{cap.title}</h3>
              <p className="cap-desc">{cap.desc}</p>
            </div>
          ))}
        </div>
      </div>

      <style>{`
        .capabilities-section {
          padding: 80px 0;
          background: #ffffff;
        }
        .section-head {
          text-align: center;
          max-width: 680px;
          margin: 0 auto 48px;
        }
        .section-head h2 {
          font-size: 30px;
          font-weight: 800;
          color: var(--text-main);
          margin: 12px 0 8px;
          letter-spacing: -0.5px;
        }
        .section-sub {
          font-size: 15px;
          color: var(--text-muted);
          line-height: 1.6;
        }
        .capabilities-grid {
          display: grid;
          grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
          gap: 24px;
        }
        .cap-card {
          border-radius: var(--radius-md);
          border: 1px solid var(--border-subtle);
          box-shadow: 0 2px 10px rgba(15, 23, 42, 0.04);
          transition: transform var(--transition-fast), box-shadow var(--transition-fast), border-color var(--transition-fast);
          padding: 28px;
        }
        .cap-card:hover {
          transform: translateY(-4px);
          box-shadow: 0 10px 24px rgba(82, 84, 230, 0.1);
          border-color: rgba(82, 84, 230, 0.3);
        }
        .cap-header {
          display: flex;
          align-items: center;
          justify-content: space-between;
          margin-bottom: 18px;
        }
        .cap-icon {
          font-size: 28px;
        }
        .cap-tag {
          font-size: 11px;
        }
        .cap-title {
          font-size: 18px;
          font-weight: 750;
          color: var(--text-main);
          margin-bottom: 10px;
        }
        .cap-desc {
          font-size: 14px;
          color: var(--text-muted);
          line-height: 1.6;
        }
      `}</style>
    </section>
  );
};
