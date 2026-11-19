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

export interface PermissionAssignmentDto {
  permissionKey: string;
  accessLevel: AccessLevel;
}

export interface RoleDto {
  id: number;
  name: string;
  description: string;
  permissions: string[];
}

export interface ClientDto {
  id: string; // Guid
  clientCode: string;
  companyName: string;
  accountNumber: string;
  status: string;
  billingCurrency: string;
  serviceLevelAgreementTier: string;
  createdAtUtc: string;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}
