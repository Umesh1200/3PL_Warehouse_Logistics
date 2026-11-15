namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Physical classification of packing containers and shipping materials.
/// Phase 4.4 Packing & Dispatch Preparation.
/// </summary>
public enum PackagingMaterialType
{
    CorrugatedBox = 1,   // Standard carton / corrugated box
    PaddedEnvelope = 2,  // Bubble mailer / poly mailer
    Pallet = 3,          // Wooden or plastic pallet base for freight
    CustomCrate = 4      // Custom wooden crate for oversized/fragile goods
}

