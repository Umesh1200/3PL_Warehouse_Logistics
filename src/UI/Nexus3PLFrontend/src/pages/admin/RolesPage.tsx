import React, { useEffect, useState } from 'react';
import { adminApi } from '../../services/adminApi';
import { RoleDto, AccessLevel, PermissionAssignmentDto, formatAccessLevel, normalizeAccessLevel } from '../../types/auth';
import { PermissionGuard } from '../../components/auth/PermissionGuard';

export const RolesPage: React.FC = () => {
  const [roles, setRoles] = useState<RoleDto[]>([]);
  const [allPermissions, setAllPermissions] = useState<string[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // Manage Permissions Modal State
  const [editingRole, setEditingRole] = useState<RoleDto | null>(null);
  const [selectedPermissions, setSelectedPermissions] = useState<string[]>([]);
  const [permissionLevels, setPermissionLevels] = useState<Record<string, AccessLevel>>({});
  const [selectedAccessLevel, setSelectedAccessLevel] = useState<AccessLevel>(AccessLevel.Read);
  const [isSaving, setIsSaving] = useState<boolean>(false);
  const [modalError, setModalError] = useState<string | null>(null);

  const loadData = async () => {
    setIsLoading(true);
    setErrorMessage(null);
    try {
      const [rolesResult, permsResult] = await Promise.all([
        adminApi.getRoles(),
        adminApi.getPermissions(),
      ]);
      setRoles(rolesResult || []);
      setAllPermissions(permsResult || []);
    } catch (err: any) {
      setErrorMessage(err?.message || 'Failed to load roles and permissions');
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    loadData();
  }, []);

  const openPermissionModal = (role: RoleDto) => {
    setEditingRole(role);
    const existingPermKeys: string[] = [];
    const initialLevels: Record<string, AccessLevel> = {};
    (role.permissions || []).forEach((p: any) => {
      const key = typeof p === 'string' ? p : p?.permissionKey;
      const rawLevel = typeof p === 'object' && p?.accessLevel !== undefined ? p.accessLevel : AccessLevel.Read;
      if (key) {
        existingPermKeys.push(key);
        initialLevels[key] = normalizeAccessLevel(rawLevel);
      }
    });
    setSelectedPermissions(existingPermKeys);
    setPermissionLevels(initialLevels);
    setSelectedAccessLevel(AccessLevel.Read);
    setModalError(null);
  };

  const closePermissionModal = () => {
    setEditingRole(null);
    setSelectedPermissions([]);
    setPermissionLevels({});
    setModalError(null);
  };

  const togglePermission = (perm: string) => {
    setSelectedPermissions((prev) => {
      if (prev.includes(perm)) {
        return prev.filter((p) => p !== perm);
      } else {
        setPermissionLevels((lvl) => ({
          ...lvl,
          [perm]: lvl[perm] !== undefined ? normalizeAccessLevel(lvl[perm]) : selectedAccessLevel,
        }));
        return [...prev, perm];
      }
    });
  };

  const updatePermissionLevel = (perm: string, level: any) => {
    setPermissionLevels((prev) => ({
      ...prev,
      [perm]: normalizeAccessLevel(level),
    }));
  };

  const handleSavePermissions = async () => {
    if (!editingRole) return;
    setIsSaving(true);
    setModalError(null);

    // Map selected permissions to PermissionAssignmentDto preserving their exact normalized accessLevel
    const assignments: PermissionAssignmentDto[] = selectedPermissions.map((p) => ({
      permissionKey: p,
      accessLevel: normalizeAccessLevel(permissionLevels[p] ?? selectedAccessLevel),
    }));

    try {
      await adminApi.assignRolePermissions(editingRole.id, assignments);
      setRoles((prev) =>
        prev.map((r) =>
          r.id === editingRole.id
            ? {
                ...r,
                permissions: assignments.map((a, idx) => ({
                  id: idx + 1,
                  permissionKey: a.permissionKey,
                  accessLevel: a.accessLevel,
                })),
              }
            : r
        )
      );
      closePermissionModal();
    } catch (err: any) {
      setModalError(err?.message || 'Failed to update role permissions');
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <PermissionGuard requiredPermission="roles:view">
      <div className="roles-page">
        <div className="card">
          <div className="card-header">
            <div>
              <h2 className="card-title">Roles & Access Control Matrix</h2>
              <p className="card-subtitle">
                Inspect system roles and configure fine-grained permission assignments.
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
              Loading security roles catalog...
            </div>
          ) : (
            <div className="table-responsive">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Role</th>
                    <th>Description</th>
                    <th>Grants Count</th>
                    <th>Assigned Permissions</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {roles.length === 0 ? (
                    <tr>
                      <td colSpan={5} className="empty-cell">
                        No roles configured.
                      </td>
                    </tr>
                  ) : (
                    roles.map((role) => {
                      const displayRoleName = role.roleName || role.name || `Role #${role.id}`;
                      return (
                        <tr key={role.id}>
                          <td>
                            <span className="role-title">{displayRoleName}</span>
                          </td>
                          <td>
                            <span className="role-desc-text">{role.description}</span>
                          </td>
                          <td>
                            <span className="badge badge-info">{role.permissions?.length || 0}</span>
                          </td>
                          <td>
                            <div className="perms-chip-wrap">
                              {role.permissions && role.permissions.length > 0 ? (
                                role.permissions.map((p: any, idx) => {
                                  const permKey = typeof p === 'string' ? p : p.permissionKey;
                                  const accessLabel =
                                    typeof p === 'object' && p.accessLevel
                                      ? ` (${formatAccessLevel(p.accessLevel)})`
                                      : '';
                                  return (
                                    <span key={`${permKey}-${idx}`} className="perm-chip">
                                      {permKey}{accessLabel}
                                    </span>
                                  );
                                })
                              ) : (
                                <span className="no-perms">No explicit grants</span>
                              )}
                            </div>
                          </td>
                          <td>
                            <button
                              type="button"
                              className="btn-action"
                              onClick={() => openPermissionModal(role)}
                              aria-label={`Configure permissions for ${displayRoleName}`}
                            >
                              Configure
                            </button>
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

        {/* Configure Permissions Modal */}
        {editingRole && (
          <div className="modal-backdrop" onClick={closePermissionModal} role="dialog" aria-modal="true">
            <div className="modal-card modal-card-wide" onClick={(e) => e.stopPropagation()}>
              <div className="modal-header">
                <div>
                  <h3 className="modal-title">
                    Configure Permissions &mdash; {editingRole.roleName || editingRole.name}
                  </h3>
                  <p className="modal-sub">{editingRole.description}</p>
                </div>
                <button
                  type="button"
                  className="modal-close-icon"
                  onClick={closePermissionModal}
                  aria-label="Close dialog"
                >
                  &times;
                </button>
              </div>

              {modalError && (
                <div className="alert-notice alert-danger" role="alert">
                  {modalError}
                </div>
              )}

              <div className="modal-body modal-scrollable">
                <div className="access-level-picker">
                  <label htmlFor="access-level-select">Default Access Level for Assigned Grants:</label>
                  <select
                    id="access-level-select"
                    className="form-control access-select"
                    value={normalizeAccessLevel(selectedAccessLevel)}
                    onChange={(e) => setSelectedAccessLevel(normalizeAccessLevel(Number(e.target.value)))}
                  >
                    <option value={AccessLevel.Read}>Read (1)</option>
                    <option value={AccessLevel.Write}>Write (2)</option>
                    <option value={AccessLevel.Admin}>Admin (3)</option>
                    <option value={AccessLevel.Execute}>Execute (4)</option>
                  </select>
                </div>

                <div className="perms-grid">
                  {allPermissions.map((perm) => {
                    const isChecked = selectedPermissions.includes(perm);
                    const currentLevel = normalizeAccessLevel(permissionLevels[perm] ?? selectedAccessLevel);
                    return (
                      <div key={perm} className={`perm-card-check ${isChecked ? 'selected' : ''}`}>
                        <label className="perm-card-label">
                          <input
                            type="checkbox"
                            checked={isChecked}
                            onChange={() => togglePermission(perm)}
                            disabled={isSaving}
                          />
                          <span className="perm-key-text">{perm}</span>
                        </label>
                        {isChecked && (
                          <div className="perm-level-inline" onClick={(e) => e.stopPropagation()}>
                            <select
                              aria-label={`Access level for ${perm}`}
                              className="form-control access-level-mini-select"
                              value={currentLevel}
                              onChange={(e) => updatePermissionLevel(perm, normalizeAccessLevel(Number(e.target.value)))}
                              disabled={isSaving}
                            >
                              <option value={AccessLevel.Read}>Read (1)</option>
                              <option value={AccessLevel.Write}>Write (2)</option>
                              <option value={AccessLevel.Admin}>Admin (3)</option>
                              <option value={AccessLevel.Execute}>Execute (4)</option>
                            </select>
                          </div>
                        )}
                      </div>
                    );
                  })}
                </div>
              </div>

              <div className="modal-footer">
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={closePermissionModal}
                  disabled={isSaving}
                >
                  Cancel
                </button>
                <button
                  type="button"
                  className="btn btn-primary"
                  onClick={handleSavePermissions}
                  disabled={isSaving}
                >
                  {isSaving ? 'Updating...' : `Save ${selectedPermissions.length} Grants`}
                </button>
              </div>
            </div>
          </div>
        )}

        <style>{`
          .roles-page {
            display: flex;
            flex-direction: column;
            gap: 20px;
          }
          .role-title {
            font-weight: 750;
            color: var(--text-main);
            font-size: 14px;
          }
          .role-desc-text {
            color: var(--text-muted);
            font-size: 13px;
          }
          .perms-chip-wrap {
            display: flex;
            flex-wrap: wrap;
            gap: 4px;
            max-width: 480px;
          }
          .perm-chip {
            background: #f1f5f9;
            color: #334155;
            font-size: 11px;
            font-weight: 600;
            padding: 2px 6px;
            border-radius: 3px;
            font-family: monospace;
          }
          .no-perms {
            color: var(--text-muted);
            font-style: italic;
            font-size: 12px;
          }
          .modal-card-wide {
            max-width: 680px;
          }
          .modal-scrollable {
            max-height: 440px;
            overflow-y: auto;
          }
          .access-level-picker {
            display: flex;
            align-items: center;
            gap: 12px;
            margin-bottom: 16px;
            background: #f8fafc;
            padding: 10px 14px;
            border-radius: var(--radius-xs);
          }
          .access-level-picker label {
            font-size: 12.5px;
            font-weight: 650;
            color: var(--text-main);
          }
          .access-select {
            height: 34px;
            padding: 4px 10px;
            width: 140px;
          }
          .perms-grid {
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
            gap: 8px;
          }
          .perm-card-check {
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 8px;
            padding: 8px 10px;
            border: 1px solid var(--border-subtle);
            border-radius: var(--radius-xs);
            cursor: pointer;
            transition: all var(--transition-fast);
          }
          .perm-card-label {
            display: flex;
            align-items: center;
            gap: 8px;
            cursor: pointer;
            flex: 1;
            margin: 0;
          }
          .perm-level-inline {
            display: flex;
            align-items: center;
          }
          .access-level-mini-select {
            height: 26px;
            padding: 2px 6px;
            font-size: 11.5px;
            width: auto;
            border-radius: 4px;
            border: 1px solid var(--border-subtle);
            background: #fff;
          }
          .perm-card-check:hover {
            background: #f8fafc;
          }
          .perm-card-check.selected {
            background: #eeefff;
            border-color: var(--brand-primary);
          }
          .perm-key-text {
            font-size: 12px;
            font-weight: 600;
            color: var(--text-main);
            font-family: monospace;
          }
        `}</style>
      </div>
    </PermissionGuard>
  );
};
