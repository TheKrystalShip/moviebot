import React from "react";

/** A seek the system's controls ask for settles this long before it goes to the room. */
const SeekSettleMs = 300;

/** The playhead is told to the system again once it is this far from where the system reckons it is. */
const PositionSlackSeconds = 1.5;

const Actions = ["play", "pause", "seekbackward", "seekforward", "seekto", "previoustrack", "nexttrack"];

const session = () => (typeof navigator !== "undefined" && "mediaSession" in navigator ? navigator.mediaSession : null);

function handle(media, action, handler) {
  try {
    media.setActionHandler(action, handler);
  } catch {
    // A browser that does not know the action has no control to draw for it.
  }
}

/**
 * What is playing, told to the system (the Media Session API): the lock screen, the notification, a
 * headset's buttons, the keyboard's media keys and the browser's own media controls show the title,
 * the poster and where the film is, and their presses move the room as the player's own controls do.
 *
 * `film` is what is told: `title`, `artist`, `artwork`, `duration`, `position` and `paused`, with
 * `ready` false while nothing can play. `controls` answers the presses, or is null where nothing this
 * player does may move the room (a guest) or where something else answers the keys (a television's
 * remote); then the system shows what is playing and the element's own play and pause stand, which
 * the sync controller puts back for a guest. A previous or next episode it does not name is no
 * control at all.
 */
export function useMediaSession(film, controls) {
  const { title, artist, artwork, duration, position, paused, ready } = film;
  const live = React.useRef(null);
  live.current = { paused, controls };

  React.useEffect(() => {
    const media = session();
    if (!media || !ready || typeof MediaMetadata === "undefined") return undefined;
    media.metadata = new MediaMetadata({ title: title || "", artist: artist || "", artwork: artwork ? [{ src: artwork }] : [] });
    return () => { media.metadata = null; };
  }, [title, artist, artwork, ready]);

  React.useEffect(() => {
    const media = session();
    if (!media) return undefined;
    media.playbackState = !ready ? "none" : paused ? "paused" : "playing";
    return () => { media.playbackState = "none"; };
  }, [paused, ready]);

  // The system runs the clock on from the last position it was told, so it is told again only when
  // that has gone wrong: a pause, a seek, another title.
  const told = React.useRef(null);
  React.useEffect(() => {
    const media = session();
    if (!media || !media.setPositionState) return;
    if (!ready || !(duration > 0)) { told.current = null; return; }
    const now = performance.now();
    const at = Math.min(Math.max(0, position), duration);
    const last = told.current;
    const reckoned = last && (last.paused ? last.position : last.position + (now - last.at) / 1000);
    if (last && last.paused === paused && last.duration === duration && Math.abs(reckoned - at) < PositionSlackSeconds) return;
    try {
      media.setPositionState({ duration, playbackRate: 1, position: at });
      told.current = { position: at, at: now, paused, duration };
    } catch {
      told.current = null;
    }
  }, [position, paused, duration, ready]);

  const hasControls = Boolean(controls);
  const hasPrevious = Boolean(controls && controls.previous);
  const hasNext = Boolean(controls && controls.next);
  React.useEffect(() => {
    const media = session();
    if (!media || !hasControls) return undefined;
    let seekTimer = null;
    const act = (fn) => () => { const c = live.current.controls; if (c) fn(c); };
    handle(media, "play", act((c) => { if (live.current.paused) c.togglePlay(); }));
    handle(media, "pause", act((c) => { if (!live.current.paused) c.togglePlay(); }));
    handle(media, "seekbackward", (details) => { const c = live.current.controls; if (c) c.seekBy(-((details && details.seekOffset) || c.skipSeconds)); });
    handle(media, "seekforward", (details) => { const c = live.current.controls; if (c) c.seekBy((details && details.seekOffset) || c.skipSeconds); });
    // A drag along the notification's bar asks for every point it passes; the room is sent the last.
    handle(media, "seekto", (details) => {
      if (!details || typeof details.seekTime !== "number") return;
      window.clearTimeout(seekTimer);
      seekTimer = window.setTimeout(() => { const c = live.current.controls; if (c) c.seek(details.seekTime); }, SeekSettleMs);
    });
    handle(media, "previoustrack", hasPrevious ? act((c) => c.previous && c.previous()) : null);
    handle(media, "nexttrack", hasNext ? act((c) => c.next && c.next()) : null);
    return () => {
      window.clearTimeout(seekTimer);
      for (const action of Actions) handle(media, action, null);
    };
  }, [hasControls, hasPrevious, hasNext]);
}
