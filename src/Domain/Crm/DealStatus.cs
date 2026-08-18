namespace ErpApp.Domain.Crm;

/// <summary>
/// erp-module-scan.md's confirmed live "3 status tabs: Pending/Won/Lost" for the Deals pipeline
/// tracker. Stays a plain enum -- no competing tenant-configurable lookup-screen evidence, the
/// same reasoning that kept WorkTaskStatus a plain enum in Phase 13 while Type/DealStage got the
/// lookup-entity treatment.
///
/// Won and Lost are both terminal (see Deal.MarkWon/MarkLost's doc comments) -- unlike
/// WorkTaskStatus, this isn't a linear progression (Pending can go to either Won or Lost, never
/// Won-to-Lost or back to Pending), so no ordinal-comparison state machine is declared here.
/// </summary>
public enum DealStatus
{
    Pending,
    Won,
    Lost,
}
