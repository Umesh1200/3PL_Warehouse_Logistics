import React, { useState } from 'react';
import { Navbar } from '../components/Navbar';
import { HeroSection } from '../components/HeroSection';
import { TrackingSimulator } from '../components/TrackingSimulator';
import { CapabilitiesGrid } from '../components/CapabilitiesGrid';
import { Footer } from '../components/Footer';

export const LandingPage: React.FC = () => {
  const [isPortalModalOpen, setIsPortalModalOpen] = useState(false);

  const handleOpenPortalNotice = () => {
    setIsPortalModalOpen(true);
  };

  const handleCloseModal = () => {
    setIsPortalModalOpen(false);
  };

  const scrollToTracking = () => {
    const el = document.getElementById('tracking');
    if (el) el.scrollIntoView({ behavior: 'smooth' });
  };

  const scrollToCapabilities = () => {
    const el = document.getElementById('capabilities');
    if (el) el.scrollIntoView({ behavior: 'smooth' });
  };

  return (
    <div className="landing-wrapper">
      <Navbar onSignInClick={handleOpenPortalNotice} />
      
      <main>
        <HeroSection 
          onExploreClick={scrollToCapabilities}
          onTrackClick={scrollToTracking}
        />
        <TrackingSimulator />
        <CapabilitiesGrid />
      </main>

      <Footer />

      {/* Portal Launch / Auth Contract Holding Modal */}
      {isPortalModalOpen && (
        <div className="modal-backdrop" onClick={handleCloseModal} role="dialog" aria-modal="true" aria-labelledby="modalTitle">
          <div className="modal-card" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <span className="badge badge-warning">Phase 7.1 Architecture Gate</span>
              <h3 id="modalTitle" className="modal-title">Enterprise Portal Authentication Notice</h3>
            </div>
            <div className="modal-body">
              <p>
                In accordance with the <strong>Phase 7 Engineering Review</strong>, operational authentication integration 
                is intentionally held behind the verified security contract:
              </p>
              <ul className="modal-rules-list">
                <li>
                  <strong>Direct Body Contract:</strong> <code>POST /api/v1/auth/refresh-token</code> takes 
                  <code>{`{ token, refreshToken }`}</code> directly in request payload.
                </li>
                <li>
                  <strong>Scope Independence:</strong> <code>/api/v1/auth/me</code> supplies authenticated user roles &amp; permissions; 
                  authorized facilities are retrieved independently via <code>/api/v1/locations/facilities</code>.
                </li>
                <li>
                  <strong>Server-Side OWASP Enforcement:</strong> UI element hiding serves as affordance only; 
                  all mutations require server policy checks and tenant boundary validation.
                </li>
              </ul>
              <p className="modal-sub">
                The public branding, design system tokens, and interactive courier tracking simulator are fully operational.
              </p>
            </div>
            <div className="modal-footer">
              <button type="button" className="btn btn-primary" onClick={handleCloseModal}>
                Understood &bull; Close
              </button>
            </div>
          </div>
        </div>
      )}

      <style>{`
        .landing-wrapper {
          min-height: 100vh;
          display: flex;
          flex-direction: column;
        }
        main {
          flex: 1;
        }
        .modal-backdrop {
          position: fixed;
          inset: 0;
          background: rgba(15, 23, 42, 0.6);
          backdrop-filter: blur(4px);
          display: flex;
          align-items: center;
          justify-content: center;
          z-index: 1000;
          padding: 20px;
        }
        .modal-card {
          background: #ffffff;
          border-radius: var(--radius-lg);
          max-width: 540px;
          width: 100%;
          box-shadow: 0 20px 40px rgba(15, 23, 42, 0.25);
          padding: 28px;
          border: 1px solid var(--border-subtle);
        }
        .modal-header {
          margin-bottom: 16px;
        }
        .modal-title {
          font-size: 20px;
          font-weight: 750;
          color: var(--text-main);
          margin-top: 8px;
        }
        .modal-body {
          font-size: 14px;
          color: var(--text-muted);
          line-height: 1.6;
        }
        .modal-rules-list {
          margin: 14px 0;
          padding-left: 20px;
          display: flex;
          flex-direction: column;
          gap: 8px;
          color: var(--text-main);
        }
        .modal-rules-list code {
          background: #f1f5f9;
          padding: 2px 5px;
          border-radius: 4px;
          font-size: 12.5px;
        }
        .modal-sub {
          font-size: 12.5px;
          color: var(--text-muted);
          margin-top: 12px;
          border-top: 1px solid var(--border-subtle);
          padding-top: 12px;
        }
        .modal-footer {
          margin-top: 24px;
          display: flex;
          justify-content: flex-end;
        }
      `}</style>
    </div>
  );
};
