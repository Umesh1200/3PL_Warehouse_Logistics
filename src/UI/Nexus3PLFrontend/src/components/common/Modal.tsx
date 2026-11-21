import React, { useEffect, useRef } from 'react';
import { AppIcon, AppIconName } from './AppIcon';

export interface ModalProps {
  isOpen: boolean;
  onClose: () => void;
  title: string;
  subtitle?: string;
  icon?: AppIconName | string;
  children: React.ReactNode;
  footer?: React.ReactNode;
  maxWidth?: string;
}

export const Modal: React.FC<ModalProps> = ({
  isOpen,
  onClose,
  title,
  subtitle,
  icon,
  children,
  footer,
  maxWidth = '640px',
}) => {
  const modalRef = useRef<HTMLDivElement>(null);
  const previouslyFocusedElement = useRef<HTMLElement | null>(null);

  useEffect(() => {
    if (!isOpen) return;

    previouslyFocusedElement.current = document.activeElement as HTMLElement;

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onClose();
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

    // Focus the first input or modal container
    setTimeout(() => {
      if (modalRef.current) {
        const firstInput = modalRef.current.querySelector<HTMLElement>('input, select, textarea, button');
        firstInput?.focus();
      }
    }, 50);

    return () => {
      window.removeEventListener('keydown', handleKeyDown);
      if (previouslyFocusedElement.current) {
        previouslyFocusedElement.current.focus();
      }
    };
  }, [isOpen, onClose]);

  if (!isOpen) return null;

  return (
    <div className="modal-overlay" onClick={onClose} role="presentation">
      <div
        className="modal-container"
        style={{ maxWidth }}
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-modal="true"
        aria-labelledby="modal-title"
        ref={modalRef}
      >
        <div className="modal-header">
          <div className="modal-title-group">
            {icon && (
              <div className="modal-icon-badge">
                <AppIcon name={icon} size="md" color="primary" />
              </div>
            )}
            <div>
              <h2 id="modal-title" className="modal-title" data-testid="modal-title">
                {title}
              </h2>
              {subtitle && <p className="modal-subtitle">{subtitle}</p>}
            </div>
          </div>
          <button
            type="button"
            className="modal-close-btn"
            onClick={onClose}
            aria-label="Close modal dialog"
          >
            <AppIcon name="X" size="sm" />
          </button>
        </div>

        <div className="modal-body">{children}</div>

        {footer && <div className="modal-footer">{footer}</div>}
      </div>

      <style>{`
        .modal-overlay {
          position: fixed;
          top: 0;
          left: 0;
          right: 0;
          bottom: 0;
          background: rgba(15, 23, 42, 0.6);
          backdrop-filter: blur(2px);
          display: flex;
          align-items: center;
          justify-content: center;
          z-index: 1000;
          padding: 16px;
          animation: fadeIn 0.15s ease-out;
        }
        .modal-container {
          background: #ffffff;
          border-radius: var(--radius-md);
          width: 100%;
          max-height: calc(100vh - 40px);
          display: flex;
          flex-direction: column;
          box-shadow: 0 20px 40px rgba(15, 23, 42, 0.2);
          border: 1px solid var(--border-subtle);
          overflow: hidden;
          animation: slideUp 0.2s cubic-bezier(0.16, 1, 0.3, 1);
        }
        .modal-header {
          display: flex;
          align-items: flex-start;
          justify-content: space-between;
          padding: 20px 24px;
          border-bottom: 1px solid var(--border-subtle);
          background: #fafbfc;
        }
        .modal-title-group {
          display: flex;
          align-items: center;
          gap: 12px;
        }
        .modal-icon-badge {
          width: 38px;
          height: 38px;
          border-radius: var(--radius-sm);
          background: var(--brand-primary-light);
          display: flex;
          align-items: center;
          justify-content: center;
          flex-shrink: 0;
        }
        .modal-title {
          font-size: 18px;
          font-weight: 700;
          color: var(--text-main);
          margin: 0;
          line-height: 1.3;
        }
        .modal-subtitle {
          font-size: 13px;
          color: var(--text-muted);
          margin: 4px 0 0;
        }
        .modal-close-btn {
          width: 32px;
          height: 32px;
          border-radius: var(--radius-xs);
          display: flex;
          align-items: center;
          justify-content: center;
          color: var(--text-muted);
          transition: background var(--transition-fast), color var(--transition-fast);
        }
        .modal-close-btn:hover {
          background: #e2e8f0;
          color: var(--text-main);
        }
        .modal-body {
          padding: 24px;
          overflow-y: auto;
          flex: 1;
        }
        .modal-footer {
          display: flex;
          align-items: center;
          justify-content: flex-end;
          gap: 12px;
          padding: 16px 24px;
          border-top: 1px solid var(--border-subtle);
          background: #fafbfc;
        }
        @keyframes fadeIn {
          from { opacity: 0; }
          to { opacity: 1; }
        }
        @keyframes slideUp {
          from { transform: translateY(12px) scale(0.98); opacity: 0; }
          to { transform: translateY(0) scale(1); opacity: 1; }
        }
      `}</style>
    </div>
  );
};
