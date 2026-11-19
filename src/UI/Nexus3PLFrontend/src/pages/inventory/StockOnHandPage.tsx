import React, { useState, useEffect, useCallback } from 'react';
import { useFacility } from '../../context/FacilityContext';
import { useAuth } from '../../context/AuthContext';
import { inventoryApi } from '../../services/inventoryApi';
import { StockOnHandDto, StockOnHandQuery } from '../../types/inventory';

export const StockOnHandPage: React.FC = () => {
  const { activeFacility } = useFacility();
  const { user } = useAuth();

  const [stockItems, setStockItems] = useState<StockOnHandDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filters
  const [searchSku, setSearchSku] = useState('');
  const [page, setPage] = useState(1);
  const pageSize = 15;

  const loadStock = useCallback(
    async (signal?: AbortSignal) => {
      if (!activeFacility) return;
      try {
        setLoading(true);
        setError(null);
        const query: StockOnHandQuery = {
          facilityId: activeFacility.id,
          searchSku: searchSku.trim() || undefined,
          pageNumber: page,
          pageSize,
        };
        const res = await inventoryApi.getStockOnHand(query, signal);
        setStockItems(res.items);
        setTotalCount(res.totalCount);
      } catch (err: any) {
        if (err.name !== 'AbortError') {
          setError(err.message || 'Failed to query stock on hand ledger');
        }
      } finally {
        setLoading(false);
      }
    },
    [activeFacility, searchSku, page]
  );

  useEffect(() => {
    const ctrl = new AbortController();
    loadStock(ctrl.signal);
    return () => ctrl.abort();
  }, [loadStock]);

  // Aggregate metrics
  const totalAvailable = stockItems.reduce((acc, s) => acc + (s.qtyAvailable || 0), 0);
  const totalQuarantinedOrDamaged = stockItems.reduce(
    (acc, s) => acc + (s.qtyQuarantined || 0) + (s.qtyDamaged || 0),
    0
  );
  const totalAllocated = stockItems.reduce((acc, s) => acc + (s.qtyAllocated || 0) + (s.qtyReserved || 0), 0);

  const isStagingLocation = (code: string) => {
    return code.toUpperCase().includes('STAGING') || code.toUpperCase().includes('STG');
  };

  return (
    <div className="stock-page-container" role="region" aria-label="Stock On Hand Ledger">
      <div className="page-header">
        <div>
          <h1 className="page-title">Stock on Hand &amp; Inventory Ledger</h1>
          <p className="page-subtitle">
            Real-Time Location Balances &bull; {activeFacility ? `${activeFacility.name} (${activeFacility.facilityCode})` : 'All Facilities'}
            {user?.companyName ? ` &bull; Tenant: ${user.companyName}` : ''}
          </p>
        </div>
      </div>

      {error && (
        <div className="alert-banner error" role="alert">
          <span>{error}</span>
        </div>
      )}

      {/* METRIC TILES */}
      <div className="metrics-grid">
        <div className="card metric-card">
          <span className="metric-label">SKU Records</span>
          <span className="metric-value" data-testid="metric-total-skus">{totalCount}</span>
          <span className="metric-sub">Active ledger lines</span>
        </div>
        <div className="card metric-card">
          <span className="metric-label">Available for Order</span>
          <span className="metric-value text-success" data-testid="metric-available-qty">{totalAvailable.toLocaleString()}</span>
          <span className="metric-sub">Unreserved stock</span>
        </div>
        <div className="card metric-card">
          <span className="metric-label">Allocated / Reserved</span>
          <span className="metric-value text-warning">{totalAllocated.toLocaleString()}</span>
          <span className="metric-sub">Waves &amp; holds</span>
        </div>
        <div className="card metric-card">
          <span className="metric-label">Quarantine / Damaged</span>
          <span className="metric-value text-danger" data-testid="metric-quarantine-qty">
            {totalQuarantinedOrDamaged.toLocaleString()}
          </span>
          <span className="metric-sub">QC hold &amp; damage</span>
        </div>
      </div>

      {/* SEARCH BAR */}
      <div className="filter-bar card">
        <div className="filter-group">
          <input
            type="text"
            className="form-input search-input"
            placeholder="Search by SKU, Product Description, or Location Code..."
            value={searchSku}
            onChange={(e) => setSearchSku(e.target.value)}
            data-testid="search-stock-input"
          />
        </div>
      </div>

      {/* STOCK TABLE */}
      <div className="card table-card">
        {loading ? (
          <div className="table-loading" data-testid="stock-loading">
            Loading real-time stock balances...
          </div>
        ) : (
          <div className="table-responsive">
            <table className="enterprise-table" data-testid="stock-table">
              <thead>
                <tr>
                  <th>SKU</th>
                  <th>Description</th>
                  <th>Location</th>
                  <th>Barcode</th>
                  <th>Lot #</th>
                  <th>Available</th>
                  <th>Allocated</th>
                  <th>Quarantine</th>
                  <th>Damaged</th>
                  <th>Total</th>
                </tr>
              </thead>
              <tbody>
                {stockItems.length === 0 ? (
                  <tr>
                    <td colSpan={10} className="empty-row" data-testid="empty-stock-row">
                      No stock-on-hand records found for this facility and tenant.
                    </td>
                  </tr>
                ) : (
                  stockItems.map((item) => (
                    <tr key={item.id} data-testid={`stock-row-${item.id}`}>
                      <td className="fw-600 code-font">{item.sku}</td>
                      <td>{item.itemDescription || '—'}</td>
                      <td>
                        <div className="location-cell">
                          <span className="code-font fw-600">{item.locationCode}</span>
                          {isStagingLocation(item.locationCode) ? (
                            <span className="loc-badge staging">Staging</span>
                          ) : (
                            <span className="loc-badge storage">Bin</span>
                          )}
                        </div>
                      </td>
                      <td className="code-font">{item.locationBarcode}</td>
                      <td>{item.lotNumber ? <span className="code-font">{item.lotNumber}</span> : '—'}</td>
                      <td className="fw-600 text-success">{item.qtyAvailable}</td>
                      <td>{item.qtyAllocated + item.qtyReserved}</td>
                      <td className={item.qtyQuarantined > 0 ? 'text-warning fw-600' : ''}>
                        {item.qtyQuarantined}
                      </td>
                      <td className={item.qtyDamaged > 0 ? 'text-danger fw-600' : ''}>
                        {item.qtyDamaged}
                      </td>
                      <td className="fw-700">{item.totalQuantity}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <style>{`
        .stock-page-container {
          display: flex;
          flex-direction: column;
          gap: 24px;
        }
        .metrics-grid {
          display: grid;
          grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
          gap: 16px;
        }
        .metric-card {
          padding: 16px 20px;
          display: flex;
          flex-direction: column;
          gap: 4px;
        }
        .metric-label {
          font-size: 12px;
          font-weight: 700;
          color: var(--text-muted);
          text-transform: uppercase;
        }
        .metric-value {
          font-size: 28px;
          font-weight: 800;
          color: var(--text-main);
        }
        .metric-sub {
          font-size: 12px;
          color: var(--text-muted);
        }
        .text-success { color: #137333; }
        .text-warning { color: #b06000; }
        .text-danger { color: #c5221f; }
        .location-cell {
          display: flex;
          align-items: center;
          gap: 8px;
        }
        .loc-badge {
          display: inline-block;
          font-size: 10px;
          font-weight: 700;
          text-transform: uppercase;
          padding: 2px 6px;
          border-radius: 4px;
        }
        .loc-badge.staging {
          background: #fef7e0;
          color: #b06000;
        }
        .loc-badge.storage {
          background: #e6f4ea;
          color: #137333;
        }
        .filter-bar {
          display: flex;
          padding: 16px;
        }
        .search-input {
          width: 100%;
          min-width: 320px;
        }
      `}</style>
    </div>
  );
};
