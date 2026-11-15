using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Service responsible for normalizing external carrier exception codes and descriptions
/// into internal domain DeliveryExceptionType classifications.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B: Delivery Exception Workflows &amp; Returns Management.
/// </summary>
public interface ICarrierExceptionClassificationService
{
    /// <summary>
    /// Classifies an external carrier exception code and description into a domain DeliveryExceptionType.
    /// Falls back to DeliveryExceptionType.Other if the external code is unmapped or unknown.
    /// </summary>
    DeliveryExceptionType ClassifyException(string carrierCode, string eventCode, string? description);
}

