import { Button } from "@thekrystalship/krystal";
import { useOnline } from "../connectivity.js";
import { planPath } from "../paths.js";
import { navigate } from "../router.js";

/** Opens the form that plans watching a title together, with the title filled in. */
export function PlanButton({ imdbId }) {
  const online = useOnline();
  return <Button size="xl" icon="calendar-plus" disabled={!online} onClick={() => navigate(planPath(imdbId))}>Plan a watch</Button>;
}
