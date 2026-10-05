import { Button } from "@thekrystalship/krystal-ui";
import { Empty } from "../components/Film.jsx";
import { homePath } from "../paths.js";
import { linkProps } from "../router.js";

/** An address the site has no page for, drawn where the page would be. */
export function NotFound() {
  return (
    <div className="kc-page">
      <Empty icon="compass" title="Not found">
        <Button size="lg" {...linkProps(homePath)}>Home</Button>
      </Empty>
    </div>
  );
}
