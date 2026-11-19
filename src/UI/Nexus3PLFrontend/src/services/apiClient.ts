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
  private sessionGeneration: number = 0;

  constructor() {
    try {
      this.inMemoryAccessToken = localStorage.getItem(TOKEN_KEY);
      this.inMemoryRefreshToken = localStorage.getItem(REFRESH_KEY);
      this.inMemoryFacilityId = localStorage.getItem(FACILITY_KEY);
    } catch (_) {}
  }

  getSessionGeneration(): number {
    return this.sessionGeneration;
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
    this.sessionGeneration++;
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
 * Single-Flight Refresh Mutex and Late-401 Cache
 */
let singleFlightRefreshPromise: Promise<string> | null = null;
let lastRefreshedOldToken: string | null = null;
let lastRefreshedNewToken: string | null = null;

export async function executeSingleFlightRefresh(failedToken?: string | null): Promise<string> {
  const currentGen = tokenManager.getSessionGeneration();

  // Late-401 Deduplication Check:
  // If the failed token was ALREADY refreshed in the current session, return the new token immediately
  if (
    failedToken &&
    lastRefreshedOldToken === failedToken &&
    lastRefreshedNewToken &&
    tokenManager.getAccessToken() === lastRefreshedNewToken
  ) {
    return lastRefreshedNewToken;
  }

  if (singleFlightRefreshPromise) {
    return singleFlightRefreshPromise;
  }

  const currentAccessToken = tokenManager.getAccessToken();
  const currentRefreshToken = tokenManager.getRefreshToken();

  if (!currentAccessToken || !currentRefreshToken) {
    tokenManager.clearSession();
    throw new AuthExpiredError();
  }

  const refreshCtrl = new AbortController();
  const unregister = requestManager.register('session', refreshCtrl);

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
        signal: refreshCtrl.signal,
      });

      // Verify session generation has NOT changed during network turnaround (e.g. user logged out)
      if (tokenManager.getSessionGeneration() !== currentGen) {
        throw new AuthExpiredError();
      }

      if (!response.ok) {
        tokenManager.clearSession();
        throw new AuthExpiredError();
      }

      const data: AuthResponseDto = await response.json();

      // Check again after body parse:
      if (tokenManager.getSessionGeneration() !== currentGen) {
        throw new AuthExpiredError();
      }

      lastRefreshedOldToken = currentAccessToken;
      lastRefreshedNewToken = data.accessToken;

      tokenManager.setTokens(data.accessToken, data.refreshToken);
      return data.accessToken;
    } catch (err: any) {
      if (tokenManager.getSessionGeneration() === currentGen) {
        tokenManager.clearSession();
      }
      throw err instanceof ApiError ? err : new AuthExpiredError();
    } finally {
      unregister();
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
  const { scope: explicitScope, skipAuth = false, headers: customHeaders, ...fetchOptions } = options;

  // Default scope to 'route' for normal operational requests, unless explicitly overridden
  const scope = explicitScope !== undefined ? explicitScope : 'route';

  let cleanupRegistration: (() => void) | undefined;
  let activeCtrl: AbortController | undefined;

  if (scope && scope !== 'none') {
    activeCtrl = new AbortController();
    cleanupRegistration = requestManager.register(scope, activeCtrl);

    // If caller provided an external signal, propagate abort to our active controller
    if (fetchOptions.signal) {
      const externalSignal = fetchOptions.signal;
      if (externalSignal.aborted) {
        activeCtrl.abort((externalSignal as any).reason);
      } else {
        externalSignal.addEventListener('abort', () => activeCtrl?.abort((externalSignal as any).reason), {
          once: true,
        });
      }
    }

    fetchOptions.signal = activeCtrl.signal;
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

    // Check if aborted immediately after fetch returns
    if (activeCtrl?.signal.aborted) {
      throw new DOMException('Request aborted', 'AbortError');
    }

    // Handle 401 Unauthorized via Single-Flight Mutex
    if (
      response.status === 401 &&
      !skipAuth &&
      !endpoint.includes('/auth/login') &&
      !endpoint.includes('/auth/refresh-token')
    ) {
      const currentToken = tokenManager.getAccessToken();

      // Late-401 Check: if the access token in tokenManager already changed while this request was running,
      // a concurrent refresh has already succeeded! Replay immediately with the new token.
      if (token && currentToken && currentToken !== token) {
        response = await fetch(endpoint, {
          ...fetchOptions,
          headers: buildHeaders(currentToken),
        });
      } else {
        const newToken = await executeSingleFlightRefresh(token);
        if (activeCtrl?.signal.aborted) {
          throw new DOMException('Request aborted', 'AbortError');
        }
        // Replay original request with refreshed token
        response = await fetch(endpoint, {
          ...fetchOptions,
          headers: buildHeaders(newToken),
        });
      }
    }

    if (activeCtrl?.signal.aborted) {
      throw new DOMException('Request aborted', 'AbortError');
    }

    if (!response.ok) {
      let errorBody: any = null;
      try {
        errorBody = await response.json();
      } catch (_) {
        errorBody = await response.text();
      }
      throw new ApiError(
        response.status,
        errorBody?.detail || errorBody?.message || `HTTP ${response.status}: Request failed`,
        errorBody
      );
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
