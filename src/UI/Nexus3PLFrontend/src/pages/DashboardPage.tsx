import React, { useEffect, useState, useCallback, useRef } from 'react';
import { useAuth } from '../context/AuthContext';
import { useFacility } from '../context/FacilityContext';
import { ControlTowerHeader, TelemetryFreshnessStatus } from '../components/dashboard/ControlTowerHeader';
import { KpiMetricsGrid } from '../components/dashboard/KpiMetricsGrid';
import { WorkQueuesSection } from '../components/dashboard/WorkQueuesSection';
import { BottlenecksCard } from '../components/dashboard/BottlenecksCard';
import { LaunchpadGrid } from '../components/dashboard/LaunchpadGrid';
import { controlTowerApi } from '../services/controlTowerApi';
import { putawayApi } from '../services/putawayApi';
import { receivingApi } from '../services/receivingApi';
import {
  ControlTowerSummaryDto,
  FacilityBottleneckAnalysisDto,
  DockYardOperationalStatusDto,
} from '../types/controlTower';
import { PutawayTaskDto, ReceivingSessionDto, PutawayTaskStatus, ReceivingStatus } from '../types/inbound';

export const DashboardPage: React.FC = () => {
  const { user, hasPermission, hasAnyPermission } = useAuth();
  const { activeFacility } = useFacility();

  // Metrics telemetry state
  const [summary, setSummary] = useState<ControlTowerSummaryDto | null>(null);
  const [loadingMetrics, setLoadingMetrics] = useState<boolean>(true);
  const [metricsRestricted, setMetricsRestricted] = useState<boolean>(false);
  const [metricsError, setMetricsError] = useState<string | null>(null);

  // Independent auxiliary telemetry states (P-CT-04)
  const [bottlenecks, setBottlenecks] = useState<FacilityBottleneckAnalysisDto | null>(null);
  const [loadingBottlenecks, setLoadingBottlenecks] = useState<boolean>(false);
  const [bottlenecksError, setBottlenecksError] = useState<string | null>(null);

  const [dockYard, setDockYard] = useState<DockYardOperationalStatusDto | null>(null);
  const [loadingDockYard, setLoadingDockYard] = useState<boolean>(false);
  const [dockYardError, setDockYardError] = useState<string | null>(null);

  // Operational Queues state (P-CT-01 & P-CT-02)
  const [putawayTasks, setPutawayTasks] = useState<PutawayTaskDto[]>([]);
  const [putawayTotalCount, setPutawayTotalCount] = useState<number>(0);
  const [loadingPutaway, setLoadingPutaway] = useState<boolean>(true);
  const [putawayRestricted, setPutawayRestricted] = useState<boolean>(false);
  const [putawayError, setPutawayError] = useState<string | null>(null);

  const [receivingSessions, setReceivingSessions] = useState<ReceivingSessionDto[]>([]);
  const [receivingTotalCount, setReceivingTotalCount] = useState<number>(0);
  const [loadingReceiving, setLoadingReceiving] = useState<boolean>(true);
  const [receivingRestricted, setReceivingRestricted] = useState<boolean>(false);
  const [receivingError, setReceivingError] = useState<string | null>(null);

  // Telemetry freshness & auto-refresh (P-CT-03)
  const [lastSuccessfulSync, setLastSuccessfulSync] = useState<Date | null>(null);
  const [isRefreshing, setIsRefreshing] = useState<boolean>(false);
  const [autoRefresh, setAutoRefresh] = useState<boolean>(true);

  const inFlightRef = useRef<boolean>(false);
  const abortControllerRef = useRef<AbortController | null>(null);
  const currentFacilityRef = useRef<string | undefined>(activeFacility?.id);

  // Periodic clock ensures live-to-stale transition triggers even when auto-refresh polling is disabled
  const [clockNow, setClockNow] = useState<number>(() => Date.now());

  useEffect(() => {
    const timer = setInterval(() => {
      setClockNow(Date.now());
    }, 5000);
    return () => clearInterval(timer);
  }, []);

  useEffect(() => {
    currentFacilityRef.current = activeFacility?.id;
  }, [activeFacility?.id]);

  // Backend RBAC permissions
  const isSuperAdmin = user?.roles.some((r) => r.toLowerCase() === 'superadmin' || r.toLowerCase() === 'admin');
  const canViewAnalytics = hasPermission('analytics:control-tower:view') || isSuperAdmin;
  // Listing queues requires inbound:view per backend controller authorization
  const canListPutawayQueue = hasPermission('inbound:view') || isSuperAdmin;
  const canListReceivingQueue = hasPermission('inbound:view') || isSuperAdmin;

  // Execution privileges for floor operators strictly require execution permission or superadmin
  const canExecutePutaway =
    hasPermission('inbound:putaway:execute') ||
    isSuperAdmin;

  const canExecuteReceiving =
    hasPermission('inbound:receive') ||
    isSuperAdmin;

  // Immediate facility wipe upon facility change (P-CT-03) and cancel in-flight requests
  useEffect(() => {
    if (abortControllerRef.current) {
      abortControllerRef.current.abort();
    }
    setSummary(null);
    setBottlenecks(null);
    setDockYard(null);
    setPutawayTasks([]);
    setReceivingSessions([]);
    setPutawayTotalCount(0);
    setReceivingTotalCount(0);
    setLastSuccessfulSync(null);
    setMetricsError(null);
    setBottlenecksError(null);
    setDockYardError(null);
    setPutawayError(null);
    setReceivingError(null);
  }, [activeFacility?.id]);

  // Independent auxiliary fetchers with facility change race-condition protection
  const fetchBottlenecks = useCallback(async (signal?: AbortSignal) => {
    const facId = activeFacility?.id;
    if (!canViewAnalytics || !facId) {
      setBottlenecks(null);
      setLoadingBottlenecks(false);
      return;
    }
    setLoadingBottlenecks(true);
    setBottlenecksError(null);
    try {
      const bResult = await controlTowerApi.getBottlenecks(facId, undefined, signal);
      if (currentFacilityRef.current !== facId) return;
      setBottlenecks(bResult);
    } catch (err: any) {
      if (err.name === 'AbortError') return;
      if (currentFacilityRef.current !== facId) return;
      setBottlenecksError(err.message || 'Failed to load bottleneck analysis');
    } finally {
      if (currentFacilityRef.current === facId) {
        setLoadingBottlenecks(false);
      }
    }
  }, [canViewAnalytics, activeFacility?.id]);

  const fetchDockYard = useCallback(async (signal?: AbortSignal) => {
    const facId = activeFacility?.id;
    if (!canViewAnalytics || !facId) {
      setDockYard(null);
      setLoadingDockYard(false);
      return;
    }
    setLoadingDockYard(true);
    setDockYardError(null);
    try {
      const dResult = await controlTowerApi.getDockYardStatus(facId, undefined, signal);
      if (currentFacilityRef.current !== facId) return;
      setDockYard(dResult);
    } catch (err: any) {
      if (err.name === 'AbortError') return;
      if (currentFacilityRef.current !== facId) return;
      setDockYardError(err.message || 'Failed to load dock and yard telemetry');
    } finally {
      if (currentFacilityRef.current === facId) {
        setLoadingDockYard(false);
      }
    }
  }, [canViewAnalytics, activeFacility?.id]);

  const loadData = useCallback(async (isManualRefresh = false) => {
    if (inFlightRef.current && isManualRefresh) {
      if (abortControllerRef.current) {
        abortControllerRef.current.abort();
      }
    }
    inFlightRef.current = true;
    const ctrl = new AbortController();
    abortControllerRef.current = ctrl;

    if (isManualRefresh) {
      setIsRefreshing(true);
    } else {
      setLoadingMetrics(true);
      setLoadingPutaway(true);
      setLoadingReceiving(true);
    }

    setMetricsError(null);
    setPutawayError(null);
    setReceivingError(null);

    const facilityId = activeFacility?.id;

    // 1. Executive Metrics & Auxiliary Telemetry
    const fetchMetricsPromise = (async () => {
      if (!canViewAnalytics) {
        setMetricsRestricted(true);
        setSummary(null);
        setBottlenecks(null);
        setDockYard(null);
        setLoadingMetrics(false);
        setLoadingBottlenecks(false);
        setLoadingDockYard(false);
        return;
      }

      setMetricsRestricted(false);
      try {
        const sumResult = await controlTowerApi.getSummary(facilityId, undefined, ctrl.signal);
        setSummary(sumResult);
        // Only mark lastSuccessfulSync when backend successfully responds with HTTP 200
        setLastSuccessfulSync(new Date());

        // Launch auxiliary telemetry independently (P-CT-04)
        if (facilityId) {
          fetchBottlenecks(ctrl.signal);
          fetchDockYard(ctrl.signal);
        }
      } catch (err: any) {
        if (err.name === 'AbortError') return;
        if (err.status === 403) {
          setMetricsRestricted(true);
        } else {
          setMetricsError(err.message || 'Failed to load control tower metrics');
        }
      } finally {
        setLoadingMetrics(false);
      }
    })();

    // 2. Putaway Staging Queue (Filtered to Active Tasks)
    const fetchPutawayPromise = (async () => {
      if (!canListPutawayQueue) {
        setPutawayRestricted(true);
        setPutawayTasks([]);
        setPutawayTotalCount(0);
        setLoadingPutaway(false);
        return;
      }

      setPutawayRestricted(false);
      try {
        const pResult = await putawayApi.getTasks(
          {
            facilityId,
            statuses: [PutawayTaskStatus.Pending, PutawayTaskStatus.Assigned, PutawayTaskStatus.InTransit],
            pageNumber: 1,
            pageSize: 5,
          },
          ctrl.signal
        );
        setPutawayTasks(pResult.items || []);
        setPutawayTotalCount(pResult.totalCount ?? (pResult.items?.length || 0));
      } catch (err: any) {
        if (err.name === 'AbortError') return;
        if (err.status === 403) {
          setPutawayRestricted(true);
        } else {
          setPutawayError(err.message || 'Failed to load putaway queue');
        }
      } finally {
        setLoadingPutaway(false);
      }
    })();

    // 3. Receiving Sessions Queue (Filtered to Active Sessions)
    const fetchReceivingPromise = (async () => {
      if (!canListReceivingQueue) {
        setReceivingRestricted(true);
        setReceivingSessions([]);
        setReceivingTotalCount(0);
        setLoadingReceiving(false);
        return;
      }

      setReceivingRestricted(false);
      try {
        const rResult = await receivingApi.getSessions(
          {
            facilityId,
            statuses: [ReceivingStatus.Started, ReceivingStatus.InProgress, ReceivingStatus.Flagged],
            pageNumber: 1,
            pageSize: 5,
          },
          ctrl.signal
        );
        setReceivingSessions(rResult.items || []);
        setReceivingTotalCount(rResult.totalCount ?? (rResult.items?.length || 0));
      } catch (err: any) {
        if (err.name === 'AbortError') return;
        if (err.status === 403) {
          setReceivingRestricted(true);
        } else {
          setReceivingError(err.message || 'Failed to load receiving sessions');
        }
      } finally {
        setLoadingReceiving(false);
      }
    })();

    await Promise.allSettled([fetchMetricsPromise, fetchPutawayPromise, fetchReceivingPromise]);
    setIsRefreshing(false);
    inFlightRef.current = false;
  }, [activeFacility?.id, canViewAnalytics, canListPutawayQueue, canListReceivingQueue, fetchBottlenecks, fetchDockYard]);

  // Initial load
  useEffect(() => {
    loadData();
    return () => {
      if (abortControllerRef.current) {
        abortControllerRef.current.abort();
      }
    };
  }, [loadData]);

  // Bounded 60s auto-refresh polling with overlap and visibility protection (P-CT-03)
  useEffect(() => {
    if (!autoRefresh) return;

    const interval = setInterval(() => {
      if (document.hidden) return;
      if (inFlightRef.current) return;
      loadData(false);
    }, 60000);

    return () => clearInterval(interval);
  }, [autoRefresh, loadData]);

  const handleRefresh = () => {
    loadData(true);
  };

  // Compute telemetry freshness status (respects lastSuccessfulSync age & backend snapshot asOfUtc age)
  const FRESHNESS_THRESHOLD_MS = 120000; // 2 minutes sync age
  const BACKEND_SNAPSHOT_STALE_MS = 300000; // 5 minutes backend snapshot age

  const isSyncExpired = !lastSuccessfulSync || (clockNow - lastSuccessfulSync.getTime() > FRESHNESS_THRESHOLD_MS);
  const isBackendSnapshotStale = summary?.asOfUtc
    ? (clockNow - new Date(summary.asOfUtc).getTime() > BACKEND_SNAPSHOT_STALE_MS)
    : false;

  let telemetryStatus: TelemetryFreshnessStatus = 'live';
  if (metricsRestricted) {
    telemetryStatus = 'restricted';
  } else if (isRefreshing) {
    telemetryStatus = 'refreshing';
  } else if (metricsError) {
    telemetryStatus = 'error';
  } else if (isSyncExpired || isBackendSnapshotStale) {
    telemetryStatus = 'stale';
  } else {
    telemetryStatus = 'live';
  }

  return (
    <div className="control-tower" data-testid="control-tower-dashboard">
      {/* 1. Header & Live Clock / Freshness Context (P-CT-03) */}
      <ControlTowerHeader
        user={user}
        activeFacility={activeFacility}
        onRefresh={handleRefresh}
        isRefreshing={isRefreshing}
        lastSuccessfulSync={lastSuccessfulSync}
        backendAsOfUtc={summary?.asOfUtc || null}
        telemetryStatus={telemetryStatus}
        autoRefresh={autoRefresh}
        onToggleAutoRefresh={() => setAutoRefresh((prev) => !prev)}
      />

      {/* 2. Executive & Floor Throughput KPIs */}
      <KpiMetricsGrid
        summary={summary}
        isLoading={loadingMetrics}
        isRestricted={metricsRestricted}
        error={metricsError}
        onRetry={handleRefresh}
      />

      {/* 3. Operational Flow & Bottleneck Diagnostics (P-CT-04 Independent Loading & Errors) */}
      <BottlenecksCard
        bottlenecks={bottlenecks}
        loadingBottlenecks={loadingBottlenecks}
        bottlenecksError={bottlenecksError}
        dockYard={dockYard}
        loadingDockYard={loadingDockYard}
        dockYardError={dockYardError}
        isRestricted={metricsRestricted}
        onRetryBottlenecks={() => fetchBottlenecks()}
        onRetryDockYard={() => fetchDockYard()}
      />

      {/* 4. Active Floor Execution Queues (P-CT-01 & P-CT-02) */}
      <WorkQueuesSection
        putawayTasks={putawayTasks}
        putawayTotalCount={putawayTotalCount}
        loadingPutaway={loadingPutaway}
        putawayRestricted={putawayRestricted}
        putawayError={putawayError}
        canExecutePutaway={canExecutePutaway}
        receivingSessions={receivingSessions}
        receivingTotalCount={receivingTotalCount}
        loadingReceiving={loadingReceiving}
        receivingRestricted={receivingRestricted}
        receivingError={receivingError}
        canExecuteReceiving={canExecuteReceiving}
      />

      {/* 5. Enterprise Navigation Launchpad */}
      <LaunchpadGrid />
    </div>
  );
};
