using System.Linq.Expressions;
using ErpApp.Domain.Common;
using FluentValidation;

namespace ErpApp.Application.Common.Validation;

/// <summary>
/// Phase 39 — the length bound on a rich-text field.
///
/// <para><b>The gap this closes was already open.</b> <c>Terms</c> has been <c>nvarchar(max)</c> with
/// <i>no validator rule at all</i> since phase 27b created it, on ten commands. That was survivable
/// while the control was a textarea somebody typed into; it is not once the control accepts a paste
/// of an entire web page, and markup roughly doubles the length of the prose it wraps. Phase 34b
/// found the same shape by asking one question of every paginated list — two queries with no
/// validator at all — and this is the answer to the same question asked of every rich-text
/// field.</para>
///
/// <para><b>The signature is an <see cref="Expression"/>, not a <c>Func</c>,</b> and that is
/// load-bearing: a FluentValidation rule built from a captured <c>Func</c> selector cannot infer the
/// property name and 500s every endpoint it guards with "Could not infer property name" — and no
/// handler test can see it, because the failure is at rule-construction time in the API's own
/// pipeline (CLAUDE.md, phase-25). <c>RichTextRulesTests</c> covers this helper directly for the
/// same reason.</para>
/// </summary>
public static class RichTextRules
{
    /// <summary>
    /// Bounds a rich-text property at <see cref="RichText.MaxLength"/> and names the field in the
    /// message. The bound is on the <b>raw</b> value rather than the sanitised one: sanitising costs
    /// a parse of whatever was sent, and refusing an oversized payload before parsing it is the
    /// point of having a bound.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> RichText<T>(
        this IRuleBuilder<T, string?> rule, string fieldName)
    {
        return rule
            .MaximumLength(Domain.Common.RichText.MaxLength)
            .WithMessage(
                $"{fieldName} is longer than the {Domain.Common.RichText.MaxLength:N0} characters this "
                + "field can hold. Paste the text without its source formatting, or shorten it.");
    }
}
