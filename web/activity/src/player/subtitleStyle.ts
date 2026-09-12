import type Player from 'video.js/dist/types/player';

/**
 * How subtitles look, and how that reaches the cues on screen.
 *
 * The player library renders cues itself rather than handing them to the browser, so a cue is an
 * absolutely positioned box holding one inline div, both carrying styles written straight onto
 * the element: the box carries the font and the position the layout pass computed, the inline div
 * carries the colour and the background. Inline styles are what a stylesheet cannot reach and
 * `::cue` addresses cues the browser drew, of which there are none here, so appearance is written
 * onto the elements on the same seam the library uses for its own overrides.
 *
 * Everything here is one viewer's own. A style is about the eyes reading it rather than about the
 * film, so it is not held per title either.
 */

export type SubtitleFont = 'sans' | 'serif' | 'mono';
export type SubtitleEdge = 'none' | 'outline' | 'shadow';

export interface SubtitleStyle {
  textColor: string;
  /** A colour, or `none` for text over the film with nothing behind it. */
  background: string;
  backgroundOpacity: number;
  font: SubtitleFont;
  bold: boolean;
  edge: SubtitleEdge;
  /** Multiplies the size the layout derives from the frame, so one setting holds at every size. */
  scale: number;
  /** How far the cue is raised off the bottom, as a fraction of the frame's height. */
  lift: number;
}

/** White on a black band, at the size the frame gives it: what a cue is drawn as untouched. */
export const DefaultSubtitleStyle: SubtitleStyle = {
  textColor: '#ffffff',
  background: '#000000',
  backgroundOpacity: 0.8,
  font: 'sans',
  bold: false,
  edge: 'none',
  scale: 1,
  lift: 0
};

export interface Swatch {
  value: string;
  name: string;
}

export const TextColors: readonly Swatch[] = [
  { value: '#ffffff', name: 'White' },
  { value: '#c8ccd2', name: 'Silver' },
  { value: '#f2d54a', name: 'Yellow' },
  { value: '#f0a33c', name: 'Amber' },
  { value: '#7ddc8b', name: 'Green' },
  { value: '#6fe3e1', name: 'Cyan' },
  { value: '#f39ac7', name: 'Pink' },
  { value: '#000000', name: 'Black' }
];

export const BackgroundColors: readonly Swatch[] = [
  { value: 'none', name: 'None' },
  { value: '#000000', name: 'Black' },
  { value: '#1b1f27', name: 'Slate' },
  { value: '#3a2a12', name: 'Sepia' },
  { value: '#ffffff', name: 'White' }
];

export const Fonts: readonly { value: SubtitleFont; name: string }[] = [
  { value: 'sans', name: 'Sans' },
  { value: 'serif', name: 'Serif' },
  { value: 'mono', name: 'Mono' }
];

export const Edges: readonly { value: SubtitleEdge; name: string }[] = [
  { value: 'none', name: 'None' },
  { value: 'outline', name: 'Outline' },
  { value: 'shadow', name: 'Shadow' }
];

export const ScaleRange = { min: 0.6, max: 2.4, step: 0.05 };
export const LiftRange = { min: 0, max: 0.3, step: 0.01 };
export const OpacityRange = { min: 0, max: 1, step: 0.05 };

const Families: Record<SubtitleFont, string> = {
  sans: 'system-ui, -apple-system, "Segoe UI", Roboto, sans-serif',
  serif: 'Georgia, "Times New Roman", Times, serif',
  mono: 'ui-monospace, SFMono-Regular, Menlo, Consolas, monospace'
};

/** What an outline and a shadow are drawn in. Both exist to separate text from a bright frame. */
const EdgeColor = 'rgba(0, 0, 0, 0.92)';

export function isDefaultStyle(style: SubtitleStyle): boolean {
  return (Object.keys(DefaultSubtitleStyle) as (keyof SubtitleStyle)[])
    .every((key) => style[key] === DefaultSubtitleStyle[key]);
}

/**
 * Reads a style back from whatever storage holds, field by field.
 *
 * A stored style is whatever was in this browser when the page last ran, which is not necessarily
 * what this build writes. Falling back per field rather than wholesale keeps everything that is
 * still recognisable: a value nobody offers any more costs its own setting and nothing else.
 */
export function normaliseSubtitleStyle(value: unknown): SubtitleStyle {
  if (value === null || typeof value !== 'object') return { ...DefaultSubtitleStyle };
  const raw = value as Partial<Record<keyof SubtitleStyle, unknown>>;

  return {
    textColor: oneOf(raw.textColor, TextColors.map((c) => c.value), DefaultSubtitleStyle.textColor),
    background: oneOf(raw.background, BackgroundColors.map((c) => c.value), DefaultSubtitleStyle.background),
    backgroundOpacity: clamped(raw.backgroundOpacity, OpacityRange, DefaultSubtitleStyle.backgroundOpacity),
    font: oneOf(raw.font, Fonts.map((f) => f.value), DefaultSubtitleStyle.font),
    bold: raw.bold === true,
    edge: oneOf(raw.edge, Edges.map((e) => e.value), DefaultSubtitleStyle.edge),
    scale: clamped(raw.scale, ScaleRange, DefaultSubtitleStyle.scale),
    lift: clamped(raw.lift, LiftRange, DefaultSubtitleStyle.lift)
  };
}

function oneOf<T extends string>(value: unknown, allowed: T[], fallback: T): T {
  return typeof value === 'string' && (allowed as string[]).includes(value) ? (value as T) : fallback;
}

function clamped(value: unknown, range: { min: number; max: number }, fallback: number): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) return fallback;
  return Math.min(range.max, Math.max(range.min, value));
}

/** A hex colour at an opacity, as the one string a background can be set to. */
export function withOpacity(hex: string, opacity: number): string {
  const value = hex.replace('#', '');
  const r = parseInt(value.slice(0, 2), 16);
  const g = parseInt(value.slice(2, 4), 16);
  const b = parseInt(value.slice(4, 6), 16);
  return `rgba(${r}, ${g}, ${b}, ${opacity})`;
}

/**
 * Paints one cue: the box the layout placed, and the inline div holding the text.
 *
 * Colour and background go on the inline div, which is where the renderer puts them and why: an
 * inline box wraps each line of text on its own, so a two-line cue gets a band per line rather
 * than one rectangle with a ragged line inside it.
 *
 * Everything is written afresh on every pass and nothing is accumulated, because the renderer
 * reuses a cue's element between passes — a size derived from what the element already carries
 * would compound every time the display was redrawn.
 */
export function paintCue(box: HTMLElement, text: HTMLElement, style: SubtitleStyle): void {
  text.style.color = style.textColor;
  text.style.backgroundColor = style.background === 'none'
    ? 'transparent'
    : withOpacity(style.background, style.backgroundOpacity);
  text.style.fontFamily = Families[style.font];
  text.style.fontWeight = style.bold ? '700' : '400';
  // Sized against the box the layout produced rather than in pixels of its own, so one setting
  // reads the same on a phone-sized frame and on a television.
  text.style.fontSize = `${style.scale}em`;

  // A stroke painted under the glyph rather than over it. Over it, a stroke wide enough to
  // separate text from a bright frame eats into the strokes it is outlining and closes up the
  // counters, which at the size subtitles are read at turns letters into shapes.
  text.style.setProperty('-webkit-text-stroke',
    style.edge === 'outline' ? `0.055em ${EdgeColor}` : '');
  text.style.setProperty('paint-order', style.edge === 'outline' ? 'stroke fill' : '');
  text.style.textShadow = style.edge === 'shadow'
    ? `0 0.04em 0.08em ${EdgeColor}, 0 0 0.04em ${EdgeColor}`
    : '';

  const frame = box.offsetParent as HTMLElement | null;
  const height = frame?.clientHeight ?? 0;

  // Whether this cue hangs off the bottom of the frame, which is where dialogue sits and the only
  // place the two settings below mean anything. A caption placed against the top of the frame —
  // a sign, a forced line over end credits — is left where it was put: raising it walks it into
  // the picture, and releasing its height drops it to the floor.
  const low = height > 0 && box.offsetTop + box.offsetHeight / 2 > height * 0.55;

  // A box measured before the text was resized holds a height that no longer fits it, so the
  // height is given back and the cue hangs from the edge it was placed against.
  const resized = style.scale !== 1 && low;
  box.style.height = resized ? 'auto' : '';
  box.style.top = resized ? 'auto' : '';

  // Raised by a transform rather than by moving the box: the position on the box is what the next
  // pass measures from, and editing it there would have to be undone before anything could be
  // measured again.
  box.style.transform = low && style.lift > 0 ? `translateY(${-style.lift * height}px)` : '';
}

/**
 * Keeps every cue on screen painted.
 *
 * The library redraws the whole display on each cue change, discarding anything written onto the
 * cues it replaces, so the paint hangs off the redraw itself rather than watching the DOM for
 * something the library already announces by doing it.
 */
export class SubtitleStyler {
  private readonly display: { updateDisplay(): void; el(): Element } | undefined;
  private readonly original: (() => void) | undefined;
  private style: SubtitleStyle;

  constructor(player: Player, style: SubtitleStyle) {
    this.style = style;
    this.display = player.getChild('textTrackDisplay') as unknown as
      { updateDisplay(): void; el(): Element } | undefined;

    if (this.display === undefined) return;

    this.original = this.display.updateDisplay.bind(this.display);
    this.display.updateDisplay = () => {
      this.original?.();
      this.paint();
    };
  }

  set(style: SubtitleStyle): void {
    this.style = style;
    this.display?.updateDisplay();
  }

  dispose(): void {
    if (this.display !== undefined && this.original !== undefined) {
      this.display.updateDisplay = this.original;
    }
  }

  private paint(): void {
    const root = this.display?.el();
    if (!(root instanceof HTMLElement)) return;

    for (const box of root.querySelectorAll<HTMLElement>('.vjs-text-track-cue')) {
      const text = box.firstElementChild;
      if (text instanceof HTMLElement) paintCue(box, text, this.style);
    }
  }
}
