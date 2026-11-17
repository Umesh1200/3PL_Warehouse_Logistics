import { useEffect, useState } from 'react';
import { request, ApiError } from '../services/transportationApi';
import type { AnalyticsRecord, Page } from '../types/transportation';
export function useReport(url: string | null, token: string, onExpired: () => void, revision = 0) {
  const [result, setResult] = useState<{ key: string; data?: AnalyticsRecord | Page; error?: string }>({ key: '' });
  const key = `${token}:${url}:${revision}`;
  useEffect(() => {
    if (!url || !token) return;
    const controller = new AbortController();
    request<AnalyticsRecord | Page>(url, token, controller.signal).then(data => {
      if (!controller.signal.aborted) setResult({ key, data });
    }).catch(error => {
      if (controller.signal.aborted) return;
      if (error instanceof ApiError && error.status === 401) onExpired();
      else setResult({ key, error: error.message });
    });
    return () => controller.abort();
  }, [url, token, key, onExpired]);
  return { data: result.key === key ? result.data : undefined, error: result.key === key ? result.error : undefined, loading: !!url && result.key !== key };
}
