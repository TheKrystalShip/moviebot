import { Button } from "@thekrystalship/krystal";
import { linkProps } from "../router.js";

/** A shelf's way to the whole of what it holds: a link opposite its heading. */
export function ViewAll({ to, children }) {
  return <Button variant="link" {...linkProps(to)}>{children}</Button>;
}
