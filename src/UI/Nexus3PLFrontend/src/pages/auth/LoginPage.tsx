import React, { useState } from 'react';
import { useAuth } from '../../context/AuthContext';
import { useRouter, Link } from '../../context/RouterContext';
import logoFull from '../../assets/logo-full.svg';

export const LoginPage: React.FC = () => {
  const { login, isAuthenticated } = useAuth();
  const { navigate, queryParams } = useRouter();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const isExpired = queryParams.get('expired') === 'true';
  const redirectTarget = queryParams.get('redirect') || '/app/dashboard';

  // If already authenticated, redirect
  React.useEffect(() => {
    if (isAuthenticated) {
      navigate(redirectTarget, { replace: true });
    }
  }, [isAuthenticated, navigate, redirectTarget]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!email.trim() || !password.trim()) {
      setErrorMessage('Please enter both your work email and password.');
      return;
    }

    setIsSubmitting(true);
    setErrorMessage(null);

    try {
      await login({ email: email.trim(), password });
      navigate(redirectTarget, { replace: true });
    } catch (err: any) {
      setErrorMessage(
        err?.data?.detail || err?.message || 'Invalid enterprise credentials. Please check your credentials.'
      );
    } finally {
      setIsSubmitting(false);
    }
  };

  const fillSyntheticCredentials = (u: string, p: string) => {
    setEmail(u);
    setPassword(p);
    setErrorMessage(null);
  };

  return (
    <div className="login-viewport">
      <div className="login-card-container">
        <div className="login-card">
          <div className="login-header">
            <Link to="/" className="brand-link" aria-label="Return to Nexus3PL homepage">
              <img src={logoFull} alt="Nexus3PL Logo" className="login-logo" />
            </Link>
            <h1 className="login-title">Operations Portal Sign In</h1>
            <p className="login-subtitle">
              Authorized access to multi-tenant warehouse, transportation, and administrative workflows.
            </p>
          </div>

          {isExpired && (
            <div className="alert-notice alert-warning" role="alert">
              <strong>Session Expired:</strong> Your security token has expired. Please sign in again.
            </div>
          )}

          {errorMessage && (
            <div className="alert-notice alert-danger" role="alert">
              <strong>Sign In Failed:</strong> {errorMessage}
            </div>
          )}

          <form onSubmit={handleSubmit} className="login-form" noValidate>
            <div className="form-group">
              <label htmlFor="login-email">Work Email</label>
              <input
                id="login-email"
                type="email"
                className="form-control"
                placeholder="name@company.com"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                autoComplete="email"
                required
                disabled={isSubmitting}
              />
            </div>

            <div className="form-group">
              <label htmlFor="login-password">Password</label>
              <input
                id="login-password"
                type="password"
                className="form-control"
                placeholder="Enter your security password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoComplete="current-password"
                required
                disabled={isSubmitting}
              />
            </div>

            <button
              type="submit"
              className="btn btn-primary btn-block"
              disabled={isSubmitting}
              aria-busy={isSubmitting}
            >
              {isSubmitting ? 'Authenticating...' : 'Sign In to Platform'}
            </button>
          </form>

          {/* Quick-Fill Sandbox Credentials for Verification */}
          <div className="sandbox-panel">
            <div className="sandbox-header">
              <span className="sandbox-badge">DEMO & QA CREDENTIALS</span>
              <span className="sandbox-note">Click to populate synthetic test account</span>
            </div>
            <div className="sandbox-buttons">
              <button
                type="button"
                className="btn-sandbox"
                onClick={() => fillSyntheticCredentials('superadmin@3pl.local', 'SuperAdmin123!')}
              >
                SuperAdmin (All Perms)
              </button>
              <button
                type="button"
                className="btn-sandbox"
                onClick={() => fillSyntheticCredentials('ops@3pl.local', 'OpsStaff123!')}
              >
                Ops Staff (Warehouse)
              </button>
              <button
                type="button"
                className="btn-sandbox"
                onClick={() => fillSyntheticCredentials('client@tenant.local', 'ClientTenant123!')}
              >
                Client Tenant (Scoped)
              </button>
            </div>
          </div>

          <div className="login-footer">
            <Link to="/" className="back-link">
              &larr; Back to Public Tracking & Platform Overview
            </Link>
          </div>
        </div>
      </div>

      <style>{`
        .login-viewport {
          min-height: 100vh;
          background: #f5f5f9;
          display: flex;
          align-items: center;
          justify-content: center;
          padding: 24px 16px;
          box-sizing: border-box;
        }
        .login-card-container {
          width: 100%;
          max-width: 460px;
        }
        .login-card {
          background: #ffffff;
          border-radius: var(--radius-lg);
          border: 1px solid var(--border-subtle);
          box-shadow: 0 12px 32px rgba(15, 23, 42, 0.08);
          padding: 36px 32px;
          box-sizing: border-box;
        }
        .login-header {
          text-align: center;
          margin-bottom: 24px;
        }
        .login-logo {
          height: 38px;
          width: auto;
          margin-bottom: 18px;
        }
        .login-title {
          font-size: 22px;
          font-weight: 750;
          color: var(--text-main);
          margin-bottom: 8px;
        }
        .login-subtitle {
          font-size: 13.5px;
          color: var(--text-muted);
          line-height: 1.5;
        }
        .alert-notice {
          padding: 12px 14px;
          border-radius: var(--radius-xs);
          font-size: 13.5px;
          line-height: 1.45;
          margin-bottom: 20px;
        }
        .alert-warning {
          background: var(--status-warning-bg);
          color: var(--status-warning-text);
          border-left: 4px solid var(--status-warning-text);
        }
        .alert-danger {
          background: var(--status-danger-bg);
          color: var(--status-danger-text);
          border-left: 4px solid var(--status-danger-text);
        }
        .login-form {
          display: flex;
          flex-direction: column;
          gap: 18px;
        }
        .form-group {
          display: flex;
          flex-direction: column;
          gap: 6px;
          text-align: left;
        }
        .form-group label {
          font-size: 13px;
          font-weight: 650;
          color: var(--text-main);
        }
        .form-control {
          height: 42px;
          padding: 8px 12px;
          font-size: 14px;
          border: 1px solid var(--border-actionable);
          border-radius: var(--radius-xs);
          background: #ffffff;
          color: var(--text-main);
          transition: border-color var(--transition-fast), box-shadow var(--transition-fast);
          box-sizing: border-box;
        }
        .form-control:focus {
          outline: none;
          border-color: var(--border-focus);
          box-shadow: 0 0 0 3px rgba(82, 84, 230, 0.15);
        }
        .btn-block {
          width: 100%;
          height: 44px;
          font-size: 15px;
          font-weight: 650;
          margin-top: 6px;
        }
        .sandbox-panel {
          margin-top: 24px;
          padding-top: 18px;
          border-top: 1px solid var(--border-subtle);
        }
        .sandbox-header {
          display: flex;
          flex-direction: column;
          align-items: flex-start;
          gap: 4px;
          margin-bottom: 10px;
        }
        .sandbox-badge {
          font-size: 11px;
          font-weight: 750;
          letter-spacing: 0.05em;
          color: var(--brand-primary);
          background: var(--brand-primary-light);
          padding: 2px 8px;
          border-radius: var(--radius-xs);
        }
        .sandbox-note {
          font-size: 12px;
          color: var(--text-muted);
        }
        .sandbox-buttons {
          display: flex;
          flex-direction: column;
          gap: 8px;
        }
        .btn-sandbox {
          background: #f8fafc;
          border: 1px solid var(--border-subtle);
          color: var(--text-main);
          padding: 7px 10px;
          border-radius: var(--radius-xs);
          font-size: 12.5px;
          font-weight: 600;
          text-align: left;
          cursor: pointer;
          transition: background var(--transition-fast), border-color var(--transition-fast);
        }
        .btn-sandbox:hover {
          background: #eeefff;
          border-color: var(--brand-primary);
          color: var(--brand-primary);
        }
        .login-footer {
          margin-top: 22px;
          text-align: center;
        }
        .back-link {
          font-size: 13px;
          color: var(--text-muted);
          text-decoration: none;
          transition: color var(--transition-fast);
        }
        .back-link:hover {
          color: var(--brand-primary);
          text-decoration: underline;
        }
      `}</style>
    </div>
  );
};
