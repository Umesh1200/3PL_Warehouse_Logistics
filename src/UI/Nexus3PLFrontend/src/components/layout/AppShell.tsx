import React from 'react';
import { Sidebar } from './Sidebar';
import { Header } from './Header';

interface AppShellProps {
  children: React.ReactNode;
  pageTitle?: string;
  breadcrumb?: string;
}

export const AppShell: React.FC<AppShellProps> = ({ children, pageTitle, breadcrumb }) => {
  return (
    <div className="app-shell-container">
      <Sidebar />
      <div className="app-shell-main">
        <Header />
        <main className="app-content">
          {(pageTitle || breadcrumb) && (
            <div className="content-page-header">
              {breadcrumb && <span className="breadcrumb-text">{breadcrumb}</span>}
              {pageTitle && <h1 className="content-title">{pageTitle}</h1>}
            </div>
          )}
          <div className="content-body">{children}</div>
        </main>
      </div>

      <style>{`
        .app-shell-container {
          display: flex;
          min-height: 100vh;
          background: #f5f5f9;
        }
        .app-shell-main {
          flex: 1;
          display: flex;
          flex-direction: column;
          min-width: 0;
        }
        .app-content {
          flex: 1;
          padding: 24px;
          overflow-y: auto;
        }
        .content-page-header {
          margin-bottom: 20px;
        }
        .breadcrumb-text {
          font-size: 12px;
          font-weight: 650;
          color: var(--text-muted);
          text-transform: uppercase;
          letter-spacing: 0.05em;
        }
        .content-title {
          font-size: 22px;
          font-weight: 750;
          color: var(--text-main);
          margin-top: 4px;
        }
        .content-body {
          min-width: 0;
        }
        @media (max-width: 768px) {
          .app-shell-container {
            flex-direction: column;
          }
          .app-content {
            padding: 16px;
          }
        }
      `}</style>
    </div>
  );
};
