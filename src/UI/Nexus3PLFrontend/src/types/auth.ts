/**
 * Authoritative Auth, RBAC, and Administrative Type Definitions
 * Directly aligned with .NET 10 WebApi controllers and domain contracts.
 */

export enum UserType {
  Staff = 1,
  ClientUser = 2,
  Driver = 3,
}

export enum UserStatus {
  Active = 1,
  Suspended = 2,
  PendingActivation = 3,
}

export interface UserDto {
  id: string; // Guid
  email: string;
  firstName: string;
  lastName: string;
  userType: UserType;
  clientId: string | null;
  status: UserStatus;
  createdAt: string;
  roles: string[];
}

export interface AuthResponseDto {
  accessToken: string;
  refreshToken: string;
  tokenType: string;
  expiresIn: number;
  user: UserDto;
}

export interface CurrentUserDto {
  userId: string;
  email: string;
  firstName: string;
  lastName: string;
  userType: UserType;
  tenantId: string | null;
  companyName: string | null;
  roles: string[];
  permissions: string[];
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RefreshTokenRequest {
  accessToken: string;
  refreshToken: string;
}

export interface FacilityDto {
  id: string; // Guid
  facilityCode: string;
  name: string;
  addressLine1?: string | null;
  city?: string | null;
  timezone: string;
  isActive: boolean;
}

export enum AccessLevel {
  Read = 1,
  Write = 2,
  Admin = 3,
  Execute = 4,
}

export function formatAccessLevel(level: AccessLevel | number | string): string {
  if (level === 1 || level === 'Read') return 'Read';
  if (level === 2 || level === 'Write') return 'Write';
  if (level === 3 || level === 'Admin') return 'Admin';
  if (level === 4 || level === 'Execute') return 'Execute';
  return String(level);
}

export interface PermissionAssignmentDto {
  permissionKey: string;
  accessLevel: AccessLevel;
}

export interface RolePermissionDto {
  id: number;
  permissionKey: string;
  accessLevel: AccessLevel | number;
}

export interface RoleDto {
  id: number;
  roleName: string;
  name?: string; // fallback alias for tests
  description?: string | null;
  isSystemRole?: boolean;
  permissions: RolePermissionDto[];
}

export enum ClientStatus {
  Onboarding = 1,
  Active = 2,
  Suspended = 3,
  Terminated = 4,
}

export function formatClientStatus(status: ClientStatus | number | string | undefined | null): string {
  if (status === 1 || status === 'Onboarding') return 'Onboarding';
  if (status === 2 || status === 'Active') return 'Active';
  if (status === 3 || status === 'Suspended') return 'Suspended';
  if (status === 4 || status === 'Terminated') return 'Terminated';
  return status ? String(status) : '';
}

export enum SlaTier {
  Standard = 1,
  Silver = 2,
  Gold = 3,
  Platinum = 4,
}

export function formatSlaTier(tier: SlaTier | number | string | undefined | null): string {
  if (tier === 1 || tier === 'Standard') return 'Standard';
  if (tier === 2 || tier === 'Silver') return 'Silver';
  if (tier === 3 || tier === 'Gold') return 'Gold';
  if (tier === 4 || tier === 'Platinum') return 'Platinum';
  return tier ? String(tier) : '';
}

export interface ClientContactDto {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  phone?: string | null;
  isBillingContact: boolean;
}

export interface ClientDto {
  id: string; // Guid
  companyName: string;
  accountCode: string;
  status: ClientStatus | number;
  slaTier: SlaTier | number;
  currency: string;
  createdAt: string;
  activeUsersCount?: number;
  contacts?: ClientContactDto[];
  // Backwards compatibility properties if needed
  clientCode?: string;
  accountNumber?: string;
  billingCurrency?: string;
  serviceLevelAgreementTier?: string;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}
