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
  const headerDismissRef = useRef<HTMLButtonElement | null>(null);

  const handleOpenPortalNotice = (e?: React.MouseEvent) => {
    triggerRef.current = (e?.currentTarget as HTMLElement) || (document.activeElement as HTMLElement);
    setIsPortalModalOpen(true);
  };

  const handleCloseModal = () => {
    setIsPortalModalOpen(false);
  };

  // Keyboard accessibility: Escape dismissal, focus trapping, and focus restoration
  useEffect(() => {
    if (!isPortalModalOpen) {
      if (triggerRef.current && typeof triggerRef.current.focus === 'function') {
        triggerRef.current.focus();
      }
      return;
    }

    // Set initial focus to the primary close button
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
        const focusableElements = Array.from(
          modalRef.current.querySelectorAll<HTMLElement>(
            'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
          )
        );
        if (focusableElements.length === 0) {
          e.preventDefault();
          return;
        }

        const firstElement = focusableElements[0];
        const lastElement = focusableElements[focusableElements.length - 1];
        const currentIndex = focusableElements.indexOf(document.activeElement as HTMLElement);

        if (e.shiftKey) {
          if (document.activeElement === firstElement || currentIndex <= 0 || document.activeElement === modalRef.current) {
            e.preventDefault();
            lastElement.focus();
          } else {
            e.preventDefault();
            focusableElements[currentIndex - 1].focus();
          }
        } else {
          if (document.activeElement === lastElement || currentIndex >= focusableElements.length - 1 || currentIndex === -1) {
            e.preventDefault();
            firstElement.focus();
          } else {
            e.preventDefault();
            focusableElements[currentIndex + 1].focus();
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
              <div className="modal-title-wrap">
                <span className="badge badge-warning">Portal Notice</span>
                <h3 id="modalTitle" className="modal-title">Enterprise Operations Portal Unavailable</h3>
              </div>
              <button 
                ref={headerDismissRef}
                type="button" 
                className="modal-close-icon" 
                onClick={handleCloseModal}
                aria-label="Dismiss notice"
              >
                &times;
              </button>
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
          padding: 16px;
          box-sizing: border-box;
          overflow-y: auto;
        }
        .modal-card {
          background: #ffffff;
          border-radius: var(--radius-lg);
          max-width: 520px;
          width: 100%;
          max-height: calc(100vh - 40px);
          max-height: calc(100dvh - 40px);
          overflow-y: auto;
          display: flex;
          flex-direction: column;
          box-shadow: 0 20px 40px rgba(15, 23, 42, 0.25);
          padding: 24px;
          border: 1px solid var(--border-subtle);
          outline: none;
          box-sizing: border-box;
        }
        .modal-header {
          display: flex;
          align-items: flex-start;
          justify-content: space-between;
          gap: 12px;
          margin-bottom: 14px;
          flex-shrink: 0;
        }
        .modal-title-wrap {
          flex: 1;
        }
        .modal-title {
          font-size: 19px;
          font-weight: 750;
          color: var(--text-main);
          margin-top: 6px;
        }
        .modal-close-icon {
          font-size: 24px;
          line-height: 1;
          color: var(--text-muted);
          background: none;
          border: none;
          cursor: pointer;
          padding: 4px 8px;
          border-radius: var(--radius-xs);
          transition: color var(--transition-fast);
        }
        .modal-close-icon:hover {
          color: var(--text-main);
        }
        .modal-body {
          font-size: 14px;
          color: var(--text-main);
          line-height: 1.6;
          flex: 1;
          overflow-y: auto;
        }
        .notice-main-text {
          margin-bottom: 12px;
        }
        .notice-info-box {
          background: #f8fafc;
          border-left: 3px solid var(--brand-primary);
          padding: 12px 14px;
          border-radius: var(--radius-xs);
          font-size: 13.5px;
          color: var(--text-muted);
          margin-bottom: 12px;
        }
        .modal-sub {
          font-size: 13px;
          color: var(--text-muted);
          border-top: 1px solid var(--border-subtle);
          padding-top: 10px;
        }
        .modal-footer {
          margin-top: 16px;
          padding-top: 12px;
          border-top: 1px solid var(--border-subtle);
          display: flex;
          justify-content: flex-end;
          flex-shrink: 0;
        }
        @media (max-width: 640px) {
          .modal-card {
            padding: 18px;
          }
          .modal-title {
            font-size: 17px;
          }
        }
      `}</style>
    </div>
  );
};
