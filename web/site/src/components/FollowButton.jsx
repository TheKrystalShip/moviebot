import React from "react";
import { Icon, Toggle, toast } from "@thekrystalship/krystal-ui";
import { api } from "../api.js";

/**
 * Follow a show, or, once following, a menu on the same button: whether new episodes are fetched
 * when they air, and unfollowing. The follow is the signed-in person's own.
 */
export function FollowButton({ imdbId, title, follow, onChange }) {
  const [open, setOpen] = React.useState(false);
  const [busy, setBusy] = React.useState(false);
  const ref = React.useRef(null);

  React.useEffect(() => {
    if (!open) return undefined;
    const outside = (e) => { if (ref.current && !ref.current.contains(e.target)) setOpen(false); };
    const escape = (e) => { if (e.key === "Escape") setOpen(false); };
    document.addEventListener("mousedown", outside);
    document.addEventListener("keydown", escape);
    return () => {
      document.removeEventListener("mousedown", outside);
      document.removeEventListener("keydown", escape);
    };
  }, [open]);

  const run = async (work, failure) => {
    setBusy(true);
    try {
      await work();
    } catch (error) {
      toast.fromError(error, failure);
    } finally {
      setBusy(false);
    }
  };

  const start = () => run(async () => {
    await api.follow(imdbId, false);
    toast.success(`You follow ${title}.`);
    onChange();
  }, "Could not follow it");
  const setAuto = (on) => run(async () => { await api.follow(imdbId, on); onChange(); }, "Could not save that");
  const stop = () => run(async () => {
    await api.unfollow(imdbId);
    setOpen(false);
    toast.info(`You no longer follow ${title}.`);
    onChange();
  }, "Could not unfollow it");

  if (!follow) {
    return (
      <button type="button" className="kc-btn kc-btn--lg" disabled={busy} onClick={start}>
        <Icon name="bell-plus" size={17} />Follow
      </button>
    );
  }

  return (
    <div className="kc-follow" ref={ref}>
      <button type="button" className="kc-btn kc-btn--lg kc-btn--accent" aria-haspopup="menu" aria-expanded={open} onClick={() => setOpen(!open)}>
        <Icon name="bell-ring" size={17} />Following<Icon name="chevron-down" size={15} />
      </button>
      {open && (
        <div className="kc-follow__menu" role="menu">
          <label className="kc-follow__option">
            <span>Download new episodes automatically</span>
            <Toggle on={follow.autoDownload} disabled={busy} label="Download new episodes automatically" onChange={setAuto} />
          </label>
          <button type="button" className="kc-follow__unfollow" role="menuitem" disabled={busy} onClick={stop}>
            <Icon name="bell-off" size={15} />Unfollow
          </button>
        </div>
      )}
    </div>
  );
}
