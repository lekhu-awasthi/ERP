using ErpApp.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Accounting;

public sealed class BankReconciliationConfiguration : IEntityTypeConfiguration<BankReconciliation>
{
    public void Configure(EntityTypeBuilder<BankReconciliation> builder)
    {
        builder.ToTable("BankReconciliations", schema: "accounting");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.BankAccountId).IsRequired();
        builder.Property(x => x.ReconciledAt).IsRequired();
        builder.Property(x => x.ReconciledByUserId).IsRequired();

        // TenantIndexConvention sees OrganizationId and no business date, so it classifies this as
        // master data and asserts a leading-tenant index exists rather than deriving one. It has to
        // be declared here, and (OrganizationId, BankAccountId) is the right one: every read of this
        // table is for one account -- the detail drawer by id, and nothing else lists them.
        //
        // ReconciledAt is deliberately NOT treated as a business date. It is a stamp of when an act
        // happened, not a date anybody ranges over: there is no reconciliation list screen, here or
        // in the reference product, whose six screens reach a reconciliation only through a row on
        // one of the two sides. The convention's DatesThatAreNotBusinessDates carries the same
        // reasoning for AlertSendLog.
        builder.HasIndex(x => new { x.OrganizationId, x.BankAccountId });

        // No navigation to either side, and no cascade. The membership lives as a nullable key on
        // BankStatementLine and GlLine (see BankReconciliation for why that shape rather than a link
        // table), and releasing them is DeleteBankReconciliationCommandHandler's explicit job --
        // a database-level cascade would delete the statement lines themselves, which is the
        // opposite of what unreconciling means.
    }
}
