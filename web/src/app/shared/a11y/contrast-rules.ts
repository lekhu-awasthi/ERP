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

// -------------------------------------------------------------------------------------------------
// Phase 40 — the focus indicator, measured the same way.
//
// Phase 34a's rules above ask one question of every colour pair: does the *text* clear 4.5:1. That
// predicate names a property, and so it silently said nothing about the other thing WCAG puts a
// contrast floor under — SC 1.4.11 Non-text Contrast, which requires **3:1** for a focus indicator
// against what it sits on. Driving the app from a keyboard is what surfaced it: every control in the
// content area was focused, visibly, and the ring was almost not there.
//
// The cause is phase 34a's own finding one layer down. Bootstrap paints `:focus-visible` as
// `box-shadow: 0 0 0 .25rem rgba(<the button's own tone>, .5)` — the brand colour at half alpha,
// composited over whatever is behind it. 34a found those tones clear 4.5:1 against pure white "and
// only just"; at 50% alpha they clear nothing at all. Measured below: 1.21:1 to 2.53:1 across the
// twelve variants the app uses, every one of them under 3:1.
//
// `styles.scss` therefore replaces the ring for every focusable element with a single 2px outline in
// the already-darkened `#0a58ca`, offset 2px so it is adjacent to the page rather than to the
// control's own fill. One indicator, one number, on every control — which is also why the left nav's
// hand-written version (phase 34b) could stop being a special case.
// -------------------------------------------------------------------------------------------------

/** WCAG 2.1 SC 1.4.11 Non-text Contrast: 3:1 for a focus indicator and other UI components. */
export const AA_NON_TEXT = 3;

/** The single focus-ring colour `styles.scss` paints, and its offset in px. */
export const FOCUS_RING = { color: '#0a58ca', widthPx: 2, offsetPx: 2 } as const;

/** `--bs-btn-focus-shadow-rgb` for the button variants this app uses, read off the built stylesheet. */
const BOOTSTRAP_FOCUS_SHADOW_RGB: Readonly<Record<string, readonly [number, number, number]>> = {
  'btn-primary': [49, 132, 253],
  'btn-secondary': [130, 138, 145],
  'btn-success': [60, 153, 110],
  'btn-danger': [225, 83, 97],
  'btn-warning': [217, 164, 6],
  'btn-info': [11, 172, 204],
  'btn-light': [211, 212, 213],
  'btn-dark': [66, 70, 73],
  'btn-outline-primary': [13, 110, 253],
  'btn-outline-secondary': [108, 117, 125],
  'btn-outline-success': [25, 135, 84],
  'btn-outline-danger': [220, 53, 69],
};

/** Composite `rgb` at `alpha` over an opaque `#rrggbb` ground, as Bootstrap's ring does. */
function composite(rgb: readonly [number, number, number], alpha: number, groundHex: string): string {
  const ground = [1, 3, 5].map((i) => parseInt(groundHex.slice(i, i + 2), 16));
  return `#${rgb
    .map((c, i) => Math.round(c * alpha + ground[i] * (1 - alpha)))
    .map((v) => v.toString(16).padStart(2, '0'))
    .join('')}`;
}

export interface FocusRingRule {
  /** The button variant, or `''` for the app's own replacement ring. */
  readonly variant: string;
  /** Worst of a white card and the #f8f9fa page body — the ring must clear 3:1 on both. */
  readonly ratio: number;
  readonly passesAA: boolean;
}

function buildFocusRules(): FocusRingRule[] {
  const worstOnBothGrounds = (colourOn: (ground: string) => string) =>
    Math.min(contrastRatio(colourOn('#ffffff'), '#ffffff'), contrastRatio(colourOn('#f8f9fa'), '#f8f9fa'));

  const rules: FocusRingRule[] = Object.entries(BOOTSTRAP_FOCUS_SHADOW_RGB).map(([variant, rgb]) => {
    const ratio = worstOnBothGrounds((ground) => composite(rgb, 0.5, ground));
    return { variant, ratio, passesAA: ratio >= AA_NON_TEXT };
  });

  // The app's replacement: opaque, so it composites to itself on either ground.
  const own = worstOnBothGrounds(() => FOCUS_RING.color);
  rules.push({ variant: '', ratio: own, passesAA: own >= AA_NON_TEXT });

  return rules;
}

/** Bootstrap's twelve stock focus rings plus the one this app paints instead. */
export const FOCUS_RING_RULES: readonly FocusRingRule[] = buildFocusRules();
