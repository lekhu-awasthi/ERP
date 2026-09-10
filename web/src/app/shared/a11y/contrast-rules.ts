/**
 * Phase 34a — the colour pairs this app puts on screen, and their measured contrast ratios.
 *
 * <b>Why this is computed rather than listed.</b> WCAG 1.4.3 is a numeric threshold, not a taste
 * judgement: 4.5:1 for normal text. Writing down "never use `text-success` on `bg-success-subtle`"
 * records a conclusion and loses the reason, so nobody can check it and nobody can extend it. This
 * file records the palette and derives the conclusion, which makes the rule auditable — and made the
 * phase's central finding legible in the first place: the codebase paired a `-subtle` background
 * with a plain tone 161 times at 3.4–3.9:1, and with the `-emphasis` tone 50 times at 7.2–10.5:1. It
 * was doing the same thing two ways, one of which failed.
 *
 * Contrast is the interesting boundary case in this phase's mechanisable/human split. In general it
 * needs rendering — you cannot know a colour without computing the cascade. Here you can, because
 * every colour in the app comes from a closed set of Bootstrap utility classes whose values are
 * fixed constants, so a class pair decides it statically. The moment a screen sets a colour any
 * other way, that stops being true and this guard stops covering it.
 *
 * Values are Bootstrap 5.3's defaults; the app overrides none of them (`styles.scss` imports
 * Bootstrap and adds layout only).
 */

/** A colour that a utility class produces, as an opaque sRGB hex. */
interface Swatch {
  readonly className: string;
  readonly hex: string;
}

/** The grounds text is rendered on: the page, a card, and the `-subtle` tinted backgrounds. */
const BACKGROUNDS: readonly Swatch[] = [
  { className: '', hex: '#ffffff' }, // .bg-white, and every card
  { className: 'bg-light', hex: '#f8f9fa' }, // the page body
  { className: 'bg-primary-subtle', hex: '#cfe2ff' },
  { className: 'bg-secondary-subtle', hex: '#e2e3e5' },
  { className: 'bg-success-subtle', hex: '#d1e7dd' },
  { className: 'bg-danger-subtle', hex: '#f8d7da' },
  { className: 'bg-warning-subtle', hex: '#fff3cd' },
  { className: 'bg-info-subtle', hex: '#cff4fc' },
];

/**
 * `bg-<tone> bg-opacity-10` is this app's other tinted ground -- the little rounded icon chips on
 * every list row, 54 of them. It composites the brand colour at 10% over the card's white, which is
 * lighter than the `-subtle` swatch and therefore the harder test of the two.
 */
function atTenPercent(hex: string): string {
  const over = [1, 3, 5]
    .map((i) => Math.round(parseInt(hex.slice(i, i + 2), 16) * 0.1 + 255 * 0.9))
    .map((v) => v.toString(16).padStart(2, '0'))
    .join('');
  return `#${over}`;
}

/**
 * The foreground values, after phase 34a re-pointed the four brand text utilities at Bootstrap's
 * -600 shades in `styles.scss`. Bootstrap's defaults (#0d6efd, #6c757d, #198754, #dc3545) clear
 * 4.5:1 against pure white by a hair and fail against this app's #f8f9fa page; these clear both.
 */
const FOREGROUNDS: readonly Swatch[] = [
  { className: 'text-primary', hex: '#0a58ca' },
  { className: 'text-secondary', hex: '#565e64' },
  { className: 'text-success', hex: '#146c43' },
  { className: 'text-danger', hex: '#b02a37' },
  { className: 'text-warning', hex: '#ffc107' },
  { className: 'text-info', hex: '#0dcaf0' },
  { className: 'text-primary-emphasis', hex: '#052c65' },
  { className: 'text-secondary-emphasis', hex: '#2b2f32' },
  { className: 'text-success-emphasis', hex: '#0a3622' },
  { className: 'text-danger-emphasis', hex: '#58151c' },
  { className: 'text-warning-emphasis', hex: '#664d03' },
  { className: 'text-info-emphasis', hex: '#055160' },
];

/**
 * The tones whose `-subtle` background is the only ground they are ever used on. A `text-*` outside
 * that pairing sits on white or `bg-light`, which is what makes `text-warning` (1.63:1 on white) a
 * failure with no background class involved at all.
 */
const TONES = ['primary', 'secondary', 'success', 'danger', 'warning', 'info'] as const;

/** The unmodified brand colours -- still what `bg-*` paints, and so what a 10% tint is made of. */
const BRAND: Readonly<Record<(typeof TONES)[number], string>> = {
  primary: '#0d6efd',
  secondary: '#6c757d',
  success: '#198754',
  danger: '#dc3545',
  warning: '#ffc107',
  info: '#0dcaf0',
};

export interface ContrastRule {
  readonly foreground: string;
  /** The background class this pairing requires, or null for "on the page's own white/near-white". */
  readonly background: string | null;
  readonly ratio: number;
  /** 4.5:1, the Level AA threshold for text below 18.66px bold / 24px regular. */
  readonly passesAA: boolean;
}

/** WCAG 2.1's relative luminance, verbatim from the definition. */
export function relativeLuminance(hex: string): number {
  const channels = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255);
  const [r, g, b] = channels.map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

/** WCAG 2.1's contrast ratio, `(L1 + 0.05) / (L2 + 0.05)` with the lighter colour as L1. */
export function contrastRatio(a: string, b: string): number {
  const [lighter, darker] = [relativeLuminance(a), relativeLuminance(b)].sort((x, y) => y - x);
  return (lighter + 0.05) / (darker + 0.05);
}

const AA_NORMAL_TEXT = 4.5;

function build(): ContrastRule[] {
  const rules: ContrastRule[] = [];

  for (const fg of FOREGROUNDS) {
    const tone = TONES.find((t) => fg.className.startsWith(`text-${t}`))!;

    // Against its own subtle background -- the badge and pill pairing used all over the app.
    const subtle = BACKGROUNDS.find((b) => b.className === `bg-${tone}-subtle`)!;
    const onSubtle = contrastRatio(fg.hex, subtle.hex);
    rules.push({
      foreground: fg.className,
      background: subtle.className,
      ratio: onSubtle,
      passesAA: onSubtle >= AA_NORMAL_TEXT,
    });

    // Against its own 10%-opacity tint -- the icon chips on the list rows.
    const tint = atTenPercent(BRAND[tone]);
    const onTint = contrastRatio(fg.hex, tint);
    rules.push({
      foreground: fg.className,
      background: `bg-${tone} bg-opacity-10`,
      ratio: onTint,
      passesAA: onTint >= AA_NORMAL_TEXT,
    });

    // Against the page itself -- the worse of a white card and the #f8f9fa body, so the rule holds
    // on both. This is the pairing Bootstrap's own defaults failed: 4.27:1 on the body.
    const onPage = Math.min(contrastRatio(fg.hex, '#ffffff'), contrastRatio(fg.hex, '#f8f9fa'));
    rules.push({
      foreground: fg.className,
      background: null,
      ratio: onPage,
      passesAA: onPage >= AA_NORMAL_TEXT,
    });
  }

  return rules;
}

/** Every pairing the app can produce, measured. The sweep guard forbids the failing ones. */
export const CONTRAST_RULES: readonly ContrastRule[] = build();
