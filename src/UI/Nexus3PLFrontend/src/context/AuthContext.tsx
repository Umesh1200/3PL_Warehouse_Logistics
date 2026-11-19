import React, { createContext, useContext, useEffect, useState, useCallback } from 'react';
import { authApi } from '../services/authApi';
import { tokenManager, requestManager } from '../services/apiClient';
import { CurrentUserDto, LoginRequest } from '../types/auth';

interface AuthContextType {
  user: CurrentUserDto | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (request: LoginRequest) => Promise<void>;
  logout: () => void;
  hasPermission: (permission: string) => boolean;
  hasAnyPermission: (permissions: string[]) => boolean;
  hasRole: (role: string) => boolean;
}

const AuthContext = createContext<AuthContextType | null>(null);

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [user, setUser] = useState<CurrentUserDto | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(true);

  const fetchProfile = useCallback(async () => {
    const token = tokenManager.getAccessToken();
    if (!token) {
      setUser(null);
      setIsLoading(false);
      return;
    }

    try {
      const profile = await authApi.getCurrentUser();
      setUser(profile);
    } catch (err) {
      tokenManager.clearSession();
      setUser(null);
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchProfile();

    const unsubscribe = tokenManager.subscribeAuth((isAuth) => {
      if (!isAuth) {
        setUser(null);
      }
    });

    return () => unsubscribe();
  }, [fetchProfile]);

  const login = async (request: LoginRequest) => {
    setIsLoading(true);
    try {
      const authResponse = await authApi.login(request);
      tokenManager.setTokens(authResponse.accessToken, authResponse.refreshToken);
      const profile = await authApi.getCurrentUser();
      setUser(profile);
    } finally {
      setIsLoading(false);
    }
  };

  const logout = () => {
    requestManager.abortAll();
    tokenManager.clearSession();
    setUser(null);
  };

  const hasRole = (role: string): boolean => {
    if (!user) return false;
    return user.roles.some((r) => r.toLowerCase() === role.toLowerCase());
  };

  const hasPermission = (permission: string): boolean => {
    if (!user) return false;
    if (hasRole('SuperAdmin')) return true;
    return user.permissions.some((p) => p.toLowerCase() === permission.toLowerCase());
  };

  const hasAnyPermission = (permissions: string[]): boolean => {
    if (!user) return false;
    if (hasRole('SuperAdmin')) return true;
    return permissions.some((p) => hasPermission(p));
  };

  return (
    <AuthContext.Provider
      value={{
        user,
        isAuthenticated: !!user,
        isLoading,
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
