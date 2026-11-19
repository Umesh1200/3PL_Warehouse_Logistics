import { AuthResponseDto, CurrentUserDto, LoginRequest, RefreshTokenRequest } from '../types/auth';
import { apiClient } from './apiClient';

export const authApi = {
  /**
   * Authenticate user credentials and receive JWT access/refresh token pair.
   */
  login: async (request: LoginRequest): Promise<AuthResponseDto> => {
    return apiClient<AuthResponseDto>('/api/v1/auth/login', {
      method: 'POST',
      body: JSON.stringify(request),
      skipAuth: true,
    });
  },

  /**
   * Manually invoke refresh token endpoint if needed.
   */
  refreshToken: async (request: RefreshTokenRequest): Promise<AuthResponseDto> => {
    return apiClient<AuthResponseDto>('/api/v1/auth/refresh-token', {
      method: 'POST',
      body: JSON.stringify(request),
      skipAuth: true,
    });
  },

  /**
   * Retrieve current user profile, tenant information, roles, and fine-grained permissions.
   */
  getCurrentUser: async (signal?: AbortSignal): Promise<CurrentUserDto> => {
    return apiClient<CurrentUserDto>('/api/v1/auth/me', {
      method: 'GET',
      signal,
    });
  },
};
