import React from "react";
import { Button, Menu, MenuCheckboxItem, MenuItem, toast } from "@thekrystalship/krystal";
import { api } from "../api.js";
import { useOnline } from "../connectivity.js";
import { useCan } from "../session.js";

/**
 * Follow a show, or, once following, a menu on the same button: whether new episodes are fetched
 * when they air, and unfollowing. The follow is the signed-in person's own.
 */
export function FollowButton({ imdbId, title, follow, onChange }) {
  const downloading = useCan("cinema:download");
  const online = useOnline();
  const [working, setBusy] = React.useState(false);
  const busy = working || !online;

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
    toast.info(`You no longer follow ${title}.`);
    onChange();
  }, "Could not unfollow it");

  if (!follow) {
    return (
      <Button size="xl" icon="bell-plus" disabled={busy} onClick={start}>Follow</Button>
    );
  }

  return (
    <Menu trigger={(
      <Button variant="accent" size="xl" icon="bell-ring" iconRight="chevron-down" disabled={!online}>Following</Button>
    )}>
      {downloading && (
        <MenuCheckboxItem checked={follow.autoDownload} disabled={busy} onCheckedChange={setAuto}>
          Download new episodes automatically
        </MenuCheckboxItem>
      )}
      <MenuItem icon="bell-off" disabled={busy} onSelect={stop}>Unfollow</MenuItem>
    </Menu>
  );
}
