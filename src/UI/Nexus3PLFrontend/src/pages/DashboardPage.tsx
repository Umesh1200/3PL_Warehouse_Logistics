import React, { useEffect, useState, useCallback, useRef } from 'react';
import { useAuth } from '../context/AuthContext';
import { useFacility } from '../context/FacilityContext';
import { ControlTowerHeader } from '../components/dashboard/ControlTowerHeader';
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
import { PutawayTaskDto, ReceivingSessionDto } from '../types/inbound';

export const DashboardPage: React.FC = () => {
  const { user, hasPermission, hasAnyPermission } = useAuth();
  const { activeFacility } = useFacility();

  // Metrics telemetry state
  const [summary, setSummary] = useState<ControlTowerSummaryDto | null>(null);
  const [bottlenecks, setBottlenecks] = useState<FacilityBottleneckAnalysisDto | null>(null);
  const [dockYard, setDockYard] = useState<DockYardOperationalStatusDto | null>(null);
  const [loadingMetrics, setLoadingMetrics] = useState<boolean>(true);
  const [metricsRestricted, setMetricsRestricted] = useState<boolean>(false);
  const [metricsError, setMetricsError] = useState<string | null>(null);

  // Operational Queues state
  const [putawayTasks, setPutawayTasks] = useState<PutawayTaskDto[]>([]);
  const [loadingPutaway, setLoadingPutaway] = useState<boolean>(true);
  const [putawayRestricted, setPutawayRestricted] = useState<boolean>(false);
  const [putawayError, setPutawayError] = useState<string | null>(null);

  const [receivingSessions, setReceivingSessions] = useState<ReceivingSessionDto[]>([]);
  const [loadingReceiving, setLoadingReceiving] = useState<boolean>(true);
  const [receivingRestricted, setReceivingRestricted] = useState<boolean>(false);
  const [receivingError, setReceivingError] = useState<string | null>(null);

  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);
  const [isRefreshing, setIsRefreshing] = useState<boolean>(false);

  const abortControllerRef = useRef<AbortController | null>(null);

  const canViewAnalytics = hasPermission('analytics:control-tower:view');
  const canViewPutaway = hasAnyPermission([
    'inbound:putaway',
    'inbound:putaway:execute',
    'inbound:putaway:manage',
    'inbound:view',
  ]);
  const canViewReceiving = hasAnyPermission([
    'inbound:view',
    'inbound:receive',
    'inbound:asn:view',
  ]);

  const loadData = useCallback(async (isManualRefresh = false) => {
    if (abortControllerRef.current) {
      abortControllerRef.current.abort();
    }
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

    // 1. Executive Metrics & Bottlenecks
    const fetchMetricsPromise = (async () => {
      if (!canViewAnalytics) {
        setMetricsRestricted(true);
        setSummary(null);
        setBottlenecks(null);
        setDockYard(null);
        setLoadingMetrics(false);
        return;
      }

      setMetricsRestricted(false);
      try {
        const sumResult = await controlTowerApi.getSummary(facilityId, undefined, ctrl.signal);
        setSummary(sumResult);

        // Fetch bottlenecks & dock status if facilityId is available
        if (facilityId) {
          try {
            const [bResult, dResult] = await Promise.all([
              controlTowerApi.getBottlenecks(facilityId, undefined, ctrl.signal).catch(() => null),
              controlTowerApi.getDockYardStatus(facilityId, undefined, ctrl.signal).catch(() => null),
            ]);
            setBottlenecks(bResult);
            setDockYard(dResult);
          } catch (_) {
            // Non-critical auxiliary telemetry
          }
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

    // 2. Putaway Staging Queue
    const fetchPutawayPromise = (async () => {
      if (!canViewPutaway) {
        setPutawayRestricted(true);
        setPutawayTasks([]);
        setLoadingPutaway(false);
        return;
      }

      setPutawayRestricted(false);
      try {
        const pResult = await putawayApi.getTasks(
          {
            facilityId,
            pageNumber: 1,
            pageSize: 5,
          },
          ctrl.signal
        );
        setPutawayTasks(pResult.items || []);
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

    // 3. Receiving Sessions Queue
    const fetchReceivingPromise = (async () => {
      if (!canViewReceiving) {
        setReceivingRestricted(true);
        setReceivingSessions([]);
        setLoadingReceiving(false);
        return;
      }

      setReceivingRestricted(false);
      try {
        const rResult = await receivingApi.getSessions(
          {
            facilityId,
            pageNumber: 1,
            pageSize: 5,
          },
          ctrl.signal
        );
        setReceivingSessions(rResult.items || []);
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
    setLastUpdated(new Date());
    setIsRefreshing(false);
  }, [activeFacility?.id, canViewAnalytics, canViewPutaway, canViewReceiving]);

  useEffect(() => {
    loadData();
    return () => {
      if (abortControllerRef.current) {
        abortControllerRef.current.abort();
      }
    };
  }, [loadData]);

  const handleRefresh = () => {
    loadData(true);
  };

  return (
    <div className="control-tower" data-testid="control-tower-dashboard">
      {/* 1. Header & Live Clock / Context */}
      <ControlTowerHeader
        user={user}
        activeFacility={activeFacility}
        onRefresh={handleRefresh}
        isRefreshing={isRefreshing}
        lastUpdated={lastUpdated}
      />

      {/* 2. Executive & Floor Throughput KPIs */}
      <KpiMetricsGrid
        summary={summary}
        isLoading={loadingMetrics}
        isRestricted={metricsRestricted}
        error={metricsError}
        onRetry={handleRefresh}
      />

      {/* 3. Operational Flow & Bottleneck Diagnostics */}
      <BottlenecksCard
        bottlenecks={bottlenecks}
        dockYard={dockYard}
        isLoading={loadingMetrics}
        isRestricted={metricsRestricted}
      />

      {/* 4. Active Floor Execution Queues */}
      <WorkQueuesSection
        putawayTasks={putawayTasks}
        loadingPutaway={loadingPutaway}
        putawayRestricted={putawayRestricted}
        putawayError={putawayError}
        receivingSessions={receivingSessions}
        loadingReceiving={loadingReceiving}
        receivingRestricted={receivingRestricted}
        receivingError={receivingError}
      />

      {/* 5. Enterprise Navigation Launchpad */}
      <LaunchpadGrid />
    </div>
  );
};
