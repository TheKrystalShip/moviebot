import { Button } from "@thekrystalship/krystal";
import { useOnline } from "../connectivity.js";
import { newPlanPath } from "../paths.js";
import { navigate } from "../router.js";
import { useWant } from "../wants.js";

/**
 * Opens the form that plans watching a title together, with the title filled in. A title that is not
 * out yet has nothing to watch at any time a plan could name, so the button is out of reach: the page
 * says so (`notOut`), or the person's own want is waiting for a release.
 */
export function PlanButton({ imdbId, kind, notOut = false }) {
  const online = useOnline();
  const want = useWant(imdbId);
  const unreleased = notOut || (want && want.state === "waiting");
  return (
    <Button size="xl" icon="calendar-plus" disabled={!online || unreleased} onClick={() => navigate(newPlanPath(imdbId, kind))}>
      Plan a watch
    </Button>
  );
}
