# 3PL Warehouse, Logistics, Courier & Algorithm Engine
## Phase 1 — Core Infrastructure Architecture Specification (DB-SPEC-3PL-2026)

Enterprise multi-tenant backend architecture built on **ASP.NET Core 10 LTS**, **Clean Architecture**, **CQRS with MediatR**, **PostgreSQL with EF Core 10**, **FluentValidation**, **Mapster**, **Serilog**, **OpenTelemetry**, and **Tenant-Aware RBAC**.

---

### 1. Solution Architecture & Folder Structure

```
c:\Code\3PL_Warehouse_Logistics/
│
├── src/
│   ├── Core/
│   │   ├── Warehouse3PL.Domain/            # Pure Enterprise Domain Layer (Zero Framework Dependencies)
│   │   │   ├── Common/                     # BaseEntity, AuditableEntity, ISoftDeletable, ITenantEntity, Result
│   │   │   ├── Constants/                  # SystemRoles, Fine-Grained Permissions Catalog
│   │   │   ├── Entities/
│   │   │   │   ├── Security/               # Client (Root Tenant), ClientContact, User, Role, RolePermission, UserRole, ClientFacilityAccess
│   │   │   │   └── Facilities/             # Facility anchor
│   │   │   ├── Enums/                      # UserType, ClientStatus, SlaTier, UserStatus, AccessLevel
│   │   │   └── Exceptions/                 # DomainException, EntityNotFoundException, ValidationException, UnauthorizedException, ForbiddenException
│   │   │
│   │   └── Warehouse3PL.Application/       # Application & Business Logic (CQRS + MediatR)
│   │       ├── Common/
│   │       │   ├── Behaviors/              # ValidationBehavior, LoggingBehavior, PerformanceBehavior
│   │       │   ├── Interfaces/             # IApplicationDbContext, ICurrentUserContext, ITenantProvider, ITokenService, ICacheService, IPasswordHasher
│   │       │   ├── Mappings/               # MapsterConfig mappings
│   │       │   └── Models/                 # DTOs, PagedResult, PagedQuery, AuthResponseDto
│   │       └── Features/                   # CQRS Vertical Slices
│   │           ├── Auth/                   # Login, Register, RefreshToken, GetCurrentUser
│   │           ├── Clients/                # CreateClient, UpdateClient, DeactivateClient, GetClients, GetClientById
│   │           ├── Users/                  # CreateUser, AssignUserRoles, GetUsers, GetUserById
│   │           └── Roles/                  # GetRoles, GetPermissions, CreateRole, AssignRolePermissions
│   │
│   ├── Infrastructure/
│   │   └── Warehouse3PL.Infrastructure/    # Infrastructure & External Systems
│   │       ├── Authentication/             # JwtTokenService, CurrentUserContext, TenantProvider, PasswordHasher, JwtOptions
│   │       ├── Authorization/              # PermissionRequirement, PermissionAuthorizationHandler, HasPermissionAttribute, PermissionPolicyProvider
│   │       ├── BackgroundJobs/             # Hangfire extensions & PostgreSQL storage
│   │       ├── Caching/                    # RedisCacheService (with resilient in-memory fallback)
│   │       ├── Logging/                    # Serilog log enrichers (TenantId, UserId, CorrelationId)
│   │       ├── Persistence/
│   │       │   ├── Configurations/         # EF Core Fluent Configurations (snake_case, B-Tree indexes, UUID/BIGINT keys)
│   │       │   ├── Interceptors/           # AuditableEntityInterceptor & Soft-Delete interceptor
│   │       │   ├── Seed/                   # DbInitializer (automatic seeding of roles, users, tenant, facility)
│   │       │   └── ApplicationDbContext.cs # EF Core DbContext with Global Multi-Tenancy & Soft-Delete Filters
│   │       └── Services/                   # DateTimeProvider
│   │
│   └── Presentation/
│       └── Warehouse3PL.WebApi/            # REST API Gateway & Observability
│           ├── Controllers/                # AuthController, ClientsController, UsersController, RolesController, HealthController
│           ├── Extensions/                 # SwaggerExtensions, OpenTelemetryExtensions, SerilogExtensions
│           ├── Middleware/                 # ExceptionHandlingMiddleware (RFC 7807), TenantResolutionMiddleware
│           ├── Program.cs                  # Startup configuration & pipeline orchestration
│           └── appsettings.json            # Configuration settings
│
├── tests/
│   └── Warehouse3PL.UnitTests/             # xUnit Test Suite
│       ├── Authentication/                 # PasswordHasherTests, JwtTokenServiceTests
│       └── Validation/                     # ValidationBehaviorTests
│
├── docker-compose.yml                      # PostgreSQL 16, Redis 7, Keycloak 24
└── Warehouse3PL.sln                        # .NET 10 Solution
```

---

### 2. Multi-Tenancy & Security Model

#### **Multi-Tenancy Anchor (`client_id`)**
- Root Tenant: `clients` table (`client_id` UUID v4).
- Automatic EF Core Global Query Filter:
  ```csharp
  // Non-staff queries are automatically constrained to their assigned client tenant
  e => (_tenantProvider.BypassTenantFilter || _currentUser.IsStaff) || e.ClientId == _tenantProvider.TenantId
  ```
- Cross-tenant breach prevention: `TenantResolutionMiddleware` blocks client users attempting to spoof another tenant via `X-Tenant-Id`.

#### **Seeded Test Accounts**
| User | Email | Password | User Type | Roles | Tenant Scope |
|---|---|---|---|---|---|
| **Platform SuperAdmin** | `admin@3pllogistics.com` | `Admin123!` | `Staff` | `SuperAdmin` | Platform-wide (all tenants) |
| **Client Administrator** | `client.admin@apexretail.com` | `Client123!` | `Client` | `ClientAdmin` | Scoped to `Apex Retail Distribution Ltd` |

---

### 3. Quick Start & Testing with Swagger

#### Step 1: Start PostgreSQL & Redis (Optional Docker)
```powershell
docker compose up -d postgres redis
```
*(Note: If PostgreSQL is not running locally, the API will still run, allowing full inspection of endpoints, models, and swagger documentation).*

#### Step 2: Run the API
```powershell
dotnet run --project src/Presentation/Warehouse3PL.WebApi
```

#### Step 3: Open Swagger UI
Navigate in your browser to:
**`http://localhost:5000`** (or `https://localhost:5001`)

1. Call **`POST /api/v1/auth/login`** with:
   ```json
   {
     "email": "admin@3pllogistics.com",
     "password": "Admin123!"
   }
   ```
2. Copy the `accessToken` from the response.
3. Click the **Authorize 🔓** button in the top right of the Swagger UI and enter:
   ```
   Bearer <your_copied_token>
   ```
4. Test tenant operations:
   - `GET /api/v1/auth/me`
   - `GET /api/v1/clients`
   - `POST /api/v1/clients`
   - `GET /api/v1/users`
   - `GET /api/v1/roles`
   - `GET /api/v1/roles/permissions`
   - `GET /health`

---

### 4. Running Automated Tests

```powershell
dotnet test
```
Outputs:
`Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5, Duration: ~3s`

