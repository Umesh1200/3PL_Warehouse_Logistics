import React, { useEffect, useState } from 'react';
import { adminApi } from '../../services/adminApi';
import { ClientDto, formatSlaTier, formatClientStatus } from '../../types/auth';
import { PermissionGuard } from '../../components/auth/PermissionGuard';

export const ClientsPage: React.FC = () => {
  const [clients, setClients] = useState<ClientDto[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const loadData = async () => {
    setIsLoading(true);
    setErrorMessage(null);
    try {
      const result = await adminApi.getClients({ pageNumber: 1, pageSize: 50 });
      setClients(result.items || []);
    } catch (err: any) {
      setErrorMessage(err?.message || 'Failed to load client tenants');
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    loadData();
  }, []);

  return (
    <PermissionGuard requiredPermission="clients:view">
      <div className="clients-page">
        <div className="card">
          <div className="card-header">
            <div>
              <h2 className="card-title">Client Tenants Directory</h2>
              <p className="card-subtitle">
                Multi-tenant accounts, billing currencies, and service level agreements.
              </p>
            </div>
            <button type="button" className="btn btn-secondary" onClick={loadData}>
              Refresh
            </button>
          </div>

          {errorMessage && (
            <div className="alert-notice alert-danger" role="alert">
              {errorMessage}
            </div>
          )}

          {isLoading ? (
            <div className="table-loading" role="status">
              Loading client tenants...
            </div>
          ) : (
            <div className="table-responsive">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Client Code</th>
                    <th>Company Name</th>
                    <th>Account #</th>
                    <th>SLA Tier</th>
                    <th>Billing Currency</th>
                    <th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  {clients.length === 0 ? (
                    <tr>
                      <td colSpan={6} className="empty-cell">
                        No client tenants found.
                      </td>
                    </tr>
                  ) : (
                    clients.map((c) => {
                      const displayCode = c.accountCode || c.clientCode || c.accountNumber || c.id.slice(0, 8);
                      const displayAccount = c.accountCode || c.accountNumber || '—';
                      const slaTierText = formatSlaTier(c.slaTier ?? c.serviceLevelAgreementTier);
                      const currencyText = c.currency || c.billingCurrency || '—';
                      const statusText = formatClientStatus(c.status);

                      return (
                        <tr key={c.id}>
                          <td>
                            <span className="code-badge">{displayCode}</span>
                          </td>
                          <td>
                            <span className="client-company-name">{c.companyName}</span>
                          </td>
                          <td>
                            <span className="account-num">{displayAccount}</span>
                          </td>
                          <td>
                            <span className="badge badge-info">{slaTierText}</span>
                          </td>
                          <td>
                            <span className="currency-pill">{currencyText}</span>
                          </td>
                          <td>
                            <span className="badge badge-success">{statusText}</span>
                          </td>
                        </tr>
                      );
                    })
                  )}
                </tbody>
              </table>
            </div>
          )}
        </div>

        <style>{`
          .clients-page {
            display: flex;
            flex-direction: column;
            gap: 20px;
          }
          .code-badge {
            font-family: monospace;
            font-weight: 700;
            background: #f1f5f9;
            padding: 3px 8px;
            border-radius: var(--radius-xs);
            color: #0f172a;
          }
          .client-company-name {
            font-weight: 700;
            color: var(--text-main);
          }
          .account-num {
            font-family: monospace;
            color: var(--text-muted);
            font-size: 13px;
          }
          .currency-pill {
            background: #f0fdf4;
            color: #166534;
            border: 1px solid #bbf7d0;
            padding: 2px 7px;
            border-radius: 4px;
            font-size: 11.5px;
            font-weight: 700;
          }
        `}</style>
      </div>
    </PermissionGuard>
  );
};
