import React, { useEffect, useState } from 'react';
import { adminApi } from '../../services/adminApi';
import { UserDto, RoleDto, UserStatus, UserType, normalizeUserType, normalizeUserStatus } from '../../types/auth';
import { PermissionGuard } from '../../components/auth/PermissionGuard';
import { useAuth } from '../../context/AuthContext';

export const UsersPage: React.FC = () => {
  const { hasPermission } = useAuth();
  const canViewRoles = hasPermission('roles:view') || hasPermission('roles:manage-permissions');

  const [users, setUsers] = useState<UserDto[]>([]);
  const [availableRoles, setAvailableRoles] = useState<RoleDto[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // Role Assignment Modal State
  const [editingUser, setEditingUser] = useState<UserDto | null>(null);
  const [selectedRoles, setSelectedRoles] = useState<string[]>([]);
  const [isSavingRoles, setIsSavingRoles] = useState<boolean>(false);
  const [modalError, setModalError] = useState<string | null>(null);

  const loadData = async () => {
    setIsLoading(true);
    setErrorMessage(null);

    // 1. Load users independently so users:view callers never fail even if roles returns 403
    try {
      const usersResult = await adminApi.getUsers({ pageNumber: 1, pageSize: 50 });
      setUsers(usersResult.items || []);
    } catch (err: any) {
      setErrorMessage(err?.message || 'Failed to load users directory');
    } finally {
      setIsLoading(false);
    }

    // 2. Conditionally fetch roles only if caller has role permissions
    if (canViewRoles) {
      try {
        const rolesResult = await adminApi.getRoles();
        setAvailableRoles(rolesResult || []);
      } catch (err: any) {
        console.warn('Roles fetch omitted or failed for current caller:', err);
      }
    }
  };

  useEffect(() => {
    loadData();
  }, [canViewRoles]);

  const openRoleModal = async (user: UserDto) => {
    setEditingUser(user);
    setSelectedRoles([...user.roles]);
    setModalError(null);

    if (availableRoles.length === 0 && canViewRoles) {
      try {
        const rolesResult = await adminApi.getRoles();
        setAvailableRoles(rolesResult || []);
      } catch (err: any) {
        setModalError(err?.message || 'Unable to retrieve system roles');
      }
    }
  };

  const closeRoleModal = () => {
    setEditingUser(null);
    setSelectedRoles([]);
    setModalError(null);
  };

  const handleToggleRole = (roleName: string) => {
    setSelectedRoles((prev) =>
      prev.includes(roleName) ? prev.filter((r) => r !== roleName) : [...prev, roleName]
    );
  };

  const handleSaveRoles = async () => {
    if (!editingUser) return;
    setIsSavingRoles(true);
    setModalError(null);
    try {
      await adminApi.assignUserRoles(editingUser.id, selectedRoles);
      // Update local state
      setUsers((prev) =>
        prev.map((u) => (u.id === editingUser.id ? { ...u, roles: selectedRoles } : u))
      );
      closeRoleModal();
    } catch (err: any) {
      setModalError(err?.message || 'Failed to update user security roles');
    } finally {
      setIsSavingRoles(false);
    }
  };

  const getUserTypeLabel = (type: any) => {
    const t = normalizeUserType(type);
    switch (t) {
      case UserType.Staff:
        return 'Staff';
      case UserType.Client:
        return 'Client User';
      case UserType.Driver:
        return 'Driver';
      default:
        return 'Unknown';
    }
  };

  const getUserStatusBadge = (status: any) => {
    const s = normalizeUserStatus(status);
    switch (s) {
      case UserStatus.Active:
        return <span className="badge badge-success">Active</span>;
      case UserStatus.Inactive:
        return <span className="badge badge-secondary">Inactive</span>;
      case UserStatus.Locked:
        return <span className="badge badge-danger">Locked</span>;
      case UserStatus.PendingActivation:
        return <span className="badge badge-warning">Pending</span>;
      default:
        return <span className="badge">{status}</span>;
    }
  };

  return (
    <PermissionGuard requiredPermission="users:view">
      <div className="users-page">
        <div className="card">
          <div className="card-header">
            <div>
              <h2 className="card-title">Enterprise Users Directory</h2>
              <p className="card-subtitle">
                Manage operational identities, tenant affiliations, and security role assignments.
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
              Loading user accounts...
            </div>
          ) : (
            <div className="table-responsive">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>User</th>
                    <th>Type</th>
                    <th>Status</th>
                    <th>Assigned Roles</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {users.length === 0 ? (
                    <tr>
                      <td colSpan={5} className="empty-cell">
                        No user accounts found.
                      </td>
                    </tr>
                  ) : (
                    users.map((user) => (
                      <tr key={user.id}>
                        <td>
                          <div className="user-primary-cell">
                            <span className="user-full-name">
                              {user.firstName} {user.lastName}
                            </span>
                            <span className="user-email">{user.email}</span>
                          </div>
                        </td>
                        <td>
                          <span className="type-tag">{getUserTypeLabel(user.userType)}</span>
                        </td>
                        <td>{getUserStatusBadge(user.status)}</td>
                        <td>
                          <div className="roles-wrap">
                            {user.roles && user.roles.length > 0 ? (
                              user.roles.map((r) => (
                                <span key={r} className="badge badge-role">
                                  {r}
                                </span>
                              ))
                            ) : (
                              <span className="no-roles">None</span>
                            )}
                          </div>
                        </td>
                        <td>
                          <button
                            type="button"
                            className="btn-action"
                            onClick={() => openRoleModal(user)}
                            aria-label={`Assign roles to ${user.firstName} ${user.lastName}`}
                          >
                            Assign Roles
                          </button>
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          )}
        </div>

        {/* Assign Roles Modal */}
        {editingUser && (
          <div className="modal-backdrop" onClick={closeRoleModal} role="dialog" aria-modal="true">
            <div className="modal-card" onClick={(e) => e.stopPropagation()}>
              <div className="modal-header">
                <div>
                  <h3 className="modal-title">Assign Security Roles</h3>
                  <p className="modal-sub">
                    Configure authorized system roles for{' '}
                    <strong>
                      {editingUser.firstName} {editingUser.lastName}
                    </strong>{' '}
                    ({editingUser.email}).
                  </p>
                </div>
                <button
                  type="button"
                  className="modal-close-icon"
                  onClick={closeRoleModal}
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

              <div className="modal-body">
                <fieldset className="role-checkbox-group">
                  <legend className="sr-only">Available System Roles</legend>
                  {availableRoles.map((role) => {
                    const roleName = role.roleName || role.name || `Role #${role.id}`;
                    const isChecked = selectedRoles.includes(roleName);
                    return (
                      <label key={role.id} className="role-checkbox-label">
                        <input
                          type="checkbox"
                          checked={isChecked}
                          onChange={() => handleToggleRole(roleName)}
                          disabled={isSavingRoles}
                        />
                        <div className="role-meta">
                          <span className="role-name">{roleName}</span>
                          <span className="role-desc">{role.description}</span>
                        </div>
                      </label>
                    );
                  })}
                </fieldset>
              </div>

              <div className="modal-footer">
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={closeRoleModal}
                  disabled={isSavingRoles}
                >
                  Cancel
                </button>
                <button
                  type="button"
                  className="btn btn-primary"
                  onClick={handleSaveRoles}
                  disabled={isSavingRoles}
                >
                  {isSavingRoles ? 'Saving...' : 'Save Role Assignments'}
                </button>
              </div>
            </div>
          </div>
        )}

        <style>{`
          .users-page {
            display: flex;
            flex-direction: column;
            gap: 20px;
          }
          .card {
            background: #ffffff;
            border-radius: var(--radius-md);
            border: 1px solid var(--border-subtle);
            box-shadow: 0 2px 8px rgba(15, 23, 42, 0.04);
            padding: 24px;
          }
          .card-header {
            display: flex;
            align-items: flex-start;
            justify-content: space-between;
            margin-bottom: 20px;
          }
          .card-title {
            font-size: 18px;
            font-weight: 750;
            color: var(--text-main);
            margin-bottom: 4px;
          }
          .card-subtitle {
            font-size: 13.5px;
            color: var(--text-muted);
          }
          .table-responsive {
            overflow-x: auto;
          }
          .data-table {
            width: 100%;
            border-collapse: collapse;
            text-align: left;
            font-size: 13.5px;
          }
          .data-table th {
            background: #f8fafc;
            color: var(--text-muted);
            font-weight: 700;
            font-size: 11.5px;
            text-transform: uppercase;
            letter-spacing: 0.05em;
            padding: 12px 14px;
            border-bottom: 1px solid var(--border-subtle);
          }
          .data-table td {
            padding: 14px;
            border-bottom: 1px solid #f1f5f9;
            color: var(--text-main);
            vertical-align: middle;
          }
          .user-primary-cell {
            display: flex;
            flex-direction: column;
          }
          .user-full-name {
            font-weight: 700;
            color: var(--text-main);
          }
          .user-email {
            font-size: 12.5px;
            color: var(--text-muted);
          }
          .type-tag {
            font-size: 12px;
            font-weight: 600;
            color: #334155;
            background: #f1f5f9;
            padding: 3px 8px;
            border-radius: var(--radius-xs);
          }
          .roles-wrap {
            display: flex;
            flex-wrap: wrap;
            gap: 4px;
          }
          .badge-role {
            background: #eef2ff;
            color: #4338ca;
            font-size: 11.5px;
            font-weight: 650;
            padding: 2px 7px;
            border-radius: 4px;
          }
          .no-roles {
            color: var(--text-muted);
            font-style: italic;
            font-size: 12px;
          }
          .btn-action {
            background: #f8fafc;
            border: 1px solid var(--border-actionable);
            border-radius: var(--radius-xs);
            padding: 5px 10px;
            font-size: 12.5px;
            font-weight: 600;
            color: var(--text-main);
            cursor: pointer;
            transition: all var(--transition-fast);
          }
          .btn-action:hover {
            background: var(--brand-primary-light);
            border-color: var(--brand-primary);
            color: var(--brand-primary);
          }
          .role-checkbox-group {
            border: none;
            padding: 0;
            margin: 0;
            display: flex;
            flex-direction: column;
            gap: 10px;
          }
          .role-checkbox-label {
            display: flex;
            align-items: flex-start;
            gap: 12px;
            padding: 10px 12px;
            border: 1px solid var(--border-subtle);
            border-radius: var(--radius-xs);
            cursor: pointer;
            transition: background var(--transition-fast);
          }
          .role-checkbox-label:hover {
            background: #f8fafc;
          }
          .role-checkbox-label input {
            margin-top: 3px;
          }
          .role-meta {
            display: flex;
            flex-direction: column;
          }
          .role-name {
            font-weight: 700;
            font-size: 13.5px;
            color: var(--text-main);
          }
          .role-desc {
            font-size: 12px;
            color: var(--text-muted);
          }
          .sr-only {
            position: absolute;
            width: 1px;
            height: 1px;
            padding: 0;
            margin: -1px;
            overflow: hidden;
            clip: rect(0, 0, 0, 0);
            border: 0;
          }
        `}</style>
      </div>
    </PermissionGuard>
  );
};
