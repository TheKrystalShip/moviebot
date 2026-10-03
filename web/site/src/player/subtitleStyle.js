/**
 * How a person's subtitles look: the defaults, the choices offered, and the CSS a style becomes.
 *
 * A stored style holds only what the person changed, and every field missing is read from
 * `DEFAULT_STYLE`, so the player's own look can be retuned for everybody who never touched it. The
 * cues are sized against the frame (`cqh`), so a size reads the same on a phone and a television.
 */

/** White letters with a black outline and no box: legible over any picture, hiding none of it. */
export const DEFAULT_STYLE = {
  size: 100,
  font: "sans",
  bold: false,
  textColor: "#ffffff",
  edge: "outline",
  edgeColor: "#000000",
  backgroundColor: "#000000",
  backgroundOpacity: 0,
  position: 6,
};

/**
 * The text height at 100%, as a share of the picture's height. The picture is the frame's height or
 * its width at 16:9, whichever is less: a phone held upright is a tall frame with the film across the
 * middle of it, and text sized to the frame would be sized to the black above and below.
 */
const BASE_SHARE = 3.8;
const FLOOR_PX = 12;

export const SIZES = [50, 75, 100, 125, 150, 200];

export const FONTS = [
  { value: "sans", label: "Sans", stack: 'system-ui, -apple-system, "Segoe UI", Roboto, sans-serif' },
  { value: "serif", label: "Serif", stack: 'Georgia, "Times New Roman", serif' },
  { value: "mono", label: "Mono", stack: 'ui-monospace, "SF Mono", Menlo, Consolas, monospace' },
  { value: "casual", label: "Casual", stack: '"Comic Neue", "Comic Sans MS", "Chalkboard SE", cursive' },
];

export const EDGES = [
  { value: "none", label: "None" },
  { value: "outline", label: "Outline" },
  { value: "shadow", label: "Shadow" },
  { value: "raised", label: "Raised" },
  { value: "depressed", label: "Depressed" },
];

/** The caption palette televisions offer, white first. */
export const COLORS = [
  { value: "#ffffff", label: "White" },
  { value: "#ffe14d", label: "Yellow" },
  { value: "#5ee0f0", label: "Cyan" },
  { value: "#7ee07e", label: "Green" },
  { value: "#f07ef0", label: "Magenta" },
  { value: "#f06a6a", label: "Red" },
  { value: "#6a8cf0", label: "Blue" },
  { value: "#000000", label: "Black" },
];

export const OPACITIES = [0, 0.25, 0.5, 0.75, 1];

/** The style with every field filled, from the person's choices over the defaults. */
export function resolveStyle(style) {
  const out = { ...DEFAULT_STYLE };
  if (style) {
    for (const key of Object.keys(DEFAULT_STYLE)) {
      if (style[key] !== null && style[key] !== undefined) out[key] = style[key];
    }
  }
  return out;
}

/**
 * Only what differs from the defaults, which is what is stored: a person who sets everything back
 * holds no style, and follows the defaults again.
 */
export function storedStyle(style) {
  const out = {};
  for (const key of Object.keys(DEFAULT_STYLE)) {
    if (style[key] !== undefined && style[key] !== null && style[key] !== DEFAULT_STYLE[key]) out[key] = style[key];
  }
  return Object.keys(out).length > 0 ? out : null;
}

function rgba(hex, alpha) {
  const n = Number.parseInt(hex.slice(1), 16);
  return `rgba(${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255}, ${alpha})`;
}

/**
 * The edge as a text shadow, in em so it scales with the text. An outline is eight offsets rather
 * than a text stroke: a stroke is drawn over the letters and thins them, and paint order is not
 * honoured for HTML text everywhere the player runs.
 */
function edgeShadow(edge, color) {
  const w = "0.06em";
  switch (edge) {
    case "outline":
      return [
        `-${w} -${w}`, `0 -${w}`, `${w} -${w}`, `-${w} 0`, `${w} 0`, `-${w} ${w}`, `0 ${w}`, `${w} ${w}`,
      ].map((offset) => `${offset} 0 ${color}`).join(", ") + `, 0 0 0.2em ${rgba(color, 0.6)}`;
    case "shadow":
      return `0.06em 0.06em 0.1em ${color}, 0 0 0.3em ${rgba(color, 0.7)}`;
    case "raised":
      return `0.05em 0.05em 0 ${color}, -0.03em -0.03em 0 rgba(255, 255, 255, 0.45)`;
    case "depressed":
      return `-0.05em -0.05em 0 ${color}, 0.03em 0.03em 0 rgba(255, 255, 255, 0.45)`;
    default:
      return "none";
  }
}

/** The custom properties `.mp-cues` reads, for a style as stored. */
export function cueVars(style) {
  const s = resolveStyle(style);
  const font = FONTS.find((f) => f.value === s.font) || FONTS[0];
  const k = s.size / 100;
  const share = BASE_SHARE * k;
  return {
    "--cue-size": `max(${FLOOR_PX * k}px, min(${share}cqh, ${(share * 9) / 16}cqw))`,
    "--cue-font": font.stack,
    "--cue-weight": s.bold ? 700 : 500,
    "--cue-color": s.textColor,
    "--cue-shadow": edgeShadow(s.edge, s.edgeColor),
    "--cue-background": s.backgroundOpacity > 0 ? rgba(s.backgroundColor, s.backgroundOpacity) : "transparent",
    "--cue-bottom": `${s.position}%`,
  };
}
