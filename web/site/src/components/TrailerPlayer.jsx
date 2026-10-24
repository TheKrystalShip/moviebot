import React from "react";
import { createPortal } from "react-dom";
import { Button } from "@thekrystalship/krystal";
import { browserLandscape } from "../player/engine/environment.ts";
import { Rank, onBack, onTv } from "../tv/tv.js";

/** Where a trailer plays from: YouTube's player, which sets no cookie until it is played. */
const YOUTUBE = "https://www.youtube-nocookie.com";

/** A screen narrow enough to be a phone's, where a trailer takes the whole screen. */
const PHONE = "(max-width: 720px)";

/** YouTube's player state for a video that played to its end. */
const ENDED = 0;

/**
 * A title's trailer playing, from YouTube's player with its own controls and sound on: in the hero's
 * place on a wide screen, the hero kept beneath so the page keeps its height, and full screen and
 * sideways on a phone (the film player's `browserLandscape`). It closes on its close button, on Esc,
 * on leaving full screen, and on the trailer's end, which the player says through its messages. On a
 * television, whose head is taller than its screen, it takes the whole screen, and has no close button:
 * Back closes it, before anything else Back would do.
 */
export function TrailerPlayer({ trailer, title, onClose }) {
  const phone = typeof window !== "undefined" && !!window.matchMedia && window.matchMedia(PHONE).matches;
  const frameRef = React.useRef(null);
  const videoRef = React.useRef(null);
  const closeRef = React.useRef(null);
  const latest = React.useRef(onClose);
  latest.current = onClose;

  React.useEffect(() => {
    const video = videoRef.current;
    if (!video) return undefined;
    // The player says what it does only to a page that has said it is listening, once it has loaded.
    const listen = () => video.contentWindow
      && video.contentWindow.postMessage(JSON.stringify({ event: "listening", id: 1, channel: "widget" }), YOUTUBE);
    const heard = (e) => {
      if (e.origin !== YOUTUBE || e.source !== video.contentWindow) return;
      let data;
      try { data = typeof e.data === "string" ? JSON.parse(e.data) : e.data; } catch { return; }
      const state = data && (data.event === "onStateChange" ? data.info
        : data.event === "infoDelivery" && data.info ? data.info.playerState : undefined);
      if (state === ENDED) latest.current();
    };
    const key = (e) => { if (e.key === "Escape") latest.current(); };
    video.addEventListener("load", listen);
    window.addEventListener("message", heard);
    window.addEventListener("keydown", key);
    if (closeRef.current) closeRef.current.focus();
    return () => {
      video.removeEventListener("load", listen);
      window.removeEventListener("message", heard);
      window.removeEventListener("keydown", key);
    };
  }, []);

  React.useEffect(() => (onTv ? onBack(() => { latest.current(); return true; }, Rank.screen) : undefined), []);

  // Whole-screen, the page under it takes no focus and no press, and the player has the focus: a
  // remote's keys go to the trailer, never to the rows it covers.
  React.useEffect(() => {
    if (!(phone || onTv)) return undefined;
    const page = document.getElementById("root");
    if (!page) return undefined;
    page.inert = true;
    if (videoRef.current) videoRef.current.focus();
    return () => { page.inert = false; };
  }, [phone]);

  React.useEffect(() => {
    const frame = frameRef.current;
    if (!phone || !frame) return undefined;
    const landscape = browserLandscape(frame);
    let full = false;
    const changed = () => {
      if (document.fullscreenElement === frame) full = true;
      else if (full) latest.current();
    };
    document.addEventListener("fullscreenchange", changed);
    landscape.enter();
    return () => {
      document.removeEventListener("fullscreenchange", changed);
      landscape.release();
    };
  }, [phone]);

  const src = `${YOUTUBE}/embed/${encodeURIComponent(trailer.key)}?autoplay=1&enablejsapi=1&rel=0&playsinline=1`
    + `&origin=${encodeURIComponent(window.location.origin)}`;
  const screen = phone || onTv;
  const player = (
    <div ref={frameRef} className={"kc-trailer" + (screen ? " kc-trailer--screen" : "")}>
      <iframe ref={videoRef} className="kc-trailer__video" src={src} title={`${title}: ${trailer.name}`}
        allow="autoplay; encrypted-media; picture-in-picture; fullscreen" allowFullScreen />
      {!onTv && (
        <Button ref={closeRef} variant="secondary" size="md" icon="x" className="kc-trailer__close"
          aria-label="Close the trailer" onClick={onClose} />
      )}
    </div>
  );
  // Whole-screen, it is drawn at the document's root: a page moved by a transform (a television's)
  // would otherwise carry a fixed frame with it.
  return screen ? createPortal(player, document.body) : player;
}
