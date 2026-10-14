import { Button, RadioGroup, Slider } from "@thekrystalship/krystal";
import { COLORS, DEFAULT_STYLE, EDGES, FONTS, OPACITIES, SIZES, resolveStyle, storedStyle } from "./subtitleStyle.js";
import "./styleEditor.css";

function Choices({ label, options, value, onPick }) {
  return (
    <div className="mp-style__row">
      <span className="mp-style__label">{label}</span>
      <RadioGroup size="sm" label={label} options={options} value={value} onChange={onPick} />
    </div>
  );
}

/** The palette as swatches, and the browser's own picker for anything else. */
function Colours({ label, value, onPick }) {
  return (
    <div className="mp-style__row">
      <span className="mp-style__label">{label}</span>
      <RadioGroup variant="swatch" size="sm" label={label} picker="Another colour" value={value} onChange={onPick}
        options={COLORS.map((c) => ({ value: c.value, label: c.label, color: c.value }))} />
    </div>
  );
}

/**
 * Every choice about how subtitles look. `style` is the stored style (only what differs from the
 * defaults, or null) and `onChange` is handed the same shape back, so a person who sets everything
 * to the defaults again holds no style.
 */
export function SubtitleStyleEditor({ style, onChange }) {
  const s = resolveStyle(style);
  const set = (patch) => onChange(storedStyle({ ...s, ...patch }));

  return (
    <div className="mp-style">
      <Choices label="Size" options={SIZES.map((v) => ({ value: v, label: `${v}%` }))} value={s.size} onPick={(size) => set({ size })} />
      <Choices label="Font" options={FONTS} value={s.font} onPick={(font) => set({ font })} />
      <Choices label="Weight" options={[{ value: false, label: "Regular" }, { value: true, label: "Bold" }]} value={s.bold}
        onPick={(bold) => set({ bold })} />
      <Colours label="Text" value={s.textColor} onPick={(textColor) => set({ textColor })} />
      <Choices label="Edge" options={EDGES} value={s.edge} onPick={(edge) => set({ edge })} />
      {s.edge !== "none" && <Colours label="Edge colour" value={s.edgeColor} onPick={(edgeColor) => set({ edgeColor })} />}
      <Choices label="Background" options={OPACITIES.map((v) => ({ value: v, label: v === 0 ? "None" : `${v * 100}%` }))}
        value={s.backgroundOpacity} onPick={(backgroundOpacity) => set({ backgroundOpacity })} />
      {s.backgroundOpacity > 0 && (
        <Colours label="Background colour" value={s.backgroundColor} onPick={(backgroundColor) => set({ backgroundColor })} />
      )}
      <div className="mp-style__row">
        <span className="mp-style__label">Height above the bottom<span className="mp-style__value">{s.position}%</span></span>
        <Slider size="sm" min={0} max={30} value={s.position} label="Height above the bottom" onChange={(position) => set({ position })} />
      </div>
      <Button variant="outline" size="sm" className="mp-style__reset" disabled={storedStyle(s) === null}
        onClick={() => onChange(storedStyle(DEFAULT_STYLE))}>Reset to default</Button>
    </div>
  );
}
