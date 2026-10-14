import { Button, Slider } from "@thekrystalship/krystal";
import { COLORS, DEFAULT_STYLE, EDGES, FONTS, OPACITIES, SIZES, resolveStyle, storedStyle } from "./subtitleStyle.js";
import "./styleEditor.css";

function Choices({ label, options, value, onPick }) {
  return (
    <div className="mp-style__row">
      <span className="mp-style__label">{label}</span>
      <div className="mp-style__choices" role="radiogroup" aria-label={label}>
        {options.map((o) => (
          <button key={String(o.value)} type="button" role="radio" aria-checked={o.value === value}
            className={"mp-style__choice" + (o.value === value ? " is-on" : "")} onClick={() => onPick(o.value)}>
            {o.label}
          </button>
        ))}
      </div>
    </div>
  );
}

/** The palette as swatches, and the browser's own picker for anything else. */
function Colours({ label, value, onPick }) {
  const custom = !COLORS.some((c) => c.value === value);
  return (
    <div className="mp-style__row">
      <span className="mp-style__label">{label}</span>
      <div className="mp-style__choices" role="radiogroup" aria-label={label}>
        {COLORS.map((c) => (
          <button key={c.value} type="button" role="radio" aria-checked={c.value === value} aria-label={c.label} title={c.label}
            className={"mp-style__swatch" + (c.value === value ? " is-on" : "")} style={{ "--swatch": c.value }}
            onClick={() => onPick(c.value)} />
        ))}
        <label className={"mp-style__swatch mp-style__swatch--custom" + (custom ? " is-on" : "")} title="Another colour"
          style={custom ? { "--swatch": value } : undefined}>
          <input type="color" value={value} aria-label={`${label}, another colour`} onChange={(e) => onPick(e.target.value)} />
        </label>
      </div>
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
