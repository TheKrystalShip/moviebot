// What is measured on every view, in the page itself. The site scrolls inside `.app__main` rather than
// the document, so a page-level overflow check alone reads clean while a box runs out of the column;
// both are measured, and so is every element's own right edge.
//
// A finding is a fault (something a person would see as broken), a warning (worth a look), or a note.
//   fault    the page or the column scrolls sideways; an element runs past the screen's edge; text is
//            cut off without an ellipsis; text runs into other text; the page raised an error
//   warning  a control smaller than 32px either way; a control mostly under a floating button, or
//            text partly under one
//   note     text shortened with an ellipsis, which is usually intended

export function inspect() {
  const vw = window.innerWidth;
  const main = document.querySelector(".app__main");
  const describe = (e) => {
    const id = e.id ? "#" + e.id : "";
    const cls = typeof e.className === "string" && e.className.trim() ? "." + e.className.trim().split(/\s+/).slice(0, 2).join(".") : "";
    const text = (e.innerText || e.getAttribute("aria-label") || "").trim().replace(/\s+/g, " ").slice(0, 40);
    return `${e.tagName.toLowerCase()}${id}${cls}${text ? ` "${text}"` : ""}`;
  };
  const shown = (e) => {
    const r = e.getBoundingClientRect();
    if (r.width < 1 || r.height < 1) return false;
    for (let p = e; p; p = p.parentElement) {
      const cs = getComputedStyle(p);
      if (cs.display === "none" || cs.visibility === "hidden" || cs.opacity === "0") return false;
    }
    return true;
  };
  // Off-canvas furniture that is closed is meant to sit past the edge.
  const parked = (e) => !!e.closest(".sidebar:not(.sidebar--open), .assistant-dock:not(.assistant-dock--open)");
  // An ancestor below the page column that clips sideways hides what overflows it: a rail's track, a
  // card. Overflow there is that ancestor's business, and is reported as cut text if it cuts words.
  const clippedInside = (e) => {
    for (let p = e.parentElement; p && p !== main && p !== document.body; p = p.parentElement) {
      if (/(auto|scroll|hidden|clip)/.test(getComputedStyle(p).overflowX)) return true;
    }
    return false;
  };

  const faults = [], warnings = [], notes = [];
  if (document.documentElement.scrollWidth > vw + 1) faults.push(`the page scrolls sideways: ${document.documentElement.scrollWidth}px wide on a ${vw}px screen`);
  if (main && main.scrollWidth > main.clientWidth + 1) faults.push(`the page column scrolls sideways: ${main.scrollWidth}px of content in ${main.clientWidth}px`);

  const all = [...document.body.querySelectorAll("*")].filter((e) => !(e instanceof SVGElement && e.tagName !== "svg"));
  const past = [];
  for (const e of all) {
    if (parked(e) || !shown(e)) continue;
    const r = e.getBoundingClientRect();
    if ((r.right > vw + 1 || r.left < -1) && !clippedInside(e) && getComputedStyle(e).position !== "fixed") past.push(e);
  }
  // Report the outermost element that overflows, not every descendant of it.
  for (const e of past.filter((e) => !past.some((o) => o !== e && o.contains(e))).slice(0, 8)) {
    const r = e.getBoundingClientRect();
    faults.push(`${describe(e)} runs past the screen: ${Math.round(r.left)}..${Math.round(r.right)} of ${vw}px`);
  }

  for (const e of all) {
    if (!shown(e) || parked(e)) continue;
    const own = [...e.childNodes].some((n) => n.nodeType === 3 && n.textContent.trim());
    if (!own) continue;
    const cs = getComputedStyle(e);
    if (cs.overflowX === "visible" || e.scrollWidth <= e.clientWidth + 1) continue;
    if (cs.textOverflow === "ellipsis") notes.push(`${describe(e)} is shortened with an ellipsis`);
    else faults.push(`${describe(e)} is cut off: ${e.scrollWidth}px of text in ${e.clientWidth}px`);
  }

  // Text that runs into other text: a value too wide for its cell spills over its neighbour without
  // being clipped, which no overflow measure sees. Each element's own text is boxed by a Range over
  // its text nodes, trimmed to whatever clips it (an ellipsis cuts the text where the box ends), and
  // kept only where that text is what is on top: text under a drawer or a popover is covered, not
  // collided with. Boxes of unrelated elements on screen must not cross.
  const clipOf = (e, r) => {
    let { left, top, right, bottom } = r;
    for (let p = e; p && p !== document.body; p = p.parentElement) {
      if (getComputedStyle(p).overflowX === "visible") continue;
      const c = p.getBoundingClientRect();
      left = Math.max(left, c.left); top = Math.max(top, c.top); right = Math.min(right, c.right); bottom = Math.min(bottom, c.bottom);
    }
    return { left, top, right, bottom };
  };
  const onTop = (e, r) => {
    const hit = document.elementFromPoint((r.left + r.right) / 2, (r.top + r.bottom) / 2);
    return !!hit && (hit === e || e.contains(hit) || hit.contains(e));
  };
  const boxes = [];
  for (const e of all) {
    if (!shown(e) || parked(e)) continue;
    for (const n of e.childNodes) {
      if (n.nodeType !== 3 || !n.textContent.trim()) continue;
      const range = document.createRange();
      range.selectNodeContents(n);
      const r = clipOf(e, range.getBoundingClientRect());
      if (r.right - r.left > 0 && r.bottom - r.top > 0 && r.bottom > 0 && r.top < window.innerHeight && onTop(e, r)) boxes.push({ e, r });
    }
  }
  // An icon is read like a word: a value that runs into the next one's icon collides as surely. A
  // floating control's icon is not on the page; text under one is reported below, as a warning.
  const floats = (e) => {
    for (let p = e; p && p !== document.body; p = p.parentElement) if (getComputedStyle(p).position === "fixed") return true;
    return false;
  };
  for (const e of document.body.querySelectorAll("svg")) {
    if (!shown(e) || parked(e) || floats(e)) continue;
    const r = clipOf(e, e.getBoundingClientRect());
    if (r.right - r.left > 0 && r.bottom - r.top > 0 && r.bottom > 0 && r.top < window.innerHeight && onTop(e, r)) boxes.push({ e, r });
  }
  for (let i = 0; i < boxes.length; i++) {
    for (let j = i + 1; j < boxes.length; j++) {
      const a = boxes[i], b = boxes[j];
      if (a.e === b.e || a.e.contains(b.e) || b.e.contains(a.e)) continue;
      const w = Math.min(a.r.right, b.r.right) - Math.max(a.r.left, b.r.left);
      const h = Math.min(a.r.bottom, b.r.bottom) - Math.max(a.r.top, b.r.top);
      if (w > 2 && h > 2) faults.push(`${describe(a.e)} runs into ${describe(b.e)}`);
    }
  }

  const controls = all.filter((e) => e.matches("a[href], button, input, select, textarea, [role=button], [role=tab]") && shown(e) && !parked(e));
  for (const e of controls) {
    const r = e.getBoundingClientRect();
    if (Math.min(r.width, r.height) < 32 && !e.closest(".rail__track") && !e.disabled) {
      warnings.push(`${describe(e)} is ${Math.round(r.width)}x${Math.round(r.height)}px to touch`);
    }
  }
  const floating = all.filter((e) => getComputedStyle(e).position === "fixed" && shown(e) && !parked(e) && e.matches(".assistant-fab, .mnav"));
  for (const f of floating) {
    const fr = f.getBoundingClientRect();
    for (const { e, r } of boxes) {
      if (f.contains(e)) continue;
      if (Math.min(r.right, fr.right) - Math.max(r.left, fr.left) > 2 && Math.min(r.bottom, fr.bottom) - Math.max(r.top, fr.top) > 2) {
        warnings.push(`${describe(e)} is partly under ${describe(f)}`);
      }
    }
    for (const e of controls) {
      if (f.contains(e) || e.contains(f)) continue;
      const r = e.getBoundingClientRect();
      const w = Math.min(r.right, fr.right) - Math.max(r.left, fr.left);
      const h = Math.min(r.bottom, fr.bottom) - Math.max(r.top, fr.top);
      if (w > 0 && h > 0 && w * h > 0.4 * r.width * r.height) warnings.push(`${describe(e)} is mostly under ${describe(f)}`);
    }
  }

  const unique = (list) => [...new Set(list)];
  return { faults: unique(faults), warnings: unique(warnings), notes: unique(notes) };
}

/** Lets the page column grow to its content, so one screenshot holds the whole page. */
export function unroll() {
  const style = document.createElement("style");
  style.textContent = "html,body,.app{height:auto!important;overflow:visible!important}.app__main{height:auto!important;max-height:none!important;overflow:visible!important}";
  document.head.appendChild(style);
}
