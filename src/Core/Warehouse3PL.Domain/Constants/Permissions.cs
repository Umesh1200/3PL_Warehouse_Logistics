namespace Warehouse3PL.Domain.Constants;

public static class Permissions
{
    public static class Clients
    {
        public const string View = "clients:view";
        public const string Create = "clients:create";
        public const string Edit = "clients:edit";
        public const string Delete = "clients:delete";
        public const string ManageAccess = "clients:manage-access";
    }

    public static class Users
    {
        public const string View = "users:view";
        public const string Create = "users:create";
        public const string Edit = "users:edit";
        public const string Delete = "users:delete";
        public const string AssignRoles = "users:assign-roles";
    }

    public static class Roles
    {
        public const string View = "roles:view";
        public const string Create = "roles:create";
        public const string Edit = "roles:edit";
        public const string Delete = "roles:delete";
        public const string ManagePermissions = "roles:manage-permissions";
    }

    public static class Facilities
    {
        public const string View = "facilities:view";
        public const string Manage = "facilities:manage";
        public const string DockDoorsView = "facilities:dock-doors:view";
        public const string DockDoorsManage = "facilities:dock-doors:manage";
    }

    public static class Items
    {
        public const string View = "items:view";
        public const string Create = "items:create";
        public const string Edit = "items:edit";
        public const string Delete = "items:delete";
    }

    public static class Inventory
    {
        public const string View = "inventory:view";
        public const string Adjust = "inventory:adjust";
        public const string Count = "inventory:count";
        public const string Transfer = "inventory:transfer";
    }

    public static class Inbound
    {
        public const string View = "inbound:view";
        public const string Receive = "inbound:receive";
        public const string Putaway = "inbound:putaway";
        public const string PutawayManage = "inbound:putaway:manage";
        public const string PutawayExecute = "inbound:putaway:execute";
        public const string PutawayRules = "inbound:putaway:rules";
        public const string PutawayOverride = "inbound:putaway:override";
        public const string Approve = "inbound:receive:approve";
        public const string DiscrepancyResolve = "inbound:discrepancy:resolve";

        public const string OrdersView = "inbound:orders:view";
        public const string OrdersCreate = "inbound:orders:create";
        public const string OrdersEdit = "inbound:orders:edit";
        public const string OrdersCancel = "inbound:orders:cancel";

        public const string AsnView = "inbound:asn:view";
        public const string AsnCreate = "inbound:asn:create";
        public const string AsnEdit = "inbound:asn:edit";
        public const string AsnCancel = "inbound:asn:cancel";

        public const string SuppliersView = "inbound:suppliers:view";
        public const string SuppliersCreate = "inbound:suppliers:create";
        public const string SuppliersEdit = "inbound:suppliers:edit";

        public const string AppointmentsView = "inbound:appointments:view";
        public const string AppointmentsSchedule = "inbound:appointments:schedule";
        public const string AppointmentsCancel = "inbound:appointments:cancel";

        public const string YardView = "inbound:yard:view";
        public const string YardCheckIn = "inbound:yard:check-in";
        public const string DockAssign = "inbound:dock:assign";
        public const string DockRelease = "inbound:dock:release";
        public const string QualityInspect = "inbound:inspections:decision";

        public const string CrossDockView = "inbound:cross-dock:view";
        public const string CrossDockPlan = "inbound:cross-dock:plan";
        public const string CrossDockExecute = "inbound:cross-dock:execute";
    }

    public static class Outbound
    {
        public const string View = "outbound:view";
        public const string Create = "outbound:create";
        public const string Pick = "outbound:pick";
        public const string Pack = "outbound:pack";
        public const string Ship = "outbound:ship";

        public const string OrdersView = "outbound:orders:view";
        public const string OrdersCreate = "outbound:orders:create";
        public const string OrdersImport = "outbound:orders:import";
        public const string OrdersManage = "outbound:orders:manage";
        public const string OrdersAllocate = "outbound:orders:allocate";
        public const string OrdersCancel = "outbound:orders:cancel";

        public const string WavesView = "outbound:waves:view";
        public const string WavesManage = "outbound:waves:manage";
        public const string TasksView = "outbound:tasks:view";
        public const string TasksManage = "outbound:tasks:manage";
    }

    public static class Billing
    {
        public const string View = "billing:view";
        public const string ManageRates = "billing:manage-rates";
        public const string GenerateInvoices = "billing:generate-invoices";
    }

    public static class Dispatch
    {
        public const string View = "dispatch:view";
        public const string ManageRuns = "dispatch:manage-runs";
        public const string ExecuteDelivery = "dispatch:execute-delivery";
    }

    public static class Rules
    {
        public const string View = "rules:view";
        public const string Manage = "rules:manage";
        public const string Execute = "rules:execute";
    }

    public static class Analytics
    {
        public const string ControlTowerView = "analytics:control-tower:view";
        public const string MultiFacilityView = "analytics:multi-facility:view";
        public const string ProductivityView = "analytics:productivity:view";
        public const string ProfitabilityView = "analytics:profitability:view";
        public const string SlaView = "analytics:sla:view";
        public const string WorkforceView = "analytics:workforce:view";
        public const string WorkforceManageStandards = "analytics:workforce:manage-standards";
        public const string WorkforceSelfView = "analytics:workforce:self-view";
        public const string WorkforceCostView = "analytics:workforce:cost-view";
        public const string InventoryHealthView = "analytics:inventory-health:view";
        public const string InventoryAgingManage = "analytics:inventory-aging:manage";
        public const string AbcPolicyManage = "analytics:abc-policy:manage";
        public const string WarehouseHeatmapView = "analytics:warehouse-heatmap:view";
        public const string SlottingView = "analytics:slotting:view";
        public const string SlottingManage = "analytics:slotting:manage";
        public const string DecisionLaborView = "analytics:decisions:labor:view";
        public const string DecisionTransportationView = "analytics:decisions:transportation:view";
        public const string MonitoringView = "analytics:monitoring:view";
        public const string MonitoringManage = "analytics:monitoring:manage";
        public const string TransportationView = "analytics:transportation:view";
        public const string TransportationClaimsView = "analytics:transportation:claims:view";
        public const string TransportationFinancialView = "analytics:transportation:financial:view";
    }

    public static class Global
    {
        public const string ReferenceView = "global:reference:view";
        public const string ComplianceView = "global:compliance:view";
        public const string ComplianceManage = "global:compliance:manage";
        public const string ViewLegalEntities = "global:legal-entities:view";
        public const string ManageLegalEntities = "global:legal-entities:manage";
    }

    /// <summary>
    /// Returns all registered permission strings in the system.
    /// </summary>
    public static IReadOnlyList<string> GetAllPermissions() =>
    [
        Clients.View, Clients.Create, Clients.Edit, Clients.Delete, Clients.ManageAccess,
        Users.View, Users.Create, Users.Edit, Users.Delete, Users.AssignRoles,
        Roles.View, Roles.Create, Roles.Edit, Roles.Delete, Roles.ManagePermissions,
        Facilities.View, Facilities.Manage, Facilities.DockDoorsView, Facilities.DockDoorsManage,
        Items.View, Items.Create, Items.Edit, Items.Delete,
        Inventory.View, Inventory.Adjust, Inventory.Count, Inventory.Transfer,
        Inbound.View, Inbound.Receive, Inbound.Putaway, Inbound.PutawayManage, Inbound.PutawayExecute, Inbound.PutawayRules,
        Inbound.PutawayOverride, Inbound.Approve, Inbound.DiscrepancyResolve,
        Inbound.OrdersView, Inbound.OrdersCreate, Inbound.OrdersEdit, Inbound.OrdersCancel,
        Inbound.AsnView, Inbound.AsnCreate, Inbound.AsnEdit, Inbound.AsnCancel,
        Inbound.SuppliersView, Inbound.SuppliersCreate, Inbound.SuppliersEdit,
        Inbound.AppointmentsView, Inbound.AppointmentsSchedule, Inbound.AppointmentsCancel,
        Inbound.YardView, Inbound.YardCheckIn, Inbound.DockAssign, Inbound.DockRelease,
        Inbound.QualityInspect,
        Inbound.CrossDockView, Inbound.CrossDockPlan, Inbound.CrossDockExecute,
        Outbound.View, Outbound.Create, Outbound.Pick, Outbound.Pack, Outbound.Ship,
        Outbound.OrdersView, Outbound.OrdersCreate, Outbound.OrdersImport, Outbound.OrdersManage, Outbound.OrdersAllocate, Outbound.OrdersCancel,
        Outbound.WavesView, Outbound.WavesManage, Outbound.TasksView, Outbound.TasksManage,
        Billing.View, Billing.ManageRates, Billing.GenerateInvoices,
        Dispatch.View, Dispatch.ManageRuns, Dispatch.ExecuteDelivery,
        Rules.View, Rules.Manage, Rules.Execute,
        Analytics.ControlTowerView, Analytics.MultiFacilityView, Analytics.ProductivityView, Analytics.ProfitabilityView, Analytics.SlaView,
        Analytics.WorkforceView, Analytics.WorkforceManageStandards, Analytics.WorkforceSelfView, Analytics.WorkforceCostView,
        Analytics.InventoryHealthView, Analytics.InventoryAgingManage, Analytics.AbcPolicyManage, Analytics.WarehouseHeatmapView,
        Analytics.SlottingView, Analytics.SlottingManage,
        Analytics.MonitoringView, Analytics.MonitoringManage, Analytics.DecisionLaborView, Analytics.DecisionTransportationView,
        Analytics.TransportationView, Analytics.TransportationClaimsView, Analytics.TransportationFinancialView,
        Global.ReferenceView, Global.ComplianceView, Global.ComplianceManage,
        Global.ViewLegalEntities, Global.ManageLegalEntities
    ];
}
