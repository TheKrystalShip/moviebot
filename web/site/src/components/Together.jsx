import { WantCard } from "../pages/Wants.jsx";
import { Faces } from "./Wanters.jsx";

/** A title wanted together: its want card, and the faces of the others who want it too. */
export function TogetherCard({ together }) {
  return (
    <div className="kc-together">
      <WantCard want={together.want} />
      <Faces people={together.with} />
    </div>
  );
}
