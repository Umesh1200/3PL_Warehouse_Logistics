import React from 'react';

interface HeroSectionProps {
  onExploreClick?: () => void;
  onTrackClick?: () => void;
}

export const HeroSection: React.FC<HeroSectionProps> = ({ onExploreClick, onTrackClick }) => {
  return (
    <section className="hero-section" aria-label="Hero Introduction">
      <div className="container hero-container">
        <div className="hero-badge-wrap">
          <span className="badge badge-primary-subtle">
            <span className="dot"></span> .NET 10 &bull; Event-Sourced &bull; Multi-Tenant 3PL
          </span>
        </div>

        <h1 className="hero-headline">
          Intelligent Warehouse Operations &amp; <span className="gradient-text">Precision Courier Logistics</span>
        </h1>

        <p className="hero-subheading">
          A unified enterprise operating system for modern third-party logistics. Featuring 3D spatial warehouse heatmaps, 
          automated slotting optimization, multi-carrier rate card auditing, and immutable financial reconciliation.
        </p>

        <div className="hero-cta-group">
          <button type="button" className="btn btn-primary hero-btn" onClick={onExploreClick}>
            Explore Capabilities &rarr;
          </button>
          <button type="button" className="btn btn-secondary hero-btn" onClick={onTrackClick}>
            Track AWB Shipment
          </button>
        </div>

        {/* Live Metrics Ticker Bar (Explicitly Labeled Demo Data) */}
        <div className="metrics-wrapper" id="metrics">
          <div className="demo-notice-bar">
            <span className="badge badge-warning demo-pill">ILLUSTRATIVE DEMO METRICS &bull; UNCONNECTED TO PRODUCTION DATA</span>
          </div>
          <div className="hero-metrics-grid">
            <div className="metric-card">
              <span className="metric-val">99.85%</span>
              <span className="metric-label">On-Time SLA Adherence</span>
              <span className="metric-sub">Multi-Carrier Projection</span>
            </div>
            <div className="metric-card">
              <span className="metric-val">1.4M+</span>
              <span className="metric-label">Parcels Dispatched</span>
              <span className="metric-sub">Verified AWB Lifecycles</span>
            </div>
            <div className="metric-card">
              <span className="metric-val">5</span>
              <span className="metric-label">Enterprise Facilities</span>
              <span className="metric-sub">Full Zone Topology</span>
            </div>
            <div className="metric-card">
              <span className="metric-val">&lt; 12ms</span>
              <span className="metric-label">Scoped Query Latency</span>
              <span className="metric-sub">Indexed EF Projections</span>
            </div>
          </div>
        </div>
      </div>

      <style>{`
        .hero-section {
          padding: 80px 0 60px;
          background: radial-gradient(circle at top right, rgba(82, 84, 230, 0.08) 0%, transparent 60%),
                      radial-gradient(circle at bottom left, rgba(2, 132, 199, 0.06) 0%, transparent 50%);
        }
        .hero-container {
          text-align: center;
          max-width: 900px;
        }
        .hero-badge-wrap {
          margin-bottom: 20px;
        }
        .badge-primary-subtle {
          display: inline-flex;
          align-items: center;
          gap: 6px;
          padding: 5px 14px;
          border-radius: var(--radius-full);
          background: #eeefff;
          color: #4345d0;
          font-weight: 650;
          font-size: 12px;
          border: 1px solid rgba(82, 84, 230, 0.2);
        }
        .dot {
          width: 7px;
          height: 7px;
          border-radius: 50%;
          background: var(--brand-primary);
        }
        .hero-headline {
          font-size: 46px;
          font-weight: 800;
          line-height: 1.18;
          color: var(--text-main);
          letter-spacing: -1.2px;
          margin-bottom: 20px;
        }
        .gradient-text {
          background: linear-gradient(135deg, #5254e6 20%, #0284c7 90%);
          -webkit-background-clip: text;
          -webkit-text-fill-color: transparent;
        }
        .hero-subheading {
          font-size: 17px;
          color: var(--text-muted);
          line-height: 1.6;
          max-width: 760px;
          margin: 0 auto 36px;
        }
        .hero-cta-group {
          display: flex;
          justify-content: center;
          gap: 16px;
          margin-bottom: 56px;
        }
        .hero-btn {
          padding: 13px 26px;
          font-size: 15px;
        }
        .metrics-wrapper {
          background: #ffffff;
          border-radius: var(--radius-lg);
          border: 1px solid var(--border-subtle);
          box-shadow: 0 4px 18px rgba(15, 23, 42, 0.05);
          overflow: hidden;
        }
        .demo-notice-bar {
          background: #fffbeb;
          border-bottom: 1px solid #fef3c7;
          padding: 6px 12px;
          text-align: center;
        }
        .demo-pill {
          font-size: 10.5px;
          letter-spacing: 0.6px;
        }
        .hero-metrics-grid {
          display: grid;
          grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
          gap: 16px;
          padding: 24px;
        }
        .metric-card {
          display: flex;
          flex-direction: column;
          gap: 4px;
          text-align: center;
          padding: 8px;
        }
        .metric-val {
          font-size: 32px;
          font-weight: 800;
          color: var(--brand-primary);
          letter-spacing: -0.5px;
        }
        .metric-label {
          font-size: 13.5px;
          font-weight: 650;
          color: var(--text-main);
        }
        .metric-sub {
          font-size: 11.5px;
          color: var(--text-muted);
        }
        @media (max-width: 768px) {
          .hero-headline { font-size: 34px; }
          .hero-subheading { font-size: 15px; }
          .hero-cta-group { flex-direction: column; align-items: stretch; }
        }
      `}</style>
    </section>
  );
};
