/**
 * Authoritative Auth, RBAC, and Administrative Type Definitions
 * Directly aligned with .NET 10 WebApi controllers and domain contracts.
 */

export enum UserType {
  Staff = 'Staff',
  Client = 'Client',
  Driver = 'Driver',
}

export enum UserStatus {
  Active = 'Active',
  Inactive = 'Inactive',
  Locked = 'Locked',
  PendingActivation = 'PendingActivation',
}

export function normalizeUserType(val: any): UserType {
  if (!val) return UserType.Staff;
  const str = String(val).trim().toLowerCase();
  if (val === 1 || str === 'staff') return UserType.Staff;
  if (val === 2 || str === 'client' || str === 'clientuser') return UserType.Client;
  if (val === 3 || str === 'driver') return UserType.Driver;
  return UserType.Staff;
}

export function normalizeUserStatus(val: any): UserStatus {
  if (!val) return UserStatus.Active;
  const str = String(val).trim().toLowerCase();
  if (val === 1 || str === 'active') return UserStatus.Active;
  if (val === 2 || str === 'inactive' || str === 'suspended') return UserStatus.Inactive;
  if (val === 3 || str === 'locked') return UserStatus.Locked;
  if (val === 4 || str === 'pendingactivation' || str === 'pending') return UserStatus.PendingActivation;
  return UserStatus.Active;
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

export function normalizeAccessLevel(level: any): AccessLevel {
  if (!level) return AccessLevel.Read;
  if (typeof level === 'number') {
    if (level >= 1 && level <= 4) return level as AccessLevel;
    return AccessLevel.Read;
  }
  const str = String(level).trim().toLowerCase();
  if (str === 'read' || str === '1') return AccessLevel.Read;
  if (str === 'write' || str === '2') return AccessLevel.Write;
  if (str === 'admin' || str === '3') return AccessLevel.Admin;
  if (str === 'execute' || str === '4') return AccessLevel.Execute;
  return AccessLevel.Read;
}

export function formatAccessLevel(level: AccessLevel | number | string): string {
  const norm = normalizeAccessLevel(level);
  if (norm === AccessLevel.Read) return 'Read';
  if (norm === AccessLevel.Write) return 'Write';
  if (norm === AccessLevel.Admin) return 'Admin';
  if (norm === AccessLevel.Execute) return 'Execute';
  return 'Read';
}

export interface PermissionAssignmentDto {
  permissionKey: string;
  accessLevel: AccessLevel;
}

export interface RolePermissionDto {
  id: number;
  permissionKey: string;
  accessLevel: AccessLevel | number | string;
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
