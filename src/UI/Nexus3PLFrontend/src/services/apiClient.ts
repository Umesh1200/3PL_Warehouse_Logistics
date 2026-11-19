import { AuthResponseDto, RefreshTokenRequest } from '../types/auth';

const TOKEN_KEY = 'nexus_access_token';
const REFRESH_KEY = 'nexus_refresh_token';
const FACILITY_KEY = 'nexus_active_facility_id';

export class ApiError extends Error {
  status: number;
  data: any;

  constructor(status: number, message: string, data?: any) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.data = data;
  }
}

export class AuthExpiredError extends ApiError {
  constructor() {
    super(401, 'Authentication session has expired');
    this.name = 'AuthExpiredError';
  }
}

/**
 * Centralized Request Cancellation Manager
 * Allows aborting active fetch queries on route transitions, facility switching, or logout.
 */
class RequestCancellationManager {
  private controllers: Map<string, Set<AbortController>> = new Map();

  register(scope: string, controller: AbortController): () => void {
    if (!this.controllers.has(scope)) {
      this.controllers.set(scope, new Set());
    }
    const set = this.controllers.get(scope)!;
    set.add(controller);

    return () => {
      set.delete(controller);
    };
  }

  abortScope(scope: string) {
    const set = this.controllers.get(scope);
    if (set) {
      set.forEach((ctrl) => {
        try {
          ctrl.abort(`Scope ${scope} cancelled`);
        } catch (_) {}
      });
      set.clear();
    }
  }

  abortAll() {
    this.controllers.forEach((set, scope) => {
      set.forEach((ctrl) => {
        try {
          ctrl.abort('All requests aborted');
        } catch (_) {}
      });
      set.clear();
    });
    this.controllers.clear();
  }
}

export const requestManager = new RequestCancellationManager();

/**
 * Token and Session Manager
 */
class TokenManager {
  private inMemoryAccessToken: string | null = null;
  private inMemoryRefreshToken: string | null = null;
  private inMemoryFacilityId: string | null = null;
  private authStateListeners: Set<(isAuth: boolean) => void> = new Set();

  constructor() {
    try {
      this.inMemoryAccessToken = localStorage.getItem(TOKEN_KEY);
      this.inMemoryRefreshToken = localStorage.getItem(REFRESH_KEY);
      this.inMemoryFacilityId = localStorage.getItem(FACILITY_KEY);
    } catch (_) {}
  }

  getAccessToken(): string | null {
    return this.inMemoryAccessToken;
  }

  getRefreshToken(): string | null {
    return this.inMemoryRefreshToken;
  }

  getActiveFacilityId(): string | null {
    return this.inMemoryFacilityId;
  }

  setTokens(accessToken: string, refreshToken: string) {
    this.inMemoryAccessToken = accessToken;
    this.inMemoryRefreshToken = refreshToken;
    try {
      localStorage.setItem(TOKEN_KEY, accessToken);
      localStorage.setItem(REFRESH_KEY, refreshToken);
    } catch (_) {}
    this.notifyAuthListeners(true);
  }

  setActiveFacilityId(facilityId: string | null) {
    this.inMemoryFacilityId = facilityId;
    try {
      if (facilityId) {
        localStorage.setItem(FACILITY_KEY, facilityId);
      } else {
        localStorage.removeItem(FACILITY_KEY);
      }
    } catch (_) {}
  }

  clearSession() {
    this.inMemoryAccessToken = null;
    this.inMemoryRefreshToken = null;
    this.inMemoryFacilityId = null;
    try {
      localStorage.removeItem(TOKEN_KEY);
      localStorage.removeItem(REFRESH_KEY);
      localStorage.removeItem(FACILITY_KEY);
    } catch (_) {}
    requestManager.abortAll();
    this.notifyAuthListeners(false);
  }

  subscribeAuth(listener: (isAuth: boolean) => void): () => void {
    this.authStateListeners.add(listener);
    return () => this.authStateListeners.delete(listener);
  }

  private notifyAuthListeners(isAuth: boolean) {
    this.authStateListeners.forEach((fn) => {
      try {
        fn(isAuth);
      } catch (_) {}
    });
  }
}

export const tokenManager = new TokenManager();

/**
 * Single-Flight Refresh Mutex
 */
let singleFlightRefreshPromise: Promise<string> | null = null;

async function executeSingleFlightRefresh(): Promise<string> {
  if (singleFlightRefreshPromise) {
    return singleFlightRefreshPromise;
  }

  const currentAccessToken = tokenManager.getAccessToken();
  const currentRefreshToken = tokenManager.getRefreshToken();

  if (!currentAccessToken || !currentRefreshToken) {
    tokenManager.clearSession();
    throw new AuthExpiredError();
  }

  singleFlightRefreshPromise = (async () => {
    try {
      const response = await fetch('/api/v1/auth/refresh-token', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          accessToken: currentAccessToken,
          refreshToken: currentRefreshToken,
        } as RefreshTokenRequest),
      });

      if (!response.ok) {
        tokenManager.clearSession();
        throw new AuthExpiredError();
      }

      const data: AuthResponseDto = await response.json();
      tokenManager.setTokens(data.accessToken, data.refreshToken);
      return data.accessToken;
    } catch (err) {
      tokenManager.clearSession();
      throw err instanceof ApiError ? err : new AuthExpiredError();
    } finally {
      singleFlightRefreshPromise = null;
    }
  })();

  return singleFlightRefreshPromise;
}

export interface RequestOptions extends RequestInit {
  scope?: string;
  skipAuth?: boolean;
}

/**
 * Core Typed API Client
 */
export async function apiClient<T>(endpoint: string, options: RequestOptions = {}): Promise<T> {
  const { scope, skipAuth = false, headers: customHeaders, ...fetchOptions } = options;

  let cleanupRegistration: (() => void) | undefined;
  if (scope) {
    const ctrl = new AbortController();
    fetchOptions.signal = ctrl.signal;
    cleanupRegistration = requestManager.register(scope, ctrl);
  }

  const buildHeaders = (token: string | null): HeadersInit => {
    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
      Accept: 'application/json',
    };

    if (!skipAuth && token) {
      headers['Authorization'] = `Bearer ${token}`;
    }

    const facilityId = tokenManager.getActiveFacilityId();
    if (facilityId) {
      headers['X-Facility-Id'] = facilityId;
    }

    return { ...headers, ...(customHeaders as Record<string, string>) };
  };

  try {
    let token = tokenManager.getAccessToken();
    let response = await fetch(endpoint, {
      ...fetchOptions,
      headers: buildHeaders(token),
    });

    // Handle 401 Unauthorized via Single-Flight Mutex
    if (response.status === 401 && !skipAuth && !endpoint.includes('/auth/login') && !endpoint.includes('/auth/refresh-token')) {
      const newToken = await executeSingleFlightRefresh();
      // Replay original request with refreshed token
      response = await fetch(endpoint, {
        ...fetchOptions,
        headers: buildHeaders(newToken),
      });
    }

    if (!response.ok) {
      let errorBody: any = null;
      try {
        errorBody = await response.json();
      } catch (_) {
        errorBody = await response.text();
      }
      throw new ApiError(response.status, errorBody?.detail || errorBody?.message || `HTTP ${response.status}: Request failed`, errorBody);
    }

    if (response.status === 204) {
      return null as T;
    }

    return await response.json();
  } finally {
    if (cleanupRegistration) {
      cleanupRegistration();
    }
  }
}
