import React, { createContext, useContext, useEffect, useState, useCallback } from 'react';
import { FacilityDto } from '../types/auth';
import { facilityApi } from '../services/facilityApi';
import { tokenManager, requestManager } from '../services/apiClient';
import { useAuth } from './AuthContext';

interface FacilityContextType {
  facilities: FacilityDto[];
  activeFacility: FacilityDto | null;
  isLoadingFacilities: boolean;
  setActiveFacility: (facility: FacilityDto) => void;
  refreshFacilities: () => Promise<void>;
}

const FacilityContext = createContext<FacilityContextType | null>(null);

export const FacilityProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const { isAuthenticated, user } = useAuth();
  const [facilities, setFacilities] = useState<FacilityDto[]>([]);
  const [activeFacility, setActiveFacilityState] = useState<FacilityDto | null>(null);
  const [isLoadingFacilities, setIsLoadingFacilities] = useState<boolean>(false);

  const refreshFacilities = useCallback(async () => {
    if (!isAuthenticated) {
      setFacilities([]);
      setActiveFacilityState(null);
      tokenManager.setActiveFacilityId(null);
      return;
    }

    setIsLoadingFacilities(true);
    try {
      const data = await facilityApi.getAuthorizedFacilities();
      setFacilities(data);

      const savedId = tokenManager.getActiveFacilityId();
      const matched = data.find((f) => f.id === savedId);

      if (matched) {
        setActiveFacilityState(matched);
      } else if (data.length > 0) {
        setActiveFacilityState(data[0]);
        tokenManager.setActiveFacilityId(data[0].id);
      } else {
        setActiveFacilityState(null);
        tokenManager.setActiveFacilityId(null);
      }
    } catch (_) {
      setFacilities([]);
      setActiveFacilityState(null);
      tokenManager.setActiveFacilityId(null);
    } finally {
      setIsLoadingFacilities(false);
    }
  }, [isAuthenticated]);

  useEffect(() => {
    refreshFacilities();
  }, [refreshFacilities, user?.userId]);

  const setActiveFacility = (facility: FacilityDto) => {
    requestManager.abortScope('facility');
    setActiveFacilityState(facility);
    tokenManager.setActiveFacilityId(facility.id);
  };

  return (
    <FacilityContext.Provider
      value={{
        facilities,
        activeFacility,
        isLoadingFacilities,
        setActiveFacility,
        refreshFacilities,
      }}
    >
      {children}
    </FacilityContext.Provider>
  );
};

export const useFacility = (): FacilityContextType => {
  const context = useContext(FacilityContext);
  if (!context) {
    throw new Error('useFacility must be used within a FacilityProvider');
  }
  return context;
};
