using ErpApp.Domain.Common;

namespace ErpApp.Domain.UnitTests.Common;

/// <summary>
/// Phase 39. <see cref="RichText"/> is the only thing standing between a Source-code box in the
/// reference product's editor and a customer's browser, so its tests are written adversarially: the
/// XSS cases below are the standard evasion families, not a token <c>&lt;script&gt;</c>.
///
/// <para>The property under test is stronger than "these payloads are removed", which any blocklist
/// can be made to satisfy one payload at a time. It is
/// <see cref="No_output_ever_contains_an_attribute_this_file_did_not_write"/>: whatever the input,
/// the output's only attribute is the four-valued <c>text-align</c> the emitter owns. A new evasion
/// technique fails that assertion without anybody having had to think of it first.</para>
/// </summary>
public class RichTextSanitizerTests
{
    // --- the security bar ---------------------------------------------------------------------

    /// <summary>
    /// The payloads a stored-XSS review would actually try. Each is a family rather than a
    /// one-off: an event handler, a javascript: URL, an unquoted attribute, a malformed tag that
    /// browsers re-interpret, a CSS expression, a data: URI, an SVG vector, an entity-encoded
    /// payload, and mXSS through a re-parsed innerHTML.
    /// </summary>
    public static TheoryData<string> Payloads() =>
    [
        "<script>alert(1)</script>",
        "<p onclick=\"alert(1)\">hi</p>",
        "<p onmouseover=alert(1)>hi</p>",
        "<a href=\"javascript:alert(1)\">click</a>",
        "<a href=javascript:alert(1)>click</a>",
        "<img src=x onerror=alert(1)>",
        "<img src=\"data:image/svg+xml;base64,PHN2Zz48c2NyaXB0PmFsZXJ0KDEpPC9zY3JpcHQ+PC9zdmc+\">",
        "<svg><script>alert(1)</script></svg>",
        "<svg/onload=alert(1)>",
        "<iframe src=\"javascript:alert(1)\"></iframe>",
        "<object data=\"javascript:alert(1)\"></object>",
        "<style>body{background:url('javascript:alert(1)')}</style>",
        "<p style=\"background:url(javascript:alert(1))\">hi</p>",
        "<p style=\"width:expression(alert(1))\">hi</p>",
        "<div><![CDATA[<script>alert(1)</script>]]></div>",
        "<scr<script>ipt>alert(1)</script>",
        "<SCRIPT SRC=//evil.test/x.js></SCRIPT>",
        "<p><!--<script>-->alert(1)</p>",
        "<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\">",
        "<form><button formaction=\"javascript:alert(1)\">go</button></form>",
        "<math><mtext><table><mglyph><style><img src=x onerror=alert(1)>",
        "<template><script>alert(1)</script></template>",
        "&lt;script&gt;alert(1)&lt;/script&gt;",
        "&#60;script&#62;alert(1)&#60;/script&#62;",
        "<base href=\"http://evil.test/\">",
        "<meta http-equiv=\"refresh\" content=\"0;url=javascript:alert(1)\">",
        "<textarea></textarea><script>alert(1)</script>",
        "<p>legit</p><script>alert(1)</script><p>also legit</p>",
    ];

    /// <summary>
    /// The blanket property. Every character of the output is either markup this file emitted or an
    /// escaped text character, so the only way an attribute can appear is if the emitter wrote it --
    /// and the emitter writes exactly one.
    /// </summary>
    [Theory]
    [MemberData(nameof(Payloads))]
    public void No_output_ever_contains_an_attribute_this_file_did_not_write(string payload)
    {
        var output = RichText.Sanitize(payload) ?? string.Empty;

        foreach (var tag in Tags(output))
        {
            // Two legal shapes, and nothing else: a bare tag, or the one styled paragraph.
            var legal = !tag.Contains(' ', StringComparison.Ordinal)
                        || tag is "<p style=\"text-align:center\">"
                            or "<p style=\"text-align:right\">"
                            or "<p style=\"text-align:justify\">"
                            or "<p style=\"text-align:left\">"
                            or "<br />"
                            or "<hr />";

            Assert.True(legal, $"Sanitized output carried an unexpected tag: {tag}\nFull output: {output}");
        }
    }

    /// <summary>
    /// The narrower, more legible property: the tag vocabulary is closed. Bolted onto the attribute
    /// assertion rather than replacing it, because a payload can be attribute-free and still be a
    /// tag nobody sanctioned.
    /// </summary>
    [Theory]
    [MemberData(nameof(Payloads))]
    public void Output_uses_only_the_nine_tags_in_the_grammar(string payload)
    {
        string[] allowed =
        [
            "<p>", "</p>", "<ul>", "</ul>", "<ol>", "</ol>", "<li>", "</li>",
            "<strong>", "</strong>", "<em>", "</em>", "<u>", "</u>", "<s>", "</s>",
            "<br />", "<hr />",
            "<p style=\"text-align:center\">", "<p style=\"text-align:right\">",
            "<p style=\"text-align:justify\">", "<p style=\"text-align:left\">",
        ];

        var output = RichText.Sanitize(payload) ?? string.Empty;

        Assert.All(Tags(output), tag => Assert.Contains(tag, allowed));
    }

    /// <summary>
    /// The one payload family whose *text* must also disappear rather than merely be defanged. An
    /// unwrapped <c>&lt;a&gt;</c> keeping "click" is correct; a dropped <c>&lt;script&gt;</c>
    /// leaving "alert(1)" as visible prose is a sanitiser that technically passed and produced a
    /// document nobody would sign.
    /// </summary>
    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<style>body{color:red}</style>")]
    [InlineData("<p>before</p><script>alert('x')</script><p>after</p>")]
    public void Script_and_style_content_is_dropped_with_the_element(string payload)
    {
        var output = RichText.Sanitize(payload) ?? string.Empty;

        Assert.DoesNotContain("alert", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("color:red", output, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The entity-encoded payloads must survive as *visible text*, not vanish and not decode into
    /// live markup. This is the case a decode-then-emit pipeline gets wrong in one direction and a
    /// never-decode pipeline gets wrong in the other.
    /// </summary>
    [Fact]
    public void An_entity_encoded_script_stays_text_and_is_re_escaped()
    {
        var output = RichText.Sanitize("<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>");

        Assert.Equal("<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>", output);
    }

    // --- idempotence --------------------------------------------------------------------------

    /// <summary>
    /// A document is loaded into the editor and saved again on every edit, so a sanitiser that is
    /// not idempotent rots a field a little each time -- ampersands doubling, whitespace growing.
    /// Asserted over the adversarial corpus and the ordinary one together.
    /// </summary>
    [Theory]
    [MemberData(nameof(Payloads))]
    public void Sanitizing_twice_equals_sanitizing_once(string payload)
    {
        var once = RichText.Sanitize(payload);

        Assert.Equal(once, RichText.Sanitize(once));
    }

    [Theory]
    [InlineData("<p>Terms &amp; conditions</p>")]
    [InlineData("<p>5 &lt; 10 &amp;&amp; 10 &gt; 5</p>")]
    [InlineData("<ul><li>One</li><li>Two</li></ul>")]
    [InlineData("<p style=\"text-align:center\">Centred</p>")]
    [InlineData("<p><strong>Bold</strong> and <em>italic</em></p>")]
    public void Ordinary_content_survives_a_round_trip_unchanged(string html)
    {
        Assert.Equal(html, RichText.Sanitize(html));
    }

    // --- the grammar --------------------------------------------------------------------------

    [Theory]
    [InlineData("<b>x</b>", "<p><strong>x</strong></p>")]
    [InlineData("<i>x</i>", "<p><em>x</em></p>")]
    [InlineData("<strike>x</strike>", "<p><s>x</s></p>")]
    [InlineData("<del>x</del>", "<p><s>x</s></p>")]
    [InlineData("<ins>x</ins>", "<p><u>x</u></p>")]
    public void Legacy_emphasis_tags_normalize_onto_the_four_the_editor_offers(string input, string expected)
    {
        Assert.Equal(expected, RichText.Sanitize(input));
    }

    [Fact]
    public void Nested_emphasis_composes_rather_than_replacing()
    {
        Assert.Equal(
            "<p><strong><em>both</em></strong></p>",
            RichText.Sanitize("<b><i>both</i></b>"));
    }

    /// <summary>
    /// Font family, size and colour are what the live editor emits as <c>&lt;span style&gt;</c>
    /// (the status bar read <c>p &gt; span</c>). They are dropped and the text is kept -- Decision B.
    /// </summary>
    [Fact]
    public void A_styled_span_loses_its_styling_and_keeps_its_text()
    {
        Assert.Equal(
            "<p>Coloured</p>",
            RichText.Sanitize("<p><span style=\"color:#ff0000;font-size:24pt\">Coloured</span></p>"));
    }

    [Fact]
    public void A_link_keeps_its_text_and_loses_its_destination()
    {
        var output = RichText.Sanitize("<p>See <a href=\"https://example.test/x\">our site</a>.</p>");

        Assert.Equal("<p>See our site.</p>", output);
    }

    /// <summary>A pasted table degrades to one paragraph per row, with cells space-separated --
    /// the alternative, dropping it, would silently lose a price list somebody pasted in.</summary>
    [Fact]
    public void A_table_degrades_to_one_paragraph_per_row()
    {
        var output = RichText.Sanitize(
            "<table><tbody><tr><td>Item</td><td>Rate</td></tr><tr><td>Cyl</td><td>1500</td></tr></tbody></table>");

        Assert.Equal("<p>Item Rate</p><p>Cyl 1500</p>", output);
    }

    [Fact]
    public void Headings_keep_their_weight_and_lose_their_scale()
    {
        Assert.Equal("<p><strong>Title</strong></p>", RichText.Sanitize("<h1>Title</h1>"));
    }

    [Fact]
    public void Alignment_is_carried_on_the_paragraph_and_reduced_to_the_four_known_values()
    {
        Assert.Equal("<p style=\"text-align:center\">x</p>", RichText.Sanitize("<p style=\"text-align:center\">x</p>"));
        Assert.Equal("<p style=\"text-align:right\">x</p>", RichText.Sanitize("<p align=\"right\">x</p>"));
        Assert.Equal("<p>x</p>", RichText.Sanitize("<p style=\"text-align:inherit\">x</p>"));
    }

    [Fact]
    public void Lists_survive_with_their_kind()
    {
        Assert.Equal("<ul><li>a</li><li>b</li></ul>", RichText.Sanitize("<ul><li>a</li><li>b</li></ul>"));
        Assert.Equal("<ol><li>a</li></ol>", RichText.Sanitize("<ol><li>a</li></ol>"));
    }

    // --- malformed input ----------------------------------------------------------------------

    /// <summary>
    /// The Source-code box means malformed markup is a thing a user can save, and refusing the save
    /// would be a worse outcome than rendering it approximately. Each of these must produce
    /// *something* and must not throw.
    /// </summary>
    [Theory]
    [InlineData("<p>unclosed")]
    [InlineData("</p>stray close")]
    [InlineData("<p><b>crossed</p></b>")]
    [InlineData("<<<<>>>>text")]
    [InlineData("a < b and c > d")]
    [InlineData("<p title='unterminated>text</p>")]
    [InlineData("<")]
    [InlineData("<p>")]
    public void Malformed_markup_never_throws(string html)
    {
        var exception = Record.Exception(() => RichText.Sanitize(html));

        Assert.Null(exception);
    }

    /// <summary>
    /// A bare "&lt;" that starts no tag is prose, and prose is what an SME's terms are full of
    /// ("orders &lt; 5,000 attract a fee"). It must survive, escaped.
    /// </summary>
    [Fact]
    public void A_less_than_sign_in_prose_survives_as_text()
    {
        Assert.Equal("<p>orders &lt; 5,000 attract a fee</p>", RichText.Sanitize("<p>orders < 5,000 attract a fee</p>"));
    }

    /// <summary>A stray end tag must not unwind the elements above it -- one typo in a Source-code
    /// edit would otherwise reparent everything that followed.</summary>
    [Fact]
    public void A_stray_end_tag_is_ignored_rather_than_unwinding_the_stack()
    {
        Assert.Equal("<p><strong>kept</strong></p>", RichText.Sanitize("<p><b></div>kept</b></p>"));
    }

    // --- emptiness ----------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<p></p>")]
    [InlineData("<p><br></p>")]
    [InlineData("<p>   </p>")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=\"x\">")]
    public void Content_that_would_render_as_nothing_stores_as_null(string? html)
    {
        Assert.Null(RichText.Sanitize(html));
        Assert.True(RichText.IsEmpty(html));
    }

    /// <summary>The editor emits a trailing empty paragraph almost every time; a blank line the user
    /// typed between two paragraphs is content. Both halves asserted, because a rule that only
    /// trimmed would delete the second.</summary>
    [Fact]
    public void Trailing_empty_paragraphs_are_trimmed_and_interior_ones_are_kept()
    {
        Assert.Equal("<p>a</p>", RichText.Sanitize("<p>a</p><p><br></p>"));
        Assert.Equal("<p>a</p><p></p><p>b</p>", RichText.Sanitize("<p>a</p><p><br></p><p>b</p>"));
    }

    // --- the plain-text fold ------------------------------------------------------------------

    [Fact]
    public void Plain_text_keeps_the_words_and_the_list_bullets()
    {
        var text = RichText.ToPlainText(
            "<p>Payment terms:</p><ul><li>Net 30</li><li>2% early</li></ul><p>Thanks.</p>");

        Assert.Equal("Payment terms:\n• Net 30\n• 2% early\nThanks.", text);
    }

    [Fact]
    public void Plain_text_numbers_an_ordered_list()
    {
        Assert.Equal("1. first\n2. second", RichText.ToPlainText("<ol><li>first</li><li>second</li></ol>"));
    }

    [Fact]
    public void Plain_text_decodes_entities_rather_than_printing_them()
    {
        Assert.Equal("Terms & conditions", RichText.ToPlainText("<p>Terms &amp; conditions</p>"));
    }

    // --- the migration's converter ------------------------------------------------------------

    /// <summary>
    /// <see cref="RichText.FromPlainText"/> is what the phase-39 migration runs over every
    /// plain-text <c>Terms</c> phases 27b and 20d stored. It must escape, or a row whose terms
    /// happen to contain "&lt;" becomes markup on conversion.
    /// </summary>
    [Fact]
    public void Plain_text_converts_to_paragraphs_with_its_markup_characters_escaped()
    {
        Assert.Equal(
            "<p>Goods once sold &amp; delivered<br />are not returnable.</p><p>Orders &lt; 500 attract a fee.</p>",
            RichText.FromPlainText("Goods once sold & delivered\nare not returnable.\n\nOrders < 500 attract a fee."));
    }

    [Fact]
    public void Converting_plain_text_produces_something_already_sanitized()
    {
        var converted = RichText.FromPlainText("A & B\nsecond line");

        Assert.Equal(converted, RichText.Sanitize(converted));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n  ")]
    public void Converting_empty_plain_text_yields_null(string? text)
    {
        Assert.Null(RichText.FromPlainText(text));
    }

    // --- helpers ------------------------------------------------------------------------------

    /// <summary>Every tag in the output, whole, so an assertion can look at what was emitted rather
    /// than at a substring that happens not to match.</summary>
    private static IEnumerable<string> Tags(string html)
    {
        var i = 0;

        while (i < html.Length)
        {
            var open = html.IndexOf('<', i);

            if (open < 0)
            {
                yield break;
            }

            var close = html.IndexOf('>', open);

            if (close < 0)
            {
                yield break;
            }

            yield return html[open..(close + 1)];
            i = close + 1;
        }
    }
}
