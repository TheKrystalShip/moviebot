import { Breadcrumbs } from "@thekrystalship/krystal";
import { trailFor, useCrumbName } from "../crumbs.js";
import { navigate } from "../router.js";

/** The trail of the page shown, above it. The root has none. */
export function Trail({ route }) {
  const items = trailFor(route, useCrumbName(route.key));
  if (items.length === 0) return null;
  return <div className="kc-trail"><Breadcrumbs items={items} onNavigate={(href) => navigate(href)} /></div>;
}
