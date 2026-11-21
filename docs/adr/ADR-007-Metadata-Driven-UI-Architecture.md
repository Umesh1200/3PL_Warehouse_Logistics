# ADR-007: Frappe-Inspired Metadata-Driven Frontend Architecture

**Status:** Accepted  
**Date:** 2026-11-21  
**Context:** Nexus3PL Enterprise UI/UX Workstream  
**Verified Baseline:** `0bfcf90`  

---

## 1. Context and Problem Statement

The Nexus3PL frontend has reached complete functional coverage across core modules (Inbound Goods Receiving, Dual-Scan Directed Putaway, Real-Time Inventory Ledger, Administration & RBAC). However, creating dedicated, hardcoded React views for every standard CRUD master entity (Clients, Facilities, Users, Roles, Items, Suppliers, Carriers) leads to maintenance overhead, code duplication, and tight coupling between database attributes and UI components.

We need a flexible, metadata-driven architecture inspired by Frappe/ERPNext principles to define lists, forms, validation, and permissions through authoritative declarative schemas, while preserving specialized hand-crafted React components for high-velocity physical execution workflows (e.g., floor barcode scanning, dual-scan putaway, RF terminal).

---

## 2. Decision

We adopt a **hybrid metadata-driven architecture** consisting of:
1. Version-controlled JSON EntityDefinition schemas served authoritatively by a backend schema provider.
2. An allowlisted `lucide-react` icon system (`AppIcon`) with typed icon keys and zero arbitrary runtime code execution.
3. Reusable dynamic renderers: `DynamicList` (sorting, filtering, pagination, responsive table/card views) and `DynamicForm` (layout grids, field types, validation, required/read-only enforcement).
4. Controlled extension points allowing complex execution workflows to mount custom specialized widgets.

---

## 3. Architecture Specification

### 3.1 Versioned EntityDefinition Schemas
Each entity is represented by an authoritative schema definition containing:
- `name`: Unique entity identifier (e.g. `"Client"`).
- `version`: Semantic version string (e.g. `"1.0.0"`).
- `title` & `pluralTitle`: Human-readable localized entity labels.
- `icon`: Typed allowlisted icon name from the icon registry.
- `primaryKey`: Identity field (e.g. `"id"`).
- `endpoints`: Authoritative REST endpoint map (`list`, `get`, `create`, `update`, `delete`).
- `permissions`: Required RBAC permission strings for CRUD actions.
- `fields`: Strongly-typed `FieldDefinition` array.
- `views`: Definitions for `listView` and `formView`.
- `actions`: Schema-based actions (`create`, `edit`, `deactivate`, `refresh`).

### 3.2 FieldDefinition and Field-Type Registry
A typed field-type registry supports:
- `string`: Standard text input.
- `text`: Multiline text area.
- `code`: Monospace alphanumeric input with pattern validation (e.g. `^[A-Z0-9_\-]+$`).
- `integer` & `decimal`: Right-aligned numeric inputs with `tabular-nums`.
- `boolean`: Accessible checkbox/switch.
- `select`: Enumerated dropdown with defined options.
- `badge`: Categorical visual indicator with semantic color tokens.
- `currency`: ISO 4217 3-letter currency code selector.
- `email` & `phone`: Specialized formatted text inputs.
- `date` & `datetime`: Standardized ISO-8601 temporal inputs.

### 3.3 ListView and FormView Metadata
- **ListView**:
  - `columns`: Ordered list of visible fields, responsive priority, column alignment, width hints, and custom cell formatters.
  - `defaultSort`: Primary sort field and direction (`asc` / `desc`).
  - `filters`: Searchable and filterable fields with operator definitions (`contains`, `equals`).
  - `emptyState`: Icon key, title, description, and primary call-to-action button.
  - `pagination`: Page size choices (e.g., `[10, 25, 50]`).
- **FormView**:
  - `sections`: Visual field groupings with title, description, and grid column configurations (1 to 12 columns).
  - `fields`: Field placement within sections, column spans (`span: 6` for two-column, `span: 12` for full-width).
  - `stickyActions`: Bottom or top bar with Save/Cancel/Action buttons.

### 3.4 Schema-Based Actions and Navigation
Actions define:
- `id`: Action identifier (e.g. `"create"`, `"edit"`, `"deactivate"`).
- `label`: Display text.
- `icon`: Allowlisted icon key.
- `variant`: Button style (`primary`, `secondary`, `danger`, `outline`).
- `target`: Action context (`table-header`, `row-action`, `form-action`).
- `permission`: Required RBAC permission string.
- `workflowConstraint`: State guard (e.g. only show "Activate" if `status === 'Inactive'`).
- `confirmPrompt`: Optional confirmation dialog metadata for destructive actions.

### 3.5 EffectiveSchema Resolution
The backend exposes `GET /api/v1/schemas/{entityName}`.
Authoritative resolution on the server merges:
1. Base system schema (`BaseDefinition`).
2. Optional tenant-level configuration overlay.
3. User authorization pruning: fields or actions that the authenticated user lacks permission to access are pruned or marked `readOnly: true` before delivery to the frontend.
The client receives a clean, fully-resolved `EffectiveSchema` without needing complex client-side policy evaluation.

### 3.6 Existing RBAC Integration
Metadata attributes directly bind to existing backend permissions:
- `clients:view` &rarr; Can fetch schema, list clients, view client details.
- `clients:create` &rarr; Can see "+ New Client" action and submit create form.
- `clients:edit` &rarr; Can see "Edit" action and submit update form.
- `clients:delete` &rarr; Can see "Deactivate" action and issue delete command.
Field-level permissions enforce read-only or hidden states for restricted properties.

### 3.7 Tenant and Role Overrides and Precedence
- **Precedence Order:** `System Baseline Schema` &rarr; `Tenant Customization Overlay` &rarr; `Role-Based Pruning`.
- Handled strictly on the server side.
- No client-side tampering can bypass hidden or read-only constraints.

### 3.8 Server-Side Enforcement and Validation
- Client-side metadata interpretation is strictly for presentation, usability, and immediate UX feedback.
- All mutations are validated server-side by `FluentValidation` validators, `HasPermission` authorization filters, and MediatR transaction pipelines.
- Tampered payloads submitted by malicious clients are rejected at the WebApi boundary.

### 3.9 Schema Caching and Versioning
- Schemas carry semantic version strings (`1.0.0`).
- Frontend caches resolved schemas in memory and sessionStorage using `entityName + version + userRoleHash`.
- Cache invalidation occurs automatically when the backend increments the schema version or user session changes.

### 3.10 Controlled Extension Points for Complex Workflows
- Metadata supports a `customWidget` property on fields or views.
- When specified, the dynamic renderer delegates to a registered React component (e.g., DualScanPutawayWorkstation, BarcodeScannerPad).
- **Prohibited:** No `eval()`, no dynamic string execution, and no unvalidated script injection from tenant-provided schemas.

---

## 4. Consequences and Invariant Guarantees

### Positive:
- Drastically reduces boilerplate for administrative master tables.
- Changes to column order, labels, and validation rules can be made in version-controlled JSON schema files without altering React JSX code.
- Ensures uniform enterprise styling, iconography, and responsive accessibility across all screens.

### Preserved Invariants:
- All 1,296 backend unit/integration tests and existing frontend Vitest suites remain untouched and passing.
- Backend authorization boundaries, multi-tenant isolation, and database constraints remain 100% authoritative.
- No scaffold ZIP regeneration and no database schema migrations required for this initial metadata foundation.
