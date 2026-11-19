import React from 'react';

interface CapabilityItem {
  id: string;
  icon: string;
  tag: string;
  title: string;
  desc: string;
}

const CAPABILITIES: CapabilityItem[] = [
  {
    id: 'topology',
    icon: '🗺️',
    tag: 'Phase 6.3 Spatial WMS',
    title: '3D Spatial Warehouse Heatmap',
    desc: 'Interactive 2D/3D warehouse topology viewer with real-time level utilization, SKU velocity heatmaps, and spatial hotspot identification.'
  },
  {
    id: 'inventory',
    icon: '📦',
    tag: 'Phase 3 Inventory Control',
    title: 'Directed Putaway & Cycle Counting',
    desc: 'Velocity-based putaway strategies, multi-attribute stock balance explorer (Lot, LPN, Expiry, Zone), and blind cycle count reconciliations.'
  },
  {
    id: 'fulfillment',
    icon: '⚡',
    tag: 'Phase 4 Outbound Engine',
    title: 'Batch Pick Waves & Packing Station',
    desc: 'Optimized wave planning, pick path optimization, integrated cartonization, scale weight capture, and cross-docking bypass.'
  },
  {
    id: 'courier',
    icon: '🚚',
    tag: 'Phase 6.4 Transportation',
    title: 'Multi-Carrier Routing & AWB Dispatch',
    desc: 'Dynamic courier rate quoting, automated Air Waybill (AWB) generation with Code128 barcodes, dispatch manifests, and milestone telemetry.'
  },
  {
    id: 'billing',
    icon: '💰',
    tag: 'Phase 5 Financial Reconciliation',
    title: 'Freight Margin Engine & Leakage Audits',
    desc: 'Buy-side carrier rate agreements vs sell-side client tariffs, automated accessorial leakage classification, and discrepancy quarantine queues.'
  },
  {
    id: 'discrepancies',
    icon: '⚖️',
    tag: 'Phase 6.4 Financial Variance',
    title: 'Accessorial Leakage & Discrepancy Queue',
    desc: 'Automated invoice line auditing against rate cards with strict quarantine isolation for unverified carrier surcharges.'
  },
  {
    id: 'analytics',
    icon: '📊',
    tag: 'Phase 6.6–6.7 Intelligence',
    title: 'Executive Control Tower & AI Decisions',
    desc: 'Real-time throughput metrics, carrier performance scorecards, evidence-based labor allocation advice, and bottleneck resolution.'
  },
  {
    id: 'governance',
    icon: '🛡️',
    tag: 'Phase 6.8 Analytics Governance',
    title: 'Event Ingestion Journal & Auditability',
    desc: '29 captured mutation paths with SHA-256 payload digests, durable event replay, and zero loss of historic accounting evidence.'
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
          {CAPABILITIES.map((cap) => (
            <div key={cap.id} id={cap.id} className="card cap-card">
              <div className="cap-header">
                <span className="cap-icon" aria-hidden="true">{cap.icon}</span>
                <span className="badge badge-warning cap-tag">{cap.tag}</span>
              </div>
              <h3 className="cap-title">{cap.title}</h3>
              <p className="cap-desc">{cap.desc}</p>
            </div>
          ))}
        </div>

        {/* Technical Architecture Overview Section */}
        <div id="architecture" className="architecture-block">
          <div className="card arch-card">
            <span className="badge badge-info">System Architecture</span>
            <h3 className="arch-title">Technical Architecture &amp; Governance Stack</h3>
            <p className="arch-desc">
              Built on a high-throughput <strong>.NET 10 WebApi</strong> core, PostgreSQL persistence with advisory transaction locks, 
              and a reactive <strong>Vite 7 + React 19 SPA</strong> frontend styled with Sneat-inspired accessible tokens.
            </p>
            <div className="arch-specs-grid">
              <div className="spec-item">
                <span className="spec-label">Runtime Engine</span>
                <strong>.NET 10.0 (ASP.NET Core)</strong>
              </div>
              <div className="spec-item">
                <span className="spec-label">Frontend Framework</span>
                <strong>React 19 + TypeScript 5.9 (Vite 7)</strong>
              </div>
              <div className="spec-item">
                <span className="spec-label">Security &amp; Isolation</span>
                <strong>OWASP Defense-in-Depth &bull; EF Multi-Tenant</strong>
              </div>
              <div className="spec-item">
                <span className="spec-label">Design Standard</span>
                <strong>WCAG 2.2 Level AA Verified</strong>
              </div>
            </div>
          </div>
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
          grid-template-columns: repeat(auto-fit, minmax(min(100%, 280px), 1fr));
          gap: 24px;
        }
        .cap-card {
          border-radius: var(--radius-md);
          border: 1px solid var(--border-subtle);
          box-shadow: 0 2px 10px rgba(15, 23, 42, 0.04);
          transition: transform var(--transition-fast), box-shadow var(--transition-fast), border-color var(--transition-fast);
          padding: 24px;
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
          margin-bottom: 16px;
        }
        .cap-icon {
          font-size: 26px;
        }
        .cap-tag {
          font-size: 11px;
        }
        .cap-title {
          font-size: 17px;
          font-weight: 750;
          color: var(--text-main);
          margin-bottom: 8px;
        }
        .cap-desc {
          font-size: 13.5px;
          color: var(--text-muted);
          line-height: 1.6;
        }
        .architecture-block {
          margin-top: 48px;
        }
        .arch-card {
          background: #f8fafc;
          border: 1.5px solid var(--border-subtle);
          border-radius: var(--radius-lg);
          padding: 32px;
        }
        .arch-title {
          font-size: 22px;
          font-weight: 800;
          color: var(--text-main);
          margin: 12px 0 8px;
        }
        .arch-desc {
          font-size: 14.5px;
          color: var(--text-muted);
          max-width: 800px;
          line-height: 1.6;
          margin-bottom: 24px;
        }
        .arch-specs-grid {
          display: grid;
          grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
          gap: 16px;
          padding-top: 20px;
          border-top: 1px solid var(--border-subtle);
        }
        .spec-item {
          display: flex;
          flex-direction: column;
          gap: 4px;
        }
        .spec-label {
          font-size: 11.5px;
          text-transform: uppercase;
          letter-spacing: 0.5px;
          color: var(--text-muted);
          font-weight: 650;
        }
        .spec-item strong {
          color: var(--text-main);
          font-size: 13.5px;
        }
        @media (max-width: 640px) {
          .capabilities-grid {
            grid-template-columns: 1fr;
            gap: 16px;
          }
          .cap-card {
            padding: 18px;
          }
          .arch-card {
            padding: 20px;
          }
        }
      `}</style>
    </section>
  );
};
