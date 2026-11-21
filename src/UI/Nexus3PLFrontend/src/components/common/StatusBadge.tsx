import React from 'react';
import { AppIcon, AppIconName } from './AppIcon';

export interface StatusBadgeProps {
  label: string;
  variant?: 'success' | 'warning' | 'danger' | 'info' | 'primary' | 'secondary' | string;
  icon?: AppIconName | string;
  className?: string;
  style?: React.CSSProperties;
}

export const StatusBadge: React.FC<StatusBadgeProps> = ({
  label,
  variant = 'secondary',
  icon,
  className = '',
  style,
}) => {
  const badgeClass = `status-pill status-pill-${variant} ${className}`.trim();

  return (
    <span className={badgeClass} style={style}>
      {icon && <AppIcon name={icon} size="xs" className="badge-icon" />}
      <span className="badge-label">{label}</span>

      <style>{`
        .status-pill {
          display: inline-flex;
          align-items: center;
          gap: 6px;
          padding: 3px 10px;
          border-radius: var(--radius-full);
          font-size: 11.5px;
          font-weight: 650;
          letter-spacing: 0.04em;
          text-transform: uppercase;
          line-height: 1.2;
          white-space: nowrap;
          border: 1px solid transparent;
        }
        .status-pill-success {
          background-color: var(--status-success-bg);
          color: var(--status-success-text);
          border-color: var(--status-success-border);
        }
        .status-pill-warning {
          background-color: var(--status-warning-bg);
          color: var(--status-warning-text);
          border-color: var(--status-warning-border);
        }
        .status-pill-danger {
          background-color: var(--status-danger-bg);
          color: var(--status-danger-text);
          border-color: var(--status-danger-border);
        }
        .status-pill-info {
          background-color: var(--status-info-bg);
          color: var(--status-info-text);
          border-color: var(--status-info-border);
        }
        .status-pill-primary {
          background-color: var(--brand-primary-light);
          color: var(--brand-primary);
          border-color: #c7c9f8;
        }
        .status-pill-secondary {
          background-color: #f1f5f9;
          color: var(--text-muted);
          border-color: #cbd5e1;
        }
        .badge-icon {
          flex-shrink: 0;
        }
      `}</style>
    </span>
  );
};
