using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MDM1_3_R03_F02_QuarantineLegacyFxRows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "transformation_trace",
                table: "fx_rates",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            // MDM13-R03-F02 Safe Legacy Upgrade Policy:
            // Preserve historical rows without deletion, but quarantine any previously approved/superseded
            // official-authority FX record (NRB/FRB/ECB/BOE) lacking verified trust-anchor evidence or referencing
            // the deprecated synthetic NRB_FX_* fixtures so it can never be resolved as authenticated central-bank data.
            migrationBuilder.Sql("""
                UPDATE fx_rates
                SET
                    verification_notes = LEFT(
                        '[QUARANTINED_LEGACY_UNVERIFIED on MDM13-R03-F02 upgrade: previous_status=' || approval_status ||
                        ', previous_active=' || is_active::text ||
                        ', previous_hash=' || COALESCE(integrity_hash, 'NULL') ||
                        '] ' || COALESCE(verification_notes, ''),
                        500),
                    approval_status = 'Quarantined',
                    is_active = false,
                    evidence_reference = 'QUARANTINED_UNVERIFIED_LEGACY',
                    transformation_trace = 'MDM13-R03-F02 upgrade quarantined pre-R03/unverified official FX row without deletion.',
                    config_revision = config_revision + 1,
                    updated_at = NOW()
                WHERE source_authority IN ('NRB', 'FRB', 'ECB', 'BOE')
                  AND approval_status IN ('VerifiedApproved', 'Superseded')
                  AND (
                        evidence_reference IS NULL
                     OR btrim(evidence_reference) = ''
                     OR evidence_reference = 'MANUAL_GOVERNANCE_OVERRIDE'
                     OR evidence_reference LIKE '%NRB_FX_20261120.json%'
                     OR evidence_reference LIKE '%NRB_FX_20261121.json%'
                     OR integrity_hash IS NULL
                     OR integrity_hash !~ '^SHA256:[0-9a-fA-F]{64}$'
                     OR approved_by_user_id IS NULL
                     OR verification_notes IS NULL
                     OR length(btrim(verification_notes)) < 10
                  );

                -- Explicitly label pre-upgrade ManualApproved governance records that already carry valid approver + notes.
                UPDATE fx_rates
                SET
                    evidence_reference = 'MANUAL_GOVERNANCE_OVERRIDE',
                    transformation_trace = COALESCE(transformation_trace, 'MDM13-R03-F02 upgrade labeled pre-existing ManualApproved governance override.'),
                    updated_at = NOW()
                WHERE source_authority = 'ManualApproved'
                  AND approval_status IN ('VerifiedApproved', 'Superseded')
                  AND (evidence_reference IS NULL OR btrim(evidence_reference) = '')
                  AND approved_by_user_id IS NOT NULL
                  AND verification_notes IS NOT NULL
                  AND length(btrim(verification_notes)) >= 10;

                -- Quarantine any incomplete ManualApproved legacy rows lacking approver identity or substantive notes.
                UPDATE fx_rates
                SET
                    verification_notes = LEFT(
                        '[QUARANTINED_LEGACY_MANUAL_INCOMPLETE on MDM13-R03-F02 upgrade: previous_status=' || approval_status || '] ' ||
                        COALESCE(verification_notes, ''),
                        500),
                    approval_status = 'Quarantined',
                    is_active = false,
                    evidence_reference = 'QUARANTINED_UNVERIFIED_LEGACY',
                    transformation_trace = 'MDM13-R03-F02 upgrade quarantined incomplete legacy ManualApproved FX row without deletion.',
                    config_revision = config_revision + 1,
                    updated_at = NOW()
                WHERE source_authority = 'ManualApproved'
                  AND approval_status IN ('VerifiedApproved', 'Superseded')
                  AND (evidence_reference IS NULL OR btrim(evidence_reference) = '');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "transformation_trace",
                table: "fx_rates");
        }
    }
}
