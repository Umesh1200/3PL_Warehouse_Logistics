export interface User { email: string; firstName: string; roles: string[]; permissions: string[] }
export type AnalyticsRecord = Record<string, unknown>;
export interface Page { items: AnalyticsRecord[]; totalCount: number; pageNumber: number; totalPages: number }
export interface Filters { carrierCode: string; startDateUtc: string; endDateUtc: string; clientId: string; facilityId: string; serviceLevel: string; destinationCountry: string; destinationState: string; asOfUtc: string; statusFilter: string; minimumDiscrepancyUsd: string; shipmentId: string; claimType: string }
export const permissions = { performance: 'analytics:transportation:view', financial: 'analytics:transportation:financial:view', claims: 'analytics:transportation:claims:view' };
export function permitted(user: User, permission: string) { return user.roles.includes('SuperAdmin') || user.permissions.includes(permission); }
