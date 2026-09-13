using System.Reflection;
using System.Runtime.CompilerServices;
using ErpApp.Domain.Common;

namespace ErpApp.Domain.UnitTests.Common;

/// <summary>
/// Phase 39's sweep guard, in the shape phases 24, 27a, 32b and 34b established.
///
/// <para><b>The defect it exists to prevent.</b> <see cref="RichText.Sanitize"/> being correct is
/// worth nothing if a write path forgets to call it, and a forgotten call is not a compile error and
/// breaks no other test -- it is one aggregate quietly storing raw markup while its four siblings do
/// not. That is exactly the failure a sweep guard converts into a build failure, and it is the
/// higher-stakes version of it: the consequence is a stored-XSS hole rather than a missing
/// control.</para>
///
/// <para>These assertions are <b>behavioural</b>, not structural. A guard that checked "does this
/// file mention RichText" would pass for a handler that called it and threw the result away. Each
/// test below drives the real write path with a real payload and looks at what got stored.</para>
/// </summary>
public class RichTextWritePathSweepGuardTests
{
    private static readonly Assembly DomainAssembly = typeof(RichText).Assembly;

    /// <summary>A payload that is unmistakable in the output either way: if it is stored raw the
    /// script tag is still there, and if it is sanitised the surrounding prose survives.</summary>
    private const string Payload = "<p>Net 30 days.</p><script>alert(1)</script>";

    private const string Sanitized = "<p>Net 30 days.</p>";

    /// <summary>
    /// Bodies that deliberately store raw text, each with the reason -- phase-34a's rule that an
    /// allow-list entry nobody can check is not a reason.
    ///
    /// <para><b>Empty today, and that is a finding rather than an oversight.</b> The one plain-text
    /// message body in this codebase is an SMS, and SMS calls its field <c>Content</c> rather than
    /// <c>Body</c> (<c>SmsTemplate</c>, <c>SmsLog</c>), so it is outside this sweep by naming rather
    /// than by exemption. The dictionary exists because the next plain-text body will not be, and
    /// the alternative to an exemption slot is somebody widening the sweep's definition to make a
    /// failure go away.</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ExemptBodies =
        new Dictionary<string, string>(StringComparer.Ordinal);

    // --- Terms, on the five types that carry it -----------------------------------------------

    /// <summary>
    /// The list is derived from the assembly, not written down, so a sixth aggregate that grows a
    /// <c>SetTerms</c> is covered by the test below on the day it appears rather than on the day
    /// somebody remembers to add it here.
    /// </summary>
    public static TheoryData<Type> TermsBearingTypes()
    {
        var data = new TheoryData<Type>();

        foreach (var type in DomainAssembly.GetTypes().Where(HasTermsWritePath).OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            data.Add(type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(TermsBearingTypes))]
    public void Every_SetTerms_sanitizes_what_it_stores(Type type)
    {
        // A Draft instance without running a constructor: Draft is the zero value of every one of
        // these aggregates' status enums, so EnsureDraft() is satisfied and the guard does not have
        // to know each aggregate's Create signature -- which is what would make it rot.
        var instance = RuntimeHelpers.GetUninitializedObject(type);

        type.GetMethod("SetTerms")!.Invoke(instance, [Payload]);

        var stored = (string?)type.GetProperty("Terms")!.GetValue(instance);

        Assert.Equal(Sanitized, stored);
    }

    /// <summary>
    /// Pins the count against <see cref="DocumentMechanisms.TermsAndConditions"/>. The two are
    /// derived independently -- one from the aggregates, one from the list the forms and the client
    /// sweep guard read -- so a type gaining Terms without gaining the editor, or the reverse, fails
    /// here. Phase 27b shipped that list; this is what keeps it true.
    /// </summary>
    [Fact]
    public void The_aggregates_with_terms_are_exactly_the_document_types_that_declare_the_mechanism()
    {
        var fromAggregates = DomainAssembly.GetTypes()
            .Where(HasTermsWritePath)
            .Select(t => t.Name)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var fromMechanisms = DocumentMechanisms.TermsAndConditions
            .Select(x => x.ToString())
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(fromMechanisms, fromAggregates);
    }

    // --- template and log bodies --------------------------------------------------------------

    /// <summary>
    /// The other half of the sweep. A <c>Body</c> that reaches a browser or a PDF has the same
    /// exposure as <c>Terms</c> and a different write path, so it needs its own answer rather than
    /// inheriting one.
    /// </summary>
    [Fact]
    public void Every_domain_body_property_either_sanitizes_or_states_why_not()
    {
        var unanswered = new List<string>();

        foreach (var property in BodyProperties())
        {
            var name = $"{property.DeclaringType!.Name}.{property.Name}";

            if (ExemptBodies.ContainsKey(name) || SanitizesOnWrite(property))
            {
                continue;
            }

            unanswered.Add(name);
        }

        Assert.True(
            unanswered.Count == 0,
            "These Domain rich-text bodies store what they are given without sanitising it, and are "
            + "not listed as exempt. A body that is rendered back into a browser or a PDF has to go "
            + "through RichText.Sanitize on the way in:\n  " + string.Join("\n  ", unanswered));
    }

    [Fact]
    public void Every_body_exemption_names_a_property_that_still_exists()
    {
        var names = BodyProperties()
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}")
            .ToHashSet(StringComparer.Ordinal);

        var stale = ExemptBodies.Keys.Where(k => !names.Contains(k)).ToList();

        Assert.True(stale.Count == 0, "Stale exemptions: " + string.Join(", ", stale));
    }

    // --- helpers ------------------------------------------------------------------------------

    private static bool HasTermsWritePath(Type type) =>
        type is { IsClass: true, IsAbstract: false }
        && type.GetMethod("SetTerms", [typeof(string)]) is not null
        && type.GetProperty("Terms")?.PropertyType == typeof(string);

    private static IEnumerable<PropertyInfo> BodyProperties() =>
        DomainAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Select(t => t.GetProperty("Body", BindingFlags.Public | BindingFlags.Instance))
            .Where(p => p is not null && p.PropertyType == typeof(string))
            .Select(p => p!)
            .OrderBy(p => p.DeclaringType!.Name, StringComparer.Ordinal);

    /// <summary>
    /// Drives the type's own factory -- whichever static method returns it -- with the payload in
    /// every string parameter, and asks whether what landed in <c>Body</c> came out sanitised.
    /// Filling <i>every</i> string parameter is what lets one probe serve factories whose signatures
    /// have nothing else in common.
    /// </summary>
    private static bool SanitizesOnWrite(PropertyInfo property)
    {
        var type = property.DeclaringType!;

        foreach (var factory in type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                     .Where(m => m.ReturnType == type))
        {
            object? created;

            try
            {
                created = factory.Invoke(null, factory.GetParameters().Select(ProbeValue).ToArray());
            }
            catch (Exception)
            {
                // A factory this probe cannot satisfy proves nothing either way; try the next one.
                continue;
            }

            if (created is not null && (string?)property.GetValue(created) == Sanitized)
            {
                return true;
            }
        }

        return false;
    }

    private static object? ProbeValue(ParameterInfo parameter)
    {
        // A nullable parameter probes as null, not as default(T). The difference is not cosmetic:
        // EmailSendLog.Queue refuses a BalanceAsOfDate on a context that is not a balance
        // confirmation, and default(DateOnly) is a date rather than an absent one -- so a probe that
        // filled it would make the factory throw and the guard report the body as unsanitised.
        // Optional-and-absent is what a nullable parameter means, so it is what the probe should say.
        if (Nullable.GetUnderlyingType(parameter.ParameterType) is not null)
        {
            return null;
        }

        var underlying = parameter.ParameterType;

        if (underlying == typeof(string))
        {
            return Payload;
        }

        if (underlying == typeof(Guid))
        {
            return Guid.NewGuid();
        }

        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(underlying) && underlying != typeof(string))
        {
            return null;
        }

        return underlying.IsValueType ? Activator.CreateInstance(underlying) : null;
    }
}
