import React, { useEffect } from 'react';
import { useAuth } from '../../context/AuthContext';
import { useRouter } from '../../context/RouterContext';

export const ProtectedRoute: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const { isAuthenticated, isLoading } = useAuth();
  const { navigate, path } = useRouter();

  useEffect(() => {
    if (!isLoading && !isAuthenticated) {
      navigate(`/login?redirect=${encodeURIComponent(path)}`, { replace: true });
    }
  }, [isLoading, isAuthenticated, navigate, path]);

  if (isLoading) {
    return (
      <div className="auth-loading-screen" role="status" aria-live="polite">
        <div className="loading-spinner"></div>
        <p>Verifying enterprise security credentials...</p>
        <style>{`
          .auth-loading-screen {
            display: flex;
            flex-direction: column;
            align-items: center;
            justify-content: center;
            min-height: 100vh;
            background: var(--bg-app);
            color: var(--text-muted);
            font-size: 15px;
          }
          .loading-spinner {
            width: 36px;
            height: 36px;
            border: 3px solid var(--border-subtle);
            border-top-color: var(--brand-primary);
            border-radius: 50%;
            animation: spin 0.8s linear infinite;
            margin-bottom: 16px;
          }
          @keyframes spin {
            to { transform: rotate(360deg); }
          }
        `}</style>
      </div>
    );
  }

  if (!isAuthenticated) {
    return null;
  }

  return <>{children}</>;
};
