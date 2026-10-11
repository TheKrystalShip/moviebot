import React from "react";

/**
 * The stretch at the end of an episode where its next is offered in front of everything: from the
 * last chapter when the last chapter is the credits, else the last two minutes. Shared by the player and
 * by the phone, which asks it of the film a television holds.
 */
export function inClosingStretch(manifest, position) {
  const duration = manifest.durationSeconds;
  const chapters = manifest.chapters || [];
  const last = chapters.length > 1 ? chapters[chapters.length - 1] : null;
  const from = last && last.startSeconds >= duration * 0.85 ? last.startSeconds : duration - 120;
  return duration > 0 && position >= from;
}

/** A playhead this close to a stretch's start got there by playing; further in, somebody seeked. */
const EnterSlackSeconds = 2;

export const SkipLabels = { intro: "Skip intro", recap: "Skip recap" };

/**
 * The marker this viewer is offered to skip, or null. A stretch is offered only when playback
 * entered it by playing through, and once skipped it is not offered again. With the viewer's
 * preference on and nobody else in the room, entering a stretch skips it.
 *
 * `film` is what the film says of itself: its `markers`, its `titleId`, whether it is `ready` to play and
 * whether the viewer's preference is to skip `auto`matically. `seek` moves the room.
 */
export function useSkipOffer(seek, film, position, alone) {
  const { markers, titleId, ready, auto } = film;
  const stretch = React.useRef({ titleId: null, index: -1, offered: false, handled: new Set() });
  const [offer, setOffer] = React.useState(-1);
  const seeks = React.useRef(seek);
  seeks.current = seek;

  const skip = React.useCallback((index) => {
    const s = stretch.current;
    s.handled.add(index);
    s.offered = false;
    setOffer(-1);
    seeks.current(markers[index].endSeconds);
  }, [markers]);

  React.useEffect(() => {
    if (stretch.current.titleId !== titleId) stretch.current = { titleId, index: -1, offered: false, handled: new Set() };
    const s = stretch.current;
    const index = ready && markers ? markers.findIndex((m) => position >= m.startSeconds && position < m.endSeconds) : -1;
    if (index !== s.index) {
      s.index = index;
      s.offered = index >= 0 && !s.handled.has(index) && position <= markers[index].startSeconds + EnterSlackSeconds;
      if (s.offered && auto && alone) { skip(index); return; }
    }
    setOffer(s.offered ? index : -1);
  }, [position, markers, titleId, ready, auto, alone, skip]);

  return offer >= 0 && markers && markers[offer] ? { marker: markers[offer], skip: () => skip(offer) } : null;
}
