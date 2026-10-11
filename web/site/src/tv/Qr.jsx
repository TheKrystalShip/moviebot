import React from "react";

/**
 * A QR code of `value`, drawn as SVG by `lean-qr`, which only a television ever fetches. It is encoded
 * at error correction M or better, and drawn with `quiet` modules of quiet zone inside the SVG, for a
 * frame whose own light margin makes up the rest of the four a scanner expects.
 */
export function Qr({ value, quiet = 4, className, label }) {
  const [drawn, setDrawn] = React.useState(null);
  React.useEffect(() => {
    let live = true;
    Promise.all([import("lean-qr"), import("lean-qr/extras/svg")]).then(([{ correction, generate }, { toSvgPath }]) => {
      if (!live) return;
      const code = generate(value, { minCorrectionLevel: correction.M });
      setDrawn({ value, size: code.size, path: toSvgPath(code) });
    }, () => {});
    return () => { live = false; };
  }, [value]);
  return (
    <div className={className} role={label ? "img" : undefined} aria-label={label}>
      {drawn && drawn.value === value && (
        <svg viewBox={`${-quiet} ${-quiet} ${drawn.size + quiet * 2} ${drawn.size + quiet * 2}`} shapeRendering="crispEdges"
          aria-hidden="true" data-modules={drawn.size}>
          <path d={drawn.path} />
        </svg>
      )}
    </div>
  );
}
