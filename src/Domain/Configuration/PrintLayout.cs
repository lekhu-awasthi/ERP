namespace ErpApp.Domain.Configuration;

/// <summary>
/// Phase 73 (Decision A) -- the fixed A4 layouts a <see cref="PrintingTemplate"/> chooses between. A layout is drawing
/// code, never data: the vendor's five A4 layouts share one settings shape key for key and differ only in how they
/// are drawn (phase-73-status.md § Live read 1), so ours carry nothing but this name.
///
/// <para><see cref="Standard"/> is member 0 on purpose: it is the frame every document printed before this phase, so
/// a template row written before the column existed, and an organization with no template at all, keep printing
/// exactly what they printed.</para>
/// </summary>
public enum PrintLayout
{
    /// <summary>The centred organization block over a centred title, plain tables (phase 44's arrangement).</summary>
    Standard = 0,

    /// <summary>Every block ruled: an organization box, a party | details box, a gridded table, totals in a ruled
    /// column -- the familiar Nepali bill.</summary>
    Traditional = 1,

    /// <summary>Left-aligned, no logo, no rules but the table header's, smaller type.</summary>
    Minimal = 2,
}
