using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "abc_classification_policies",
                columns: table => new
                {
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    criteria = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    class_a_cumulative_threshold_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    class_b_cumulative_threshold_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    observation_window_days = table.Column<int>(type: "integer", nullable: false),
                    observation_window = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    cost_valuation_basis = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    weight_acv = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    weight_pick_frequency = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    weight_unit_velocity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    weight_order_lines = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    effective_from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    effective_to_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_abc_classification_policies", x => x.policy_id);
                });

            migrationBuilder.CreateTable(
                name: "abc_classification_scope_versions",
                columns: table => new
                {
                    scope_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criteria = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    observation_window = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: true),
                    latest_version = table.Column<int>(type: "integer", nullable: false),
                    last_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_allocated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_abc_classification_scope_versions", x => x.scope_version_id);
                });

            migrationBuilder.CreateTable(
                name: "analytics_event_deduplications",
                columns: table => new
                {
                    deduplication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    projection_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    processed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analytics_event_deduplications", x => x.deduplication_id);
                });

            migrationBuilder.CreateTable(
                name: "carrier_contractual_sla_policies",
                columns: table => new
                {
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    carrier_service_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origin_facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origin_region = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    destination_country = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    destination_state_or_region = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    destination_postal_prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    transit_commitment_days = table.Column<int>(type: "integer", nullable: false),
                    calculation_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    daily_cutoff_time = table.Column<TimeSpan>(type: "interval", nullable: false),
                    cutoff_time_zone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    effective_from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    effective_to_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    sla_policy_version = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carrier_contractual_sla_policies", x => x.policy_id);
                });

            migrationBuilder.CreateTable(
                name: "carrier_exception_summaries",
                columns: table => new
                {
                    exception_summary_id = table.Column<Guid>(type: "uuid", nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    time_grain = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    exception_category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    total_exceptions_count = table.Column<int>(type: "integer", nullable: false),
                    resolved_delivered_count = table.Column<int>(type: "integer", nullable: false),
                    pending_resolution_count = table.Column<int>(type: "integer", nullable: false),
                    returned_to_origin_count = table.Column<int>(type: "integer", nullable: false),
                    lost_claim_recommended_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carrier_exception_summaries", x => x.exception_summary_id);
                });

            migrationBuilder.CreateTable(
                name: "carrier_freight_cost_summaries",
                columns: table => new
                {
                    summary_id = table.Column<Guid>(type: "uuid", nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    time_grain = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    total_shipments_quoted_count = table.Column<int>(type: "integer", nullable: false),
                    total_shipments_billed_count = table.Column<int>(type: "integer", nullable: false),
                    total_shipments_reconciled_count = table.Column<int>(type: "integer", nullable: false),
                    total_quoted_base_freight_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_quoted_accessorials_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_quoted_amount_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_estimated_amount_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_actual_base_freight_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_actual_accessorials_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_billed_amount_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    fuel_surcharge_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    residential_surcharge_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    liftgate_fee_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    address_correction_fee_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    other_adjustments_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    estimated_variance_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    billed_variance_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reconciled_variance_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    accessorial_density_ratio = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carrier_freight_cost_summaries", x => x.summary_id);
                });

            migrationBuilder.CreateTable(
                name: "carrier_lost_shipment_alerts",
                columns: table => new
                {
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    carrier_service_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispatched_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expected_delivery_date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_tracking_event_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_known_status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_known_location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    hours_without_scan_movement = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    policy_threshold_hours = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    trigger_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    triggered_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    alert_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    sla_policy_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    resolution_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carrier_lost_shipment_alerts", x => x.alert_id);
                });

            migrationBuilder.CreateTable(
                name: "carrier_rate_agreements",
                columns: table => new
                {
                    carrier_rate_agreement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    carrier_service_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    agreement_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    zone_or_lane = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    rate_model = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    tier_model = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    base_rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_of_measure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    minimum_charge = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    dimensional_weight_divisor = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    weight_rounding_increment_kg = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    effective_from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    effective_to_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carrier_rate_agreements", x => x.carrier_rate_agreement_id);
                });

            migrationBuilder.CreateTable(
                name: "carrier_sla_compliance_records",
                columns: table => new
                {
                    compliance_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    carrier_service_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin_region = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    destination_country = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    destination_state_or_region = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    destination_postal_prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    dispatched_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    estimated_delivery_date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    delivered_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    is_first_attempt_success = table.Column<bool>(type: "boolean", nullable: false),
                    first_attempt_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    contractual_transit_days = table.Column<int>(type: "integer", nullable: false),
                    actual_transit_days = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    transit_time_variance_days = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    is_on_time = table.Column<bool>(type: "boolean", nullable: true),
                    has_delivery_exception = table.Column<bool>(type: "boolean", nullable: false),
                    qualifying_exceptions_count = table.Column<int>(type: "integer", nullable: false),
                    sla_policy_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sla_policy_version = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    evaluation_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    data_quality_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carrier_sla_compliance_records", x => x.compliance_record_id);
                });

            migrationBuilder.CreateTable(
                name: "clients",
                columns: table => new
                {
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    account_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    sla_tier = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clients", x => x.client_id);
                });

            migrationBuilder.CreateTable(
                name: "daily_carrier_performance_records",
                columns: table => new
                {
                    record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    carrier_service_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    time_grain = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    dispatched_shipments_count = table.Column<int>(type: "integer", nullable: false),
                    delivered_shipments_count = table.Column<int>(type: "integer", nullable: false),
                    in_transit_shipments_count = table.Column<int>(type: "integer", nullable: false),
                    overdue_in_transit_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    completed_delivery_outcomes_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    on_time_delivered_count = table.Column<int>(type: "integer", nullable: false),
                    late_delivered_count = table.Column<int>(type: "integer", nullable: false),
                    first_attempt_delivered_count = table.Column<int>(type: "integer", nullable: false),
                    delivery_exceptions_count = table.Column<int>(type: "integer", nullable: false),
                    on_time_delivery_rate_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    first_attempt_delivery_rate_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    delivery_exception_rate_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    total_transit_days_sum = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    average_transit_days = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_carrier_performance_records", x => x.record_id);
                });

            migrationBuilder.CreateTable(
                name: "daily_client_profitability_summaries",
                columns: table => new
                {
                    summary_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    time_grain = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    billed_storage_revenue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    billed_handling_revenue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    billed_freight_revenue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    billed_vas_revenue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_billed_revenue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    carrier_freight_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    net_freight_margin = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    freight_margin_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    direct_labor_estimated_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    net_commercial_margin = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    commercial_margin_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    invoices_issued_count = table.Column<int>(type: "integer", nullable: false),
                    total_invoiced_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_tax_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    payments_collected_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    credits_applied_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    outstanding_ar_balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_client_profitability_summaries", x => x.summary_id);
                });

            migrationBuilder.CreateTable(
                name: "daily_facility_productivity_summaries",
                columns: table => new
                {
                    summary_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    time_grain = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    orders_received_count = table.Column<int>(type: "integer", nullable: false),
                    orders_allocated_count = table.Column<int>(type: "integer", nullable: false),
                    lines_picked_count = table.Column<int>(type: "integer", nullable: false),
                    units_picked_count = table.Column<int>(type: "integer", nullable: false),
                    cartons_packed_count = table.Column<int>(type: "integer", nullable: false),
                    orders_dispatched_count = table.Column<int>(type: "integer", nullable: false),
                    packages_dispatched_count = table.Column<int>(type: "integer", nullable: false),
                    inbound_pallets_received_count = table.Column<int>(type: "integer", nullable: false),
                    inbound_putaway_completed_count = table.Column<int>(type: "integer", nullable: false),
                    returns_inspected_count = table.Column<int>(type: "integer", nullable: false),
                    active_worker_headcount = table.Column<int>(type: "integer", nullable: false),
                    total_labor_hours = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_pick_hours = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_putaway_hours = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    units_per_labor_hour = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    putaway_per_labor_hour = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    average_fulfillment_cycle_time_hours = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_cycle_time_orders_count = table.Column<int>(type: "integer", nullable: false),
                    total_cycle_time_hours_sum = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    eligible_orders_cutoff_count = table.Column<int>(type: "integer", nullable: false),
                    same_day_dispatched_cutoff_count = table.Column<int>(type: "integer", nullable: false),
                    same_day_dispatch_adherence_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_facility_productivity_summaries", x => x.summary_id);
                });

            migrationBuilder.CreateTable(
                name: "daily_inventory_aging_snapshots",
                columns: table => new
                {
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    lpn_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    snapshot_date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    holding_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    qty_available = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    qty_allocated = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    qty_reserved = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    qty_quarantined = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    qty_damaged = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    qty_on_hold = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    receipt_age_days = table.Column<int>(type: "integer", nullable: false),
                    last_moved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_movement_age_days = table.Column<int>(type: "integer", nullable: false),
                    last_demanded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_demand_age_days = table.Column<int>(type: "integer", nullable: false),
                    demand_inactivity_days = table.Column<int>(type: "integer", nullable: true),
                    expiration_date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    days_until_expiry = table.Column<int>(type: "integer", nullable: true),
                    holding_duration_days = table.Column<int>(type: "integer", nullable: false),
                    aging_bucket = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    velocity_category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_aged = table.Column<bool>(type: "boolean", nullable: false),
                    is_slow_moving = table.Column<bool>(type: "boolean", nullable: false),
                    is_non_moving = table.Column<bool>(type: "boolean", nullable: false),
                    is_dormant = table.Column<bool>(type: "boolean", nullable: false),
                    is_dead_stock = table.Column<bool>(type: "boolean", nullable: false),
                    is_near_expiry = table.Column<bool>(type: "boolean", nullable: false),
                    is_critical_expiry = table.Column<bool>(type: "boolean", nullable: false),
                    is_expired = table.Column<bool>(type: "boolean", nullable: false),
                    quantity_on_hand = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_inventory_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    daily_holding_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    accrued_holding_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    occupied_cbm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    pallet_footprint = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    has_missing_dimensions_fallback = table.Column<bool>(type: "boolean", nullable: false),
                    source_inventory_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_inventory_aging_snapshots", x => x.snapshot_id);
                });

            migrationBuilder.CreateTable(
                name: "daily_inventory_utilization_snapshots",
                columns: table => new
                {
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    time_grain = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    total_storage_locations = table.Column<int>(type: "integer", nullable: false),
                    occupied_storage_locations = table.Column<int>(type: "integer", nullable: false),
                    pallet_position_occupancy_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_capacity_cubic_meters = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    occupied_cubic_meters = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    volumetric_cube_fill_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_active_skus = table.Column<int>(type: "integer", nullable: false),
                    total_inventory_units = table.Column<int>(type: "integer", nullable: false),
                    total_inventory_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    dead_stock_skus_count = table.Column<int>(type: "integer", nullable: false),
                    dead_stock_units_count = table.Column<int>(type: "integer", nullable: false),
                    dead_stock_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    dead_stock_ratio_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_inventory_utilization_snapshots", x => x.snapshot_id);
                });

            migrationBuilder.CreateTable(
                name: "daily_location_space_utilization_records",
                columns: table => new
                {
                    record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    location_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    snapshot_date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    aisle_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    rack_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    shelf_level = table.Column<int>(type: "integer", nullable: true),
                    bin_number = table.Column<int>(type: "integer", nullable: true),
                    x_coord = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    y_coord = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    z_coord = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    physical_cubic_meters = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    usable_capacity_factor = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    usable_cubic_meters = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    occupied_cubic_meters = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    available_cubic_meters = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cube_utilization_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    physical_pallet_positions = table.Column<int>(type: "integer", nullable: false),
                    usable_pallet_positions = table.Column<int>(type: "integer", nullable: false),
                    occupied_pallet_positions = table.Column<int>(type: "integer", nullable: false),
                    available_pallet_positions = table.Column<int>(type: "integer", nullable: false),
                    pallet_occupancy_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    is_blocked = table.Column<bool>(type: "boolean", nullable: false),
                    block_reason = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    max_weight_kg = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    occupied_weight_kg = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    weight_utilization_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    is_weight_constrained = table.Column<bool>(type: "boolean", nullable: false),
                    is_over_capacity = table.Column<bool>(type: "boolean", nullable: false),
                    over_capacity_excess_cbm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    distinct_sku_count = table.Column<int>(type: "integer", nullable: false),
                    total_units_stored = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    has_missing_dimensions_fallback = table.Column<bool>(type: "boolean", nullable: false),
                    units_available = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    units_allocated = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    units_reserved = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    units_quarantined = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    units_damaged = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    units_on_hold = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    occupied_cbm_available = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    occupied_cbm_allocated = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    occupied_cbm_reserved = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    occupied_cbm_quarantined = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    occupied_cbm_damaged = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    occupied_cbm_on_hold = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    space_classification = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_location_space_utilization_records", x => x.record_id);
                });

            migrationBuilder.CreateTable(
                name: "daily_worker_productivity_summaries",
                columns: table => new
                {
                    summary_id = table.Column<Guid>(type: "uuid", nullable: false),
                    worker_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    time_grain = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    total_shift_minutes = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    productive_work_minutes = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    idle_minutes = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    scheduled_break_minutes = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tasks_completed = table.Column<int>(type: "integer", nullable: false),
                    units_completed = table.Column<int>(type: "integer", nullable: false),
                    lines_completed = table.Column<int>(type: "integer", nullable: false),
                    cartons_completed = table.Column<int>(type: "integer", nullable: false),
                    pallets_completed = table.Column<int>(type: "integer", nullable: false),
                    actual_labor_hours = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    standard_labor_hours = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    labor_efficiency_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    labor_utilization_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    units_per_hour = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    lines_per_hour = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cartons_per_hour = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tasks_per_hour = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    performance_tier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    hourly_wage_rate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_labor_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    labor_cost_per_unit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    labor_cost_per_task = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    last_task_completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_worker_productivity_summaries", x => x.summary_id);
                });

            migrationBuilder.CreateTable(
                name: "facilities",
                columns: table => new
                {
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address_line1 = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    address_line2 = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    state_or_province = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    postal_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    country_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "USA"),
                    timezone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    contact_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    contact_phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    total_area_sq_m = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    dock_door_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 4),
                    operating_hours_json = table.Column<string>(type: "text", nullable: true),
                    configuration_json = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_facilities", x => x.facility_id);
                });

            migrationBuilder.CreateTable(
                name: "facility_dock_yard_snapshots",
                columns: table => new
                {
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    time_grain = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    total_dock_doors_count = table.Column<int>(type: "integer", nullable: false),
                    occupied_dock_doors_count = table.Column<int>(type: "integer", nullable: false),
                    dock_door_utilization_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_yard_spots_count = table.Column<int>(type: "integer", nullable: false),
                    occupied_yard_spots_count = table.Column<int>(type: "integer", nullable: false),
                    yard_occupancy_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    trucks_checked_in_count = table.Column<int>(type: "integer", nullable: false),
                    trucks_departed_count = table.Column<int>(type: "integer", nullable: false),
                    trucks_waiting_in_yard_count = table.Column<int>(type: "integer", nullable: false),
                    trucks_at_dock_count = table.Column<int>(type: "integer", nullable: false),
                    total_yard_wait_minutes_sum = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_yard_wait_trucks_count = table.Column<int>(type: "integer", nullable: false),
                    average_yard_wait_time_minutes = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_dock_service_minutes_sum = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_dock_service_trucks_count = table.Column<int>(type: "integer", nullable: false),
                    average_dock_service_time_minutes = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_turn_time_minutes_sum = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_departed_trucks_count = table.Column<int>(type: "integer", nullable: false),
                    average_total_turn_time_minutes = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    projection_version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_facility_dock_yard_snapshots", x => x.snapshot_id);
                });

            migrationBuilder.CreateTable(
                name: "hourly_facility_throughput_snapshots",
                columns: table => new
                {
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hour_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    orders_received_count = table.Column<int>(type: "integer", nullable: false),
                    orders_allocated_count = table.Column<int>(type: "integer", nullable: false),
                    lines_picked_count = table.Column<int>(type: "integer", nullable: false),
                    units_picked_count = table.Column<int>(type: "integer", nullable: false),
                    cartons_packed_count = table.Column<int>(type: "integer", nullable: false),
                    orders_dispatched_count = table.Column<int>(type: "integer", nullable: false),
                    packages_dispatched_count = table.Column<int>(type: "integer", nullable: false),
                    inbound_pallets_received_count = table.Column<int>(type: "integer", nullable: false),
                    inbound_putaway_completed_count = table.Column<int>(type: "integer", nullable: false),
                    returns_inspected_count = table.Column<int>(type: "integer", nullable: false),
                    active_worker_headcount = table.Column<int>(type: "integer", nullable: false),
                    total_labor_hours = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    pick_duration_hours = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    putaway_duration_hours = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    units_per_labor_hour = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    putaway_per_labor_hour = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    average_cycle_time_hours = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_cycle_orders_count = table.Column<int>(type: "integer", nullable: false),
                    total_cycle_hours_sum = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    eligible_cutoff_count = table.Column<int>(type: "integer", nullable: false),
                    same_day_dispatched_count = table.Column<int>(type: "integer", nullable: false),
                    same_day_dispatch_adherence_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    projection_version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hourly_facility_throughput_snapshots", x => x.snapshot_id);
                });

            migrationBuilder.CreateTable(
                name: "inventory_aging_policies",
                columns: table => new
                {
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    slow_moving_threshold_days = table.Column<int>(type: "integer", nullable: false),
                    dormant_threshold_days = table.Column<int>(type: "integer", nullable: false),
                    dead_stock_threshold_days = table.Column<int>(type: "integer", nullable: false),
                    near_expiry_threshold_days = table.Column<int>(type: "integer", nullable: false),
                    critical_expiry_threshold_days = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    daily_holding_cost_per_cbm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    daily_holding_cost_per_pallet = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    default_basis = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    effective_from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    effective_to_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_aging_policies", x => x.policy_id);
                });

            migrationBuilder.CreateTable(
                name: "inventory_movement_event_logs",
                columns: table => new
                {
                    log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    event_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_qualifying_demand = table.Column<bool>(type: "boolean", nullable: false),
                    is_physical_movement = table.Column<bool>(type: "boolean", nullable: false),
                    is_canonical_demand = table.Column<bool>(type: "boolean", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_cost_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    source_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    destination_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reference_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sequence_number = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_movement_event_logs", x => x.log_id);
                });

            migrationBuilder.CreateTable(
                name: "item_categories",
                columns: table => new
                {
                    category_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    parent_category_id = table.Column<int>(type: "integer", nullable: true),
                    category_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    tax_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    storage_attribute_flag = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_categories", x => x.category_id);
                    table.ForeignKey(
                        name: "fk_item_categories_item_categories_parent_category_id",
                        column: x => x.parent_category_id,
                        principalTable: "item_categories",
                        principalColumn: "category_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "labor_standard_profiles",
                columns: table => new
                {
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    zone_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    activity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    department = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    base_setup_seconds = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    seconds_per_unit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    seconds_per_line = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    seconds_per_carton = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    seconds_per_pallet = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    seconds_per_travel_meter = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    equipment_factor = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    complexity_multiplier = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    effective_from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    effective_to_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_labor_standard_profiles", x => x.profile_id);
                });

            migrationBuilder.CreateTable(
                name: "lane_transit_summaries",
                columns: table => new
                {
                    lane_summary_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin_facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin_region = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    destination_country = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    destination_state_or_region = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    carrier_service_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    time_grain = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    dispatched_shipments_count = table.Column<int>(type: "integer", nullable: false),
                    delivered_shipments_count = table.Column<int>(type: "integer", nullable: false),
                    total_transit_days_sum = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    average_transit_days = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    min_transit_days = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    max_transit_days = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    p95_transit_days = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    on_time_delivered_count = table.Column<int>(type: "integer", nullable: false),
                    on_time_delivery_rate_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lane_transit_summaries", x => x.lane_summary_id);
                });

            migrationBuilder.CreateTable(
                name: "product_affinity_clusters",
                columns: table => new
                {
                    cluster_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mining_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cluster_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    primary_anchor_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    primary_anchor_sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    item_count = table.Column<int>(type: "integer", nullable: false),
                    average_lift = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    max_lift = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    window_start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    window_end_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    calculated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_affinity_clusters", x => x.cluster_id);
                });

            migrationBuilder.CreateTable(
                name: "product_affinity_pairs",
                columns: table => new
                {
                    pair_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mining_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_a_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_a_sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    item_b_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_b_sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    total_orders_evaluated = table.Column<int>(type: "integer", nullable: false),
                    item_a_order_count = table.Column<int>(type: "integer", nullable: false),
                    item_b_order_count = table.Column<int>(type: "integer", nullable: false),
                    co_occurrence_count = table.Column<int>(type: "integer", nullable: false),
                    support = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    item_a_support = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    item_b_support = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    confidence_ato_b = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    confidence_bto_a = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    max_confidence = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    kulczynski_measure = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    lift = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    chi_square_statistic = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    min_expected_cell_frequency = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    has_yates_correction = table.Column<bool>(type: "boolean", nullable: false),
                    p_value = table.Column<decimal>(type: "numeric(10,6)", precision: 10, scale: 6, nullable: true),
                    is_statistically_significant = table.Column<bool>(type: "boolean", nullable: false),
                    window_start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    window_end_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    calculated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_affinity_pairs", x => x.pair_id);
                });

            migrationBuilder.CreateTable(
                name: "product_affinity_policies",
                columns: table => new
                {
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    analysis_window_days = table.Column<int>(type: "integer", nullable: false),
                    min_order_count = table.Column<int>(type: "integer", nullable: false),
                    min_co_occurrence_count = table.Column<int>(type: "integer", nullable: false),
                    min_support = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    min_confidence = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    min_lift = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    min_cluster_lift = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    max_cluster_size = table.Column<int>(type: "integer", nullable: false),
                    weight_product_affinity = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    include_unassigned_facility_orders = table.Column<bool>(type: "boolean", nullable: false),
                    seasonal_start_date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    seasonal_end_date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_affinity_policies", x => x.policy_id);
                });

            migrationBuilder.CreateTable(
                name: "projection_checkpoints",
                columns: table => new
                {
                    checkpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    projection_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    source_stream_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, defaultValue: "default"),
                    last_processed_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_processed_sequence_number = table.Column<long>(type: "bigint", nullable: false),
                    last_processed_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    processed_count = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projection_checkpoints", x => x.checkpoint_id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    role_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_system_role = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.role_id);
                });

            migrationBuilder.CreateTable(
                name: "sku_velocity_classification_records",
                columns: table => new
                {
                    record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    abc_class = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    criteria_used = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    record_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    velocity_category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    observation_window = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    classification_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    period_start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    period_end_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    total_pick_tasks_count = table.Column<int>(type: "integer", nullable: false),
                    total_units_demanded = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_demand_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    period_demand_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    annualized_consumption_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    order_lines_count = table.Column<int>(type: "integer", nullable: false),
                    total_demand_events_count = table.Column<int>(type: "integer", nullable: false),
                    total_movement_events_count = table.Column<int>(type: "integer", nullable: false),
                    total_units_moved = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_replenishment_count = table.Column<int>(type: "integer", nullable: false),
                    total_relocation_count = table.Column<int>(type: "integer", nullable: false),
                    cost_of_goods_consumed = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    average_inventory_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    inventory_turnover_ratio = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    days_of_supply = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    days_of_supply_financial = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    days_of_supply_operational = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    current_on_hand_units = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    is_demand_spike = table.Column<bool>(type: "boolean", nullable: false),
                    is_seasonal_pattern = table.Column<bool>(type: "boolean", nullable: false),
                    cumulative_percentage = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    rank_in_portfolio = table.Column<int>(type: "integer", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: true),
                    composite_score = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    class_a_threshold_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    class_b_threshold_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    valuation_basis_used = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    aisle = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    calculated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sku_velocity_classification_records", x => x.record_id);
                });

            migrationBuilder.CreateTable(
                name: "slotting_policies",
                columns: table => new
                {
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    primary_goal = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    secondary_goal = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    golden_zone_min_z_meters = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    golden_zone_max_z_meters = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    golden_zone_shelf_levels = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_class_a_pick_face = table.Column<bool>(type: "boolean", nullable: false),
                    target_class_b_intermediate = table.Column<bool>(type: "boolean", nullable: false),
                    target_class_c_reserve = table.Column<bool>(type: "boolean", nullable: false),
                    protect_dead_stock = table.Column<bool>(type: "boolean", nullable: false),
                    max_relocations_per_batch = table.Column<int>(type: "integer", nullable: false),
                    min_labor_savings_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    estimated_relocation_labor_minutes = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    labor_hourly_rate_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    disruption_penalty_weight = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    weight_travel_distance = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    weight_ergonomics = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    weight_pick_density = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    weight_abc_affinity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    weight_velocity_affinity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    weight_cube_fill = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    weight_product_affinity = table.Column<decimal>(type: "numeric", nullable: false),
                    weight_financial_benefit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    weight_operational_urgency = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    weight_placement_suitability = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    weight_disruption_penalty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    negative_net_benefit_policy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    urgency_threshold_for_negative_benefit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    target_monthly_savings_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    max_acceptable_relocation_cost_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    max_acceptable_disruption_cost_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    use_total_relocation_cost_for_disruption_penalty = table.Column<bool>(type: "boolean", nullable: false),
                    handling_cost_per_unit_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    heavy_handling_surcharge_per_tier_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    base_disruption_cost_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    golden_zone_opportunity_gain_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    amortization_months = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    max_projection_age_days = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    effective_from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    effective_to_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_slotting_policies", x => x.policy_id);
                });

            migrationBuilder.CreateTable(
                name: "worker_idle_interval_records",
                columns: table => new
                {
                    idle_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    worker_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ended_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    duration_minutes = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    duration_seconds = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    supervisor_approved = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_worker_idle_interval_records", x => x.idle_record_id);
                });

            migrationBuilder.CreateTable(
                name: "worker_task_execution_logs",
                columns: table => new
                {
                    execution_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    worker_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    department = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    units_processed = table.Column<int>(type: "integer", nullable: false),
                    lines_processed = table.Column<int>(type: "integer", nullable: false),
                    cartons_processed = table.Column<int>(type: "integer", nullable: false),
                    pallets_processed = table.Column<int>(type: "integer", nullable: false),
                    travel_distance_meters = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    actual_duration_seconds = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    standard_duration_seconds = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    standard_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    standard_profile_version = table.Column<int>(type: "integer", nullable: false),
                    variance_seconds = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    efficiency_percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    complexity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    equipment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    hourly_wage_rate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    calculated_labor_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_worker_task_execution_logs", x => x.execution_log_id);
                });

            migrationBuilder.CreateTable(
                name: "carrier_rate_tiers",
                columns: table => new
                {
                    carrier_rate_tier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    carrier_rate_agreement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tier_number = table.Column<int>(type: "integer", nullable: false),
                    lower_bound = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    upper_bound = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carrier_rate_tiers", x => x.carrier_rate_tier_id);
                    table.ForeignKey(
                        name: "fk_carrier_rate_tiers_carrier_rate_agreements_carrier_rate_agr",
                        column: x => x.carrier_rate_agreement_id,
                        principalTable: "carrier_rate_agreements",
                        principalColumn: "carrier_rate_agreement_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "client_contacts",
                columns: table => new
                {
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_billing_contact = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_contacts", x => x.contact_id);
                    table.ForeignKey(
                        name: "fk_client_contacts_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customer_payments",
                columns: table => new
                {
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payment_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    payment_method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    allocated_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unallocated_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    is_reversed = table.Column<bool>(type: "boolean", nullable: false),
                    reversed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reversal_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_payments", x => x.payment_id);
                    table.ForeignKey(
                        name: "fk_customer_payments_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    customer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    customer_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    contact_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    email = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    default_address_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    default_address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    default_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    default_state_or_province = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    default_postal_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    default_country_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.customer_id);
                    table.ForeignKey(
                        name: "fk_customers_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoice_sequences",
                columns: table => new
                {
                    sequence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fiscal_year = table.Column<int>(type: "integer", nullable: false),
                    prefix = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    last_sequence_number = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_sequences", x => x.sequence_id);
                    table.ForeignKey(
                        name: "fk_invoice_sequences_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "items",
                columns: table => new
                {
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    short_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    weight_kg = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    length_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    width_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    height_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    volume_cm3 = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    abc_velocity = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    storage_temp_class = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    hazard_class = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    stackability_factor = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    allow_planar_rotation = table.Column<bool>(type: "boolean", nullable: false),
                    is_lot_tracked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_serialized = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    requires_expiry_date = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    shelf_life_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_items", x => x.item_id);
                    table.ForeignKey(
                        name: "fk_items_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contact_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    address_line1 = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    address_line2 = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suppliers", x => x.supplier_id);
                    table.ForeignKey(
                        name: "fk_suppliers_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    phone_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    user_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    refresh_token = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    refresh_token_expiry_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_users_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "billing_periods",
                columns: table => new
                {
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    period_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    start_date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    frequency = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    locked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    invoiced_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_periods", x => x.period_id);
                    table.ForeignKey(
                        name: "fk_billing_periods_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_billing_periods_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_facility_access",
                columns: table => new
                {
                    access_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    primary_operating_facility = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_facility_access", x => x.access_id);
                    table.ForeignKey(
                        name: "fk_client_facility_access_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_client_facility_access_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "client_freight_agreements",
                columns: table => new
                {
                    freight_agreement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    agreement_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    carrier_service_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    pricing_model = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    markup_percentage = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    flat_fee_per_shipment = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    flat_fee_per_package = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    fixed_freight_rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    weight_rounding_increment_kg = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    minimum_markup_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    maximum_markup_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    default_accessorial_treatment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    effective_from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    effective_to_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_freight_agreements", x => x.freight_agreement_id);
                    table.ForeignKey(
                        name: "fk_client_freight_agreements_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_client_freight_agreements_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "daily_inventory_snapshots",
                columns: table => new
                {
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_date = table.Column<DateOnly>(type: "date", nullable: false),
                    snapshot_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    total_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_billable_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_pallet_count = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_billable_pallet_count = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_carton_count = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_billable_carton_count = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_cbm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_billable_cbm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    record_count = table.Column<int>(type: "integer", nullable: false),
                    has_missing_dimensions_records = table.Column<bool>(type: "boolean", nullable: false),
                    finalized_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    finalized_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_inventory_snapshots", x => x.snapshot_id);
                    table.ForeignKey(
                        name: "fk_daily_inventory_snapshots_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_daily_inventory_snapshots_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dock_doors",
                columns: table => new
                {
                    dock_door_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    door_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    door_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    supports_refrigerated = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    max_trailer_length_feet = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dock_doors", x => x.dock_door_id);
                    table.ForeignKey(
                        name: "fk_dock_doors_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_import_batches",
                columns: table => new
                {
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    total_rows = table.Column<int>(type: "integer", nullable: false),
                    successful_rows = table.Column<int>(type: "integer", nullable: false),
                    failed_rows = table.Column<int>(type: "integer", nullable: false),
                    duplicate_rows = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error_details_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_import_batches", x => x.batch_id);
                    table.ForeignKey(
                        name: "fk_order_import_batches_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_order_import_batches_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "packaging_materials",
                columns: table => new
                {
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    material_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    length_cm = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    width_cm = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    height_cm = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    max_weight_kg = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    tare_weight_kg = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    cost_per_unit = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_packaging_materials", x => x.material_id);
                    table.ForeignKey(
                        name: "fk_packaging_materials_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_packaging_materials_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "putaway_strategy_rules",
                columns: table => new
                {
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    rule_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    temp_class_requirement = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    target_velocity = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    prefer_consolidation = table.Column<bool>(type: "boolean", nullable: false),
                    allow_dynamic_overflow = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_putaway_strategy_rules", x => x.rule_id);
                    table.ForeignKey(
                        name: "fk_putaway_strategy_rules_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_putaway_strategy_rules_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rate_cards",
                columns: table => new
                {
                    rate_card_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rate_card_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    default_facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rate_cards", x => x.rate_card_id);
                    table.ForeignKey(
                        name: "fk_rate_cards_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rate_cards_facilities_default_facility_id",
                        column: x => x.default_facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "storage_billing_policies",
                columns: table => new
                {
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    policy_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    available_treatment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    allocated_treatment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    picked_treatment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    damaged_treatment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    quarantined_treatment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    on_hold_treatment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    in_transit_treatment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    return_pending_inspection_treatment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    fallback_unit_cbm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    fallback_units_per_pallet = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    fallback_units_per_carton = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_billing_policies", x => x.policy_id);
                    table.ForeignKey(
                        name: "fk_storage_billing_policies_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_storage_billing_policies_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "warehouse_zones",
                columns: table => new
                {
                    zone_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    zone_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    temp_min_c = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    temp_max_c = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warehouse_zones", x => x.zone_id);
                    table.ForeignKey(
                        name: "fk_warehouse_zones_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "allocation_policies",
                columns: table => new
                {
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_category_id = table.Column<int>(type: "integer", nullable: true),
                    default_strategy = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    allow_partial_allocation = table.Column<bool>(type: "boolean", nullable: false),
                    allow_mixed_lots = table.Column<bool>(type: "boolean", nullable: false),
                    min_days_before_expiry = table.Column<int>(type: "integer", nullable: false),
                    prefer_full_handling_units = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_allocation_policies", x => x.policy_id);
                    table.ForeignKey(
                        name: "fk_allocation_policies_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_allocation_policies_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_allocation_policies_item_categories_item_category_id",
                        column: x => x.item_category_id,
                        principalTable: "item_categories",
                        principalColumn: "category_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "product_affinity_cluster_members",
                columns: table => new
                {
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cluster_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_anchor = table.Column<bool>(type: "boolean", nullable: false),
                    within_cluster_degree = table.Column<int>(type: "integer", nullable: false),
                    average_within_cluster_lift = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_affinity_cluster_members", x => x.member_id);
                    table.ForeignKey(
                        name: "fk_product_affinity_cluster_members_product_affinity_clusters_",
                        column: x => x.cluster_id,
                        principalTable: "product_affinity_clusters",
                        principalColumn: "cluster_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    role_perm_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<int>(type: "integer", nullable: false),
                    permission_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    access_level = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => x.role_perm_id);
                    table.ForeignKey(
                        name: "fk_role_permissions_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "role_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "slotting_recommendation_batches",
                columns: table => new
                {
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_version = table.Column<int>(type: "integer", nullable: false),
                    policy_snapshot_json = table.Column<string>(type: "text", nullable: true),
                    batch_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    snapshot_date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    total_proposed_moves = table.Column<int>(type: "integer", nullable: false),
                    approved_moves_count = table.Column<int>(type: "integer", nullable: false),
                    rejected_moves_count = table.Column<int>(type: "integer", nullable: false),
                    executed_moves_count = table.Column<int>(type: "integer", nullable: false),
                    estimated_labor_minutes_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    estimated_travel_distance_saved_meters_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    estimated_monthly_net_savings_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    average_suitability_score = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    execution_started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    execution_completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_slotting_recommendation_batches", x => x.batch_id);
                    table.ForeignKey(
                        name: "fk_slotting_recommendation_batches_slotting_policies_policy_id",
                        column: x => x.policy_id,
                        principalTable: "slotting_policies",
                        principalColumn: "policy_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outbound_orders",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    external_order_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    order_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    delivery_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "PendingShipment"),
                    delivery_status_updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    delivered_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status_before_hold = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    service_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    customer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    destination_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    order_date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    required_ship_date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    earliest_ship_date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    hold_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    held_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancelled_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    allocation_idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbound_orders", x => x.order_id);
                    table.ForeignKey(
                        name: "fk_outbound_orders_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_outbound_orders_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_outbound_orders_facilities_destination_facility_id",
                        column: x => x.destination_facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_outbound_orders_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_lots",
                columns: table => new
                {
                    lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    mfg_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expiry_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    qa_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    qa_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_lots", x => x.lot_id);
                    table.ForeignKey(
                        name: "fk_inventory_lots_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_lots_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "item_barcodes",
                columns: table => new
                {
                    barcode_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    barcode_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    barcode_symbology = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_barcodes", x => x.barcode_id);
                    table.ForeignKey(
                        name: "fk_item_barcodes_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "item_category_mappings",
                columns: table => new
                {
                    map_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_category_mappings", x => x.map_id);
                    table.ForeignKey(
                        name: "fk_item_category_mappings_item_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "item_categories",
                        principalColumn: "category_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_item_category_mappings_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "packaging_units",
                columns: table => new
                {
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_level = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    unit_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    conversion_qty = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    weight_kg = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    length_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    width_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    height_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    volume_cm3 = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    barcode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_packaging_units", x => x.unit_id);
                    table.ForeignKey(
                        name: "fk_packaging_units_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "purchase_orders",
                columns: table => new
                {
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    po_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    order_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expected_delivery_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "USD"),
                    total_estimated_cost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_orders", x => x.purchase_order_id);
                    table.ForeignKey(
                        name: "fk_purchase_orders_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_orders_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_orders_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "supplier_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    user_role_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<int>(type: "integer", nullable: false),
                    assigned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    assigned_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => x.user_role_id);
                    table.ForeignKey(
                        name: "fk_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "role_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "billable_activities",
                columns: table => new
                {
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    billing_period_id = table.Column<Guid>(type: "uuid", nullable: true),
                    activity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_of_measure = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    rate_card_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rate_card_schedule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rate_card_tier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rate_card_version = table.Column<int>(type: "integer", nullable: true),
                    rated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invoice_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invoiced_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    metadata_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billable_activities", x => x.activity_id);
                    table.ForeignKey(
                        name: "fk_billable_activities_billing_periods_billing_period_id",
                        column: x => x.billing_period_id,
                        principalTable: "billing_periods",
                        principalColumn: "period_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_billable_activities_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_billable_activities_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    billing_period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    invoice_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    due_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    payment_terms_days = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    subtotal_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    tax_rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    paid_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    credited_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    outstanding_balance = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    approved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    issued_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    issued_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    voided_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    voided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    replacement_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.invoice_id);
                    table.ForeignKey(
                        name: "fk_invoices_billing_periods_billing_period_id",
                        column: x => x.billing_period_id,
                        principalTable: "billing_periods",
                        principalColumn: "period_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_invoices_replacement_invoice_id",
                        column: x => x.replacement_invoice_id,
                        principalTable: "invoices",
                        principalColumn: "invoice_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "client_accessorial_rules",
                columns: table => new
                {
                    accessorial_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_freight_agreement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accessorial_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    treatment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    markup_percentage = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    fixed_fee = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_accessorial_rules", x => x.accessorial_rule_id);
                    table.ForeignKey(
                        name: "fk_client_accessorial_rules_client_freight_agreements_client_f",
                        column: x => x.client_freight_agreement_id,
                        principalTable: "client_freight_agreements",
                        principalColumn: "freight_agreement_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_import_batch_rows",
                columns: table => new
                {
                    row_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_number = table.Column<int>(type: "integer", nullable: false),
                    external_order_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_success = table.Column<bool>(type: "boolean", nullable: false),
                    is_duplicate = table.Column<bool>(type: "boolean", nullable: false),
                    error_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_order_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_import_batch_rows", x => x.row_id);
                    table.ForeignKey(
                        name: "fk_order_import_batch_rows_order_import_batches_order_import_b",
                        column: x => x.batch_id,
                        principalTable: "order_import_batches",
                        principalColumn: "batch_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rate_card_schedules",
                columns: table => new
                {
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rate_card_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    effective_from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    effective_to_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    minimum_monthly_spend = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rate_card_schedules", x => x.schedule_id);
                    table.ForeignKey(
                        name: "fk_rate_card_schedules_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rate_card_schedules_rate_cards_rate_card_id",
                        column: x => x.rate_card_id,
                        principalTable: "rate_cards",
                        principalColumn: "rate_card_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cycle_counts",
                columns: table => new
                {
                    count_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    count_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    count_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    scheduled_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cycle_counts", x => x.count_id);
                    table.ForeignKey(
                        name: "fk_cycle_counts_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cycle_counts_warehouse_zones_zone_id",
                        column: x => x.zone_id,
                        principalTable: "warehouse_zones",
                        principalColumn: "zone_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "locations",
                columns: table => new
                {
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parent_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    location_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    barcode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    aisle_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    rack_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    shelf_level = table.Column<int>(type: "integer", nullable: true),
                    bin_number = table.Column<int>(type: "integer", nullable: true),
                    x_coord = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    y_coord = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    z_coord = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    pick_sequence = table.Column<int>(type: "integer", nullable: true),
                    length_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    width_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    height_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    max_vol_cm3 = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    max_weight_kg = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_pickable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_receiving = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_damaged = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_locked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_locations", x => x.location_id);
                    table.ForeignKey(
                        name: "fk_locations_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_locations_locations_parent_location_id",
                        column: x => x.parent_location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_locations_warehouse_zones_zone_id",
                        column: x => x.zone_id,
                        principalTable: "warehouse_zones",
                        principalColumn: "zone_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "pick_waves",
                columns: table => new
                {
                    pick_wave_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wave_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    wave_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    temp_class = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    assigned_to_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    released_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancelled_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pick_waves", x => x.pick_wave_id);
                    table.ForeignKey(
                        name: "fk_pick_waves_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pick_waves_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pick_waves_users_assigned_to_user_id",
                        column: x => x.assigned_to_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_pick_waves_warehouse_zones_zone_id",
                        column: x => x.zone_id,
                        principalTable: "warehouse_zones",
                        principalColumn: "zone_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "order_status_histories",
                columns: table => new
                {
                    history_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    to_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_status_histories", x => x.history_id);
                    table.ForeignKey(
                        name: "fk_order_status_histories_outbound_orders_outbound_order_id",
                        column: x => x.order_id,
                        principalTable: "outbound_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shipping_addresses",
                columns: table => new
                {
                    shipping_address_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    company_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    address_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    state_or_province = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    postal_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    country_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    phone_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    delivery_instructions = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_amended = table.Column<bool>(type: "boolean", nullable: false),
                    amended_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    amended_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amendment_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    original_address_snapshot = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipping_addresses", x => x.shipping_address_id);
                    table.ForeignKey(
                        name: "fk_shipping_addresses_outbound_orders_outbound_order_id",
                        column: x => x.order_id,
                        principalTable: "outbound_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "outbound_order_lines",
                columns: table => new
                {
                    line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    packaging_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    quantity_ordered = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    quantity_allocated = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    quantity_shipped = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    required_lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    min_expiry_date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbound_order_lines", x => x.line_id);
                    table.ForeignKey(
                        name: "fk_outbound_order_lines_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_outbound_order_lines_outbound_orders_outbound_order_id",
                        column: x => x.order_id,
                        principalTable: "outbound_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_outbound_order_lines_packaging_units_packaging_unit_id",
                        column: x => x.packaging_unit_id,
                        principalTable: "packaging_units",
                        principalColumn: "unit_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "advance_shipping_notices",
                columns: table => new
                {
                    asn_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    asn_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supplier_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    carrier_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    tracking_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    trailer_container_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    expected_arrival = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    actual_arrival = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_advance_shipping_notices", x => x.asn_id);
                    table.ForeignKey(
                        name: "fk_advance_shipping_notices_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_advance_shipping_notices_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_advance_shipping_notices_purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalTable: "purchase_orders",
                        principalColumn: "purchase_order_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_advance_shipping_notices_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "supplier_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_lines",
                columns: table => new
                {
                    po_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_qty = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    received_qty = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false, defaultValue: 0m),
                    unit_price = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "USD"),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_lines", x => x.po_line_id);
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalTable: "purchase_orders",
                        principalColumn: "purchase_order_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                columns: table => new
                {
                    invoice_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    charge_category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_of_measure = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    billable_activity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    activity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    metadata_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_lines", x => x.invoice_line_id);
                    table.ForeignKey(
                        name: "fk_invoice_lines_billable_activities_billable_activity_id",
                        column: x => x.billable_activity_id,
                        principalTable: "billable_activities",
                        principalColumn: "activity_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_invoice_lines_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "invoice_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_allocations",
                columns: table => new
                {
                    allocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    allocated_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    allocated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_reversed = table.Column<bool>(type: "boolean", nullable: false),
                    reversed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_allocations", x => x.allocation_id);
                    table.ForeignKey(
                        name: "fk_payment_allocations_customer_payments_customer_payment_id",
                        column: x => x.customer_payment_id,
                        principalTable: "customer_payments",
                        principalColumn: "payment_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_payment_allocations_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "invoice_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rate_card_lines",
                columns: table => new
                {
                    line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rate_card_schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    unit_of_measure = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    rate_model = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    tier_model = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    base_rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    minimum_charge = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rate_card_lines", x => x.line_id);
                    table.ForeignKey(
                        name: "fk_rate_card_lines_rate_card_schedules_rate_card_schedule_id",
                        column: x => x.rate_card_schedule_id,
                        principalTable: "rate_card_schedules",
                        principalColumn: "schedule_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cycle_count_items",
                columns: table => new
                {
                    count_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    count_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expected_qty = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    counted_qty = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    counter_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    counted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_reconciled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cycle_count_items", x => x.count_item_id);
                    table.ForeignKey(
                        name: "fk_cycle_count_items_cycle_counts_cycle_count_id",
                        column: x => x.count_id,
                        principalTable: "cycle_counts",
                        principalColumn: "count_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cycle_count_items_inventory_lots_lot_id",
                        column: x => x.lot_id,
                        principalTable: "inventory_lots",
                        principalColumn: "lot_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_cycle_count_items_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cycle_count_items_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cycle_count_items_users_approved_by_user_id",
                        column: x => x.approved_by,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_cycle_count_items_users_counter_user_id",
                        column: x => x.counter_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "inventory_serials",
                columns: table => new
                {
                    serial_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    serial_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_serials", x => x.serial_id);
                    table.ForeignKey(
                        name: "fk_inventory_serials_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_serials_inventory_lots_lot_id",
                        column: x => x.lot_id,
                        principalTable: "inventory_lots",
                        principalColumn: "lot_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_inventory_serials_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_serials_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_transactions",
                columns: table => new
                {
                    tx_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    from_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tx_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    qty = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    reference_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tx_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_transactions", x => x.tx_id);
                    table.ForeignKey(
                        name: "fk_inventory_transactions_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_transactions_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_transactions_inventory_lots_lot_id",
                        column: x => x.lot_id,
                        principalTable: "inventory_lots",
                        principalColumn: "lot_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_inventory_transactions_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_transactions_locations_from_location_id",
                        column: x => x.from_location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_transactions_locations_to_location_id",
                        column: x => x.to_location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_transactions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "location_locks",
                columns: table => new
                {
                    lock_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lock_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    locked_by_user = table.Column<Guid>(type: "uuid", nullable: true),
                    locked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    released_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    released_by_user = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_location_locks", x => x.lock_id);
                    table.ForeignKey(
                        name: "fk_location_locks_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_location_locks_users_locked_by_user_id",
                        column: x => x.locked_by_user,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_location_locks_users_released_by_user_id",
                        column: x => x.released_by_user,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "packing_sessions",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    packing_station_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outbound_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_packing_sessions", x => x.session_id);
                    table.ForeignKey(
                        name: "fk_packing_sessions_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_packing_sessions_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_packing_sessions_locations_packing_station_location_id",
                        column: x => x.packing_station_location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_packing_sessions_outbound_orders_outbound_order_id",
                        column: x => x.outbound_order_id,
                        principalTable: "outbound_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_packing_sessions_users_operator_user_id",
                        column: x => x.operator_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shipments",
                columns: table => new
                {
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    carrier_service_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    delivery_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "PendingHandover"),
                    delivery_status_updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    delivered_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    total_packages_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_weight_kg = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false, defaultValue: 0m),
                    total_volume_cm3 = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false, defaultValue: 0m),
                    staging_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trailer_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    seal_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    bill_of_lading_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    dispatched_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipments", x => x.shipment_id);
                    table.ForeignKey(
                        name: "fk_shipments_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shipments_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shipments_locations_staging_location_id",
                        column: x => x.staging_location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "slotting_recommendations",
                columns: table => new
                {
                    recommendation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_location_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    proposed_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposed_location_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_zone_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    destination_zone_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    sku_velocity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    abc_class = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    velocity_trend = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    reason_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    suitability_score = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ergonomic_score = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    abc_affinity_score = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    velocity_affinity_score = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    pick_density_score = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cumulative_weight_consumed_kg = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cumulative_volume_consumed_cm3 = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    travel_distance_delta_meters = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    estimated_move_labor_minutes = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    estimated_monthly_net_savings_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    gross_monthly_savings_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    handling_cost_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    movement_cost_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    disruption_cost_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_relocation_cost_usd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    urgency_score = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    urgency_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    priority_score = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    disruption_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_operationally_feasible = table.Column<bool>(type: "boolean", nullable: false),
                    is_hard_constraint_violated = table.Column<bool>(type: "boolean", nullable: false),
                    violated_constraints_summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_projection_stale_or_missing = table.Column<bool>(type: "boolean", nullable: false),
                    classification_source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    projection_calculated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    projection_age_days = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_slotting_recommendations", x => x.recommendation_id);
                    table.ForeignKey(
                        name: "fk_slotting_recommendations_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_slotting_recommendations_locations_current_location_id",
                        column: x => x.current_location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_slotting_recommendations_locations_proposed_location_id",
                        column: x => x.proposed_location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_slotting_recommendations_slotting_recommendation_batches_ba",
                        column: x => x.batch_id,
                        principalTable: "slotting_recommendation_batches",
                        principalColumn: "batch_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "handling_units",
                columns: table => new
                {
                    handling_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lpn_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    unit_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    current_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    current_pick_wave_id = table.Column<Guid>(type: "uuid", nullable: true),
                    current_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    max_weight_kg = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    max_volume_cm3 = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_handling_units", x => x.handling_unit_id);
                    table.ForeignKey(
                        name: "fk_handling_units_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_handling_units_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_handling_units_locations_current_location_id",
                        column: x => x.current_location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_handling_units_outbound_orders_current_outbound_order_id",
                        column: x => x.current_order_id,
                        principalTable: "outbound_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_handling_units_pick_waves_current_pick_wave_id",
                        column: x => x.current_pick_wave_id,
                        principalTable: "pick_waves",
                        principalColumn: "pick_wave_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "pick_wave_orders",
                columns: table => new
                {
                    pick_wave_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pick_wave_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outbound_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pick_wave_orders", x => x.pick_wave_order_id);
                    table.ForeignKey(
                        name: "fk_pick_wave_orders_outbound_orders_outbound_order_id",
                        column: x => x.outbound_order_id,
                        principalTable: "outbound_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pick_wave_orders_pick_waves_pick_wave_id",
                        column: x => x.pick_wave_id,
                        principalTable: "pick_waves",
                        principalColumn: "pick_wave_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "allocation_shortages",
                columns: table => new
                {
                    shortage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity_requested = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    quantity_allocated = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    shortage_quantity = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recorded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_resolved = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_allocation_shortages", x => x.shortage_id);
                    table.ForeignKey(
                        name: "fk_allocation_shortages_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_allocation_shortages_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_allocation_shortages_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_allocation_shortages_outbound_order_lines_outbound_order_li",
                        column: x => x.line_id,
                        principalTable: "outbound_order_lines",
                        principalColumn: "line_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_allocation_shortages_outbound_orders_outbound_order_id",
                        column: x => x.order_id,
                        principalTable: "outbound_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dock_appointments",
                columns: table => new
                {
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asn_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dock_door_id = table.Column<Guid>(type: "uuid", nullable: true),
                    appointment_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    start_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    carrier_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    trailer_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    driver_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    driver_phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dock_appointments", x => x.appointment_id);
                    table.ForeignKey(
                        name: "fk_dock_appointments_advance_shipping_notices_asn_id",
                        column: x => x.asn_id,
                        principalTable: "advance_shipping_notices",
                        principalColumn: "asn_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dock_appointments_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dock_appointments_dock_doors_dock_door_id",
                        column: x => x.dock_door_id,
                        principalTable: "dock_doors",
                        principalColumn: "dock_door_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_dock_appointments_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "asn_line_items",
                columns: table => new
                {
                    asn_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asn_id = table.Column<Guid>(type: "uuid", nullable: false),
                    po_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_qty = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    received_qty = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false, defaultValue: 0m),
                    expected_lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    manufacture_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expiry_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    packaging_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asn_line_items", x => x.asn_line_id);
                    table.ForeignKey(
                        name: "fk_asn_line_items_advance_shipping_notices_asn_id",
                        column: x => x.asn_id,
                        principalTable: "advance_shipping_notices",
                        principalColumn: "asn_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_asn_line_items_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_asn_line_items_packaging_units_packaging_unit_id",
                        column: x => x.packaging_unit_id,
                        principalTable: "packaging_units",
                        principalColumn: "unit_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_asn_line_items_purchase_order_lines_purchase_order_line_id",
                        column: x => x.po_line_id,
                        principalTable: "purchase_order_lines",
                        principalColumn: "po_line_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "rate_card_tiers",
                columns: table => new
                {
                    tier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rate_card_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tier_index = table.Column<int>(type: "integer", nullable: false),
                    tier_min_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    tier_max_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rate_card_tiers", x => x.tier_id);
                    table.ForeignKey(
                        name: "fk_rate_card_tiers_rate_card_lines_rate_card_line_id",
                        column: x => x.rate_card_line_id,
                        principalTable: "rate_card_lines",
                        principalColumn: "line_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bills_of_lading",
                columns: table => new
                {
                    bol_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bol_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    carrier_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    service_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    trailer_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    seal_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    total_package_count = table.Column<int>(type: "integer", nullable: false),
                    total_gross_weight_kg = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    total_volume_cm3 = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    freight_class = table.Column<string>(type: "text", nullable: true),
                    special_instructions = table.Column<string>(type: "text", nullable: true),
                    shipper_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    shipper_address_snapshot = table.Column<string>(type: "text", nullable: false),
                    consignee_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    consignee_address_snapshot = table.Column<string>(type: "text", nullable: false),
                    package_tracking_numbers = table.Column<string>(type: "text", nullable: false),
                    document_snapshot_json = table.Column<string>(type: "text", nullable: false),
                    prepared_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    authorized_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    issued_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_voided = table.Column<bool>(type: "boolean", nullable: false),
                    void_reason = table.Column<string>(type: "text", nullable: true),
                    voided_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bills_of_lading", x => x.bol_id);
                    table.ForeignKey(
                        name: "fk_bills_of_lading_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bills_of_lading_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bills_of_lading_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalTable: "shipments",
                        principalColumn: "shipment_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shipment_custody_transfers",
                columns: table => new
                {
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    driver_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    driver_license_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    driver_signature_acknowledgment = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    trailer_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    seal_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    bill_of_lading_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    total_packages_confirmed = table.Column<int>(type: "integer", nullable: false),
                    total_weight_kg = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    authorized_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transferred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipment_custody_transfers", x => x.transfer_id);
                    table.ForeignKey(
                        name: "fk_shipment_custody_transfers_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_shipment_custody_transfers_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_shipment_custody_transfers_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalTable: "shipments",
                        principalColumn: "shipment_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shipment_custody_transfers_users_authorized_by_user_id",
                        column: x => x.authorized_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "daily_inventory_snapshot_records",
                columns: table => new
                {
                    record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    handling_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lpn_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    holding_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    pallet_count = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    carton_count = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cbm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    has_missing_dimensions = table.Column<bool>(type: "boolean", nullable: false),
                    is_billable = table.Column<bool>(type: "boolean", nullable: false),
                    source_inventory_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_inventory_snapshot_records", x => x.record_id);
                    table.ForeignKey(
                        name: "fk_daily_inventory_snapshot_records_daily_inventory_snapshots_",
                        column: x => x.snapshot_id,
                        principalTable: "daily_inventory_snapshots",
                        principalColumn: "snapshot_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_daily_inventory_snapshot_records_handling_units_handling_un",
                        column: x => x.handling_unit_id,
                        principalTable: "handling_units",
                        principalColumn: "handling_unit_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_daily_inventory_snapshot_records_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_daily_inventory_snapshot_records_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "packages",
                columns: table => new
                {
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outbound_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    handling_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    packaging_material_id = table.Column<Guid>(type: "uuid", nullable: true),
                    package_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    carton_sequence = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    gross_weight_kg = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false, defaultValue: 0m),
                    tare_weight_kg = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false, defaultValue: 0m),
                    net_weight_kg = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false, defaultValue: 0m),
                    length_cm = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false, defaultValue: 0m),
                    width_cm = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false, defaultValue: 0m),
                    height_cm = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false, defaultValue: 0m),
                    volumetric_divisor = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false, defaultValue: 5000m),
                    volumetric_weight_kg = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false, defaultValue: 0m),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    carrier_service_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    current_tracking_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "PendingHandover"),
                    tracking_status_updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    delivered_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    current_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    packed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    packed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_packages", x => x.package_id);
                    table.ForeignKey(
                        name: "fk_packages_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_packages_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_packages_handling_units_handling_unit_id",
                        column: x => x.handling_unit_id,
                        principalTable: "handling_units",
                        principalColumn: "handling_unit_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_packages_locations_current_location_id",
                        column: x => x.current_location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_packages_outbound_orders_outbound_order_id",
                        column: x => x.outbound_order_id,
                        principalTable: "outbound_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_packages_packaging_materials_packaging_material_id",
                        column: x => x.packaging_material_id,
                        principalTable: "packaging_materials",
                        principalColumn: "material_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_packages_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalTable: "shipments",
                        principalColumn: "shipment_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_packages_users_packed_by_user_id",
                        column: x => x.packed_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "stock_on_hand",
                columns: table => new
                {
                    stock_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    handling_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lpn_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    qty_available = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false, defaultValue: 0m),
                    qty_allocated = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false, defaultValue: 0m),
                    qty_reserved = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false, defaultValue: 0m),
                    qty_quarantined = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false, defaultValue: 0m),
                    qty_damaged = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false, defaultValue: 0m),
                    qty_on_hold = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false, defaultValue: 0m),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_counted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_on_hand", x => x.stock_id);
                    table.ForeignKey(
                        name: "fk_stock_on_hand_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_on_hand_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_on_hand_handling_units_handling_unit_id",
                        column: x => x.handling_unit_id,
                        principalTable: "handling_units",
                        principalColumn: "handling_unit_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_stock_on_hand_inventory_lots_lot_id",
                        column: x => x.lot_id,
                        principalTable: "inventory_lots",
                        principalColumn: "lot_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_stock_on_hand_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_on_hand_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "yard_visits",
                columns: table => new
                {
                    yard_visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    asn_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dock_appointment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visit_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    carrier_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    trailer_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    license_plate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    driver_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    driver_phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    check_in_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    departure_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    parking_location = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_yard_visits", x => x.yard_visit_id);
                    table.ForeignKey(
                        name: "fk_yard_visits_advance_shipping_notices_asn_id",
                        column: x => x.asn_id,
                        principalTable: "advance_shipping_notices",
                        principalColumn: "asn_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_yard_visits_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_yard_visits_dock_appointments_dock_appointment_id",
                        column: x => x.dock_appointment_id,
                        principalTable: "dock_appointments",
                        principalColumn: "appointment_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_yard_visits_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "carrier_cost_records",
                columns: table => new
                {
                    cost_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: true),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    carrier_service_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    carrier_invoice_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    base_transportation_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    fuel_surcharge = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    insurance_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    accessorial_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_carrier_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    billed_weight_kg = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    cost_assessed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    invoiced_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reconciled_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carrier_cost_records", x => x.cost_record_id);
                    table.ForeignKey(
                        name: "fk_carrier_cost_records_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_carrier_cost_records_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_carrier_cost_records_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "package_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_carrier_cost_records_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalTable: "shipments",
                        principalColumn: "shipment_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "carrier_operation_logs",
                columns: table => new
                {
                    log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    service_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    request_payload = table.Column<string>(type: "text", nullable: false),
                    response_payload = table.Column<string>(type: "text", nullable: true),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carrier_operation_logs", x => x.log_id);
                    table.ForeignKey(
                        name: "fk_carrier_operation_logs_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_carrier_operation_logs_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_carrier_operation_logs_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "package_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "proof_of_deliveries",
                columns: table => new
                {
                    pod_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    recipient_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    delivery_location_description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_signed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    signature_object_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    photo_object_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    signature_content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    photo_content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    signature_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    photo_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    is_storage_finalized = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    carrier_confirmation_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    carrier_event_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    evidence_source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    captured_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    delivered_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_superseded = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    superseded_by_pod_id = table.Column<Guid>(type: "uuid", nullable: true),
                    superseded_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    superseded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proof_of_deliveries", x => x.pod_id);
                    table.ForeignKey(
                        name: "fk_proof_of_deliveries_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_proof_of_deliveries_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_proof_of_deliveries_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "package_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_proof_of_deliveries_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalTable: "shipments",
                        principalColumn: "shipment_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "public_tracking_shares",
                columns: table => new
                {
                    share_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    public_token = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    masked_recipient_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    destination_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    destination_state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    masked_postal_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    access_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    max_access_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 500)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_public_tracking_shares", x => x.share_id);
                    table.ForeignKey(
                        name: "fk_public_tracking_shares_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_public_tracking_shares_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_public_tracking_shares_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "package_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_public_tracking_shares_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalTable: "shipments",
                        principalColumn: "shipment_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shipment_audit_logs",
                columns: table => new
                {
                    audit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    details = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipment_audit_logs", x => x.audit_id);
                    table.ForeignKey(
                        name: "fk_shipment_audit_logs_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_shipment_audit_logs_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_shipment_audit_logs_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "package_id");
                    table.ForeignKey(
                        name: "fk_shipment_audit_logs_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalTable: "shipments",
                        principalColumn: "shipment_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shipment_tracking_events",
                columns: table => new
                {
                    tracking_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    carrier_event_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    milestone_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    carrier_event_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    carrier_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    event_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    event_state_or_province = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    event_postal_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    event_country = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    event_fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    raw_payload_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipment_tracking_events", x => x.tracking_event_id);
                    table.ForeignKey(
                        name: "fk_shipment_tracking_events_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shipment_tracking_events_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shipment_tracking_events_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "package_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shipment_tracking_events_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalTable: "shipments",
                        principalColumn: "shipment_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shipping_labels",
                columns: table => new
                {
                    label_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    service_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    label_format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    label_data = table.Column<string>(type: "text", nullable: false),
                    routing_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    generated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    printed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    voided_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipping_labels", x => x.label_id);
                    table.ForeignKey(
                        name: "fk_shipping_labels_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "package_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inventory_adjustments",
                columns: table => new
                {
                    adj_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    qty_adjusted = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reason_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    accounting_impact = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    approved_by_user = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_adjustments", x => x.adj_id);
                    table.ForeignKey(
                        name: "fk_inventory_adjustments_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_adjustments_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_adjustments_inventory_lots_lot_id",
                        column: x => x.lot_id,
                        principalTable: "inventory_lots",
                        principalColumn: "lot_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_inventory_adjustments_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_adjustments_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_adjustments_stock_on_hand_stock_id",
                        column: x => x.stock_id,
                        principalTable: "stock_on_hand",
                        principalColumn: "stock_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_adjustments_users_approved_by_user_id",
                        column: x => x.approved_by_user,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "inventory_allocations",
                columns: table => new
                {
                    allocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lpn_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    quantity_requested = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    quantity_allocated = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    quantity_released = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false, defaultValue: 0m),
                    quantity_picked = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false, defaultValue: 0m),
                    picked_stock_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_handling_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_lpn_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    strategy_used = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    allocated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    allocated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    released_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    released_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    release_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_allocations", x => x.allocation_id);
                    table.ForeignKey(
                        name: "fk_inventory_allocations_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_allocations_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_allocations_handling_units_target_handling_unit_id",
                        column: x => x.target_handling_unit_id,
                        principalTable: "handling_units",
                        principalColumn: "handling_unit_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_inventory_allocations_inventory_lots_lot_id",
                        column: x => x.lot_id,
                        principalTable: "inventory_lots",
                        principalColumn: "lot_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_inventory_allocations_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_allocations_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_allocations_outbound_order_lines_outbound_order_l",
                        column: x => x.line_id,
                        principalTable: "outbound_order_lines",
                        principalColumn: "line_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_inventory_allocations_outbound_orders_outbound_order_id",
                        column: x => x.order_id,
                        principalTable: "outbound_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_inventory_allocations_stock_on_hand_picked_stock_on_hand_id",
                        column: x => x.picked_stock_id,
                        principalTable: "stock_on_hand",
                        principalColumn: "stock_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_inventory_allocations_stock_on_hand_stock_on_hand_id",
                        column: x => x.stock_id,
                        principalTable: "stock_on_hand",
                        principalColumn: "stock_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dock_assignments",
                columns: table => new
                {
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dock_door_id = table.Column<Guid>(type: "uuid", nullable: false),
                    yard_visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asn_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    arrived_at_dock = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    released_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    assigned_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dock_assignments", x => x.assignment_id);
                    table.ForeignKey(
                        name: "fk_dock_assignments_advance_shipping_notices_asn_id",
                        column: x => x.asn_id,
                        principalTable: "advance_shipping_notices",
                        principalColumn: "asn_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_dock_assignments_dock_doors_dock_door_id",
                        column: x => x.dock_door_id,
                        principalTable: "dock_doors",
                        principalColumn: "dock_door_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dock_assignments_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dock_assignments_yard_visits_yard_visit_id",
                        column: x => x.yard_visit_id,
                        principalTable: "yard_visits",
                        principalColumn: "yard_visit_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "receiving_sessions",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dock_door_id = table.Column<Guid>(type: "uuid", nullable: true),
                    yard_visit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    asn_id = table.Column<Guid>(type: "uuid", nullable: true),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    session_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    received_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    initial_probe_temperature = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    seal_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    seal_intact = table.Column<bool>(type: "boolean", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_blind_receiving = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    requires_supervisor_approval = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_supervisor_approved = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receiving_sessions", x => x.session_id);
                    table.ForeignKey(
                        name: "fk_receiving_sessions_advance_shipping_notices_asn_id",
                        column: x => x.asn_id,
                        principalTable: "advance_shipping_notices",
                        principalColumn: "asn_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_receiving_sessions_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_receiving_sessions_dock_doors_dock_door_id",
                        column: x => x.dock_door_id,
                        principalTable: "dock_doors",
                        principalColumn: "dock_door_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_receiving_sessions_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_receiving_sessions_purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalTable: "purchase_orders",
                        principalColumn: "purchase_order_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_receiving_sessions_users_approved_by_user_id",
                        column: x => x.approved_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_receiving_sessions_users_received_by_user_id",
                        column: x => x.received_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_receiving_sessions_yard_visits_yard_visit_id",
                        column: x => x.yard_visit_id,
                        principalTable: "yard_visits",
                        principalColumn: "yard_visit_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "carrier_accessorial_charges",
                columns: table => new
                {
                    accessorial_charge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    carrier_cost_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: true),
                    accessorial_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    carrier_accessorial_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    carrier_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    client_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    billable_treatment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    assessed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    source_document_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_reconciled = table.Column<bool>(type: "boolean", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carrier_accessorial_charges", x => x.accessorial_charge_id);
                    table.ForeignKey(
                        name: "fk_carrier_accessorial_charges_carrier_cost_records_carrier_co",
                        column: x => x.carrier_cost_record_id,
                        principalTable: "carrier_cost_records",
                        principalColumn: "cost_record_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_carrier_accessorial_charges_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_carrier_accessorial_charges_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_carrier_accessorial_charges_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "package_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_carrier_accessorial_charges_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalTable: "shipments",
                        principalColumn: "shipment_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shipment_freight_margin_records",
                columns: table => new
                {
                    margin_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    carrier_cost_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    freight_agreement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    billable_activity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    carrier_service_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    pricing_model_applied = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    package_count = table.Column<int>(type: "integer", nullable: false),
                    carrier_base_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    carrier_fuel_surcharge = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    carrier_insurance_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    carrier_accessorial_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_carrier_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    client_base_freight = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    client_fuel_charge = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    client_insurance_charge = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    client_accessorial_charge = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    client_markup_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    client_handling_fee = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_client_freight = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    gross_margin_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    markup_percentage = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    margin_percentage = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    rated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_reconciled = table.Column<bool>(type: "boolean", nullable: false),
                    adjustment_count = table.Column<int>(type: "integer", nullable: false),
                    net_adjusted_carrier_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    net_adjusted_client_freight = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    net_adjusted_margin = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipment_freight_margin_records", x => x.margin_record_id);
                    table.ForeignKey(
                        name: "fk_shipment_freight_margin_records_billable_activities_billabl",
                        column: x => x.billable_activity_id,
                        principalTable: "billable_activities",
                        principalColumn: "activity_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_shipment_freight_margin_records_carrier_cost_records_carrie",
                        column: x => x.carrier_cost_record_id,
                        principalTable: "carrier_cost_records",
                        principalColumn: "cost_record_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shipment_freight_margin_records_client_freight_agreements_f",
                        column: x => x.freight_agreement_id,
                        principalTable: "client_freight_agreements",
                        principalColumn: "freight_agreement_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_shipment_freight_margin_records_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shipment_freight_margin_records_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shipment_freight_margin_records_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalTable: "shipments",
                        principalColumn: "shipment_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "package_lines",
                columns: table => new
                {
                    package_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outbound_order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    source_handling_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    allocation_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_package_lines", x => x.package_line_id);
                    table.ForeignKey(
                        name: "fk_package_lines_handling_units_source_handling_unit_id",
                        column: x => x.source_handling_unit_id,
                        principalTable: "handling_units",
                        principalColumn: "handling_unit_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_package_lines_inventory_allocations_inventory_allocation_id",
                        column: x => x.allocation_id,
                        principalTable: "inventory_allocations",
                        principalColumn: "allocation_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_package_lines_inventory_lots_lot_id",
                        column: x => x.lot_id,
                        principalTable: "inventory_lots",
                        principalColumn: "lot_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_package_lines_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_package_lines_outbound_order_lines_outbound_order_line_id",
                        column: x => x.outbound_order_line_id,
                        principalTable: "outbound_order_lines",
                        principalColumn: "line_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_package_lines_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "package_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pick_tasks",
                columns: table => new
                {
                    pick_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pick_wave_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    allocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expected_lpn_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    target_handling_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_tote_barcode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    quantity_to_pick = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    quantity_picked = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false, defaultValue: 0m),
                    pick_sequence = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    short_pick_reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    short_pick_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    assigned_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pick_tasks", x => x.pick_task_id);
                    table.ForeignKey(
                        name: "fk_pick_tasks_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pick_tasks_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pick_tasks_handling_units_target_handling_unit_id",
                        column: x => x.target_handling_unit_id,
                        principalTable: "handling_units",
                        principalColumn: "handling_unit_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_pick_tasks_inventory_allocations_inventory_allocation_id",
                        column: x => x.allocation_id,
                        principalTable: "inventory_allocations",
                        principalColumn: "allocation_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pick_tasks_inventory_lots_lot_id",
                        column: x => x.lot_id,
                        principalTable: "inventory_lots",
                        principalColumn: "lot_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_pick_tasks_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pick_tasks_locations_destination_location_id",
                        column: x => x.destination_location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_pick_tasks_locations_source_location_id",
                        column: x => x.source_location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pick_tasks_outbound_order_lines_outbound_order_line_id",
                        column: x => x.line_id,
                        principalTable: "outbound_order_lines",
                        principalColumn: "line_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pick_tasks_outbound_orders_outbound_order_id",
                        column: x => x.order_id,
                        principalTable: "outbound_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pick_tasks_pick_waves_pick_wave_id",
                        column: x => x.pick_wave_id,
                        principalTable: "pick_waves",
                        principalColumn: "pick_wave_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_pick_tasks_stock_on_hand_stock_on_hand_id",
                        column: x => x.stock_id,
                        principalTable: "stock_on_hand",
                        principalColumn: "stock_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pick_tasks_users_assigned_user_id",
                        column: x => x.assigned_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "cross_dock_plans",
                columns: table => new
                {
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receiving_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    plan_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    total_planned_quantity = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    total_executed_quantity = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cross_dock_plans", x => x.plan_id);
                    table.ForeignKey(
                        name: "fk_cross_dock_plans_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cross_dock_plans_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cross_dock_plans_receiving_sessions_receiving_session_id",
                        column: x => x.receiving_session_id,
                        principalTable: "receiving_sessions",
                        principalColumn: "session_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "receipt_lines",
                columns: table => new
                {
                    receipt_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    asn_line_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lpn = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    quantity_received = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    quantity_expected = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    lot_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    serial_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    catch_weight_kg = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    manufacture_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expiration_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    condition = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    staging_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    scanned_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipt_lines", x => x.receipt_line_id);
                    table.ForeignKey(
                        name: "fk_receipt_lines_asn_line_items_asn_line_item_id",
                        column: x => x.asn_line_item_id,
                        principalTable: "asn_line_items",
                        principalColumn: "asn_line_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_receipt_lines_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_receipt_lines_locations_staging_location_id",
                        column: x => x.staging_location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_receipt_lines_purchase_order_lines_purchase_order_line_id",
                        column: x => x.purchase_order_line_id,
                        principalTable: "purchase_order_lines",
                        principalColumn: "po_line_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_receipt_lines_receiving_sessions_receiving_session_id",
                        column: x => x.session_id,
                        principalTable: "receiving_sessions",
                        principalColumn: "session_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "carrier_cost_adjustments",
                columns: table => new
                {
                    adjustment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    carrier_cost_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_freight_margin_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    adjustment_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    carrier_invoice_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    original_carrier_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    adjusted_carrier_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    carrier_cost_delta = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    client_freight_delta = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    billed_weight_kg = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    reason = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    adjusted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    billable_activity_adjustment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carrier_cost_adjustments", x => x.adjustment_id);
                    table.ForeignKey(
                        name: "fk_carrier_cost_adjustments_carrier_cost_records_carrier_cost_",
                        column: x => x.carrier_cost_record_id,
                        principalTable: "carrier_cost_records",
                        principalColumn: "cost_record_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_carrier_cost_adjustments_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_carrier_cost_adjustments_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_carrier_cost_adjustments_shipment_freight_margin_records_sh",
                        column: x => x.shipment_freight_margin_record_id,
                        principalTable: "shipment_freight_margin_records",
                        principalColumn: "margin_record_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "pick_exceptions",
                columns: table => new
                {
                    exception_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pick_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expected_quantity = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    actual_quantity_picked = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reported_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    generated_cycle_count_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_lock_applied = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pick_exceptions", x => x.exception_id);
                    table.ForeignKey(
                        name: "fk_pick_exceptions_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pick_exceptions_cycle_counts_generated_cycle_count_id",
                        column: x => x.generated_cycle_count_id,
                        principalTable: "cycle_counts",
                        principalColumn: "count_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_pick_exceptions_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pick_exceptions_inventory_lots_lot_id",
                        column: x => x.lot_id,
                        principalTable: "inventory_lots",
                        principalColumn: "lot_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_pick_exceptions_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pick_exceptions_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pick_exceptions_outbound_order_lines_outbound_order_line_id",
                        column: x => x.line_id,
                        principalTable: "outbound_order_lines",
                        principalColumn: "line_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pick_exceptions_outbound_orders_outbound_order_id",
                        column: x => x.order_id,
                        principalTable: "outbound_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pick_exceptions_pick_tasks_pick_task_id",
                        column: x => x.pick_task_id,
                        principalTable: "pick_tasks",
                        principalColumn: "pick_task_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pick_task_status_histories",
                columns: table => new
                {
                    history_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pick_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    to_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pick_task_status_histories", x => x.history_id);
                    table.ForeignKey(
                        name: "fk_pick_task_status_histories_pick_tasks_pick_task_id",
                        column: x => x.pick_task_id,
                        principalTable: "pick_tasks",
                        principalColumn: "pick_task_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cross_dock_plan_lines",
                columns: table => new
                {
                    plan_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inbound_lpn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    source_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inbound_available_qty = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    outbound_demand_qty = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    planned_qty = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    executed_qty = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cross_dock_plan_lines", x => x.plan_line_id);
                    table.ForeignKey(
                        name: "fk_cross_dock_plan_lines_cross_dock_plans_cross_dock_plan_id",
                        column: x => x.plan_id,
                        principalTable: "cross_dock_plans",
                        principalColumn: "plan_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cross_dock_plan_lines_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cross_dock_plan_lines_locations_source_location_id",
                        column: x => x.source_location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cross_dock_plan_lines_locations_target_location_id",
                        column: x => x.target_location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cross_dock_plan_lines_receipt_lines_receipt_line_id",
                        column: x => x.receipt_line_id,
                        principalTable: "receipt_lines",
                        principalColumn: "receipt_line_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "putaway_tasks",
                columns: table => new
                {
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    lpn_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lpn_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    source_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    suggested_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actual_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    priority = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    assigned_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    exception_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    target_location_id1 = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_putaway_tasks", x => x.task_id);
                    table.ForeignKey(
                        name: "fk_putaway_tasks_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_putaway_tasks_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_putaway_tasks_inventory_lots_lot_id",
                        column: x => x.lot_id,
                        principalTable: "inventory_lots",
                        principalColumn: "lot_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_putaway_tasks_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_putaway_tasks_locations_actual_location_id",
                        column: x => x.actual_location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_putaway_tasks_locations_source_location_id",
                        column: x => x.source_location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_putaway_tasks_locations_suggested_location_id",
                        column: x => x.suggested_location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_putaway_tasks_locations_target_location_id1",
                        column: x => x.target_location_id1,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_putaway_tasks_receipt_lines_lpn_id",
                        column: x => x.lpn_id,
                        principalTable: "receipt_lines",
                        principalColumn: "receipt_line_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_putaway_tasks_users_assigned_user_id",
                        column: x => x.assigned_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "receiving_discrepancies",
                columns: table => new
                {
                    discrepancy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    discrepancy_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    discrepant_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reported_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receiving_discrepancies", x => x.discrepancy_id);
                    table.ForeignKey(
                        name: "fk_receiving_discrepancies_receipt_lines_receipt_line_id",
                        column: x => x.receipt_line_id,
                        principalTable: "receipt_lines",
                        principalColumn: "receipt_line_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_receiving_discrepancies_receiving_sessions_receiving_sessio",
                        column: x => x.session_id,
                        principalTable: "receiving_sessions",
                        principalColumn: "session_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_receiving_discrepancies_users_resolved_by_user_id",
                        column: x => x.resolved_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "cross_dock_allocations",
                columns: table => new
                {
                    allocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inbound_lpn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    outbound_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outbound_order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    allocated_quantity = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    allocated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    transferred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cross_dock_allocations", x => x.allocation_id);
                    table.ForeignKey(
                        name: "fk_cross_dock_allocations_cross_dock_plan_lines_cross_dock_pla",
                        column: x => x.plan_line_id,
                        principalTable: "cross_dock_plan_lines",
                        principalColumn: "plan_line_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cross_dock_allocations_inventory_lots_lot_id",
                        column: x => x.lot_id,
                        principalTable: "inventory_lots",
                        principalColumn: "lot_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_cross_dock_allocations_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cross_dock_allocations_outbound_order_lines_outbound_order_",
                        column: x => x.outbound_order_line_id,
                        principalTable: "outbound_order_lines",
                        principalColumn: "line_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cross_dock_allocations_outbound_orders_outbound_order_id",
                        column: x => x.outbound_order_id,
                        principalTable: "outbound_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cross_dock_tasks",
                columns: table => new
                {
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    task_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    lpn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actual_destination_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    priority = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    assigned_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    scanned_lpn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    scanned_destination_barcode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cross_dock_tasks", x => x.task_id);
                    table.ForeignKey(
                        name: "fk_cross_dock_tasks_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cross_dock_tasks_cross_dock_plan_lines_cross_dock_plan_line",
                        column: x => x.plan_line_id,
                        principalTable: "cross_dock_plan_lines",
                        principalColumn: "plan_line_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cross_dock_tasks_cross_dock_plans_cross_dock_plan_id",
                        column: x => x.plan_id,
                        principalTable: "cross_dock_plans",
                        principalColumn: "plan_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cross_dock_tasks_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cross_dock_tasks_inventory_lots_lot_id",
                        column: x => x.lot_id,
                        principalTable: "inventory_lots",
                        principalColumn: "lot_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_cross_dock_tasks_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cross_dock_tasks_locations_actual_destination_location_id",
                        column: x => x.actual_destination_location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cross_dock_tasks_locations_destination_location_id",
                        column: x => x.destination_location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cross_dock_tasks_locations_source_location_id",
                        column: x => x.source_location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cross_dock_tasks_users_assigned_user_id",
                        column: x => x.assigned_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "cross_dock_exceptions",
                columns: table => new
                {
                    exception_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    quantity_discrepant = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    reported_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reported_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_resolved = table.Column<bool>(type: "boolean", nullable: false),
                    resolution_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    resolved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cross_dock_exceptions", x => x.exception_id);
                    table.ForeignKey(
                        name: "fk_cross_dock_exceptions_cross_dock_plans_cross_dock_plan_id",
                        column: x => x.plan_id,
                        principalTable: "cross_dock_plans",
                        principalColumn: "plan_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cross_dock_exceptions_cross_dock_tasks_cross_dock_task_id",
                        column: x => x.task_id,
                        principalTable: "cross_dock_tasks",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_cross_dock_exceptions_users_reported_by_user_id",
                        column: x => x.reported_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "credit_note_lines",
                columns: table => new
                {
                    credit_note_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_note_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_invoice_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_note_lines", x => x.credit_note_line_id);
                    table.ForeignKey(
                        name: "fk_credit_note_lines_invoice_lines_original_invoice_line_id",
                        column: x => x.original_invoice_line_id,
                        principalTable: "invoice_lines",
                        principalColumn: "invoice_line_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "credit_notes",
                columns: table => new
                {
                    credit_note_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispute_id = table.Column<Guid>(type: "uuid", nullable: true),
                    credit_note_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    issue_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    allocated_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unallocated_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_notes", x => x.credit_note_id);
                    table.ForeignKey(
                        name: "fk_credit_notes_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_credit_notes_invoices_original_invoice_id",
                        column: x => x.original_invoice_id,
                        principalTable: "invoices",
                        principalColumn: "invoice_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoice_disputes",
                columns: table => new
                {
                    dispute_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dispute_reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    disputed_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reason_description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    submitted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    review_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    resolved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    approved_credit_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    credit_note_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_disputes", x => x.dispute_id);
                    table.ForeignKey(
                        name: "fk_invoice_disputes_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoice_disputes_credit_notes_credit_note_id",
                        column: x => x.credit_note_id,
                        principalTable: "credit_notes",
                        principalColumn: "credit_note_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_invoice_disputes_invoice_lines_invoice_line_id",
                        column: x => x.invoice_line_id,
                        principalTable: "invoice_lines",
                        principalColumn: "invoice_line_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_invoice_disputes_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "invoice_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "delivery_exception_contact_logs",
                columns: table => new
                {
                    contact_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exception_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    outcome = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    contacted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    staff_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    resolution_action = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_exception_contact_logs", x => x.contact_log_id);
                    table.ForeignKey(
                        name: "fk_delivery_exception_contact_logs_users_staff_user_id",
                        column: x => x.staff_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "return_authorizations",
                columns: table => new
                {
                    return_authorization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin_facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    authorization_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    rma_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_shipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_exception_id = table.Column<Guid>(type: "uuid", nullable: true),
                    return_shipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    authorized_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    authorized_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    in_transit_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    delivered_at_facility_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    inbound_handoff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    authorization_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    cancellation_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    cancelled_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancelled_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_return_authorizations", x => x.return_authorization_id);
                    table.ForeignKey(
                        name: "fk_return_authorizations_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_authorizations_facilities_destination_facility_id",
                        column: x => x.destination_facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_authorizations_facilities_origin_facility_id",
                        column: x => x.origin_facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_authorizations_outbound_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "outbound_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_authorizations_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "package_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_authorizations_shipments_original_shipment_id",
                        column: x => x.original_shipment_id,
                        principalTable: "shipments",
                        principalColumn: "shipment_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_authorizations_users_authorized_by_user_id",
                        column: x => x.authorized_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_authorizations_users_cancelled_by_user_id",
                        column: x => x.cancelled_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shipment_delivery_exceptions",
                columns: table => new
                {
                    exception_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    return_shipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    return_authorization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    exception_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    severity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    carrier_exception_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    carrier_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    reattempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    scheduled_reattempt_date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    contact_attempts_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    corrected_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    hold_depot_location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    resolution = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    resolution_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    resolved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reported_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    latitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: true),
                    carrier_event_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    raw_payload = table.Column<string>(type: "text", nullable: true),
                    event_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipment_delivery_exceptions", x => x.exception_id);
                    table.ForeignKey(
                        name: "fk_shipment_delivery_exceptions_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shipment_delivery_exceptions_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shipment_delivery_exceptions_outbound_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "outbound_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shipment_delivery_exceptions_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "package_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shipment_delivery_exceptions_return_authorizations_return_a",
                        column: x => x.return_authorization_id,
                        principalTable: "return_authorizations",
                        principalColumn: "return_authorization_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shipment_delivery_exceptions_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalTable: "shipments",
                        principalColumn: "shipment_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shipment_delivery_exceptions_users_reported_by_user_id",
                        column: x => x.reported_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shipment_delivery_exceptions_users_resolved_by_user_id",
                        column: x => x.resolved_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "return_shipments",
                columns: table => new
                {
                    return_shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin_facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_shipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_exception_id = table.Column<Guid>(type: "uuid", nullable: true),
                    return_authorization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    return_rma_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    authorized_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    authorized_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    authorization_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    carrier_service_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    label_generated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    carrier_shipment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    label_format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "ZPL"),
                    label_data = table.Column<string>(type: "text", nullable: true),
                    label_object_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    rate_charged = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "USD"),
                    carrier_cancellation_confirmed = table.Column<bool>(type: "boolean", nullable: true),
                    carrier_cancellation_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    origin_address_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    origin_address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    origin_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    origin_state = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    origin_postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    origin_country = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    destination_address_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    destination_address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    destination_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    destination_state = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    destination_postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    destination_country = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    carrier_handover_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expected_warehouse_arrival_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    delivered_at_warehouse_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    received_and_inspected_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    inspected_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inbound_handoff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    final_disposition = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    disposition_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_return_shipments", x => x.return_shipment_id);
                    table.ForeignKey(
                        name: "fk_return_shipments_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_shipments_facilities_destination_facility_id",
                        column: x => x.destination_facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_shipments_facilities_origin_facility_id",
                        column: x => x.origin_facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_shipments_outbound_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "outbound_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_shipments_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "package_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_shipments_return_authorizations_return_authorization",
                        column: x => x.return_authorization_id,
                        principalTable: "return_authorizations",
                        principalColumn: "return_authorization_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_shipments_shipment_delivery_exceptions_delivery_exce",
                        column: x => x.delivery_exception_id,
                        principalTable: "shipment_delivery_exceptions",
                        principalColumn: "exception_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_shipments_shipments_original_shipment_id",
                        column: x => x.original_shipment_id,
                        principalTable: "shipments",
                        principalColumn: "shipment_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_shipments_users_authorized_by_user_id",
                        column: x => x.authorized_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_shipments_users_inspected_by_user_id",
                        column: x => x.inspected_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "return_inbound_handoffs",
                columns: table => new
                {
                    handoff_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_authorization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handoff_reference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    return_rma_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    carrier_delivered_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    handoff_created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    dock_door_location = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_return_inbound_handoffs", x => x.handoff_id);
                    table.ForeignKey(
                        name: "fk_return_inbound_handoffs_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_inbound_handoffs_facilities_destination_facility_id",
                        column: x => x.destination_facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_inbound_handoffs_outbound_orders_original_order_id",
                        column: x => x.original_order_id,
                        principalTable: "outbound_orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_inbound_handoffs_packages_original_package_id",
                        column: x => x.original_package_id,
                        principalTable: "packages",
                        principalColumn: "package_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_inbound_handoffs_return_authorizations_return_author",
                        column: x => x.return_authorization_id,
                        principalTable: "return_authorizations",
                        principalColumn: "return_authorization_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_inbound_handoffs_return_shipments_return_shipment_id",
                        column: x => x.return_shipment_id,
                        principalTable: "return_shipments",
                        principalColumn: "return_shipment_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "return_tracking_events",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    event_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    carrier_status_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    carrier_status_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    event_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_return_tracking_events", x => x.event_id);
                    table.ForeignKey(
                        name: "fk_return_tracking_events_return_shipments_return_shipment_id",
                        column: x => x.return_shipment_id,
                        principalTable: "return_shipments",
                        principalColumn: "return_shipment_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "return_receiving_sessions",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_inbound_handoff_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_authorization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    received_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    physical_arrival_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    inspected_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    package_condition = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    package_condition_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_inventory_posted = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_return_receiving_sessions", x => x.session_id);
                    table.ForeignKey(
                        name: "fk_return_receiving_sessions_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_receiving_sessions_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_receiving_sessions_return_authorizations_return_auth",
                        column: x => x.return_authorization_id,
                        principalTable: "return_authorizations",
                        principalColumn: "return_authorization_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_receiving_sessions_return_inbound_handoffs_return_in",
                        column: x => x.return_inbound_handoff_id,
                        principalTable: "return_inbound_handoffs",
                        principalColumn: "handoff_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_receiving_sessions_return_shipments_return_shipment_",
                        column: x => x.return_shipment_id,
                        principalTable: "return_shipments",
                        principalColumn: "return_shipment_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_receiving_sessions_users_received_by_user_id",
                        column: x => x.received_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "return_receiving_discrepancies",
                columns: table => new
                {
                    discrepancy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_receiving_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expected_sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    actual_sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    discrepancy_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    expected_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    received_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    discrepancy_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    reported_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_return_receiving_discrepancies", x => x.discrepancy_id);
                    table.ForeignKey(
                        name: "fk_return_receiving_discrepancies_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_receiving_discrepancies_return_receiving_sessions_re",
                        column: x => x.return_receiving_session_id,
                        principalTable: "return_receiving_sessions",
                        principalColumn: "session_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "return_receiving_lines",
                columns: table => new
                {
                    line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_receiving_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_order_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    expected_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    received_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    accepted_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    damaged_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    quarantined_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    disposition = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    target_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inspection_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    inspected_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_inventory_posted = table.Column<bool>(type: "boolean", nullable: false),
                    inventory_transaction_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_return_receiving_lines", x => x.line_id);
                    table.ForeignKey(
                        name: "fk_return_receiving_lines_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_receiving_lines_locations_target_location_id",
                        column: x => x.target_location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_receiving_lines_return_receiving_sessions_return_rec",
                        column: x => x.return_receiving_session_id,
                        principalTable: "return_receiving_sessions",
                        principalColumn: "session_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_abc_classification_policies_facility_client_active",
                table: "abc_classification_policies",
                columns: new[] { "facility_id", "client_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_abc_scope_versions_facility_client_criteria_window",
                table: "abc_classification_scope_versions",
                columns: new[] { "facility_id", "client_id", "criteria", "observation_window" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_advance_shipping_notices_client_id_asn_number",
                table: "advance_shipping_notices",
                columns: new[] { "client_id", "asn_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_advance_shipping_notices_facility_id",
                table: "advance_shipping_notices",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_advance_shipping_notices_purchase_order_id",
                table: "advance_shipping_notices",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_advance_shipping_notices_supplier_id",
                table: "advance_shipping_notices",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_allocation_policies_client_id_facility_id_item_category_id",
                table: "allocation_policies",
                columns: new[] { "client_id", "facility_id", "item_category_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_allocation_policies_facility_id",
                table: "allocation_policies",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_allocation_policies_item_category_id",
                table: "allocation_policies",
                column: "item_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_allocation_shortages_client_id_facility_id_item_id_is_resol",
                table: "allocation_shortages",
                columns: new[] { "client_id", "facility_id", "item_id", "is_resolved" });

            migrationBuilder.CreateIndex(
                name: "ix_allocation_shortages_facility_id",
                table: "allocation_shortages",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_allocation_shortages_item_id",
                table: "allocation_shortages",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_allocation_shortages_outbound_order_id",
                table: "allocation_shortages",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_allocation_shortages_outbound_order_line_id",
                table: "allocation_shortages",
                column: "line_id");

            migrationBuilder.CreateIndex(
                name: "ix_analytics_event_deduplications_proj_event",
                table: "analytics_event_deduplications",
                columns: new[] { "projection_name", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asn_line_items_asn_id",
                table: "asn_line_items",
                column: "asn_id");

            migrationBuilder.CreateIndex(
                name: "ix_asn_line_items_item_id",
                table: "asn_line_items",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_asn_line_items_packaging_unit_id",
                table: "asn_line_items",
                column: "packaging_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_asn_line_items_purchase_order_line_id",
                table: "asn_line_items",
                column: "po_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_billable_activities_client_idempotency",
                table: "billable_activities",
                columns: new[] { "client_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_billable_activities_client_status",
                table: "billable_activities",
                columns: new[] { "client_id", "status", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_billable_activities_facility_id",
                table: "billable_activities",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_billable_activities_period",
                table: "billable_activities",
                columns: new[] { "billing_period_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_billable_activities_source",
                table: "billable_activities",
                columns: new[] { "client_id", "source_type", "source_id" });

            migrationBuilder.CreateIndex(
                name: "ix_billing_periods_client_code",
                table: "billing_periods",
                columns: new[] { "client_id", "period_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_billing_periods_client_dates",
                table: "billing_periods",
                columns: new[] { "client_id", "start_date_utc", "end_date_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_billing_periods_client_status",
                table: "billing_periods",
                columns: new[] { "client_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_billing_periods_facility_id",
                table: "billing_periods",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_bills_of_lading_client_id",
                table: "bills_of_lading",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_bills_of_lading_facility_id_bol_number",
                table: "bills_of_lading",
                columns: new[] { "facility_id", "bol_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bills_of_lading_shipment_id",
                table: "bills_of_lading",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_carrier_accessorial_charges_client_idempotency",
                table: "carrier_accessorial_charges",
                columns: new[] { "client_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_carrier_accessorial_charges_cost_record",
                table: "carrier_accessorial_charges",
                column: "carrier_cost_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_carrier_accessorial_charges_facility_id",
                table: "carrier_accessorial_charges",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_carrier_accessorial_charges_package_id",
                table: "carrier_accessorial_charges",
                column: "package_id");

            migrationBuilder.CreateIndex(
                name: "ix_carrier_accessorial_charges_shipment",
                table: "carrier_accessorial_charges",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_carrier_sla_policy_carrier_svc_origin_client_eff_version",
                table: "carrier_contractual_sla_policies",
                columns: new[] { "carrier_code", "carrier_service_level", "origin_facility_id", "client_id", "effective_from_utc", "sla_policy_version" });

            migrationBuilder.CreateIndex(
                name: "ix_carrier_cost_adjustments_cost_record",
                table: "carrier_cost_adjustments",
                column: "carrier_cost_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_carrier_cost_adjustments_facility_id",
                table: "carrier_cost_adjustments",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_carrier_cost_adjustments_idempotency",
                table: "carrier_cost_adjustments",
                columns: new[] { "client_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_carrier_cost_adjustments_invoice",
                table: "carrier_cost_adjustments",
                column: "carrier_invoice_number");

            migrationBuilder.CreateIndex(
                name: "ix_carrier_cost_adjustments_shipment_freight_margin_record_id",
                table: "carrier_cost_adjustments",
                column: "shipment_freight_margin_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_carrier_cost_records_client_idempotency",
                table: "carrier_cost_records",
                columns: new[] { "client_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_carrier_cost_records_facility_id",
                table: "carrier_cost_records",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_carrier_cost_records_invoice",
                table: "carrier_cost_records",
                column: "carrier_invoice_number");

            migrationBuilder.CreateIndex(
                name: "ix_carrier_cost_records_package_id",
                table: "carrier_cost_records",
                column: "package_id");

            migrationBuilder.CreateIndex(
                name: "ix_carrier_cost_records_shipment",
                table: "carrier_cost_records",
                columns: new[] { "client_id", "shipment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_carrier_cost_records_shipment_id",
                table: "carrier_cost_records",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_carrier_exception_carrier_fac_client_cat_date_grain",
                table: "carrier_exception_summaries",
                columns: new[] { "carrier_code", "facility_id", "client_id", "exception_category", "date_utc", "time_grain" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_carrier_freight_cost_summary_unique",
                table: "carrier_freight_cost_summaries",
                columns: new[] { "carrier_code", "facility_id", "client_id", "date_utc", "time_grain", "currency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_carrier_lost_shipment_alert_fac_client_status",
                table: "carrier_lost_shipment_alerts",
                columns: new[] { "facility_id", "client_id", "alert_status" });

            migrationBuilder.CreateIndex(
                name: "ix_carrier_lost_shipment_alert_shipment",
                table: "carrier_lost_shipment_alerts",
                column: "shipment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_carrier_lost_shipment_alert_tracking",
                table: "carrier_lost_shipment_alerts",
                column: "tracking_number");

            migrationBuilder.CreateIndex(
                name: "ix_carrier_operation_logs_client_id_facility_id_status",
                table: "carrier_operation_logs",
                columns: new[] { "client_id", "facility_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_carrier_operation_logs_facility_id",
                table: "carrier_operation_logs",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_carrier_operation_logs_package_id_idempotency_key",
                table: "carrier_operation_logs",
                columns: new[] { "package_id", "idempotency_key" });

            migrationBuilder.CreateIndex(
                name: "ix_carrier_rate_agreements_carrier_code_is_active_effective_fr",
                table: "carrier_rate_agreements",
                columns: new[] { "carrier_code", "is_active", "effective_from_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_carrier_rate_tiers_carrier_rate_agreement_id_tier_number",
                table: "carrier_rate_tiers",
                columns: new[] { "carrier_rate_agreement_id", "tier_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_carrier_sla_compliance_records_carrier_code_carrier_service",
                table: "carrier_sla_compliance_records",
                columns: new[] { "carrier_code", "carrier_service_level", "dispatched_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_carrier_sla_compliance_records_facility_id_client_id_dispat",
                table: "carrier_sla_compliance_records",
                columns: new[] { "facility_id", "client_id", "dispatched_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_carrier_sla_compliance_records_shipment_id",
                table: "carrier_sla_compliance_records",
                column: "shipment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_carrier_sla_compliance_records_tracking_number",
                table: "carrier_sla_compliance_records",
                column: "tracking_number");

            migrationBuilder.CreateIndex(
                name: "ix_client_accessorial_rules_agreement_type",
                table: "client_accessorial_rules",
                columns: new[] { "client_freight_agreement_id", "accessorial_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_client_contacts_client_id",
                table: "client_contacts",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_facility_access_client_id",
                table: "client_facility_access",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_facility_access_client_id_facility_id",
                table: "client_facility_access",
                columns: new[] { "client_id", "facility_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_client_facility_access_facility_id",
                table: "client_facility_access",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_facility_access_status",
                table: "client_facility_access",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_client_freight_agreements_code",
                table: "client_freight_agreements",
                columns: new[] { "client_id", "agreement_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_client_freight_agreements_facility_id",
                table: "client_freight_agreements",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_freight_agreements_lookup",
                table: "client_freight_agreements",
                columns: new[] { "client_id", "facility_id", "carrier_code", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_clients_account_code",
                table: "clients",
                column: "account_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_clients_status",
                table: "clients",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_credit_note_lines_number",
                table: "credit_note_lines",
                columns: new[] { "credit_note_id", "line_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_credit_note_lines_original_invoice_line_id",
                table: "credit_note_lines",
                column: "original_invoice_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_notes_client_number",
                table: "credit_notes",
                columns: new[] { "client_id", "credit_note_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_credit_notes_dispute_id",
                table: "credit_notes",
                column: "dispute_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_notes_invoice",
                table: "credit_notes",
                column: "original_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_allocations_cross_dock_plan_line_id",
                table: "cross_dock_allocations",
                column: "plan_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_allocations_facility_id_item_id_status",
                table: "cross_dock_allocations",
                columns: new[] { "facility_id", "item_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_allocations_item_id",
                table: "cross_dock_allocations",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_allocations_lot_id",
                table: "cross_dock_allocations",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_allocations_outbound_order_id_outbound_order_lin",
                table: "cross_dock_allocations",
                columns: new[] { "outbound_order_id", "outbound_order_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_allocations_outbound_order_line_id",
                table: "cross_dock_allocations",
                column: "outbound_order_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_exceptions_cross_dock_task_id",
                table: "cross_dock_exceptions",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_exceptions_plan_id_is_resolved",
                table: "cross_dock_exceptions",
                columns: new[] { "plan_id", "is_resolved" });

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_exceptions_reported_by_user_id",
                table: "cross_dock_exceptions",
                column: "reported_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_plan_lines_inbound_lpn",
                table: "cross_dock_plan_lines",
                column: "inbound_lpn");

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_plan_lines_item_id",
                table: "cross_dock_plan_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_plan_lines_plan_id_item_id",
                table: "cross_dock_plan_lines",
                columns: new[] { "plan_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_plan_lines_receipt_line_id",
                table: "cross_dock_plan_lines",
                column: "receipt_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_plan_lines_source_location_id",
                table: "cross_dock_plan_lines",
                column: "source_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_plan_lines_target_location_id",
                table: "cross_dock_plan_lines",
                column: "target_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_plans_client_id_status",
                table: "cross_dock_plans",
                columns: new[] { "client_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_plans_facility_id_plan_number",
                table: "cross_dock_plans",
                columns: new[] { "facility_id", "plan_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_plans_receiving_session_id",
                table: "cross_dock_plans",
                column: "receiving_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_tasks_actual_destination_location_id",
                table: "cross_dock_tasks",
                column: "actual_destination_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_tasks_assigned_user_id",
                table: "cross_dock_tasks",
                column: "assigned_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_tasks_client_id",
                table: "cross_dock_tasks",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_tasks_cross_dock_plan_id",
                table: "cross_dock_tasks",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_tasks_cross_dock_plan_line_id",
                table: "cross_dock_tasks",
                column: "plan_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_tasks_destination_location_id",
                table: "cross_dock_tasks",
                column: "destination_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_tasks_facility_id_status_priority",
                table: "cross_dock_tasks",
                columns: new[] { "facility_id", "status", "priority" });

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_tasks_facility_id_task_number",
                table: "cross_dock_tasks",
                columns: new[] { "facility_id", "task_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_tasks_item_id",
                table: "cross_dock_tasks",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_tasks_lot_id",
                table: "cross_dock_tasks",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_tasks_lpn",
                table: "cross_dock_tasks",
                column: "lpn");

            migrationBuilder.CreateIndex(
                name: "ix_cross_dock_tasks_source_location_id",
                table: "cross_dock_tasks",
                column: "source_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_payments_client_date",
                table: "customer_payments",
                columns: new[] { "client_id", "payment_date" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_payments_client_ref",
                table: "customer_payments",
                columns: new[] { "client_id", "payment_reference" });

            migrationBuilder.CreateIndex(
                name: "ix_customers_client_id_customer_code",
                table: "customers",
                columns: new[] { "client_id", "customer_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customers_client_id_customer_name",
                table: "customers",
                columns: new[] { "client_id", "customer_name" });

            migrationBuilder.CreateIndex(
                name: "ix_cycle_count_items_approved_by_user_id",
                table: "cycle_count_items",
                column: "approved_by");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_count_items_counter_user_id",
                table: "cycle_count_items",
                column: "counter_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_count_items_cycle_count_id",
                table: "cycle_count_items",
                column: "count_id");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_count_items_item_id",
                table: "cycle_count_items",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_count_items_location_id",
                table: "cycle_count_items",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_count_items_lot_id",
                table: "cycle_count_items",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_counts_count_number",
                table: "cycle_counts",
                column: "count_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cycle_counts_facility_id",
                table: "cycle_counts",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_counts_status",
                table: "cycle_counts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_counts_zone_id",
                table: "cycle_counts",
                column: "zone_id");

            migrationBuilder.CreateIndex(
                name: "ix_daily_carrier_perf_carrier_date_grain",
                table: "daily_carrier_performance_records",
                columns: new[] { "carrier_code", "date_utc", "time_grain" });

            migrationBuilder.CreateIndex(
                name: "ix_daily_carrier_perf_carrier_svc_fac_client_date_grain",
                table: "daily_carrier_performance_records",
                columns: new[] { "carrier_code", "carrier_service_level", "facility_id", "client_id", "date_utc", "time_grain" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_daily_client_profitability_client_date_grain",
                table: "daily_client_profitability_summaries",
                columns: new[] { "client_id", "date_utc", "time_grain" });

            migrationBuilder.CreateIndex(
                name: "ix_daily_client_profitability_client_fac_date_grain",
                table: "daily_client_profitability_summaries",
                columns: new[] { "client_id", "facility_id", "date_utc", "time_grain" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_daily_facility_productivity_date_grain",
                table: "daily_facility_productivity_summaries",
                columns: new[] { "date_utc", "time_grain" });

            migrationBuilder.CreateIndex(
                name: "ix_daily_facility_productivity_facility_date_grain",
                table: "daily_facility_productivity_summaries",
                columns: new[] { "facility_id", "date_utc", "time_grain" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_daily_inventory_aging_snapshots_facility_client_date_item",
                table: "daily_inventory_aging_snapshots",
                columns: new[] { "facility_id", "client_id", "snapshot_date_utc", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_daily_inventory_aging_snapshots_facility_date_bucket",
                table: "daily_inventory_aging_snapshots",
                columns: new[] { "facility_id", "snapshot_date_utc", "aging_bucket" });

            migrationBuilder.CreateIndex(
                name: "ix_daily_inventory_snapshot_records_handling_unit_id",
                table: "daily_inventory_snapshot_records",
                column: "handling_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_daily_inventory_snapshot_records_item_id",
                table: "daily_inventory_snapshot_records",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_daily_inventory_snapshot_records_location_id",
                table: "daily_inventory_snapshot_records",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_snapshot_records_client_facility_item",
                table: "daily_inventory_snapshot_records",
                columns: new[] { "client_id", "facility_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_snapshot_records_snapshot_billable",
                table: "daily_inventory_snapshot_records",
                columns: new[] { "snapshot_id", "is_billable" });

            migrationBuilder.CreateIndex(
                name: "ix_daily_inventory_snapshots_client_facility_date",
                table: "daily_inventory_snapshots",
                columns: new[] { "client_id", "facility_id", "snapshot_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_daily_inventory_snapshots_client_status",
                table: "daily_inventory_snapshots",
                columns: new[] { "client_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_daily_inventory_snapshots_facility_id",
                table: "daily_inventory_snapshots",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_daily_inv_utilization_fac_client_date_grain",
                table: "daily_inventory_utilization_snapshots",
                columns: new[] { "facility_id", "client_id", "date_utc", "time_grain" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_daily_inv_utilization_fac_date_grain",
                table: "daily_inventory_utilization_snapshots",
                columns: new[] { "facility_id", "date_utc", "time_grain" });

            migrationBuilder.CreateIndex(
                name: "ix_daily_location_space_facility_date_location",
                table: "daily_location_space_utilization_records",
                columns: new[] { "facility_id", "snapshot_date_utc", "location_id" });

            migrationBuilder.CreateIndex(
                name: "ix_daily_location_space_facility_zone_date",
                table: "daily_location_space_utilization_records",
                columns: new[] { "facility_id", "zone_id", "snapshot_date_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_daily_worker_productivity_dept_date",
                table: "daily_worker_productivity_summaries",
                columns: new[] { "department", "date_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_daily_worker_productivity_facility_date",
                table: "daily_worker_productivity_summaries",
                columns: new[] { "facility_id", "date_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_daily_worker_productivity_worker_date",
                table: "daily_worker_productivity_summaries",
                columns: new[] { "worker_user_id", "date_utc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_exception_contact_logs_client_id",
                table: "delivery_exception_contact_logs",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_exception_contact_logs_exception_id",
                table: "delivery_exception_contact_logs",
                columns: new[] { "exception_id", "contacted_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_exception_contact_logs_staff_user_id",
                table: "delivery_exception_contact_logs",
                column: "staff_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_dock_appointments_asn_id",
                table: "dock_appointments",
                column: "asn_id");

            migrationBuilder.CreateIndex(
                name: "ix_dock_appointments_client_id",
                table: "dock_appointments",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_dock_appointments_dock_door_id",
                table: "dock_appointments",
                column: "dock_door_id");

            migrationBuilder.CreateIndex(
                name: "ix_dock_appointments_facility_id_appointment_number",
                table: "dock_appointments",
                columns: new[] { "facility_id", "appointment_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dock_assignments_asn_id",
                table: "dock_assignments",
                column: "asn_id");

            migrationBuilder.CreateIndex(
                name: "ix_dock_assignments_dock_door_id",
                table: "dock_assignments",
                column: "dock_door_id");

            migrationBuilder.CreateIndex(
                name: "ix_dock_assignments_facility_id",
                table: "dock_assignments",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_dock_assignments_yard_visit_id",
                table: "dock_assignments",
                column: "yard_visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_dock_doors_facility_id_door_number",
                table: "dock_doors",
                columns: new[] { "facility_id", "door_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_facilities_facility_code",
                table: "facilities",
                column: "facility_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_facility_dock_yard_date_grain",
                table: "facility_dock_yard_snapshots",
                columns: new[] { "date_utc", "time_grain" });

            migrationBuilder.CreateIndex(
                name: "ix_facility_dock_yard_fac_date_grain",
                table: "facility_dock_yard_snapshots",
                columns: new[] { "facility_id", "date_utc", "time_grain" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_handling_units_client_id",
                table: "handling_units",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_handling_units_current_location_id",
                table: "handling_units",
                column: "current_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_handling_units_current_outbound_order_id",
                table: "handling_units",
                column: "current_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_handling_units_current_pick_wave_id",
                table: "handling_units",
                column: "current_pick_wave_id");

            migrationBuilder.CreateIndex(
                name: "ix_handling_units_facility_id_lpn_code",
                table: "handling_units",
                columns: new[] { "facility_id", "lpn_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_handling_units_status",
                table: "handling_units",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_hourly_facility_throughput_fac_hour",
                table: "hourly_facility_throughput_snapshots",
                columns: new[] { "facility_id", "hour_timestamp_utc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hourly_facility_throughput_hour",
                table: "hourly_facility_throughput_snapshots",
                column: "hour_timestamp_utc");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_adjustments_approved_by_user_id",
                table: "inventory_adjustments",
                column: "approved_by_user");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_adjustments_client_id",
                table: "inventory_adjustments",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_adjustments_facility_id",
                table: "inventory_adjustments",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_adjustments_item_id",
                table: "inventory_adjustments",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_adjustments_location_id",
                table: "inventory_adjustments",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_adjustments_lot_id",
                table: "inventory_adjustments",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_adjustments_stock_id",
                table: "inventory_adjustments",
                column: "stock_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_aging_policies_facility_client",
                table: "inventory_aging_policies",
                columns: new[] { "facility_id", "client_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_allocations_client_id_facility_id",
                table: "inventory_allocations",
                columns: new[] { "client_id", "facility_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_allocations_facility_id",
                table: "inventory_allocations",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_allocations_item_id",
                table: "inventory_allocations",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_allocations_location_id",
                table: "inventory_allocations",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_allocations_lot_id",
                table: "inventory_allocations",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_allocations_outbound_order_id",
                table: "inventory_allocations",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_allocations_outbound_order_line_id",
                table: "inventory_allocations",
                column: "line_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_allocations_picked_stock_on_hand_id",
                table: "inventory_allocations",
                column: "picked_stock_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_allocations_status",
                table: "inventory_allocations",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_allocations_stock_on_hand_id",
                table: "inventory_allocations",
                column: "stock_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_allocations_target_handling_unit_id",
                table: "inventory_allocations",
                column: "target_handling_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_lots_client_id",
                table: "inventory_lots",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_lots_expiry_date",
                table: "inventory_lots",
                column: "expiry_date");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_lots_item_id",
                table: "inventory_lots",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_lots_item_id_lot_number",
                table: "inventory_lots",
                columns: new[] { "item_id", "lot_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_movement_logs_facility_client_item_timestamp",
                table: "inventory_movement_event_logs",
                columns: new[] { "facility_id", "client_id", "item_id", "timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_movement_logs_facility_demand_timestamp",
                table: "inventory_movement_event_logs",
                columns: new[] { "facility_id", "is_qualifying_demand", "timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_serials_client_id",
                table: "inventory_serials",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_serials_item_id",
                table: "inventory_serials",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_serials_item_id_serial_number",
                table: "inventory_serials",
                columns: new[] { "item_id", "serial_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_serials_location_id",
                table: "inventory_serials",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_serials_lot_id",
                table: "inventory_serials",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_transactions_client_id_tx_timestamp",
                table: "inventory_transactions",
                columns: new[] { "client_id", "tx_timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_transactions_facility_id",
                table: "inventory_transactions",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_transactions_from_location_id_to_location_id",
                table: "inventory_transactions",
                columns: new[] { "from_location_id", "to_location_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_transactions_item_id",
                table: "inventory_transactions",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_transactions_lot_id",
                table: "inventory_transactions",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_transactions_reference_id",
                table: "inventory_transactions",
                column: "reference_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_transactions_to_location_id",
                table: "inventory_transactions",
                column: "to_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_transactions_user_id",
                table: "inventory_transactions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_disputes_client_ref",
                table: "invoice_disputes",
                columns: new[] { "client_id", "dispute_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoice_disputes_credit_note_id",
                table: "invoice_disputes",
                column: "credit_note_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_disputes_invoice_line_id",
                table: "invoice_disputes",
                column: "invoice_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_disputes_invoice_status",
                table: "invoice_disputes",
                columns: new[] { "invoice_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_activity",
                table: "invoice_lines",
                column: "billable_activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_invoice_number",
                table: "invoice_lines",
                columns: new[] { "invoice_id", "line_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoice_sequences_client_year_prefix",
                table: "invoice_sequences",
                columns: new[] { "client_id", "fiscal_year", "prefix" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_billing_period_id",
                table: "invoices",
                column: "billing_period_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_client_number",
                table: "invoices",
                columns: new[] { "client_id", "invoice_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_client_period_currency",
                table: "invoices",
                columns: new[] { "client_id", "billing_period_id", "currency" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_client_status_due",
                table: "invoices",
                columns: new[] { "client_id", "status", "due_date" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_facility_id",
                table: "invoices",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_replacement_invoice_id",
                table: "invoices",
                column: "replacement_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_barcodes_barcode_value",
                table: "item_barcodes",
                column: "barcode_value");

            migrationBuilder.CreateIndex(
                name: "ix_item_barcodes_item_id",
                table: "item_barcodes",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_categories_parent_category_id",
                table: "item_categories",
                column: "parent_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_category_mappings_category_id",
                table: "item_category_mappings",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_category_mappings_item_id_category_id",
                table: "item_category_mappings",
                columns: new[] { "item_id", "category_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_items_abc_velocity",
                table: "items",
                column: "abc_velocity");

            migrationBuilder.CreateIndex(
                name: "ix_items_client_id",
                table: "items",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_items_client_id_sku",
                table: "items",
                columns: new[] { "client_id", "sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_items_status",
                table: "items",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_labor_standard_profiles_facility_zone_activity",
                table: "labor_standard_profiles",
                columns: new[] { "facility_id", "zone_id", "activity_type", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_lane_transit_origin_dest_carrier_svc_date_grain",
                table: "lane_transit_summaries",
                columns: new[] { "origin_facility_id", "destination_country", "destination_state_or_region", "carrier_code", "carrier_service_level", "date_utc", "time_grain" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_location_locks_location_id",
                table: "location_locks",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_locks_locked_by_user_id",
                table: "location_locks",
                column: "locked_by_user");

            migrationBuilder.CreateIndex(
                name: "ix_location_locks_released_by_user_id",
                table: "location_locks",
                column: "released_by_user");

            migrationBuilder.CreateIndex(
                name: "ix_locations_facility_id",
                table: "locations",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_locations_facility_id_barcode",
                table: "locations",
                columns: new[] { "facility_id", "barcode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_locations_facility_id_location_code",
                table: "locations",
                columns: new[] { "facility_id", "location_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_locations_parent_location_id",
                table: "locations",
                column: "parent_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_locations_pick_sequence",
                table: "locations",
                column: "pick_sequence");

            migrationBuilder.CreateIndex(
                name: "ix_locations_zone_id",
                table: "locations",
                column: "zone_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_import_batch_rows_batch_id_row_number",
                table: "order_import_batch_rows",
                columns: new[] { "batch_id", "row_number" });

            migrationBuilder.CreateIndex(
                name: "ix_order_import_batches_client_id_batch_number",
                table: "order_import_batches",
                columns: new[] { "client_id", "batch_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_import_batches_facility_id_status",
                table: "order_import_batches",
                columns: new[] { "facility_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_order_status_histories_order_id_changed_at_utc",
                table: "order_status_histories",
                columns: new[] { "order_id", "changed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_order_lines_item_id",
                table: "outbound_order_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbound_order_lines_order_id_line_number",
                table: "outbound_order_lines",
                columns: new[] { "order_id", "line_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbound_order_lines_packaging_unit_id",
                table: "outbound_order_lines",
                column: "packaging_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbound_orders_client_id_delivery_status",
                table: "outbound_orders",
                columns: new[] { "client_id", "delivery_status" });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_orders_client_id_external_order_reference",
                table: "outbound_orders",
                columns: new[] { "client_id", "external_order_reference" });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_orders_client_id_order_number",
                table: "outbound_orders",
                columns: new[] { "client_id", "order_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbound_orders_client_id_status",
                table: "outbound_orders",
                columns: new[] { "client_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_orders_customer_id",
                table: "outbound_orders",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbound_orders_destination_facility_id",
                table: "outbound_orders",
                column: "destination_facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbound_orders_facility_id_order_number",
                table: "outbound_orders",
                columns: new[] { "facility_id", "order_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbound_orders_facility_id_status_priority",
                table: "outbound_orders",
                columns: new[] { "facility_id", "status", "priority" });

            migrationBuilder.CreateIndex(
                name: "ix_package_lines_inventory_allocation_id",
                table: "package_lines",
                column: "allocation_id");

            migrationBuilder.CreateIndex(
                name: "ix_package_lines_item_id",
                table: "package_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_package_lines_lot_id",
                table: "package_lines",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_package_lines_outbound_order_line_id",
                table: "package_lines",
                column: "outbound_order_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_package_lines_package_id",
                table: "package_lines",
                column: "package_id");

            migrationBuilder.CreateIndex(
                name: "ix_package_lines_source_handling_unit_id",
                table: "package_lines",
                column: "source_handling_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_packages_client_id",
                table: "packages",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_packages_current_location_id",
                table: "packages",
                column: "current_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_packages_facility_id_package_number",
                table: "packages",
                columns: new[] { "facility_id", "package_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_packages_handling_unit_id",
                table: "packages",
                column: "handling_unit_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_packages_outbound_order_id",
                table: "packages",
                column: "outbound_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_packages_packaging_material_id",
                table: "packages",
                column: "packaging_material_id");

            migrationBuilder.CreateIndex(
                name: "ix_packages_packed_by_user_id",
                table: "packages",
                column: "packed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_packages_shipment_id",
                table: "packages",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_packages_status",
                table: "packages",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_packages_tracking_number",
                table: "packages",
                column: "tracking_number");

            migrationBuilder.CreateIndex(
                name: "ix_packaging_materials_client_id",
                table: "packaging_materials",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_packaging_materials_facility_id_code",
                table: "packaging_materials",
                columns: new[] { "facility_id", "code" });

            migrationBuilder.CreateIndex(
                name: "ix_packaging_materials_is_active",
                table: "packaging_materials",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_packaging_units_barcode",
                table: "packaging_units",
                column: "barcode");

            migrationBuilder.CreateIndex(
                name: "ix_packaging_units_item_id",
                table: "packaging_units",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_packing_sessions_client_id",
                table: "packing_sessions",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_packing_sessions_facility_id_outbound_order_id",
                table: "packing_sessions",
                columns: new[] { "facility_id", "outbound_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_packing_sessions_operator_user_id",
                table: "packing_sessions",
                column: "operator_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_packing_sessions_outbound_order_id",
                table: "packing_sessions",
                column: "outbound_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_packing_sessions_packing_station_location_id",
                table: "packing_sessions",
                column: "packing_station_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_packing_sessions_status",
                table: "packing_sessions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_invoice",
                table: "payment_allocations",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_payment_invoice",
                table: "payment_allocations",
                columns: new[] { "customer_payment_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pick_exceptions_client_id_facility_id",
                table: "pick_exceptions",
                columns: new[] { "client_id", "facility_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pick_exceptions_facility_id",
                table: "pick_exceptions",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_exceptions_generated_cycle_count_id",
                table: "pick_exceptions",
                column: "generated_cycle_count_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_exceptions_item_id",
                table: "pick_exceptions",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_exceptions_location_id",
                table: "pick_exceptions",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_exceptions_lot_id",
                table: "pick_exceptions",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_exceptions_outbound_order_id",
                table: "pick_exceptions",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_exceptions_outbound_order_line_id",
                table: "pick_exceptions",
                column: "line_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_exceptions_pick_task_id",
                table: "pick_exceptions",
                column: "pick_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_task_status_histories_changed_at_utc",
                table: "pick_task_status_histories",
                column: "changed_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_pick_task_status_histories_pick_task_id",
                table: "pick_task_status_histories",
                column: "pick_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_tasks_assigned_user_id",
                table: "pick_tasks",
                column: "assigned_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_tasks_client_id_facility_id",
                table: "pick_tasks",
                columns: new[] { "client_id", "facility_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pick_tasks_destination_location_id",
                table: "pick_tasks",
                column: "destination_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_tasks_facility_id",
                table: "pick_tasks",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_tasks_inventory_allocation_id",
                table: "pick_tasks",
                column: "allocation_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_tasks_item_id",
                table: "pick_tasks",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_tasks_lot_id",
                table: "pick_tasks",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_tasks_outbound_order_id",
                table: "pick_tasks",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_tasks_outbound_order_line_id",
                table: "pick_tasks",
                column: "line_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_tasks_pick_sequence",
                table: "pick_tasks",
                column: "pick_sequence");

            migrationBuilder.CreateIndex(
                name: "ix_pick_tasks_pick_wave_id",
                table: "pick_tasks",
                column: "pick_wave_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_tasks_source_location_id",
                table: "pick_tasks",
                column: "source_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_tasks_status",
                table: "pick_tasks",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_pick_tasks_stock_on_hand_id",
                table: "pick_tasks",
                column: "stock_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_tasks_target_handling_unit_id",
                table: "pick_tasks",
                column: "target_handling_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_wave_orders_outbound_order_id",
                table: "pick_wave_orders",
                column: "outbound_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_wave_orders_pick_wave_id_outbound_order_id",
                table: "pick_wave_orders",
                columns: new[] { "pick_wave_id", "outbound_order_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pick_waves_assigned_to_user_id",
                table: "pick_waves",
                column: "assigned_to_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_waves_client_id",
                table: "pick_waves",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_waves_facility_id_wave_number",
                table: "pick_waves",
                columns: new[] { "facility_id", "wave_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pick_waves_status",
                table: "pick_waves",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_pick_waves_zone_id",
                table: "pick_waves",
                column: "zone_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_affinity_cluster_members_cluster_id_item_id",
                table: "product_affinity_cluster_members",
                columns: new[] { "cluster_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_product_affinity_cluster_members_item_id",
                table: "product_affinity_cluster_members",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_affinity_clusters_facility_id_client_id_cluster_code",
                table: "product_affinity_clusters",
                columns: new[] { "facility_id", "client_id", "cluster_code" });

            migrationBuilder.CreateIndex(
                name: "ix_product_affinity_pairs_facility_id_client_id_is_statistical",
                table: "product_affinity_pairs",
                columns: new[] { "facility_id", "client_id", "is_statistically_significant", "lift" });

            migrationBuilder.CreateIndex(
                name: "ix_product_affinity_pairs_facility_id_client_id_item_a_id_item",
                table: "product_affinity_pairs",
                columns: new[] { "facility_id", "client_id", "item_a_id", "item_b_id" });

            migrationBuilder.CreateIndex(
                name: "ix_product_affinity_policies_facility_id_client_id_is_active",
                table: "product_affinity_policies",
                columns: new[] { "facility_id", "client_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_projection_checkpoints_projection_stream",
                table: "projection_checkpoints",
                columns: new[] { "projection_name", "source_stream_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_proof_of_deliveries_client_carrier_conf",
                table: "proof_of_deliveries",
                columns: new[] { "client_id", "carrier_confirmation_number" });

            migrationBuilder.CreateIndex(
                name: "ix_proof_of_deliveries_client_package_idempotency",
                table: "proof_of_deliveries",
                columns: new[] { "client_id", "package_id", "idempotency_key" });

            migrationBuilder.CreateIndex(
                name: "ix_proof_of_deliveries_client_tracking",
                table: "proof_of_deliveries",
                columns: new[] { "client_id", "tracking_number" });

            migrationBuilder.CreateIndex(
                name: "ix_proof_of_deliveries_facility_id",
                table: "proof_of_deliveries",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_proof_of_deliveries_package_unique",
                table: "proof_of_deliveries",
                column: "package_id",
                unique: true,
                filter: "is_superseded = false");

            migrationBuilder.CreateIndex(
                name: "ix_proof_of_deliveries_shipment",
                table: "proof_of_deliveries",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_public_tracking_shares_client_id",
                table: "public_tracking_shares",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_public_tracking_shares_facility_id",
                table: "public_tracking_shares",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_public_tracking_shares_package",
                table: "public_tracking_shares",
                column: "package_id");

            migrationBuilder.CreateIndex(
                name: "ix_public_tracking_shares_shipment_id",
                table: "public_tracking_shares",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_public_tracking_shares_token_unique",
                table: "public_tracking_shares",
                column: "public_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_public_tracking_shares_tracking_expires",
                table: "public_tracking_shares",
                columns: new[] { "tracking_number", "expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_item_id",
                table: "purchase_order_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_purchase_order_id_line_number",
                table: "purchase_order_lines",
                columns: new[] { "purchase_order_id", "line_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_client_id_po_number",
                table: "purchase_orders",
                columns: new[] { "client_id", "po_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_facility_id",
                table: "purchase_orders",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_supplier_id",
                table: "purchase_orders",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_putaway_strategy_rules_client_id_facility_id",
                table: "putaway_strategy_rules",
                columns: new[] { "client_id", "facility_id" });

            migrationBuilder.CreateIndex(
                name: "ix_putaway_strategy_rules_facility_id_sequence",
                table: "putaway_strategy_rules",
                columns: new[] { "facility_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_putaway_tasks_actual_location_id",
                table: "putaway_tasks",
                column: "actual_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_putaway_tasks_assigned_user_id",
                table: "putaway_tasks",
                column: "assigned_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_putaway_tasks_client_id",
                table: "putaway_tasks",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_putaway_tasks_facility_id_status_priority",
                table: "putaway_tasks",
                columns: new[] { "facility_id", "status", "priority" });

            migrationBuilder.CreateIndex(
                name: "ix_putaway_tasks_item_id",
                table: "putaway_tasks",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_putaway_tasks_lot_id",
                table: "putaway_tasks",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_putaway_tasks_lpn_code",
                table: "putaway_tasks",
                column: "lpn_code");

            migrationBuilder.CreateIndex(
                name: "ix_putaway_tasks_lpn_id",
                table: "putaway_tasks",
                column: "lpn_id");

            migrationBuilder.CreateIndex(
                name: "ix_putaway_tasks_source_location_id",
                table: "putaway_tasks",
                column: "source_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_putaway_tasks_suggested_location_id",
                table: "putaway_tasks",
                column: "suggested_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_putaway_tasks_target_location_id1",
                table: "putaway_tasks",
                column: "target_location_id1");

            migrationBuilder.CreateIndex(
                name: "ix_rate_card_lines_schedule_activity",
                table: "rate_card_lines",
                columns: new[] { "rate_card_schedule_id", "activity_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rate_card_schedules_card_dates",
                table: "rate_card_schedules",
                columns: new[] { "rate_card_id", "effective_from_utc", "effective_to_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_rate_card_schedules_card_version",
                table: "rate_card_schedules",
                columns: new[] { "rate_card_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rate_card_schedules_client_facility_status",
                table: "rate_card_schedules",
                columns: new[] { "client_id", "facility_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_rate_card_schedules_facility_id",
                table: "rate_card_schedules",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_rate_card_tiers_line_index",
                table: "rate_card_tiers",
                columns: new[] { "rate_card_line_id", "tier_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rate_cards_client_code",
                table: "rate_cards",
                columns: new[] { "client_id", "rate_card_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rate_cards_client_status",
                table: "rate_cards",
                columns: new[] { "client_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_rate_cards_default_facility_id",
                table: "rate_cards",
                column: "default_facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_lines_asn_line_item_id",
                table: "receipt_lines",
                column: "asn_line_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_lines_item_id",
                table: "receipt_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_lines_purchase_order_line_id",
                table: "receipt_lines",
                column: "purchase_order_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_lines_session_id_lpn",
                table: "receipt_lines",
                columns: new[] { "session_id", "lpn" });

            migrationBuilder.CreateIndex(
                name: "ix_receipt_lines_staging_location_id",
                table: "receipt_lines",
                column: "staging_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_receiving_discrepancies_receipt_line_id",
                table: "receiving_discrepancies",
                column: "receipt_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_receiving_discrepancies_receiving_session_id",
                table: "receiving_discrepancies",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_receiving_discrepancies_resolved_by_user_id",
                table: "receiving_discrepancies",
                column: "resolved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_receiving_sessions_approved_by_user_id",
                table: "receiving_sessions",
                column: "approved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_receiving_sessions_asn_id",
                table: "receiving_sessions",
                column: "asn_id");

            migrationBuilder.CreateIndex(
                name: "ix_receiving_sessions_client_id",
                table: "receiving_sessions",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_receiving_sessions_dock_door_id",
                table: "receiving_sessions",
                column: "dock_door_id");

            migrationBuilder.CreateIndex(
                name: "ix_receiving_sessions_facility_id_session_number",
                table: "receiving_sessions",
                columns: new[] { "facility_id", "session_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_receiving_sessions_purchase_order_id",
                table: "receiving_sessions",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_receiving_sessions_received_by_user_id",
                table: "receiving_sessions",
                column: "received_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_receiving_sessions_yard_visit_id",
                table: "receiving_sessions",
                column: "yard_visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_authorizations_authorized_by_user_id",
                table: "return_authorizations",
                column: "authorized_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_authorizations_cancelled_by_user_id",
                table: "return_authorizations",
                column: "cancelled_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_authorizations_client_auth_num",
                table: "return_authorizations",
                columns: new[] { "client_id", "authorization_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_return_authorizations_client_status",
                table: "return_authorizations",
                columns: new[] { "client_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_return_authorizations_dest_facility_status",
                table: "return_authorizations",
                columns: new[] { "destination_facility_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_return_authorizations_exception_id",
                table: "return_authorizations",
                column: "delivery_exception_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_return_authorizations_order_id",
                table: "return_authorizations",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_authorizations_origin_facility_id",
                table: "return_authorizations",
                column: "origin_facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_authorizations_original_shipment_id",
                table: "return_authorizations",
                column: "original_shipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_authorizations_package_status",
                table: "return_authorizations",
                columns: new[] { "package_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_return_authorizations_return_shipment_id",
                table: "return_authorizations",
                column: "return_shipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_inbound_handoffs_auth_id",
                table: "return_inbound_handoffs",
                column: "return_authorization_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_return_inbound_handoffs_client_ref",
                table: "return_inbound_handoffs",
                columns: new[] { "client_id", "handoff_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_return_inbound_handoffs_facility_status",
                table: "return_inbound_handoffs",
                columns: new[] { "destination_facility_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_return_inbound_handoffs_original_order_id",
                table: "return_inbound_handoffs",
                column: "original_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_inbound_handoffs_original_package_id",
                table: "return_inbound_handoffs",
                column: "original_package_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_inbound_handoffs_shipment_id",
                table: "return_inbound_handoffs",
                column: "return_shipment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_return_receiving_discrepancies_item_id",
                table: "return_receiving_discrepancies",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_receiving_discrepancies_session_type",
                table: "return_receiving_discrepancies",
                columns: new[] { "return_receiving_session_id", "discrepancy_type" });

            migrationBuilder.CreateIndex(
                name: "ix_return_receiving_lines_client_sku",
                table: "return_receiving_lines",
                columns: new[] { "client_id", "sku" });

            migrationBuilder.CreateIndex(
                name: "ix_return_receiving_lines_item_id",
                table: "return_receiving_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_receiving_lines_session_item",
                table: "return_receiving_lines",
                columns: new[] { "return_receiving_session_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_return_receiving_lines_target_location_id",
                table: "return_receiving_lines",
                column: "target_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_receiving_sessions_client_num",
                table: "return_receiving_sessions",
                columns: new[] { "client_id", "session_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_return_receiving_sessions_facility_status",
                table: "return_receiving_sessions",
                columns: new[] { "facility_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_return_receiving_sessions_handoff",
                table: "return_receiving_sessions",
                column: "return_inbound_handoff_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_return_receiving_sessions_received_by_user_id",
                table: "return_receiving_sessions",
                column: "received_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_receiving_sessions_return_authorization_id",
                table: "return_receiving_sessions",
                column: "return_authorization_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_receiving_sessions_return_shipment_id",
                table: "return_receiving_sessions",
                column: "return_shipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_shipments_auth_id",
                table: "return_shipments",
                column: "return_authorization_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_return_shipments_authorized_by_user_id",
                table: "return_shipments",
                column: "authorized_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_shipments_client_status",
                table: "return_shipments",
                columns: new[] { "client_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_return_shipments_client_tracking",
                table: "return_shipments",
                columns: new[] { "client_id", "tracking_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_return_shipments_delivery_exception_id",
                table: "return_shipments",
                column: "delivery_exception_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_return_shipments_dest_facility_status",
                table: "return_shipments",
                columns: new[] { "destination_facility_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_return_shipments_inspected_by_user_id",
                table: "return_shipments",
                column: "inspected_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_shipments_order_id",
                table: "return_shipments",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_shipments_origin_facility_id",
                table: "return_shipments",
                column: "origin_facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_shipments_original_shipment_id",
                table: "return_shipments",
                column: "original_shipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_shipments_package_id",
                table: "return_shipments",
                column: "package_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_shipments_rma",
                table: "return_shipments",
                columns: new[] { "client_id", "return_rma_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_return_shipments_tracking_number",
                table: "return_shipments",
                column: "tracking_number");

            migrationBuilder.CreateIndex(
                name: "ix_return_tracking_events_fingerprint",
                table: "return_tracking_events",
                columns: new[] { "client_id", "carrier_code", "event_fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_return_tracking_events_shipment_occurred",
                table: "return_tracking_events",
                columns: new[] { "return_shipment_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_role_id_permission_key",
                table: "role_permissions",
                columns: new[] { "role_id", "permission_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roles_role_name",
                table: "roles",
                column: "role_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shipment_audit_logs_client_id",
                table: "shipment_audit_logs",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_audit_logs_facility_id",
                table: "shipment_audit_logs",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_audit_logs_package_id",
                table: "shipment_audit_logs",
                column: "package_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_audit_logs_shipment_id_timestamp_utc",
                table: "shipment_audit_logs",
                columns: new[] { "shipment_id", "timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_shipment_custody_transfers_authorized_by_user_id",
                table: "shipment_custody_transfers",
                column: "authorized_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_custody_transfers_client_id",
                table: "shipment_custody_transfers",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_custody_transfers_facility_id_transferred_at_utc",
                table: "shipment_custody_transfers",
                columns: new[] { "facility_id", "transferred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_shipment_custody_transfers_shipment_id",
                table: "shipment_custody_transfers",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_delivery_exceptions_client_resolved",
                table: "shipment_delivery_exceptions",
                columns: new[] { "client_id", "resolved_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_shipment_delivery_exceptions_client_status",
                table: "shipment_delivery_exceptions",
                columns: new[] { "client_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_shipment_delivery_exceptions_client_type",
                table: "shipment_delivery_exceptions",
                columns: new[] { "client_id", "exception_type" });

            migrationBuilder.CreateIndex(
                name: "ix_shipment_delivery_exceptions_facility_id",
                table: "shipment_delivery_exceptions",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_delivery_exceptions_fingerprint",
                table: "shipment_delivery_exceptions",
                columns: new[] { "client_id", "carrier_code", "event_fingerprint" },
                unique: true,
                filter: "event_fingerprint IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_delivery_exceptions_order_id",
                table: "shipment_delivery_exceptions",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_delivery_exceptions_pkg_occurred",
                table: "shipment_delivery_exceptions",
                columns: new[] { "package_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_shipment_delivery_exceptions_reported_by_user_id",
                table: "shipment_delivery_exceptions",
                column: "reported_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_delivery_exceptions_resolved_by_user_id",
                table: "shipment_delivery_exceptions",
                column: "resolved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_delivery_exceptions_return_authorization_id",
                table: "shipment_delivery_exceptions",
                column: "return_authorization_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_delivery_exceptions_shipment_id",
                table: "shipment_delivery_exceptions",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_freight_margin_records_billable_activity_id",
                table: "shipment_freight_margin_records",
                column: "billable_activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_freight_margin_records_carrier_cost_record_id",
                table: "shipment_freight_margin_records",
                column: "carrier_cost_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_freight_margin_records_client_rated",
                table: "shipment_freight_margin_records",
                columns: new[] { "client_id", "rated_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_shipment_freight_margin_records_facility_id",
                table: "shipment_freight_margin_records",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_freight_margin_records_freight_agreement_id",
                table: "shipment_freight_margin_records",
                column: "freight_agreement_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_freight_margin_records_idempotency",
                table: "shipment_freight_margin_records",
                columns: new[] { "client_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shipment_freight_margin_records_shipment",
                table: "shipment_freight_margin_records",
                column: "shipment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shipment_tracking_events_client_status",
                table: "shipment_tracking_events",
                columns: new[] { "client_id", "milestone_status" });

            migrationBuilder.CreateIndex(
                name: "ix_shipment_tracking_events_facility_id",
                table: "shipment_tracking_events",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_tracking_events_pkg_occurred",
                table: "shipment_tracking_events",
                columns: new[] { "package_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_shipment_tracking_events_shipment_id",
                table: "shipment_tracking_events",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_tracking_events_tenant_fingerprint",
                table: "shipment_tracking_events",
                columns: new[] { "client_id", "carrier_code", "event_fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shipment_tracking_events_tracking_occurred",
                table: "shipment_tracking_events",
                columns: new[] { "tracking_number", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_carrier_code",
                table: "shipments",
                column: "carrier_code");

            migrationBuilder.CreateIndex(
                name: "ix_shipments_client_id",
                table: "shipments",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipments_facility_id_shipment_number",
                table: "shipments",
                columns: new[] { "facility_id", "shipment_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shipments_staging_location_id",
                table: "shipments",
                column: "staging_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipments_status",
                table: "shipments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_shipping_addresses_outbound_order_id",
                table: "shipping_addresses",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shipping_labels_package_id",
                table: "shipping_labels",
                column: "package_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipping_labels_status",
                table: "shipping_labels",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_shipping_labels_tracking_number",
                table: "shipping_labels",
                column: "tracking_number");

            migrationBuilder.CreateIndex(
                name: "ix_sku_velocity_records_facility_client_item_window_criteria_type_version",
                table: "sku_velocity_classification_records",
                columns: new[] { "facility_id", "client_id", "item_id", "observation_window", "criteria_used", "record_type", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_slotting_policies_facility_id_client_id_is_active",
                table: "slotting_policies",
                columns: new[] { "facility_id", "client_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_slotting_recommendation_batches_facility_id_batch_number",
                table: "slotting_recommendation_batches",
                columns: new[] { "facility_id", "batch_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_slotting_recommendation_batches_facility_id_status",
                table: "slotting_recommendation_batches",
                columns: new[] { "facility_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_slotting_recommendation_batches_policy_id",
                table: "slotting_recommendation_batches",
                column: "policy_id");

            migrationBuilder.CreateIndex(
                name: "ix_slotting_recommendations_batch_id_item_id",
                table: "slotting_recommendations",
                columns: new[] { "batch_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_slotting_recommendations_batch_id_status",
                table: "slotting_recommendations",
                columns: new[] { "batch_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_slotting_recommendations_current_location_id",
                table: "slotting_recommendations",
                column: "current_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_slotting_recommendations_item_id",
                table: "slotting_recommendations",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_slotting_recommendations_proposed_location_id",
                table: "slotting_recommendations",
                column: "proposed_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_on_hand_client_id",
                table: "stock_on_hand",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_on_hand_client_id_facility_id_item_id_location_id_lot",
                table: "stock_on_hand",
                columns: new[] { "client_id", "facility_id", "item_id", "location_id", "lot_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_on_hand_facility_id",
                table: "stock_on_hand",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_on_hand_handling_unit_id",
                table: "stock_on_hand",
                column: "handling_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_on_hand_item_id",
                table: "stock_on_hand",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_on_hand_location_id",
                table: "stock_on_hand",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_on_hand_lot_id",
                table: "stock_on_hand",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_on_hand_received_at_utc",
                table: "stock_on_hand",
                column: "received_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_storage_billing_policies_client_facility",
                table: "storage_billing_policies",
                columns: new[] { "client_id", "facility_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_storage_billing_policies_facility_id",
                table: "storage_billing_policies",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_client_id_supplier_code",
                table: "suppliers",
                columns: new[] { "client_id", "supplier_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_user_id_role_id",
                table: "user_roles",
                columns: new[] { "user_id", "role_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_client_id",
                table: "users",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_status",
                table: "users",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_zones_facility_id",
                table: "warehouse_zones",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_zones_facility_id_zone_code",
                table: "warehouse_zones",
                columns: new[] { "facility_id", "zone_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_worker_idle_interval_records_facility_started",
                table: "worker_idle_interval_records",
                columns: new[] { "facility_id", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_worker_idle_interval_records_worker_started",
                table: "worker_idle_interval_records",
                columns: new[] { "worker_user_id", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_worker_task_execution_logs_facility_completed",
                table: "worker_task_execution_logs",
                columns: new[] { "facility_id", "completed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_worker_task_execution_logs_task_id",
                table: "worker_task_execution_logs",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_worker_task_execution_logs_worker_completed",
                table: "worker_task_execution_logs",
                columns: new[] { "worker_user_id", "completed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_yard_visits_asn_id",
                table: "yard_visits",
                column: "asn_id");

            migrationBuilder.CreateIndex(
                name: "ix_yard_visits_client_id",
                table: "yard_visits",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_yard_visits_dock_appointment_id",
                table: "yard_visits",
                column: "dock_appointment_id");

            migrationBuilder.CreateIndex(
                name: "ix_yard_visits_facility_id_visit_number",
                table: "yard_visits",
                columns: new[] { "facility_id", "visit_number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_credit_note_lines_credit_notes_credit_note_id",
                table: "credit_note_lines",
                column: "credit_note_id",
                principalTable: "credit_notes",
                principalColumn: "credit_note_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_credit_notes_invoice_disputes_dispute_id",
                table: "credit_notes",
                column: "dispute_id",
                principalTable: "invoice_disputes",
                principalColumn: "dispute_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_delivery_exception_contact_logs_shipment_delivery_exception",
                table: "delivery_exception_contact_logs",
                column: "exception_id",
                principalTable: "shipment_delivery_exceptions",
                principalColumn: "exception_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_return_authorizations_shipment_delivery_exceptions_delivery",
                table: "return_authorizations",
                column: "delivery_exception_id",
                principalTable: "shipment_delivery_exceptions",
                principalColumn: "exception_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_billable_activities_clients_client_id",
                table: "billable_activities");

            migrationBuilder.DropForeignKey(
                name: "fk_billing_periods_clients_client_id",
                table: "billing_periods");

            migrationBuilder.DropForeignKey(
                name: "fk_credit_notes_clients_client_id",
                table: "credit_notes");

            migrationBuilder.DropForeignKey(
                name: "fk_customers_clients_client_id",
                table: "customers");

            migrationBuilder.DropForeignKey(
                name: "fk_handling_units_clients_client_id",
                table: "handling_units");

            migrationBuilder.DropForeignKey(
                name: "fk_invoice_disputes_clients_client_id",
                table: "invoice_disputes");

            migrationBuilder.DropForeignKey(
                name: "fk_invoices_clients_client_id",
                table: "invoices");

            migrationBuilder.DropForeignKey(
                name: "fk_outbound_orders_clients_client_id",
                table: "outbound_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_packages_clients_client_id",
                table: "packages");

            migrationBuilder.DropForeignKey(
                name: "fk_packaging_materials_clients_client_id",
                table: "packaging_materials");

            migrationBuilder.DropForeignKey(
                name: "fk_pick_waves_clients_client_id",
                table: "pick_waves");

            migrationBuilder.DropForeignKey(
                name: "fk_return_authorizations_clients_client_id",
                table: "return_authorizations");

            migrationBuilder.DropForeignKey(
                name: "fk_shipment_delivery_exceptions_clients_client_id",
                table: "shipment_delivery_exceptions");

            migrationBuilder.DropForeignKey(
                name: "fk_shipments_clients_client_id",
                table: "shipments");

            migrationBuilder.DropForeignKey(
                name: "fk_users_clients_client_id",
                table: "users");

            migrationBuilder.DropForeignKey(
                name: "fk_billable_activities_facilities_facility_id",
                table: "billable_activities");

            migrationBuilder.DropForeignKey(
                name: "fk_billing_periods_facilities_facility_id",
                table: "billing_periods");

            migrationBuilder.DropForeignKey(
                name: "fk_handling_units_facilities_facility_id",
                table: "handling_units");

            migrationBuilder.DropForeignKey(
                name: "fk_invoices_facilities_facility_id",
                table: "invoices");

            migrationBuilder.DropForeignKey(
                name: "fk_locations_facilities_facility_id",
                table: "locations");

            migrationBuilder.DropForeignKey(
                name: "fk_outbound_orders_facilities_destination_facility_id",
                table: "outbound_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_outbound_orders_facilities_facility_id",
                table: "outbound_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_packages_facilities_facility_id",
                table: "packages");

            migrationBuilder.DropForeignKey(
                name: "fk_packaging_materials_facilities_facility_id",
                table: "packaging_materials");

            migrationBuilder.DropForeignKey(
                name: "fk_pick_waves_facilities_facility_id",
                table: "pick_waves");

            migrationBuilder.DropForeignKey(
                name: "fk_return_authorizations_facilities_destination_facility_id",
                table: "return_authorizations");

            migrationBuilder.DropForeignKey(
                name: "fk_return_authorizations_facilities_origin_facility_id",
                table: "return_authorizations");

            migrationBuilder.DropForeignKey(
                name: "fk_shipment_delivery_exceptions_facilities_facility_id",
                table: "shipment_delivery_exceptions");

            migrationBuilder.DropForeignKey(
                name: "fk_shipments_facilities_facility_id",
                table: "shipments");

            migrationBuilder.DropForeignKey(
                name: "fk_warehouse_zones_facilities_facility_id",
                table: "warehouse_zones");

            migrationBuilder.DropForeignKey(
                name: "fk_handling_units_outbound_orders_current_outbound_order_id",
                table: "handling_units");

            migrationBuilder.DropForeignKey(
                name: "fk_packages_outbound_orders_outbound_order_id",
                table: "packages");

            migrationBuilder.DropForeignKey(
                name: "fk_return_authorizations_outbound_orders_order_id",
                table: "return_authorizations");

            migrationBuilder.DropForeignKey(
                name: "fk_shipment_delivery_exceptions_outbound_orders_order_id",
                table: "shipment_delivery_exceptions");

            migrationBuilder.DropForeignKey(
                name: "fk_billable_activities_billing_periods_billing_period_id",
                table: "billable_activities");

            migrationBuilder.DropForeignKey(
                name: "fk_invoices_billing_periods_billing_period_id",
                table: "invoices");

            migrationBuilder.DropForeignKey(
                name: "fk_packages_shipments_shipment_id",
                table: "packages");

            migrationBuilder.DropForeignKey(
                name: "fk_return_authorizations_shipments_original_shipment_id",
                table: "return_authorizations");

            migrationBuilder.DropForeignKey(
                name: "fk_shipment_delivery_exceptions_shipments_shipment_id",
                table: "shipment_delivery_exceptions");

            migrationBuilder.DropForeignKey(
                name: "fk_return_authorizations_packages_package_id",
                table: "return_authorizations");

            migrationBuilder.DropForeignKey(
                name: "fk_shipment_delivery_exceptions_packages_package_id",
                table: "shipment_delivery_exceptions");

            migrationBuilder.DropForeignKey(
                name: "fk_invoice_disputes_credit_notes_credit_note_id",
                table: "invoice_disputes");

            migrationBuilder.DropForeignKey(
                name: "fk_return_authorizations_users_authorized_by_user_id",
                table: "return_authorizations");

            migrationBuilder.DropForeignKey(
                name: "fk_return_authorizations_users_cancelled_by_user_id",
                table: "return_authorizations");

            migrationBuilder.DropForeignKey(
                name: "fk_shipment_delivery_exceptions_users_reported_by_user_id",
                table: "shipment_delivery_exceptions");

            migrationBuilder.DropForeignKey(
                name: "fk_shipment_delivery_exceptions_users_resolved_by_user_id",
                table: "shipment_delivery_exceptions");

            migrationBuilder.DropForeignKey(
                name: "fk_return_authorizations_shipment_delivery_exceptions_delivery",
                table: "return_authorizations");

            migrationBuilder.DropTable(
                name: "abc_classification_policies");

            migrationBuilder.DropTable(
                name: "abc_classification_scope_versions");

            migrationBuilder.DropTable(
                name: "allocation_policies");

            migrationBuilder.DropTable(
                name: "allocation_shortages");

            migrationBuilder.DropTable(
                name: "analytics_event_deduplications");

            migrationBuilder.DropTable(
                name: "bills_of_lading");

            migrationBuilder.DropTable(
                name: "carrier_accessorial_charges");

            migrationBuilder.DropTable(
                name: "carrier_contractual_sla_policies");

            migrationBuilder.DropTable(
                name: "carrier_cost_adjustments");

            migrationBuilder.DropTable(
                name: "carrier_exception_summaries");

            migrationBuilder.DropTable(
                name: "carrier_freight_cost_summaries");

            migrationBuilder.DropTable(
                name: "carrier_lost_shipment_alerts");

            migrationBuilder.DropTable(
                name: "carrier_operation_logs");

            migrationBuilder.DropTable(
                name: "carrier_rate_tiers");

            migrationBuilder.DropTable(
                name: "carrier_sla_compliance_records");

            migrationBuilder.DropTable(
                name: "client_accessorial_rules");

            migrationBuilder.DropTable(
                name: "client_contacts");

            migrationBuilder.DropTable(
                name: "client_facility_access");

            migrationBuilder.DropTable(
                name: "credit_note_lines");

            migrationBuilder.DropTable(
                name: "cross_dock_allocations");

            migrationBuilder.DropTable(
                name: "cross_dock_exceptions");

            migrationBuilder.DropTable(
                name: "cycle_count_items");

            migrationBuilder.DropTable(
                name: "daily_carrier_performance_records");

            migrationBuilder.DropTable(
                name: "daily_client_profitability_summaries");

            migrationBuilder.DropTable(
                name: "daily_facility_productivity_summaries");

            migrationBuilder.DropTable(
                name: "daily_inventory_aging_snapshots");

            migrationBuilder.DropTable(
                name: "daily_inventory_snapshot_records");

            migrationBuilder.DropTable(
                name: "daily_inventory_utilization_snapshots");

            migrationBuilder.DropTable(
                name: "daily_location_space_utilization_records");

            migrationBuilder.DropTable(
                name: "daily_worker_productivity_summaries");

            migrationBuilder.DropTable(
                name: "delivery_exception_contact_logs");

            migrationBuilder.DropTable(
                name: "dock_assignments");

            migrationBuilder.DropTable(
                name: "facility_dock_yard_snapshots");

            migrationBuilder.DropTable(
                name: "hourly_facility_throughput_snapshots");

            migrationBuilder.DropTable(
                name: "inventory_adjustments");

            migrationBuilder.DropTable(
                name: "inventory_aging_policies");

            migrationBuilder.DropTable(
                name: "inventory_movement_event_logs");

            migrationBuilder.DropTable(
                name: "inventory_serials");

            migrationBuilder.DropTable(
                name: "inventory_transactions");

            migrationBuilder.DropTable(
                name: "invoice_sequences");

            migrationBuilder.DropTable(
                name: "item_barcodes");

            migrationBuilder.DropTable(
                name: "item_category_mappings");

            migrationBuilder.DropTable(
                name: "labor_standard_profiles");

            migrationBuilder.DropTable(
                name: "lane_transit_summaries");

            migrationBuilder.DropTable(
                name: "location_locks");

            migrationBuilder.DropTable(
                name: "order_import_batch_rows");

            migrationBuilder.DropTable(
                name: "order_status_histories");

            migrationBuilder.DropTable(
                name: "package_lines");

            migrationBuilder.DropTable(
                name: "packing_sessions");

            migrationBuilder.DropTable(
                name: "payment_allocations");

            migrationBuilder.DropTable(
                name: "pick_exceptions");

            migrationBuilder.DropTable(
                name: "pick_task_status_histories");

            migrationBuilder.DropTable(
                name: "pick_wave_orders");

            migrationBuilder.DropTable(
                name: "product_affinity_cluster_members");

            migrationBuilder.DropTable(
                name: "product_affinity_pairs");

            migrationBuilder.DropTable(
                name: "product_affinity_policies");

            migrationBuilder.DropTable(
                name: "projection_checkpoints");

            migrationBuilder.DropTable(
                name: "proof_of_deliveries");

            migrationBuilder.DropTable(
                name: "public_tracking_shares");

            migrationBuilder.DropTable(
                name: "putaway_strategy_rules");

            migrationBuilder.DropTable(
                name: "putaway_tasks");

            migrationBuilder.DropTable(
                name: "rate_card_tiers");

            migrationBuilder.DropTable(
                name: "receiving_discrepancies");

            migrationBuilder.DropTable(
                name: "return_receiving_discrepancies");

            migrationBuilder.DropTable(
                name: "return_receiving_lines");

            migrationBuilder.DropTable(
                name: "return_tracking_events");

            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "shipment_audit_logs");

            migrationBuilder.DropTable(
                name: "shipment_custody_transfers");

            migrationBuilder.DropTable(
                name: "shipment_tracking_events");

            migrationBuilder.DropTable(
                name: "shipping_addresses");

            migrationBuilder.DropTable(
                name: "shipping_labels");

            migrationBuilder.DropTable(
                name: "sku_velocity_classification_records");

            migrationBuilder.DropTable(
                name: "slotting_recommendations");

            migrationBuilder.DropTable(
                name: "storage_billing_policies");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "worker_idle_interval_records");

            migrationBuilder.DropTable(
                name: "worker_task_execution_logs");

            migrationBuilder.DropTable(
                name: "shipment_freight_margin_records");

            migrationBuilder.DropTable(
                name: "carrier_rate_agreements");

            migrationBuilder.DropTable(
                name: "cross_dock_tasks");

            migrationBuilder.DropTable(
                name: "daily_inventory_snapshots");

            migrationBuilder.DropTable(
                name: "item_categories");

            migrationBuilder.DropTable(
                name: "order_import_batches");

            migrationBuilder.DropTable(
                name: "customer_payments");

            migrationBuilder.DropTable(
                name: "cycle_counts");

            migrationBuilder.DropTable(
                name: "pick_tasks");

            migrationBuilder.DropTable(
                name: "product_affinity_clusters");

            migrationBuilder.DropTable(
                name: "rate_card_lines");

            migrationBuilder.DropTable(
                name: "return_receiving_sessions");

            migrationBuilder.DropTable(
                name: "slotting_recommendation_batches");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "carrier_cost_records");

            migrationBuilder.DropTable(
                name: "client_freight_agreements");

            migrationBuilder.DropTable(
                name: "cross_dock_plan_lines");

            migrationBuilder.DropTable(
                name: "inventory_allocations");

            migrationBuilder.DropTable(
                name: "rate_card_schedules");

            migrationBuilder.DropTable(
                name: "return_inbound_handoffs");

            migrationBuilder.DropTable(
                name: "slotting_policies");

            migrationBuilder.DropTable(
                name: "cross_dock_plans");

            migrationBuilder.DropTable(
                name: "receipt_lines");

            migrationBuilder.DropTable(
                name: "outbound_order_lines");

            migrationBuilder.DropTable(
                name: "stock_on_hand");

            migrationBuilder.DropTable(
                name: "rate_cards");

            migrationBuilder.DropTable(
                name: "return_shipments");

            migrationBuilder.DropTable(
                name: "asn_line_items");

            migrationBuilder.DropTable(
                name: "receiving_sessions");

            migrationBuilder.DropTable(
                name: "inventory_lots");

            migrationBuilder.DropTable(
                name: "packaging_units");

            migrationBuilder.DropTable(
                name: "purchase_order_lines");

            migrationBuilder.DropTable(
                name: "yard_visits");

            migrationBuilder.DropTable(
                name: "items");

            migrationBuilder.DropTable(
                name: "dock_appointments");

            migrationBuilder.DropTable(
                name: "advance_shipping_notices");

            migrationBuilder.DropTable(
                name: "dock_doors");

            migrationBuilder.DropTable(
                name: "purchase_orders");

            migrationBuilder.DropTable(
                name: "suppliers");

            migrationBuilder.DropTable(
                name: "clients");

            migrationBuilder.DropTable(
                name: "facilities");

            migrationBuilder.DropTable(
                name: "outbound_orders");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "billing_periods");

            migrationBuilder.DropTable(
                name: "shipments");

            migrationBuilder.DropTable(
                name: "packages");

            migrationBuilder.DropTable(
                name: "handling_units");

            migrationBuilder.DropTable(
                name: "packaging_materials");

            migrationBuilder.DropTable(
                name: "locations");

            migrationBuilder.DropTable(
                name: "pick_waves");

            migrationBuilder.DropTable(
                name: "warehouse_zones");

            migrationBuilder.DropTable(
                name: "credit_notes");

            migrationBuilder.DropTable(
                name: "invoice_disputes");

            migrationBuilder.DropTable(
                name: "invoice_lines");

            migrationBuilder.DropTable(
                name: "billable_activities");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "shipment_delivery_exceptions");

            migrationBuilder.DropTable(
                name: "return_authorizations");
        }
    }
}
