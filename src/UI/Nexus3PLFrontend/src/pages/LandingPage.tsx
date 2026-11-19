import React, { useState, useEffect, useRef } from 'react';
import { Navbar } from '../components/Navbar';
import { HeroSection } from '../components/HeroSection';
import { TrackingSimulator } from '../components/TrackingSimulator';
import { CapabilitiesGrid } from '../components/CapabilitiesGrid';
import { Footer } from '../components/Footer';

export const LandingPage: React.FC = () => {
  const [isPortalModalOpen, setIsPortalModalOpen] = useState(false);
  const triggerRef = useRef<HTMLElement | null>(null);
  const modalRef = useRef<HTMLDivElement | null>(null);
  const closeBtnRef = useRef<HTMLButtonElement | null>(null);

  const handleOpenPortalNotice = (e?: React.MouseEvent) => {
    triggerRef.current = (e?.currentTarget as HTMLElement) || (document.activeElement as HTMLElement);
    setIsPortalModalOpen(true);
  };

  const handleCloseModal = () => {
    setIsPortalModalOpen(false);
  };

  // Keyboard accessibility: Escape to close, focus trapping, and focus restoration
  useEffect(() => {
    if (!isPortalModalOpen) {
      // Focus restoration when modal closes
      if (triggerRef.current && typeof triggerRef.current.focus === 'function') {
        triggerRef.current.focus();
      }
      return;
    }

    // Set initial focus to the close button inside modal
    const focusTimer = setTimeout(() => {
      closeBtnRef.current?.focus();
    }, 50);

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.preventDefault();
        handleCloseModal();
        return;
      }

      if (e.key === 'Tab' && modalRef.current) {
        const focusableElements = modalRef.current.querySelectorAll<HTMLElement>(
          'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
        );
        if (focusableElements.length === 0) return;

        const firstElement = focusableElements[0];
        const lastElement = focusableElements[focusableElements.length - 1];

        if (e.shiftKey) {
          if (document.activeElement === firstElement) {
            e.preventDefault();
            lastElement.focus();
          }
        } else {
          if (document.activeElement === lastElement) {
            e.preventDefault();
            firstElement.focus();
          }
        }
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => {
      clearTimeout(focusTimer);
      window.removeEventListener('keydown', handleKeyDown);
    };
  }, [isPortalModalOpen]);

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

      {/* Accessible Portal Launch Notice Modal */}
      {isPortalModalOpen && (
        <div 
          className="modal-backdrop" 
          onClick={handleCloseModal} 
          role="dialog" 
          aria-modal="true" 
          aria-labelledby="modalTitle"
          aria-describedby="modalDesc"
        >
          <div 
            ref={modalRef}
            className="modal-card" 
            onClick={(e) => e.stopPropagation()}
            tabIndex={-1}
          >
            <div className="modal-header">
              <span className="badge badge-warning">Portal Notice</span>
              <h3 id="modalTitle" className="modal-title">Enterprise Operations Portal Unavailable</h3>
            </div>
            
            <div id="modalDesc" className="modal-body">
              <p className="notice-main-text">
                The Enterprise Operations Portal is currently unavailable for direct user sign-in 
                while multi-tenant security verification and system integration audits are in progress.
              </p>
              <div className="notice-info-box">
                <p>
                  Operational warehouse workflows, facility configurations, and financial management panels 
                  will become accessible upon formal technical gate authorization.
                </p>
              </div>
              <p className="modal-sub">
                In the interim, you can freely test the public AWB tracking simulator and review platform capabilities on this page.
              </p>
            </div>

            <div className="modal-footer">
              <button 
                ref={closeBtnRef}
                type="button" 
                className="btn btn-primary" 
                onClick={handleCloseModal}
                aria-label="Understood, close dialog"
              >
                Understood &bull; Return to Platform
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
          max-width: 520px;
          width: 100%;
          box-shadow: 0 20px 40px rgba(15, 23, 42, 0.25);
          padding: 30px;
          border: 1px solid var(--border-subtle);
          outline: none;
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
          font-size: 14.5px;
          color: var(--text-main);
          line-height: 1.6;
        }
        .notice-main-text {
          margin-bottom: 14px;
        }
        .notice-info-box {
          background: #f8fafc;
          border-left: 3px solid var(--brand-primary);
          padding: 12px 16px;
          border-radius: var(--radius-xs);
          font-size: 13.5px;
          color: var(--text-muted);
          margin-bottom: 14px;
        }
        .modal-sub {
          font-size: 13px;
          color: var(--text-muted);
          border-top: 1px solid var(--border-subtle);
          padding-top: 12px;
        }
        .modal-footer {
          margin-top: 24px;
          display: flex;
          justify-content: flex-end;
        }
        @media (max-width: 640px) {
          .modal-card {
            padding: 22px;
          }
        }
      `}</style>
    </div>
  );
};
