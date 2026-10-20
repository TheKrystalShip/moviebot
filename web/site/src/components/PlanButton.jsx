import { Button } from "@thekrystalship/krystal";
import { useOnline } from "../connectivity.js";
import { newPlanPath } from "../paths.js";
import { navigate } from "../router.js";

/** Opens the form that plans watching a title together, with the title filled in. */
export function PlanButton({ imdbId, kind }) {
  const online = useOnline();
  return <Button size="xl" icon="calendar-plus" disabled={!online} onClick={() => navigate(newPlanPath(imdbId, kind))}>Plan a watch</Button>;
}
