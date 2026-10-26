import React from "react";

/**
 * The stretch at the end of an episode where its next is offered in front of everything: from the
 * start of its credits where they are marked, else from the last chapter when the last chapter is the
 * credits, else the last two minutes. Shared by the player and by the phone, which asks it of the film
 * a television holds.
 */
export function inClosingStretch(manifest, position) {
  const duration = manifest.durationSeconds;
  const credits = (manifest.markers || []).find((m) => m.kind === "credits");
  const chapters = manifest.chapters || [];
  const last = chapters.length > 1 ? chapters[chapters.length - 1] : null;
  const from = credits ? credits.startSeconds
    : last && last.startSeconds >= duration * 0.85 ? last.startSeconds : duration - 120;
  return duration > 0 && position >= from;
}

/** A playhead this close to a stretch's start got there by playing; further in, somebody seeked. */
const EnterSlackSeconds = 2;

export const SkipLabels = { intro: "Skip intro", recap: "Skip recap", credits: "Skip credits" };

/** A playhead this far from the end has nothing after it a skip could take it to. */
const EndSlackSeconds = 1;

/**
 * Whether a stretch is one a skip takes anywhere. Credits that run to the end of the episode skip to
 * nothing: there the offer is the next episode, which the closing stretch makes.
 */
const skippable = (marker, duration) =>
  marker.kind !== "credits" || marker.endSeconds < duration - EndSlackSeconds;

/**
 * The marker this viewer is offered to skip, or null. A stretch is offered only when playback
 * entered it by playing through, and once skipped it is not offered again. With the viewer's
 * preference on and nobody else in the room, entering an intro or a recap skips it; the credits are
 * only ever offered, since what follows them is the episode's own.
 *
 * `film` is what the film says of itself: its `markers`, its `duration`, its `titleId`, whether it is
 * `ready` to play and whether the viewer's preference is to skip `auto`matically. `seek` moves the room.
 */
export function useSkipOffer(seek, film, position, alone) {
  const { markers, duration, titleId, ready, auto } = film;
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
    const index = ready && markers
      ? markers.findIndex((m) => position >= m.startSeconds && position < m.endSeconds && skippable(m, duration)) : -1;
    if (index !== s.index) {
      s.index = index;
      s.offered = index >= 0 && !s.handled.has(index) && position <= markers[index].startSeconds + EnterSlackSeconds;
      if (s.offered && auto && alone && markers[index].kind !== "credits") { skip(index); return; }
    }
    setOffer(s.offered ? index : -1);
  }, [position, markers, duration, titleId, ready, auto, alone, skip]);

  return offer >= 0 && markers && markers[offer] ? { marker: markers[offer], skip: () => skip(offer) } : null;
}
