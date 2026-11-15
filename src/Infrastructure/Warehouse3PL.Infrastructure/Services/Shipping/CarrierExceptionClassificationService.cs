using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Services.Shipping;

/// <summary>
/// Normalizes external carrier-specific exception codes and status descriptions
/// into enterprise domain DeliveryExceptionType classifications.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B: Delivery Exception Workflows &amp; Returns Management.
/// </summary>
public class CarrierExceptionClassificationService : ICarrierExceptionClassificationService
{
    public DeliveryExceptionType ClassifyException(string carrierCode, string eventCode, string? description)
    {
        var code = eventCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var desc = description?.Trim().ToLowerInvariant() ?? string.Empty;

        // 1. Package Damaged in Transit (Highest priority / critical)
        if (code is "DAMAGED" or "DMG" or "DMG01" or "CRUSHED" or "LEAKING" or "BROKEN" or "07" ||
            desc.Contains("damaged in transit") || desc.Contains("visible damage") || desc.Contains("crushed") ||
            desc.Contains("package leaking") || desc.Contains("carton broken") || desc.Contains("damaged parcel"))
        {
            return DeliveryExceptionType.PackageDamagedInTransit;
        }

        // 2. Lost in Transit
        if (code is "LOST" or "MISSING" or "LOST_FREIGHT" or "UNTRACED" or "LOST_IN_TRANSIT" or "08" ||
            desc.Contains("lost in transit") || desc.Contains("package missing") || desc.Contains("cannot be located") ||
            desc.Contains("untraced freight") || desc.Contains("lost parcel") || desc.Contains("missing package"))
        {
            return DeliveryExceptionType.LostInTransit;
        }

        // 3. Customer Refused
        if (code is "REFUSED" or "REJECTED" or "R01" or "CUST_REF" or "REF" or "04" ||
            desc.Contains("customer refused") || desc.Contains("consignee refused") || desc.Contains("rejected delivery") ||
            desc.Contains("refused by recipient") || desc.Contains("refused by customer") || desc.Contains("delivery refused"))
        {
            return DeliveryExceptionType.CustomerRefused;
        }

        // 4. Address Not Found / Invalid Address
        if (code is "INV_ADDR" or "BAD_ADDR" or "A01" or "NO_SUCH_NUM" or "WRONG_ZIP" or "ADDRESS" or "ADDR_ERROR" or "03" ||
            desc.Contains("invalid address") || desc.Contains("address not found") || desc.Contains("missing apartment") ||
            desc.Contains("bad street") || desc.Contains("no such address") || desc.Contains("undeliverable address") ||
            desc.Contains("incorrect address") || desc.Contains("wrong address"))
        {
            return DeliveryExceptionType.AddressNotFound;
        }

        // 5. Access Restricted
        if (code is "GATE_LOCKED" or "NO_ACCESS" or "DOG" or "SEC_RESTRICT" or "GATE_CODE" or "09" ||
            desc.Contains("gate locked") || desc.Contains("access restricted") || desc.Contains("security checkpoint") ||
            desc.Contains("dog on premises") || desc.Contains("access code needed") || desc.Contains("security gate") ||
            desc.Contains("intercom broken"))
        {
            return DeliveryExceptionType.AccessRestricted;
        }

        // 6. Recipient Unavailable / Business Closed
        if (code is "REC_UNAVAIL" or "D01" or "D02" or "CLOSED" or "NOT_IN" or "ABSENT" or "SIG_REQ" or "NO_ADULT" or "ATTEMPT_FAILED" or "02" ||
            desc.Contains("recipient unavailable") || desc.Contains("business closed") || desc.Contains("not available") ||
            desc.Contains("customer not available") || desc.Contains("absent") || desc.Contains("signature required") ||
            desc.Contains("no one home") || desc.Contains("missed attempt") || desc.Contains("customer unavailable"))
        {
            return DeliveryExceptionType.RecipientUnavailable;
        }

        // 7. Weather or Natural Disaster
        if (code is "WEATHER" or "NAT_DISASTER" or "W01" or "SNOW" or "FLOOD" or "STORM" or "FORCE_MAJEURE" or "05" ||
            desc.Contains("weather") || desc.Contains("natural disaster") || desc.Contains("road closure") ||
            desc.Contains("blizzard") || desc.Contains("hurricane") || desc.Contains("flood") || desc.Contains("act of god") ||
            desc.Contains("severe weather"))
        {
            return DeliveryExceptionType.WeatherOrNaturalDisaster;
        }

        // 8. Missed Delivery Appointment
        if (code is "APPT_MISSED" or "APPT_RESCHED" or "APPOINTMENT" or "10" ||
            desc.Contains("appointment missed") || desc.Contains("appointment rescheduled") || desc.Contains("missed delivery appointment") ||
            desc.Contains("dock appointment"))
        {
            return DeliveryExceptionType.MissedDeliveryAppointment;
        }

        // 9. Documentation Missing
        if (code is "DOC_MISSING" or "CUSTOMS_HOLD" or "HAZMAT_DOC" or "11" ||
            desc.Contains("documentation missing") || desc.Contains("customs clearance") || desc.Contains("hazmat paper") ||
            desc.Contains("paperwork missing") || desc.Contains("customs hold"))
        {
            return DeliveryExceptionType.DocumentationMissing;
        }

        // 10. Carrier Operational Delay
        if (code is "OPERATIONAL" or "OP_DELAY" or "SORT_DELAY" or "MECH_DELAY" or "EQUIP_FAIL" or "CUTOFF_MISSED" or "06" ||
            desc.Contains("operational delay") || desc.Contains("missed sort cutoff") || desc.Contains("mechanical delay") ||
            desc.Contains("sorting facility delay") || desc.Contains("equipment failure") || desc.Contains("airline delay") ||
            desc.Contains("hub delay") || desc.Contains("transit delay"))
        {
            return DeliveryExceptionType.CarrierOperationalDelay;
        }

        // 11. Fallback: Unclassified/Other
        return DeliveryExceptionType.Other;
    }
}

