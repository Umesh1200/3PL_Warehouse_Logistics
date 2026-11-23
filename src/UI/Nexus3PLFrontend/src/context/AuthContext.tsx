import React, { createContext, useContext, useEffect, useState, useCallback } from 'react';
import { authApi } from '../services/authApi';
import { tokenManager, requestManager } from '../services/apiClient';
import { schemaService } from '../services/schemaService';
import { CurrentUserDto, LoginRequest, UserType, normalizeUserTypeStrict } from '../types/auth';

interface AuthContextType {
  user: CurrentUserDto | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  isStaff: boolean;
  isClient: boolean;
  isDriver: boolean;
  isSuperAdmin: boolean;
  login: (request: LoginRequest) => Promise<void>;
  logout: () => void;
  hasPermission: (permission: string) => boolean;
  hasAnyPermission: (permissions: string[]) => boolean;
  hasRole: (role: string) => boolean;
}

export const AuthContext = createContext<AuthContextType | null>(null);

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [user, setUser] = useState<CurrentUserDto | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(true);

  const fetchProfile = useCallback(async () => {
    const token = tokenManager.getAccessToken();
    const gen = tokenManager.getSessionGeneration();
    if (!token) {
      setUser(null);
      setIsLoading(false);
      return;
    }

    try {
      const profile = await authApi.getCurrentUser();
      if (tokenManager.getSessionGeneration() === gen) {
        setUser(profile);
      }
    } catch (err) {
      if (tokenManager.getSessionGeneration() === gen) {
        tokenManager.clearSession();
        setUser(null);
      }
    } finally {
      if (tokenManager.getSessionGeneration() === gen || !tokenManager.getAccessToken()) {
        setIsLoading(false);
      }
    }
  }, []);

  useEffect(() => {
    fetchProfile();

    const unsubscribe = tokenManager.subscribeAuth((isAuth) => {
      if (!isAuth) {
        setUser(null);
        setIsLoading(false);
      }
    });

    return () => unsubscribe();
  }, [fetchProfile]);

  const login = async (request: LoginRequest) => {
    setIsLoading(true);
    const gen = tokenManager.getSessionGeneration();
    try {
      const authResponse = await authApi.login(request);
      if (tokenManager.getSessionGeneration() !== gen) return;
      tokenManager.setTokens(authResponse.accessToken, authResponse.refreshToken);
      const profile = await authApi.getCurrentUser();
      if (tokenManager.getSessionGeneration() !== gen) return;
      schemaService.clearCache();
      setUser(profile);
    } finally {
      if (tokenManager.getSessionGeneration() === gen) {
        setIsLoading(false);
      }
    }
  };

  const logout = () => {
    requestManager.abortAll();
    tokenManager.clearSession();
    schemaService.clearCache();
    setUser(null);
    setIsLoading(false);
  };

  const hasRole = (role: string): boolean => {
    if (!user || !Array.isArray(user.roles)) return false;
    return user.roles.some((r) => r.toLowerCase() === role.toLowerCase());
  };

  const normalizedUserType = user ? normalizeUserTypeStrict(user.userType) : null;
  const isStaff = normalizedUserType === UserType.Staff;
  const isClient = normalizedUserType === UserType.Client;
  const isDriver = normalizedUserType === UserType.Driver;
  // Trusted Staff identity + SuperAdmin role required (matches backend CurrentUserContext.IsSuperAdmin)
  const isSuperAdmin = Boolean(isStaff && hasRole('SuperAdmin'));

  const hasPermission = (permission: string): boolean => {
    if (!user) return false;
    if (isSuperAdmin) return true;
    if (!Array.isArray(user.permissions)) return false;
    return user.permissions.some((p) => p.toLowerCase() === permission.toLowerCase());
  };

  const hasAnyPermission = (permissions: string[]): boolean => {
    if (!user) return false;
    if (isSuperAdmin) return true;
    return permissions.some((p) => hasPermission(p));
  };

  return (
    <AuthContext.Provider
      value={{
        user,
        isAuthenticated: !!user,
        isLoading,
        isStaff,
        isClient,
        isDriver,
        isSuperAdmin,
        login,
        logout,
        hasPermission,
        hasAnyPermission,
        hasRole,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
};

export const useAuth = (): AuthContextType => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
};
