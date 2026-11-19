import React from 'react';
import logoFull from '../assets/logo-full.svg';

interface NavbarProps {
  onSignInClick?: () => void;
}

export const Navbar: React.FC<NavbarProps> = ({ onSignInClick }) => {
  return (
    <header className="navbar-wrapper">
      <div className="container navbar-inner">
        <div className="brand-lockup">
          <img src={logoFull} alt="Nexus3PL Logo" className="brand-logo" />
        </div>

        <nav className="nav-links" aria-label="Main Navigation">
          <a href="#tracking" className="nav-link">Live Tracking</a>
          <a href="#capabilities" className="nav-link">Capabilities</a>
          <a href="#architecture" className="nav-link">Architecture</a>
          <a href="#metrics" className="nav-link">Performance</a>
        </nav>

        <div className="nav-actions">
          <span className="badge badge-info status-pill">
            <span className="pulse-dot"></span> .NET 10 API
          </span>
          <button 
            type="button" 
            className="btn btn-primary"
            onClick={onSignInClick}
            aria-label="Open Enterprise Portal"
          >
            Launch Portal &rarr;
          </button>
        </div>
      </div>

      <style>{`
        .navbar-wrapper {
          background: rgba(255, 255, 255, 0.95);
          backdrop-filter: blur(8px);
          border-bottom: 1px solid var(--border-subtle);
          position: sticky;
          top: 0;
          z-index: 100;
        }
        .navbar-inner {
          display: flex;
          align-items: center;
          justify-content: space-between;
          height: 72px;
        }
        .brand-logo {
          height: 38px;
          display: block;
        }
        .nav-links {
          display: flex;
          gap: 28px;
        }
        .nav-link {
          color: var(--text-muted);
          font-weight: 550;
          font-size: 13.5px;
          text-decoration: none;
          transition: color var(--transition-fast);
        }
        .nav-link:hover {
          color: var(--brand-primary);
        }
        .nav-actions {
          display: flex;
          align-items: center;
          gap: 16px;
        }
        .status-pill {
          font-size: 11px;
          padding: 4px 10px;
        }
        .pulse-dot {
          display: inline-block;
          width: 6px;
          height: 6px;
          border-radius: 50%;
          background: #0284c7;
          box-shadow: 0 0 0 2px rgba(2, 132, 199, 0.2);
        }
        @media (max-width: 768px) {
          .nav-links { display: none; }
        }
        @media (max-width: 480px) {
          .status-pill { display: none; }
          .brand-logo { height: 28px; }
          .btn-primary { padding: 7px 12px; font-size: 12px; }
          .navbar-inner { height: 60px; }
        }
      `}</style>
    </header>
  );
};
