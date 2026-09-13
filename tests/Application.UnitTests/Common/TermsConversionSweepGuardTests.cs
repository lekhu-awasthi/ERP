using System.Reflection;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;

namespace ErpApp.Application.UnitTests.Common;

/// <summary>
/// Phase 39 — the guard behind "terms survive a conversion", which was phase 27b's carried item.
///
/// <para><b>The rule, rather than the list.</b> 27b's note said "the four conversion-template DTOs
/// would each need the field", which is arithmetic rather than a rule, and it is wrong: a conversion
/// can only carry Terms when the <i>target</i> has somewhere to put them. Of the five conversion
/// templates in this codebase, two target a type that carries Terms (Invoice, Credit Note) and three
/// do not (Purchase Bill, Debit Note, Production Journal). That is phase-30's "find the rule, don't
/// sample the list" applied to the field 27b deferred.</para>
///
/// <para>Asserted <b>in both directions</b>, which is the half that makes it a rule: a template
/// whose target has Terms and does not carry them fails, and so does one that carries Terms into a
/// target that has none — the second is what would happen if somebody "completed the sweep" by
/// adding the field everywhere.</para>
///
/// <para>Together with phase-35a's three-assertion rule, this covers the middle assertion. The write
/// path is <c>RichTextWritePathSweepGuardTests</c> in the Domain suite and the read path is the
/// detail DTOs, which have carried <c>Terms</c> since 27b.</para>
/// </summary>
public class TermsConversionSweepGuardTests
{
    private static readonly Assembly ApplicationAssembly = typeof(IRequirePermission).Assembly;

    private static readonly Assembly DomainAssembly = typeof(RichText).Assembly;

    [Fact]
    public void A_conversion_template_carries_terms_exactly_when_its_target_has_them()
    {
        var wrong = new List<string>();

        foreach (var dto in ConversionTemplateDtos())
        {
            var target = TargetTypeName(dto.Name);
            var targetHasTerms = DomainAssembly.GetTypes()
                .Any(t => t.Name == target && t.GetProperty("Terms")?.PropertyType == typeof(string));
            var dtoCarriesTerms = dto.GetProperty("Terms")?.PropertyType == typeof(string);

            if (targetHasTerms && !dtoCarriesTerms)
            {
                wrong.Add($"{dto.Name} converts into {target}, which stores Terms, and drops them.");
            }
            else if (!targetHasTerms && dtoCarriesTerms)
            {
                wrong.Add($"{dto.Name} carries Terms into {target}, which has nowhere to put them.");
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    /// <summary>
    /// Pins the arithmetic the rule produces, so that the test above cannot pass by finding nothing.
    /// Five templates, two of which carry Terms — if either number moves, a conversion has been
    /// added or a type has gained Terms, and both deserve a decision rather than a silent pass.
    /// </summary>
    [Fact]
    public void There_are_five_conversion_templates_and_two_of_them_carry_terms()
    {
        var dtos = ConversionTemplateDtos().ToList();

        Assert.Equal(5, dtos.Count);
        Assert.Equal(
            ["CreditNoteConversionTemplateDto", "InvoiceConversionTemplateDto"],
            dtos.Where(d => d.GetProperty("Terms") is not null)
                .Select(d => d.Name)
                .OrderBy(x => x, StringComparer.Ordinal));
    }

    private static IEnumerable<Type> ConversionTemplateDtos() =>
        ApplicationAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.Name.EndsWith("ConversionTemplateDto", StringComparison.Ordinal))
            .OrderBy(t => t.Name, StringComparer.Ordinal);

    /// <summary>The aggregate a template prefills, from the DTO's own name — the naming convention
    /// every one of them already follows.</summary>
    private static string TargetTypeName(string dtoName) =>
        dtoName.Replace("ConversionTemplateDto", string.Empty, StringComparison.Ordinal);
}
