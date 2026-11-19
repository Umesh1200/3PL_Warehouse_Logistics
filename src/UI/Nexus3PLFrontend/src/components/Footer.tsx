import React from 'react';
import logoFull from '../assets/logo-full.svg';

export const Footer: React.FC = () => {
  return (
    <footer className="footer-wrap">
      <div className="container footer-inner">
        <div className="footer-brand">
          <img src={logoFull} alt="Nexus3PL Logo" className="footer-logo" />
          <p className="footer-tagline">
            Next-Generation 3PL Warehouse Logistics &amp; Courier Transportation Operating System.
          </p>
          <span className="badge badge-success">
            Baseline Verified: 1,256 .NET 10 Tests &bull; 24 UI Tests
          </span>
        </div>

        <div className="footer-links-col">
          <h4 className="footer-heading">Platform Modules</h4>
          <ul className="footer-list">
            <li><a href="#topology">Warehouse Spatial WMS</a></li>
            <li><a href="#inventory">Inventory &amp; Cycle Counting</a></li>
            <li><a href="#fulfillment">Outbound Waves &amp; Packing</a></li>
            <li><a href="#tracking">Courier Routing &amp; AWB Tracking</a></li>
          </ul>
        </div>

        <div className="footer-links-col">
          <h4 className="footer-heading">Financial &amp; Analytics</h4>
          <ul className="footer-list">
            <li><a href="#billing">Tariffs &amp; Buy/Sell Freight</a></li>
            <li><a href="#analytics">Control Tower KPIs</a></li>
            <li><a href="#discrepancies">Accessorial Leakage Quarantine</a></li>
            <li><a href="#governance">Event Ingestion Journal</a></li>
          </ul>
        </div>

        <div className="footer-links-col">
          <h4 className="footer-heading">Architecture &amp; Standards</h4>
          <ul className="footer-list">
            <li><a href="#architecture" className="tech-link"><span className="tech-badge">.NET 10 WebApi</span></a></li>
            <li><a href="#architecture" className="tech-link"><span className="tech-badge">Vite 7 + React 19 SPA</span></a></li>
            <li><a href="#architecture" className="tech-link"><span className="tech-badge">WCAG 2.2 Level AA</span></a></li>
            <li><a href="#architecture" className="tech-link"><span className="tech-badge">EF Core Multi-Tenant</span></a></li>
          </ul>
        </div>
      </div>

      <div className="footer-bottom">
        <div className="container bottom-inner">
          <p>&copy; {new Date().getFullYear()} Nexus3PL Logistics Platform. All rights reserved.</p>
          <p className="subtext">Phase 7 Enterprise Frontend Integration &bull; feature/phase-7-enterprise-frontend</p>
        </div>
      </div>

      <style>{`
        .footer-wrap {
          background: #0f172a;
          color: #cbd5e1;
          padding: 60px 0 0;
          border-top: 1px solid var(--brand-navy-700);
        }
        .footer-inner {
          display: grid;
          grid-template-columns: 2fr 1fr 1fr 1.2fr;
          gap: 40px;
          padding-bottom: 50px;
        }
        .footer-logo {
          height: 36px;
          margin-bottom: 14px;
          filter: brightness(0) invert(1);
        }
        .footer-tagline {
          font-size: 13.5px;
          line-height: 1.6;
          margin-bottom: 16px;
          max-width: 320px;
          color: #cbd5e1;
        }
        .footer-heading {
          font-size: 13px;
          font-weight: 700;
          text-transform: uppercase;
          color: #ffffff;
          letter-spacing: 0.8px;
          margin-bottom: 16px;
        }
        .footer-list {
          list-style: none;
          display: flex;
          flex-direction: column;
          gap: 10px;
        }
        .footer-list a {
          color: #e2e8f0;
          font-size: 13.5px;
          text-decoration: none;
          transition: color var(--transition-fast);
        }
        .footer-list a:hover {
          color: #38bdf8;
          text-decoration: underline;
        }
        .tech-link {
          display: inline-block;
          text-decoration: none !important;
        }
        .tech-badge {
          font-size: 12px;
          color: #f1f5f9;
          background: #1e293b;
          padding: 3px 8px;
          border-radius: var(--radius-xs);
          border: 1px solid var(--brand-navy-700);
          display: inline-block;
          transition: border-color var(--transition-fast);
        }
        .tech-badge:hover {
          border-color: #38bdf8;
        }
        .footer-bottom {
          border-top: 1px solid #1e293b;
          padding: 20px 0;
          font-size: 12.5px;
          color: #cbd5e1;
        }
        .bottom-inner {
          display: flex;
          justify-content: space-between;
          align-items: center;
          flex-wrap: wrap;
          gap: 12px;
        }
        .subtext {
          color: #94a3b8;
        }
        @media (max-width: 850px) {
          .footer-inner { grid-template-columns: 1fr 1fr; }
        }
        @media (max-width: 500px) {
          .footer-inner { grid-template-columns: 1fr; }
        }
      `}</style>
    </footer>
  );
};
