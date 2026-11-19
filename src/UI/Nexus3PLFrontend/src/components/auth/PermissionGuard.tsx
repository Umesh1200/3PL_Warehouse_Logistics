import React from 'react';
import { useAuth } from '../../context/AuthContext';
import { useRouter } from '../../context/RouterContext';

interface PermissionGuardProps {
  children: React.ReactNode;
  requiredPermission?: string;
  requiredPermissions?: string[];
  fallback?: React.ReactNode;
}

export const PermissionGuard: React.FC<PermissionGuardProps> = ({
  children,
  requiredPermission,
  requiredPermissions,
  fallback,
}) => {
  const { hasPermission, hasAnyPermission, user } = useAuth();
  const { navigate } = useRouter();

  const isAuthorized = (() => {
    if (!user) return false;
    if (requiredPermission && !hasPermission(requiredPermission)) {
      return false;
    }
    if (requiredPermissions && requiredPermissions.length > 0 && !hasAnyPermission(requiredPermissions)) {
      return false;
    }
    return true;
  })();

  if (!isAuthorized) {
    if (fallback) return <>{fallback}</>;

    return (
      <div className="forbidden-card" role="alert">
        <div className="forbidden-badge">403 FORBIDDEN</div>
        <h2>Access Denied</h2>
        <p className="forbidden-desc">
          Your account does not possess the required security permission grant (
          <code>{requiredPermission || requiredPermissions?.join(' or ')}</code>) to access this enterprise module.
        </p>
        <p className="forbidden-sub">
          Please contact your 3PL platform administrator to request role reassignment or elevated authorization.
        </p>
        <div className="forbidden-actions">
          <button
            type="button"
            className="btn btn-primary"
            onClick={() => navigate('/app/dashboard')}
          >
            Return to Dashboard
          </button>
        </div>
        <style>{`
          .forbidden-card {
            background: #ffffff;
            border-radius: var(--radius-md);
            padding: 40px 32px;
            max-width: 560px;
            margin: 40px auto;
            text-align: center;
            border: 1px solid var(--border-subtle);
            box-shadow: 0 4px 16px rgba(15, 23, 42, 0.05);
          }
          .forbidden-badge {
            display: inline-block;
            background: var(--status-danger-bg);
            color: var(--status-danger-text);
            padding: 4px 12px;
            border-radius: var(--radius-xs);
            font-size: 12px;
            font-weight: 700;
            letter-spacing: 0.05em;
            margin-bottom: 16px;
          }
          .forbidden-card h2 {
            font-size: 24px;
            font-weight: 750;
            color: var(--text-main);
            margin-bottom: 12px;
          }
          .forbidden-desc {
            font-size: 15px;
            color: var(--text-main);
            line-height: 1.5;
            margin-bottom: 10px;
          }
          .forbidden-desc code {
            background: #f1f5f9;
            padding: 2px 6px;
            border-radius: var(--radius-xs);
            font-size: 13px;
            font-weight: 600;
            color: var(--brand-primary);
          }
          .forbidden-sub {
            font-size: 13.5px;
            color: var(--text-muted);
            margin-bottom: 24px;
          }
          .forbidden-actions {
            display: flex;
            justify-content: center;
            gap: 12px;
          }
        `}</style>
      </div>
    );
  }

  return <>{children}</>;
};
