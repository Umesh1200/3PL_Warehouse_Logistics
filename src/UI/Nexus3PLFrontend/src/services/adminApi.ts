import { ClientDto, PagedResult, PermissionAssignmentDto, RoleDto, UserDto } from '../types/auth';
import { apiClient } from './apiClient';

export const adminApi = {
  /**
   * Retrieve paginated list of users.
   */
  getUsers: async (
    params: { pageNumber?: number; pageSize?: number } = {},
    signal?: AbortSignal
  ): Promise<PagedResult<UserDto>> => {
    const query = new URLSearchParams();
    if (params.pageNumber) query.set('pageNumber', params.pageNumber.toString());
    if (params.pageSize) query.set('pageSize', params.pageSize.toString());

    const qs = query.toString() ? `?${query.toString()}` : '';
    return apiClient<PagedResult<UserDto>>(`/api/v1/users${qs}`, {
      method: 'GET',
      signal,
    });
  },

  /**
   * Assign security roles to a user.
   */
  assignUserRoles: async (userId: string, roleNames: string[]): Promise<void> => {
    return apiClient<void>(`/api/v1/users/${userId}/roles`, {
      method: 'POST',
      body: JSON.stringify(roleNames),
    });
  },

  /**
   * Retrieve all configured roles and their permissions.
   */
  getRoles: async (signal?: AbortSignal): Promise<RoleDto[]> => {
    return apiClient<RoleDto[]>('/api/v1/roles', {
      method: 'GET',
      signal,
    });
  },

  /**
   * Retrieve system permissions catalog.
   */
  getPermissions: async (signal?: AbortSignal): Promise<string[]> => {
    return apiClient<string[]>('/api/v1/roles/permissions', {
      method: 'GET',
      signal,
    });
  },

  /**
   * Update fine-grained permission assignments on a role.
   */
  assignRolePermissions: async (
    roleId: number,
    permissions: PermissionAssignmentDto[]
  ): Promise<void> => {
    return apiClient<void>(`/api/v1/roles/${roleId}/permissions`, {
      method: 'POST',
      body: JSON.stringify(permissions),
    });
  },

  /**
   * Retrieve paginated client tenants.
   */
  getClients: async (
    params: { pageNumber?: number; pageSize?: number } = {},
    signal?: AbortSignal
  ): Promise<PagedResult<ClientDto>> => {
    const query = new URLSearchParams();
    if (params.pageNumber) query.set('pageNumber', params.pageNumber.toString());
    if (params.pageSize) query.set('pageSize', params.pageSize.toString());

    const qs = query.toString() ? `?${query.toString()}` : '';
    return apiClient<PagedResult<ClientDto>>(`/api/v1/clients${qs}`, {
      method: 'GET',
      signal,
    });
  },
};
